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
    // packages on first launch can take well over a minute, so allow a generous
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
        var source = string.IsNullOrWhiteSpace(sourceLang) ? "auto" : sourceLang.ToLowerInvariant();
        var target = targetLang.ToLowerInvariant();

        await EnsureReadyAsync(source, target, cancellationToken).ConfigureAwait(false);

        var payload = new Dictionary<string, string>
        {
            ["q"] = text,
            ["source"] = source,
            ["target"] = target,
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
                    {
                        // 服务返回了空译文：可能是坏实例/语言包缺失。强制下次 EnsureReadyAsync 重新评估
                        // （若占用者非本会话，会清理并重启一个干净实例）。
                        _startedOnce = false;
                        throw new InvalidOperationException(
                            "Argos Translate 返回了空译文（引擎可能尚未就绪或语言包缺失）。请确认已通过 setup_engines.ps1 安装语言包。");
                    }
                    return translated ?? string.Empty;
                }
                throw new InvalidOperationException("Argos Translate 返回结果缺少 translatedText 字段。");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                // 翻译失败：重置就绪标记，下次 EnsureReadyAsync 会重新探测并在必要时重启坏服务。
                _startedOnce = false;
                await EnsureReadyAsync(source, target, cancellationToken).ConfigureAwait(false);
                await Task.Delay(300 * (attempt + 1), cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException(
            $"Argos Translate 本地服务调用失败：{lastError?.Message}。请确认 tools/py 已通过 setup_engines.ps1 安装，且 python 服务已启动。");
    }

    private async Task EnsureReadyAsync(string source, string target, CancellationToken cancellationToken)
    {
        if (_startedOnce) return;

        if (!LocalServerHelper.TryParseHostPort(_baseUrl, out var host, out var port))
            throw new InvalidOperationException($"无法解析 Argos 本地服务地址: {_baseUrl}");

        // 若端口未监听，或监听者不是本会话启动的进程（例如上次调试遗留的坏实例），
        // 都先清理占用再拉起一个干净的健康实例，避免“端口被占却连到坏服务/翻译失败”。
        if (!LocalServerHelper.IsPortOpen(host, port) || !LocalServerHelper.IsPortOwnedByUs(host, port))
        {
            if (LocalServerHelper.IsPortOpen(host, port))
            {
                LocalServerHelper.StopServerOnPort(host, port);
                await Task.Delay(800, cancellationToken).ConfigureAwait(false);
            }
            LocalServerHelper.TryStartBundledServer(
                PythonExeName,
                new[] { ArgosScriptName, "--port", port.ToString() },
                ArgosSubDir);
        }

        // 探测“真实翻译方向”是否可用，而不是只测 en->zh。
        // 之前只测 en->zh 会误判就绪：en->zh 正常但 zh->en（MINISBD 回归）损坏时，
        // 就绪探针通过、真实翻译却返回空译文/报错。改为探测用户真实方向（zh->en 等）。
        // 加载 38 个语言包首次启动可能耗时超过一分钟，故放宽轮询窗口。
        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        string probeText = (target == "en" && (source == "zh" || source == "auto")) ? "测试文本" : "test";

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(MaxReadyWaitSeconds);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await ProbeDirectionAsync(probe, source, target, probeText, cancellationToken).ConfigureAwait(false))
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

    /// <summary>
    /// 对指定方向发一次极小翻译探测；非空译文即视为该方向可用。
    /// 用于就绪判定，确保我们真正能翻译用户需要的方向（而非仅 en->zh）。
    /// </summary>
    private async Task<bool> ProbeDirectionAsync(HttpClient probe, string source, string target, string probeText, CancellationToken cancellationToken)
    {
        try
        {
            var body = new Dictionary<string, string>
            {
                ["q"] = probeText,
                ["source"] = source,
                ["target"] = target,
                ["format"] = "text",
            };
            using var resp = await probe.PostAsJsonAsync($"{_baseUrl}/translate", body, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return false;
            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return doc.RootElement.TryGetProperty("translatedText", out var t)
                && !string.IsNullOrWhiteSpace(t.GetString());
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
