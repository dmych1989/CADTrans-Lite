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
// bundled embedded Python lives at tools/py (shared argostranslate packages).

using System.Collections.Concurrent;
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
    // 按 URL 串行化启动，避免并发调用各自拉起一个服务（竞态导致多进程抢端口、
    // 被 ShouldStartFreshServer 误判“非本会话占用”而反复杀掉重启，造成“测试失败”）。
    // 与 Argos/NLLB 适配器保持一致。
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _startGates = new(StringComparer.OrdinalIgnoreCase);

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
                {
                    // 服务返回了空译文：可能是坏实例/语言包缺失。强制下次 EnsureReadyAsync 重新评估
                    // （若占用者非本会话，会清理并重启一个干净实例）。
                    _startedOnce = false;
                    throw new InvalidOperationException(
                        "LibreTranslate 返回了空译文（引擎可能尚未就绪或语言包缺失）。请确认已通过 setup_engines.ps1 安装语言包。");
                }
                    return translated ?? string.Empty;
                }
                throw new InvalidOperationException("LibreTranslate 返回结果缺少 translatedText 字段。");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                // 翻译失败：重置就绪标记，下次 EnsureReadyAsync 会重新探测并在必要时重启坏服务。
                _startedOnce = false;
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

        // 串行化：同一 URL 只容许一个调用进入启动流程，杜绝并发竞态导致多个
        // LibreTranslate 进程同时拉起、互相抢端口而崩溃，造成“测试失败”。
        var gate = _startGates.GetOrAdd(_baseUrl, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_startedOnce) return;

            if (!LocalServerHelper.TryParseHostPort(_baseUrl, out var host, out var port))
                throw new InvalidOperationException($"无法解析 LibreTranslate 本地服务地址: {_baseUrl}");

            // 与 NLLB 保持一致：使用 ShouldStartFreshServer 决策，清理“非本会话”占端口的
            // 残留坏实例（上次调试遗留的 python 进程会坑本地引擎，使其连到半坏服务而“测试失败”），
            // 再拉起干净的 LibreTranslate 实例，并等待端口真正可服务。
            if (LocalServerHelper.ShouldStartFreshServer(host, port))
            {
                // 若上面清理了非本会话的占用者，等端口真正释放再启动，避免新实例绑定冲突。
                var closeDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
                while (DateTime.UtcNow < closeDeadline && LocalServerHelper.IsPortOpen(host, port))
                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);

                // 最多尝试启动 2 次：若新实例因端口尚未完全释放而绑定失败，清理后重试一次。
                for (int startTry = 0; startTry < 2; startTry++)
                {
                    LocalServerHelper.TryStartBundledServer(
                        PythonExeName,
                        new[] { "libretranslate_server.py", "--host", "127.0.0.1", "--port", port.ToString() },
                        PyRuntimeSubDir);

                    // 仅等待端口开放（确认 python 进程已起来并开始监听），最多 30s；
                    // 模型冷加载由真实请求的长超时覆盖，不在此等待。
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
            }

            _startedOnce = true;
        }
        finally
        {
            gate.Release();
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
