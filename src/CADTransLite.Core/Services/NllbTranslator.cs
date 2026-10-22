// Services/NllbTranslator.cs
// NLLB (本地离线) translation engine adapter.
using CADTransLite.Core.Interfaces;
using CADTransLite.Core.Models;

//
// This engine integrates the offline NLLB (No Language Left Behind) neural machine translation
// model used by RTranslator (https://gitcode.com/GitHub_Trending/rt/RTranslator).
//
// RTranslator's translation pipeline:
//   * Whisper-Small for speech-to-text (ASR) — NOT used here, CADTrans translates CAD text, not audio.
//   * NLLB-Distilled-600M for translation, tokenized with the NLLB SentencePiece BPE model
//     (sentencepiece_bpe.model, 256k FLORES-200 vocabulary) with a +1 id offset and FLORES-200
//     language tokens (e.g. eng_Latn, zho_Hans). The source language token is prepended to the
//     source; the target language token is forced as the decoder's first generated token.
//   * Encoder/decoder with KV cache, greedy/beam search, INT8-quantized weights via ONNX Runtime.
//
// Because the host app is .NET/WPF we run NLLB as a small local HTTP server
// (`tools/py/nllb_server.py`, a pure-stdlib wrapper around HuggingFace transformers/optimum that
// replicates the exact NLLB tokenization) and call it over HTTP — exactly like the other local
// engines. The bundled `python.exe` (embeddable Python) lives in `tools/py` and is auto-started
// by this adapter when the port is closed.
//
// The wire contract matches LibreTranslate: `POST /translate` with { q, source, target, format }
// and a `translatedText` response, so the same HTTP client logic is reused.

using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Concurrent;

namespace CADTransLite.Core.Services;

/// <summary>
/// Translation API implementation backed by a local NLLB server (python wrapper).
/// Requires <see cref="TranslationApiConfig.BaseUrl"/> pointing at the running server
/// (default http://127.0.0.1:5002).
/// </summary>
public sealed class NllbTranslator : ITranslationApi
{
    public string Name => "NLLB (本地)";

    private const string DefaultUrl = "http://127.0.0.1:5002";
    private const string PythonExeName = "python.exe";
    private const string NllbSubDir = "tools/py";
    private const string NllbScriptName = "nllb_server.py";

    // NLLB-600M on CPU can take a while to load + run the first time, so use a generous timeout.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(300);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _baseUrl;
    private readonly HttpClient _http;
    private bool _startedOnce;
    // 按 URL 串行化启动，避免并发调用各自拉起一个服务（竞态导致多进程抢端口、
    // 被 ShouldStartFreshServer 误判“非本会话占用”而反复杀掉重启，造成“测试失败”）。
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _startGates = new(StringComparer.OrdinalIgnoreCase);

    public NllbTranslator(TranslationApiConfig config)
    {
        _baseUrl = string.IsNullOrWhiteSpace(config.BaseUrl) ? DefaultUrl : config.BaseUrl.TrimEnd('/');
        _http = new HttpClient { Timeout = RequestTimeout };
    }

    public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken cancellationToken = default)
    {
        // 空文本：直接返回，不发起请求，避免服务器退化输出空译文（与 TranslateBatchAsync 的空行跳过语义一致）。
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);

        var payload = new Dictionary<string, string>
        {
            ["q"] = text,
            ["source"] = string.IsNullOrWhiteSpace(sourceLang) ? "auto" : sourceLang.ToLowerInvariant(),
            ["target"] = targetLang.ToLowerInvariant(),
            ["format"] = "text",
        };

        Exception? lastError = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                // 必须用带固定 Content-Length 的 StringContent（见 LocalServerHelper.CreateJsonBody 注释）：
                // PostAsJsonAsync 会以 chunked 发送，nllb_server.py 读不到 body，q 变空串，
                // 服务端返回 200 + 空译文，宿主端误报「模型未产生有效译文」。
                using var content = LocalServerHelper.CreateJsonBody(payload, JsonOptions);
                using var resp = await _http.PostAsync($"{_baseUrl}/translate", content, cancellationToken)
                    .ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                {
                    // 服务刚刚应答了 HTTP——进程显然活着。此时杀服务只会让约 40s 的模型
                    // 冷加载前功尽弃（并可能引发「启动→被杀→再启动」风暴）。
                    // 业务错误（空译文 / 内存不足等）交由调用方按原文回退处理。
                    throw new InvalidOperationException($"NLLB 服务返回错误：{err.GetString()}");
                }
                if (!doc.RootElement.TryGetProperty("translatedText", out var t))
                {
                    // 同上：能收到响应就说明服务活着，不应杀进程。
                    throw new InvalidOperationException("NLLB 本地服务返回结果缺少 translatedText 字段。");
                }

                // 空/空白译文视为失败（模型退化输出），否则宿主端会把空串当成功，
                // UI 显示「翻译无输出」而用户无法察觉真实问题。
                var translated = t.GetString();
                if (string.IsNullOrWhiteSpace(translated))
                {
                    // 不杀进程：模型空输出属于请求级问题（源/目标语言不被支持、输入过短等），
                    // 重启服务并不能解决，反而会销毁已加载的模型（约 40s）。
                    throw new InvalidOperationException("NLLB 翻译返回为空（模型未产生有效译文，可能是源/目标语言不被支持）。");
                }
                return translated;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                _startedOnce = false;
                // 关键策略：只有当服务进程确实「无响应」时才强杀重启。
                // 服务仍在应答（包括返回业务错误、或正在冷加载模型）时杀掉它，
                // 会让约 40s 的模型加载从头再来，并引发「启动→被杀→再启动」风暴
                // （实例只能活几秒，模型永远加载不完，表现为反复「调用失败」）。
                if (!await IsNllbServerAliveAsync().ConfigureAwait(false))
                    KillFaultyServer();
                await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(300 * (attempt + 1), cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException(
            $"NLLB 本地服务调用失败：{lastError?.Message}。请确认已运行 tools/setup_nllb.ps1 安装依赖与模型（模型已随软件打包，位于 tools/py/models/nllb-200-distilled-600M），并成功启动 python 服务。");
    }

    private async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (_startedOnce) return;

        // 串行化：同一 URL 只容许一个调用进入启动流程，杜绝并发竞态导致多个
        // NLLB 进程与其他本地引擎同时拉起、互相抢端口而崩溃（ExitCode=-1）的“测试失败”。
        var gate = _startGates.GetOrAdd(_baseUrl, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_startedOnce) return;

            if (!LocalServerHelper.TryParseHostPort(_baseUrl, out var host, out var port))
                throw new InvalidOperationException($"无法解析 NLLB 本地服务地址: {_baseUrl}");

            // NLLB 在 serve_forever() 时立即开放端口，模型在「第一次翻译」时才懒加载（约 30~60s）。
            // 因此「端口开放」≠「模型已就绪」。端口被占用时的策略：
            //   - 探测占用者是否是活着的 NLLB 服务（GET / 立即应答，与模型是否加载完成无关）。
            //     活着 → 直接复用（哪怕是上次会话或 MCP 桥接进程启动的）。若按「非本会话」
            //     一刀切杀掉重启，UI 与桥接会互相杀对方实例、冷加载被反复打断（实例只能活
            //     几秒，模型永远加载不完）——这正是「启动→被杀→再启动」风暴与「翻译返回为空」
            //     反复出现的根因。
            //   - 不应答（半死 socket）→ 清理后拉起干净实例。
            if (LocalServerHelper.IsPortOpen(host, port))
            {
                if (await IsNllbServerAliveAsync().ConfigureAwait(false))
                {
                    _startedOnce = true;
                    return;
                }

                LocalServerHelper.StopServerOnPort(host, port);
                var closeDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
                while (DateTime.UtcNow < closeDeadline && LocalServerHelper.IsPortOpen(host, port))
                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }

            // 最多尝试启动 2 次：若新实例因端口尚未完全释放而绑定失败，清理后重试一次。
            for (int startTry = 0; startTry < 2; startTry++)
            {
                LocalServerHelper.TryStartBundledServer(
                    PythonExeName,
                    new[] { NllbScriptName, "--port", port.ToString() },
                    NllbSubDir);

                // 仅等待端口开放（确认 python 进程已起来并开始监听），最多 30s；
                // 不等待翻译就绪，因为那要在真实请求里由长超时覆盖。
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
                while (DateTime.UtcNow < deadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (LocalServerHelper.IsPortOpen(host, port))
                        break;
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                }

                if (LocalServerHelper.IsPortOpen(host, port))
                    break;

                // 端口仍未开放：新实例可能绑定失败，清理后重试。
                LocalServerHelper.StopServerOnPort(host, port);
                var retryClose = DateTime.UtcNow + TimeSpan.FromSeconds(6);
                while (DateTime.UtcNow < retryClose && LocalServerHelper.IsPortOpen(host, port))
                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }

            _startedOnce = true;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 杀掉当前 URL 对应端口上的服务进程。仅在确认实例已无响应（进程已死/半死 socket）时调用，
    /// 以便 <see cref="EnsureReadyAsync"/> 下次探测时端口已释放、可拉起干净实例。
    /// </summary>
    private void KillFaultyServer()
    {
        try
        {
            if (LocalServerHelper.TryParseHostPort(_baseUrl, out var host, out var port))
                LocalServerHelper.StopServerOnPort(host, port);
        }
        catch
        {
            // 杀进程失败不应阻断主流程；下次 EnsureReadyAsync 会重新评估。
        }
    }

    /// <summary>
    /// 探测端口上是否有一个「活着的 NLLB 服务」：GET / 应立即返回身份 JSON
    /// （{"message":"NLLB server is running"}），与模型是否加载完成无关
    /// （GET / 不经过翻译锁，模型冷加载期间也能秒回）。
    /// 同时校验身份，避免把同一端口上的其它服务（如 LibreTranslate）误认作 NLLB 复用。
    /// </summary>
    private async Task<bool> IsNllbServerAliveAsync()
    {
        try
        {
            using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var resp = await probe.GetAsync($"{_baseUrl}/").ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return body.Contains("NLLB", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose() => _http.Dispose();

    /// <inheritdoc/>
    public async Task<List<string>> TranslateBatchAsync(
        List<string> texts, string sourceLang, string targetLang, CancellationToken cancellationToken = default)
    {
        if (texts is null || texts.Count == 0)
            throw new ArgumentException("Texts list must not be empty.", nameof(texts));

        var results = new List<string>(texts.Count);
        int failed = 0;
        foreach (var text in texts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 单条翻译失败（空译文、服务报错、网络异常等）不应拖垮整批：
            // 该条回退为原文，计入失败数，由上层在回填时跳过并保留原文。
            // 这样「某一行是空/纯符号/语言不被支持」时，只这一行留原文，
            // 其余行正常被翻译回填，而不是整批都失败。
            if (string.IsNullOrWhiteSpace(text))
            {
                results.Add(text ?? string.Empty);
                continue;
            }

            try
            {
                results.Add(await TranslateAsync(text, sourceLang, targetLang, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                results.Add(text); // 回退为原文，回填时会被跳过
            }
        }

        if (failed > 0)
            Console.WriteLine($"[NLLB] {failed}/{texts.Count} 条翻译失败（已回退为原文，回填时跳过）：详见日志。");

        return results;
    }
}
