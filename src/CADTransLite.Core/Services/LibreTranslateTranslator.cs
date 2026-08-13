// Services/LibreTranslateTranslator.cs
// LibreTranslate local engine adapter.
using CADTransLite.Core.Interfaces;
using CADTransLite.Core.Models;

// LibreTranslate is a free/open-source machine translation API that runs fully offline once its
// language models are installed. It exposes an HTTP endpoint compatible with the simple
// `POST /translate` contract, so this adapter shares the same shape as DeepLXTranslator:
//   - it talks to a local HTTP server,
//   - it auto-starts the bundled Python service (`python -m libretranslate`) if the port is not
//     already open, using the embedded interpreter in tools/py,
//   - all traffic stays on 127.0.0.1, so no key / cloud round-trip is involved.
//
// Note: LibreTranslate has no official Windows .exe — on Windows it runs as a Python module. The
// bundled embedded Python lives at tools/py, shared with the Argos engine.

using System.Net.Http.Json;
using System.Text.Json;

namespace CADTransLite.Core.Services;

/// <summary>
/// Translation API implementation backed by a local LibreTranslate server.
/// Requires <see cref="TranslationApiConfig.BaseUrl"/> pointing at the running server
/// (default http://127.0.0.1:5000).
/// </summary>
public sealed class LibreTranslateTranslator : ITranslationApi
{
    public string Name => "LibreTranslate (本地)";

    private const string DefaultUrl = "http://127.0.0.1:5000";
    private const string PythonExeName = "python.exe";
    private const string PyRuntimeSubDir = "tools/py";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // How long to keep polling for the local engine to become ready. LibreTranslate loads its
    // language models on first launch and can take well over a minute, so allow a generous window
    // instead of giving up early and sending text into a still-warming-up server.
    private const int MaxReadyWaitSeconds = 150;
    private const int ReadyPollIntervalMs = 1000;

    private readonly string _baseUrl;
    private readonly HttpClient _http;
    private bool _startedOnce;

    public LibreTranslateTranslator(TranslationApiConfig config)
    {
        _baseUrl = string.IsNullOrWhiteSpace(config.BaseUrl) ? DefaultUrl : config.BaseUrl.TrimEnd('/');
        // Local CPU inference is slow (a Chinese sentence can take a minute or more), so give the
        // request a generous timeout instead of the default 120s which would cut translations off.
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(600) };
    }

    public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken cancellationToken = default)
    {
        // Make sure the bundled local server is up (best-effort, only once per instance).
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);

        var payload = new Dictionary<string, string>
        {
            ["q"] = text,
            ["source"] = string.IsNullOrWhiteSpace(sourceLang) ? "auto" : sourceLang.ToLowerInvariant(),
            ["target"] = targetLang.ToLowerInvariant(),
            ["format"] = "text",
        };

        // LibreTranslate is fully local: no upstream rate-limit, so a couple of quick retries for
        // transient connection issues (e.g. server still warming up) is enough.
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
                {
                    var translated = t.GetString();
                    // Surface empty translations as a clear error instead of silently writing
                    // blank cells into the Excel file.
                    if (!string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(translated))
                        throw new InvalidOperationException(
                            "LibreTranslate 返回了空译文（引擎可能尚未就绪或语言包缺失）。请确认已通过 setup_engines.ps1 安装语言包。");
                    return translated ?? string.Empty;
                }
                throw new InvalidOperationException("LibreTranslate 返回结果缺少 translatedText 字段。");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(300 * (attempt + 1), cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException(
            $"LibreTranslate 本地服务调用失败：{lastError?.Message}。请确认已运行 setup_engines.ps1 安装 libretranslate 且服务可访问 {_baseUrl}。");
    }

    private async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (_startedOnce) return;

        if (!LocalServerHelper.TryParseHostPort(_baseUrl, out var host, out var port))
            throw new InvalidOperationException($"无法解析 LibreTranslate 本地服务地址: {_baseUrl}");

        // If nothing is listening yet, launch the embedded python that hosts the libretranslate module.
        if (!LocalServerHelper.IsPortOpen(host, port))
        {
            LocalServerHelper.TryStartBundledServer(
                PythonExeName,
                new[] { "libretranslate_server.py", "--host", "127.0.0.1", "--port", port.ToString() },
                PyRuntimeSubDir);
        }

        // Wait until the server can actually translate. Loading language models on first launch can
        // take well over a minute, so poll generously and treat an empty result / error as
        // "still warming up" instead of giving up early.
        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var probeBody = new Dictionary<string, string> { ["q"] = "hi", ["source"] = "en", ["target"] = "zh", ["format"] = "text" };

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(MaxReadyWaitSeconds);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var resp = await probe.PostAsJsonAsync($"{_baseUrl}/translate", probeBody, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    if (doc.RootElement.TryGetProperty("translatedText", out var t) && !string.IsNullOrWhiteSpace(t.GetString()))
                    {
                        _startedOnce = true;
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // server still warming up or not responding yet — keep polling
            }

            await Task.Delay(ReadyPollIntervalMs, cancellationToken).ConfigureAwait(false);
        }

        // Timed out waiting for readiness. Mark as started so we don't loop forever; the real
        // translation request will surface a clear error if the server is genuinely broken.
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
