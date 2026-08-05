// Services/ArgosTranslateTranslator.cs
// Argos Translate local engine adapter.
using CADTransLite.Core.Interfaces;
using CADTransLite.Core.Models;

//
// Argos Translate is a Python neural machine translation library (the engine that powers
// LibreTranslate). Because the host app is .NET/WPF, we run Argos as a small local HTTP server
// (`tools/py/argos_server.py`, a pure-stdlib wrapper around `argos_translate`) and call it
// over HTTP — exactly like the other local engines. The bundled `python.exe` (embeddable
// Python) lives in `tools/py` and is auto-started by this adapter when the port is closed.
//
// The wire contract matches LibreTranslate: `POST /translate` with { q, source, target, format }
// and a `translatedText` response, so the same HTTP client logic is reused.

using System.Net.Http.Json;
using System.Text.Json;

namespace CADTransLite.Core.Services;

/// <summary>
/// Translation API implementation backed by a local Argos Translate server (python wrapper).
/// Requires <see cref="TranslationApiConfig.BaseUrl"/> pointing at the running server
/// (default http://127.0.0.1:5001).
/// </summary>
public sealed class ArgosTranslateTranslator : ITranslationApi
{
    public string Name => "Argos Translate (本地)";

    private const string DefaultUrl = "http://127.0.0.1:5001";
    private const string PythonExeName = "python.exe";
    private const string ArgosSubDir = "tools/py";
    private const string ArgosScriptName = "argos_server.py";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // How long to keep polling for the local engine to become ready. Loading 38 Argos language
    // packages + MiniSBD models on first launch can take well over a minute, so allow a generous
    // window instead of giving up after a few seconds and sending text into a still-warming-up server.
    private const int MaxReadyWaitSeconds = 150;
    private const int ReadyPollIntervalMs = 1000;

    private readonly string _baseUrl;
    private readonly HttpClient _http;
    private bool _startedOnce;

    public ArgosTranslateTranslator(TranslationApiConfig config)
    {
        _baseUrl = string.IsNullOrWhiteSpace(config.BaseUrl) ? DefaultUrl : config.BaseUrl.TrimEnd('/');
        // Local CPU inference is slow; give requests a generous timeout.
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(600) };
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
                {
                    var translated = t.GetString();
                    if (!string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(translated))
                        throw new InvalidOperationException(
                            "Argos Translate 返回了空译文（引擎可能尚未就绪或语言包缺失）。请确认已通过 setup_engines.ps1 安装语言包。");
                    return translated ?? string.Empty;
                }
                throw new InvalidOperationException("Argos Translate 返回结果缺少 translatedText 字段。");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(300 * (attempt + 1), cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException(
            $"Argos Translate 本地服务调用失败：{lastError?.Message}。请确认 tools/py 已通过 setup_engines.ps1 安装，且 python 服务已启动。");
    }

    private async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (_startedOnce) return;

        if (!LocalServerHelper.TryParseHostPort(_baseUrl, out var host, out var port))
            throw new InvalidOperationException($"无法解析 Argos 本地服务地址: {_baseUrl}");

        // If nothing is listening yet, launch the embedded python that hosts argos_server.py.
        if (!LocalServerHelper.IsPortOpen(host, port))
        {
            LocalServerHelper.TryStartBundledServer(
                PythonExeName,
                new[] { ArgosScriptName, "--port", port.ToString() },
                ArgosSubDir);
        }

        // Wait until the server can actually translate. Loading 38 language packages + MiniSBD
        // models on first launch can take well over a minute, so poll generously and treat an
        // empty result / error as "still warming up" instead of giving up early.
        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var probeBody = new Dictionary<string, string> { ["q"] = "hi", ["source"] = "en", ["target"] = "zh", ["format"] = "text" };

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(MaxReadyWaitSeconds);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                bool isReady = false;

                // Prefer the lightweight /ready endpoint; fall back to a real probe translation.
                using (var readyResp = await probe.GetAsync($"{_baseUrl}/ready", cancellationToken).ConfigureAwait(false))
                {
                    if (readyResp.IsSuccessStatusCode && await TryReadReadyFlagAsync(readyResp, cancellationToken).ConfigureAwait(false))
                        isReady = true;
                }

                if (!isReady)
                {
                    using var resp = await probe.PostAsJsonAsync($"{_baseUrl}/translate", probeBody, JsonOptions, cancellationToken)
                        .ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken)
                            .ConfigureAwait(false);
                        if (doc.RootElement.TryGetProperty("translatedText", out var t)
                            && !string.IsNullOrWhiteSpace(t.GetString()))
                        {
                            isReady = true;
                        }
                    }
                }

                if (isReady)
                {
                    _startedOnce = true;
                    return;
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

    private static async Task<bool> TryReadReadyFlagAsync(HttpResponseMessage resp, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return doc.RootElement.TryGetProperty("ready", out var r) && r.ValueKind == JsonValueKind.True;
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
        foreach (var text in texts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await TranslateAsync(text, sourceLang, targetLang, cancellationToken).ConfigureAwait(false));
        }
        return results;
    }
}
