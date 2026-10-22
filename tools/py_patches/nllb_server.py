#!/usr/bin/env python3
# tools/py/nllb_server.py
#
# NLLB (本地离线) translation server for CADTrans Lite.
#
# This is the local HTTP backend for the "NLLB (本地)" translation engine in CADTrans Lite.
# It integrates the SAME model RTranslator uses (Meta NLLB-200-Distilled-600M), replicating
# its translation pipeline:
#
#   * Tokenizer : NLLB SentencePiece BPE (256k FLORES-200 vocabulary) with the +1 id offset
#                 and FLORES-200 language tokens (eng_Latn, zho_Hans, ...). The source language
#                 token is prepended to the source; the target language token is forced as the
#                 decoder's first generated token (standard NLLB behaviour). HuggingFace's
#                 NllbTokenizerFast implements exactly this, so we reuse it instead of re-rolling
#                 RTranslator's Java tokenizer.
#   * Model     : encoder/decoder (M2M-style) with beam search, run on CPU by default.
#   * Quant/acc : RTranslator ships INT8 weight + FP16 activation via ONNX Runtime. Here we run
#                 the same model through HuggingFace; set NLLB_USE_ONNX=1 to use Optimum's
#                 ONNX-Runtime export (INT8/FP16) for a closer match to RTranslator's runtime.
#
# Wire contract (matches LibreTranslate / Argos so the C# client is unchanged):
#   POST /translate  { "q": "...", "source": "en", "target": "zh", "format": "text" }
#        -> { "translatedText": "..." }
#   GET  /languages  -> { "source": [...], "target": [...] }
#   GET  /           -> "NLLB server is running"
#
# The model is downloaded once (cached under NLLB_CACHE or ~/.cache/huggingface) and loaded
# lazily on the first request, so the port opens immediately for the C# auto-start probe.

import argparse
import json
import os
import sys
import threading
import traceback

# 限制 MKL / OpenMP 线程池，避免 torch/transformers 在 CPU 推理时
# 触发 Access Violation (0xC0000005) 崩溃（MKL 默认按 CPU 线程数分配大内存池，
# 多线程下内存损坏；与 Argos 的 mkl_malloc 问题同源）。必须在 import torch/transformers
# 之前设置才生效。
os.environ.setdefault("OMP_NUM_THREADS", "1")
os.environ.setdefault("MKL_NUM_THREADS", "1")
os.environ.setdefault("OPENBLAS_NUM_THREADS", "1")
os.environ.setdefault("MKL_DYNAMIC", "FALSE")
os.environ.setdefault("KMP_DUPLICATE_LIB_OK", "TRUE")

# CADTrans Lite 宿主（LocalServerHelper）会为所有本地 python 服务注入 XDG_DATA_HOME /
# XDG_CONFIG_HOME（Linux 风格缓存变量，指向 tools/py 目录）。但 transformers / HuggingFace
# 在 Windows 上若看到 XDG_DATA_HOME，会把它当作缓存根，首次 generate 时触发底层
# [Errno 22] Invalid argument 甚至进程崩溃（0xC0000005）。Windows 本不使用该变量，直接清除；
# 并把 HF 缓存固定到脚本旁的离线目录（模型已随包打包，无需联网下载）。必须在
# import transformers / torch 之前处理才生效。
if sys.platform == "win32":
    os.environ.pop("XDG_DATA_HOME", None)
    os.environ.pop("XDG_CONFIG_HOME", None)
    _hf_cache = os.path.join(os.path.dirname(os.path.abspath(__file__)), ".hf_cache")
    os.environ["HF_HOME"] = _hf_cache
    os.environ["TRANSFORMERS_CACHE"] = os.path.join(_hf_cache, "transformers")
    os.environ["HF_HUB_CACHE"] = os.path.join(_hf_cache, "hub")
    os.environ["TRANSFORMERS_OFFLINE"] = "1"
    os.environ["HF_HUB_OFFLINE"] = "1"

from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

# ----------------------------------------------------------------------------
# Language mapping: incoming ISO-639-1 (or CADTrans) code -> FLORES-200 code.
# Also accepts FLORES-200 codes directly. "auto" falls back to English source.
# Mirrors the language set CADTrans Lite exposes (ZH EN RU ES PT AR FR VI KO JA IT DE TH)
# plus a broad set of common languages.
# ----------------------------------------------------------------------------
ISO_TO_FLORES = {
    "zh": "zho_Hans", "zh-cn": "zho_Hans", "zhs": "zho_Hans", "chs": "zho_Hans",
    "zh-hans": "zho_Hans", "zh_cn": "zho_Hans", "cmn": "zho_Hans",
    "zh-tw": "zho_Hant", "zht": "zho_Hant", "cht": "zho_Hant", "zh-hant": "zho_Hant",
    "zh_hant": "zho_Hant",
    "en": "eng_Latn", "eng": "eng_Latn",
    "ru": "rus_Cyrl", "es": "spa_Latn", "spa": "spa_Latn", "pt": "por_Latn",
    "pt-br": "por_Latn", "arb": "arb_Arab", "ar": "arb_Arab", "fr": "fra_Latn",
    "fra": "fra_Latn", "vi": "vie_Latn", "ko": "kor_Hang", "ja": "jpn_Jpan",
    "jpn": "jpn_Jpan", "it": "ita_Latn", "de": "deu_Latn", "deu": "deu_Latn",
    "th": "tha_Thai", "tr": "tur_Latn", "pl": "pol_Latn", "nl": "nld_Latn",
    "cs": "ces_Latn", "sv": "swe_Latn", "uk": "ukr_Cyrl", "id": "ind_Latn",
    "ms": "zsm_Latn", "hi": "hin_Deva", "bn": "ben_Beng", "fa": "pes_Arab",
    "el": "ell_Grek", "he": "heb_Hebr", "ro": "ron_Latn", "hu": "hun_Latn",
    "da": "dan_Latn", "fi": "fin_Latn", "no": "nob_Latn",
}

FLORES_CODES = sorted(set(ISO_TO_FLORES.values()))


def to_flores(code: str):
    if not code:
        return "eng_Latn"
    c = code.strip().lower()
    if c in FLORES_CODES:
        return c
    if c in ISO_TO_FLORES:
        return ISO_TO_FLORES[c]
    # allow "zho_Hans" style passed through even if not in the small set
    if "_" in c and len(c) == 8:
        return c
    # unknown -> default to English source (NLLB needs an explicit source)
    return "eng_Latn"


# ----------------------------------------------------------------------------
# Model loading (lazy, thread-safe)
# ----------------------------------------------------------------------------
_MODEL = None
_MODEL_LOCK = threading.Lock()
_LOADING = False

# 全局翻译锁：保证同一时刻只有一个请求在执行翻译（含首次模型加载）。
# 否则「启动服务」的预热请求与用户点「测试」会并发跑到 translate，既触发上面的
# 并发加载竞争，又会让两次 model.generate 在 CPU 上互相争抢。加锁后：第一个请求
# 负责加载（耗时长），后续请求阻塞等待，加载完成后秒回。
_TRANSLATE_LOCK = threading.Lock()


def _default_model_name():
    """Resolve the model to load.

    Priority:
      1. NLLB_MODEL env var (explicit repo id or local path).
      2. A model bundled next to this script under tools/py/models/nllb-200-distilled-600M
         (installed by setup_nllb.ps1). Loading from here needs NO network download.
      3. Fall back to the HuggingFace repo id (will be downloaded + cached on first use).
    """
    env = os.environ.get("NLLB_MODEL")
    if env:
        return env
    script_dir = os.path.dirname(os.path.abspath(__file__))
    bundled = os.path.join(script_dir, "models", "nllb-200-distilled-600M")
    if os.path.isdir(bundled) and os.path.exists(os.path.join(bundled, "config.json")):
        return bundled
    return "facebook/nllb-200-distilled-600M"


def _load_model():
    global _MODEL
    # 关键：整段加载都在锁内完成。并发请求（例如「启动服务」的预热与用户点「测试」同时到达）
    # 会在锁上阻塞，直到首个请求把模型加载完毕并赋给 _MODEL，后续请求直接拿到已加载的模型。
    # 若只把「检查」放在锁内、加载放在锁外，则第二个线程会在 _MODEL 仍为 None、_LOADING 为 True
    # 时 return _MODEL（即 None），触发「cannot unpack non-iterable NoneType object」崩溃。
    with _MODEL_LOCK:
        if _MODEL is not None:
            return _MODEL

        model_name = _default_model_name()
        use_onnx = os.environ.get("NLLB_USE_ONNX", "0") == "1"
        device = os.environ.get("NLLB_DEVICE", "cpu")
        cache = os.environ.get("NLLB_CACHE", "")

        if os.path.isdir(model_name):
            print(f"[nllb] loading BUNDLED model from local dir '{model_name}' "
                  f"(onnx={use_onnx}, device={device}) ...", flush=True)
        else:
            print(f"[nllb] loading model '{model_name}' (onnx={use_onnx}, device={device}) "
                  f"— 若本地无缓存将从 HuggingFace 下载 ...", flush=True)

        if use_onnx:
            try:
                from optimum.onnxruntime import ORTModelForSeq2SeqLM
                from transformers import AutoTokenizer, AutoConfig
                tokenizer = AutoTokenizer.from_pretrained(model_name, cache_dir=cache or None)
                # NLLB 的 .bin checkpoint 同时含 model.shared.weight 与 lm_head.weight（同一存储）。
                # 见下方 torch 分支说明：设 tie_word_embeddings=False 跳过会触发崩溃的 torch.equal 比较。
                _cfg = AutoConfig.from_pretrained(model_name, cache_dir=cache or None)
                _cfg.tie_word_embeddings = False
                model = ORTModelForSeq2SeqLM.from_pretrained(
                    model_name, config=_cfg, export=True, cache_dir=cache or None)
                print("[nllb] ONNX model loaded.", flush=True)
                _MODEL = (tokenizer, model, "onnx")
                return _MODEL
            except Exception as e:  # noqa: BLE001
                print(f"[nllb] ONNX load failed ({e}); falling back to torch.", flush=True)

        from transformers import AutoTokenizer, AutoModelForSeq2SeqLM, AutoConfig
        import torch

        # 必须在 from_pretrained 之前把 torch 内部 OpenMP 线程池固定为 1。
        # 注意：torch 的内部线程池由 torch.set_num_threads() 控制，不认 OMP_NUM_THREADS
        # 环境变量；若在此处仍用全部核心，from_pretrained 的 tie_weights（共享 embedding）
        # 阶段会触发 MKL 多线程内存损坏 -> Access Violation (0xC0000005) 进程崩溃。
        _n_threads = int(os.environ.get("NLLB_THREADS", "1") or 1)
        torch.set_num_threads(max(1, min(_n_threads, 4)))

        tokenizer = AutoTokenizer.from_pretrained(model_name, cache_dir=cache or None)

        # 关键修复：NLLB-200-distilled-600M 的 .bin checkpoint 同时包含 model.shared.weight
        # 与 lm_head.weight（指向同一存储）。transformers 5.x 的 tie_weights 对称检查会对这两个
        # 超大规模嵌入张量（词表 256k × 维度 1024，约 2.5 亿元素）调用 torch.equal 做相等性比较，
        # 该比较在 torch 2.13 CPU/MKL 上触发 0xC0000005 访问违规 -> 进程直接崩溃（表现为"测试失败"）。
        # 由于 lm_head.weight 已随 checkpoint 一并加载，设 tie_word_embeddings=False 即可让
        # tie_weights 无绑定键可处理（跳过 torch.equal），且翻译结果完全正确（lm_head 用 checkpoint
        # 中的权重，与共享权重等价）。这是"测试按钮必失败"的根因修复。
        _cfg = AutoConfig.from_pretrained(model_name, cache_dir=cache or None)
        _cfg.tie_word_embeddings = False

        model = AutoModelForSeq2SeqLM.from_pretrained(
            model_name, config=_cfg, cache_dir=cache or None, low_cpu_mem_usage=True)
        # 说明：本环境（该 torch / fbgemm 构建 + 当前 CPU）下，动态 INT8 量化会出现两类
        # 致命问题 —— (a) 生成结果退化为乱码；(b) 整模型量化瞬时占用约 2x 内存，在低内存
        # 机器上被操作系统直接强杀（进程崩溃，连 500 都来不及返回）。因此此处不使用量化，
        # 保持 FP32。FP32 模型约 2.4GB，已在隔离环境中验证可正确翻译。
        model.to(device)
        model.eval()
        print("[nllb] model loaded.", flush=True)

        _MODEL = (tokenizer, model, device)
        return _MODEL


def translate(text: str, source: str, target: str, max_length: int = 256) -> str:
    if not text or not text.strip():
        return ""

    with _TRANSLATE_LOCK:
        return _translate(text, source, target, max_length)


def _translate(text: str, source: str, target: str, max_length: int = 256,
               beam: int = None) -> str:
    if beam is None:
        beam = int(os.environ.get("NLLB_BEAMS", "2") or 2)
    tokenizer, model, backend = _load_model()
    src = to_flores(source)
    tgt = to_flores(target)

    # Split into lines / short chunks to mirror RTranslator's sentence segmentation and to
    # stay within the model's context window.
    chunks = [c for c in text.split("\n") if c.strip() != ""]
    if not chunks:
        chunks = [text]

    out_lines = []
    for chunk in chunks:
        # rough token-count guard: ~4 chars per token for CJK, fall back to words for latin
        approx_tokens = max(len(chunk) // 4, len(chunk.split()) + 1)
        if approx_tokens > max_length:
            # hard split into pieces
            step = max_length * 3
            piece = chunk
            while piece:
                out_lines.append(_translate_chunk(tokenizer, model, backend, piece[:step], src, tgt, max_length, beam))
                piece = piece[step:]
            continue
        out_lines.append(_translate_chunk(tokenizer, model, backend, chunk, src, tgt, max_length, beam))

    return "\n".join(out_lines)


def _translate_chunk(tokenizer, model, backend, text, src, tgt, max_length, beam=2):
    tokenizer.src_lang = src
    inputs = tokenizer(text, return_tensors="pt", truncation=True, max_length=max_length)
    if backend == "onnx":
        gen = model.generate(
            **inputs,
            forced_bos_token_id=tokenizer.convert_tokens_to_ids(tgt),
            max_new_tokens=max_length, num_beams=beam)
    else:
        device = backend
        inputs = {k: v.to(device) for k, v in inputs.items()}
        with __import__("torch").no_grad():
            gen = model.generate(
                **inputs,
                forced_bos_token_id=tokenizer.convert_tokens_to_ids(tgt),
                max_new_tokens=max_length, num_beams=beam)
    return tokenizer.batch_decode(gen, skip_special_tokens=True)[0].strip()


# ----------------------------------------------------------------------------
# HTTP handler
# ----------------------------------------------------------------------------
class Handler(BaseHTTPRequestHandler):
    def _send(self, code, payload):
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path.startswith("/languages"):
            self._send(200, {"source": sorted(ISO_TO_FLORES.keys()),
                             "target": sorted(ISO_TO_FLORES.keys())})
            return
        self._send(200, {"status": "ok", "message": "NLLB server is running"})

    def _read_body(self):
        """读取请求体，同时支持 Content-Length 与 Transfer-Encoding: chunked。

        必须支持 chunked：.NET 的 HttpClient.PostAsJsonAsync 使用长度未知的 JsonContent，
        会以 chunked 发送请求体。BaseHTTPRequestHandler 不会自动解码 chunked，若只读
        Content-Length（此时为 0），就会读到空 body —— 表现为「q 为空 → 返回 200 + 空译文」，
        宿主端误判为「模型未产生有效译文」。这里显式解码，作为客户端的兜底。
        """
        te = (self.headers.get("Transfer-Encoding") or "").lower()
        if "chunked" in te:
            parts = []
            while True:
                line = self.rfile.readline(65536)
                if not line:
                    break
                line = line.strip()
                if not line:
                    continue
                try:
                    size = int(line.split(b";")[0], 16)
                except ValueError:
                    break
                if size == 0:
                    # 消费 trailer（以空行为结束）
                    while True:
                        trailer = self.rfile.readline(65536)
                        if trailer in (b"\r\n", b"\n", b""):
                            break
                    break
                parts.append(self.rfile.read(size))
                self.rfile.read(2)  # 每个 chunk 结尾的 CRLF
            return b"".join(parts)

        length = self.headers.get("Content-Length")
        if not length:
            return b""
        try:
            n = int(length)
        except (TypeError, ValueError):
            return b""
        return self.rfile.read(n) if n > 0 else b""

    def do_POST(self):
        if not self.path.startswith("/translate"):
            self._send(404, {"error": "not found"})
            return
        try:
            raw = self._read_body()
            data = json.loads(raw.decode("utf-8") or "{}")
        except Exception as e:  # noqa: BLE001
            self._send(400, {"error": f"bad request: {e}"})
            return

        if not isinstance(data, dict):
            self._send(400, {"error": "bad request: body must be a JSON object"})
            return

        # 缺少 q 字段 = 请求体没被正确读取（例如客户端用了 chunked 而我们没解出来）。
        # 明确报错，避免宿主端把它误判为「模型输出空译文」。
        if "q" not in data and "text" not in data:
            print(f"[nllb] 请求体缺少 q 字段（chunked={ (self.headers.get('Transfer-Encoding') or '') }，"
                  f"body={raw[:200]!r}）", flush=True)
            self._send(400, {
                "error": "请求缺少 q 字段：请求体为空或未被正确读取（可能是 chunked 传输未解析）。"
            })
            return

        q = str(data.get("q") if data.get("q") is not None else data.get("text") or "")
        source = (data.get("source") or "auto")
        target = (data.get("target") or "en")

        # 空文本：与 Argos 行为对齐——直接返回空串，不调用模型（避免退化输出空译文被误判为 500 错误）。
        # 宿主端对空文本块会跳过/回退为原文，这里干净返回即可，不写错误日志。
        if not q.strip():
            self._send(200, {"translatedText": ""})
            return

        try:
            translated = translate(q, source, target)
        except MemoryError:
            # 物理内存不足：FP32 模型常驻约 2.4GB，generate 时还会临时占用更多。
            # 把明确的中文提示返回给宿主，避免空错误体。
            tb = traceback.format_exc()
            print(f"[nllb] MemoryError during translate:\n{tb}", file=sys.stderr, flush=True)
            self._send(503, {
                "error": "内存不足：NLLB(FP32) 模型常驻约 2.4GB，生成时还会临时占用更多。"
                         "请关闭其他占用内存的程序、或增大系统虚拟内存后重试。"
                         "如仍失败，可在高级设置中改用 Argos Translate(本地) 或在线翻译。",
                "trace": tb,
            })
            return
        except Exception as e:  # noqa: BLE001
            tb = traceback.format_exc()
            print(f"[nllb] translate error:\n{tb}", file=sys.stderr, flush=True)
            self._send(500, {"error": str(e) or "unknown error", "trace": tb})
            return

        # 空译文是真实失败（源/目标语言代码不被支持、输入过短等导致模型退化输出），
        # 不能当成正常结果返回空串——否则宿主端会把空串当作成功、UI 显示「翻译无输出」。
        if not translated or not translated.strip():
            print(f"[nllb] 翻译返回空结果 source={source} target={target} text={q!r}", flush=True)
            self._send(500, {
                "error": "模型返回了空译文（可能是源/目标语言代码不被支持，或输入文本过短）。",
                "raw": translated,
            })
            return

        self._send(200, {"translatedText": translated})

    def log_message(self, fmt, *args):  # silence default logging
        pass


def main():
    ap = argparse.ArgumentParser(description="NLLB local translation server")
    ap.add_argument("--host", default=os.environ.get("NLLB_HOST", "127.0.0.1"))
    ap.add_argument("--port", type=int, default=int(os.environ.get("NLLB_PORT", "5002")))
    ap.add_argument("--model", default=None, help="Override NLLB_MODEL")
    args = ap.parse_args()

    if args.model:
        os.environ["NLLB_MODEL"] = args.model

    print(f"[nllb] starting on http://{args.host}:{args.port} "
          f"(model={os.environ.get('NLLB_MODEL', 'facebook/nllb-200-distilled-600M')})",
          flush=True)
    # 端口复用策略按平台区分：
    #  - Windows：必须独占绑定（allow_reuse_address=False）。Windows 的 SO_REUSEADDR 语义是
    #    「允许多个进程同时 bind 同一端口」，会让新旧实例并存、连接被随机分给其中一个——
    #    表现为请求打到旧实例返回空译文，宿主按端口清理时还会连同新实例一起误杀
    #    （「启动→被杀→再启动」风暴，模型永远加载不完）。独占绑定下，端口真被占用时
    #    新实例会立刻 bind 失败，宿主能明确感知并走清理/重试逻辑。
    #  - 非 Windows：SO_REUSEADDR 仅用于绕过 TIME_WAIT，保留默认行为。
    if sys.platform == "win32":
        ThreadingHTTPServer.allow_reuse_address = False
    else:
        ThreadingHTTPServer.allow_reuse_address = True
    server = ThreadingHTTPServer((args.host, args.port), Handler)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
