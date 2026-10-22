from __future__ import annotations

import os
from difflib import SequenceMatcher
from typing import List

try:
    import spacy
except ImportError:
    spacy = None

import stanza
from minisbd import SBDetect, models as minisbd_models

from argostranslate import package, settings
from argostranslate.networking import cache_spacy
from argostranslate.package import Package
from argostranslate.utils import info, warning

minisbd_models.cache_dir = str(settings.data_dir / "minisbd")

def get_stanza_processors(lang_code: str, resources: dict) -> str:
    """Get appropriate processors for a language, including MWT if available."""
    try:
        return "tokenize,mwt" if resources[lang_code].get("mwt") else "tokenize"
    except (KeyError, TypeError):
        return "tokenize"


# Cache SpaCy model once at module level (if enabled)
_cached_spacy_path = None
if settings.chunk_type == settings.ChunkType.SPACY and spacy is not None:
    _cached_spacy_path = cache_spacy()


class ISentenceBoundaryDetectionModel:
    # https://github.com/argosopentech/sbd/blob/main/main.py
    pkg: Package

    def split_sentences(self, text: str) -> List[str]:
        raise NotImplementedError


def minisbd_model_cached(lang: str) -> bool:
    """MiniSBD 的 onnx 模型是否已存在于本地缓存。

    minisbd.get_model_file() 在缓存缺失时会去 GitHub Releases 下载；离线环境下这会
    长时间阻塞（且无超时），最终表现为「正在测试 LibreTranslate (本地)连接…」卡死。
    因此必须先确认文件在本地，再决定是否使用 MiniSBD。
    """
    filename = minisbd_models.MODELS.get(lang)
    if not filename:
        return False
    return os.path.isfile(os.path.join(minisbd_models.cache_dir, filename))


class SimplePunctuationSentencizer(ISentenceBoundaryDetectionModel):
    """完全离线的分句兜底：按句末标点与换行切分。

    当 MiniSBD / Stanza 的模型不在本地缓存、或模型加载失败时使用。
    它绝不触发任何网络请求，保证本地引擎在断网环境不会因为「下载分句模型」而挂起。
    """

    def __init__(self, pkg: Package):
        self.pkg = pkg

    def split_sentences(self, text: str) -> List[str]:
        import re
        parts = re.split(r"(?<=[.!?;。！？；…])\s*|\n+", text)
        return [p.strip() for p in parts if p and p.strip()]

    def __str__(self):
        return "SimplePunctuationSentencizer"


# Spacy sentence boundary detection Sentencizer
# https://community.libretranslate.com/t/sentence-boundary-detection-for-machine-translation/606/3
# https://spacy.io/usage/linguistic-features/#sbd
# Download model:
# python -m spacy download xx_sent_ud_sm
class SpacySentencizerSmall(ISentenceBoundaryDetectionModel):
    def __init__(self, pkg: Package):
        """
        Packaging specific spacy when "xx_sent_ud_sm" doesn't cover the language improves performances over stanza.
        Please use small models ".._core/web_sm" for consistency.
        """
        self.pkg = pkg
        if spacy is None:
            raise RuntimeError("SpaCy is not installed. Install spacy or change ChunkType settings")

        if pkg.packaged_sbd_path is not None and "spacy" in str(pkg.packaged_sbd_path):
            self.nlp = spacy.load(pkg.packaged_sbd_path, exclude=["parser"])
        # Case sbd is not packaged, use cached Spacy multilingual (xx_ud_sent_sm)
        else:
            if _cached_spacy_path is None:
                raise RuntimeError("SpaCy cache not initialized")
            self.nlp = spacy.load(_cached_spacy_path, exclude=["parser"])
        self.nlp.add_pipe("sentencizer")

    def split_sentences(self, text: str) -> List[str]:
        info(f"Splitting sentences using SBD Model: ({self.pkg.from_code}) {str(self)}")
        doc = self.nlp(text)
        return [sent.text for sent in doc.sents]

    def __str__(self):
        return "Using Spacy model."


class MiniSBDSentencizer(ISentenceBoundaryDetectionModel):
    LANGUAGE_CODE_MAPPING = {
        "zt": "zh-hant",
        "zh": "zh-hans",
        "pb": "pt",

        # Fallback languages, a model for these is not available
        # so we map to a close language
        "az": "tr",
        "bn": "hi",
        "eo": "en",
        "ms": "en",
        "tl": "en",

        # These are supported by MiniSBD upstream but their .onnx models are
        # not bundled/cached locally; mapping to the cached "en" model avoids
        # an offline network download attempt (which would fail/disconnect).
        "el": "en",
        "nl": "en",
        "pl": "en",
        "id": "en",

        # Thai (th) has a bundled th.onnx but the cached file is corrupt
        # (INVALID_PROTOBUF); route it through the cached "en" model so that
        # th -> X translations keep working fully offline.
        "th": "en",
    }

    def __init__(self, pkg: Package):
        self.pkg = pkg

        sbd_path = pkg.package_path / "minisbd"
        model_file = None

        if sbd_path.exists():
            model_files = [f for f in os.listdir(sbd_path) if f.endswith('.onnx')]
            if model_files:
                model_file = str(sbd_path / model_files[0])
        
        if model_file is not None:
            # Use provided model
            lang = model_file
        else:
            # Download/use cached models from MiniSBD
            lang = self.LANGUAGE_CODE_MAPPING.get(
                pkg.from_code, pkg.from_code
            )

            # Fallback to English
            if lang not in minisbd_models.list_models():
                warning(f"{self.pkg.from_code} is not available in MiniSBD, falling back to en")
                lang = "en"
        
        self.lang = lang
        self.detector = None
        self._fallback = None

    def lazy_detector(self):
        if self.detector is None and self._fallback is None:
            try:
                # 只用本地缓存模型：缺失时 SBDetect→get_model_file 会尝试联网下载，
                # 离线环境下会长时间挂起（无超时），表现为 UI 卡在「正在测试…」。
                if not minisbd_model_cached(self.lang):
                    if minisbd_model_cached("en"):
                        warning(f"MiniSBD model for {self.lang} is not cached locally, falling back to en")
                        self.lang = "en"
                    else:
                        warning("MiniSBD models are not cached locally, falling back to punctuation splitter")
                        self._fallback = SimplePunctuationSentencizer(self.pkg)
                if self._fallback is None:
                    self.detector = SBDetect(self.lang, use_gpu=settings.device == "cuda")
            except Exception as e:  # noqa: BLE001
                warning(f"MiniSBD init failed ({e}), falling back to punctuation splitter")
                self._fallback = SimplePunctuationSentencizer(self.pkg)
        return self._fallback if self._fallback is not None else self.detector

    def split_sentences(self, text: str) -> List[str]:
        info(f"Splitting sentences using SBD Model: ({self.pkg.from_code}) {str(self)}")
        target = self.lazy_detector()
        if isinstance(target, ISentenceBoundaryDetectionModel):
            return target.split_sentences(text)
        return target.sentences(text)

    def __str__(self):
        return "MiniSBDSentencizer"


class StanzaSentencizer(ISentenceBoundaryDetectionModel):
    LANGUAGE_CODE_MAPPING = {
        "zt": "zh-hant",
        "pb": "pt",
        # 中文包内置的是 zh-hans 分词模型（stanza/zh-hans/tokenize/gsdsimp.pt），
        # 而 from_code 是 "zh"。不做映射会让 stanza 认为模型缺失并尝试从
        # huggingface.co 下载 → 离线环境长时间挂起。
        "zh": "zh-hans",
    }

    def __init__(self, pkg: Package):
        self.pkg = pkg
        self.stanza_lang_code = self.LANGUAGE_CODE_MAPPING.get(
            pkg.from_code, pkg.from_code
        )

        self.stanza_pipeline = None
        self._fallback = None

    def lazy_pipeline(self):
        if self.stanza_pipeline is None and self._fallback is None:
            try:
                # DownloadMethod.NONE：只用包内自带的模型，绝不联网下载。
                # 默认的 REUSE_RESOURCES 在模型缺失/版本不兼容时会去 huggingface.co 拉取，
                # connect 无超时 → 离线环境下请求会挂几分钟（UI 一直显示「正在测试…」）。
                from stanza.pipeline.core import DownloadMethod
                self.stanza_pipeline = stanza.Pipeline(
                    lang=self.stanza_lang_code,
                    dir=str(self.pkg.package_path / "stanza"),
                    processors="tokenize",
                    use_gpu=settings.device == "cuda",
                    logging_level="WARNING",
                    download_method=DownloadMethod.NONE,
                )
            except Exception as e:  # noqa: BLE001
                warning(f"Stanza pipeline unavailable ({e}), falling back to punctuation splitter")
                self._fallback = SimplePunctuationSentencizer(self.pkg)
        return self._fallback if self._fallback is not None else self.stanza_pipeline

    def split_sentences(self, text: str) -> List[str]:
        info(f"Splitting sentences using SBD Model: ({self.pkg.from_code}) {str(self)}")
        pipeline = self.lazy_pipeline()
        if isinstance(pipeline, ISentenceBoundaryDetectionModel):
            return pipeline.split_sentences(text)
        doc = pipeline(text)
        return [sent.text for sent in doc.sentences]

    def __str__(self):
        return "StanzaSentencizer"


###############################################
#### Few Shot Sentence Boundary Detection ####
###############################################

# The Few Shot SBD code mostly isn't used currently

fewshot_prompt = """<detect-sentence-boundaries> I walked down to the river. Then I went to the
I walked down to the river. <sentence-boundary>
----------
<detect-sentence-boundaries> Argos Translate is machine translation software. It is also
Argos Translate is machine translation software. <sentence-boundary>
----------
<detect-sentence-boundaries> Argos Translate is written in Python and uses OpenAI. It also supports
Argos Translate is written in Python and uses OpenAI. <sentence-boundary>
----------
"""

DETECT_SENTENCE_BOUNDARIES_TOKEN = "<detect-sentence-boundaries>"
SENTENCE_BOUNDARY_TOKEN = "<sentence-boundary>"
FEWSHOT_BOUNDARY_TOKEN = "-" * 10


def get_sbd_package() -> Package | None:
    packages = package.get_installed_packages()
    for pkg in packages:
        if pkg.type == "sbd":
            return pkg
    return None


def generate_fewshot_sbd_prompt(
    input_text: str, sentence_guess_length: int = 150
) -> str:
    sentence_guess = input_text[:sentence_guess_length]
    to_return = fewshot_prompt + "<detect-sentence-boundaries> " + sentence_guess
    info("generate_fewshot_sbd_prompt", to_return)
    return to_return


def parse_fewshot_response(response_text: str) -> str | None:
    response = response_text.split(FEWSHOT_BOUNDARY_TOKEN)
    info("parse_fewshot_response", response)
    if len(response) < 2:
        return None
    response = response[-2].split("\n")
    if len(response) < 2:
        return None
    return response[-1]


def process_seq2seq_sbd(input_text: str, sbd_translated_guess: str) -> int:
    sbd_translated_guess_index = sbd_translated_guess.find(SENTENCE_BOUNDARY_TOKEN)
    if sbd_translated_guess_index != -1:
        sbd_translated_guess = sbd_translated_guess[:sbd_translated_guess_index]
        info("sbd_translated_guess:", sbd_translated_guess)
        best_index = 0
        best_ratio = 0.0
        for i in range(len(input_text)):
            candidate_sentence = input_text[:i]
            sm = SequenceMatcher()
            sm.set_seqs(candidate_sentence, sbd_translated_guess)
            ratio = sm.ratio()
            if i == 0 or ratio > best_ratio:
                best_index = i
                best_ratio = ratio
        return best_index
    else:
        return -1


def detect_sentence(
    input_text: str, sbd_translation, sentence_guess_length: int = 150
) -> int:
    """Given input text, return the index after the end of the first sentence.

    Args:
        input_text: The text to detect the first sentence of.
        sbd_translation: An ITranslation for detecting sentences.
        sentence_guess_length: Estimated number of chars > than most sentences.

    Returns:
        The index of the character after the end of the sentence.
                -1 if not found.
    """
    # TODO: Cache
    sentence_guess = input_text[:sentence_guess_length]
    info("sentence_guess:", sentence_guess)
    sbd_translated_guess = sbd_translation.translate(
        DETECT_SENTENCE_BOUNDARIES_TOKEN + sentence_guess
    )
    return process_seq2seq_sbd(input_text, sbd_translated_guess)
