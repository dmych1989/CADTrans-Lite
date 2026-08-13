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
// The wire contract matches LibreTranslate/Argos: `POST /translate` with { q, source, target, format }
// and a `translatedText` response, so the same HTTP client logic is reused.

using System.Net.Http.Json;
using System.Text.Json;

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

    public NllbTranslator(TranslationApiConfig config)
    {
        _baseUrl = string.IsNullOrWhiteSpace(config.BaseUrl) ? DefaultUrl : config.BaseUrl.TrimEnd('/');
        _http = new HttpClient { Timeout = RequestTimeout };
    }

    public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken cancellationToken = default)
    {
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
                using var resp = await _http.PostAsJsonAsync($"{_baseUrl}/translate", payload, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (doc.RootElement.TryGetProperty("translatedText", out var t))
                    return t.GetString() ?? string.Empty;
                if (doc.RootElement.TryGetProperty("error", out var err))
                    throw new InvalidOperationException($"NLLB 服务返回错误：{err.GetString()}");
                throw new InvalidOperationException("NLLB 本地服务返回结果缺少 translatedText 字段。");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
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

        if (!LocalServerHelper.TryParseHostPort(_baseUrl, out var host, out var port)
            || !LocalServerHelper.IsPortOpen(host, port))
        {
            // Launch the embedded python that hosts nllb_server.py, passing the listening port.
            LocalServerHelper.TryStartBundledServer(
                PythonExeName,
                new[] { NllbScriptName, "--port", port.ToString() },
                NllbSubDir);
            for (int i = 0; i < 10; i++)
            {
                if (LocalServerHelper.TryParseHostPort(_baseUrl, out host, out port)
                    && LocalServerHelper.IsPortOpen(host, port))
                    break;
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
        }

        _startedOnce = true;
    }

    public void Dispose() => _http.Dispose();

    /// <inheritdoc/>
    public async Task<List<string>> TranslateBatchAsync(
        List<string> texts, string sourceLang, string targetLang, CancellationToken cancellationToken = default)
    {
        if (texts is null || texts.Count == 0)
            throw new ArgumentException("Texts list must not be empty.", nameof(texts));

        var results = new List<string>(texts.Count);
        foreach (var text in texts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await TranslateAsync(text, sourceLang, targetLang, cancellationToken).ConfigureAwait(false));
        }
        return results;
    }
}
