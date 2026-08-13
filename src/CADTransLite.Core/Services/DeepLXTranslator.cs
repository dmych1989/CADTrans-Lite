// Services/DeepLXTranslator.cs
// DeepLX (local DeepL proxy) translation API implementation.
// No batch support — translates one by one with concurrency control.
// v2: Added 429 retry with exponential backoff, reduced concurrency, per-request delay.

using System.Text;
using System.Text.Json;
using CADTransLite.Core.Interfaces;
using CADTransLite.Core.Models;

namespace CADTransLite.Core.Services;

/// <summary>
/// Translation provider using a local DeepLX proxy.
/// DeepLX is an open-source DeepL free API proxy that runs locally.
/// No native batch support — translates one by one with concurrency control.
/// Implements automatic retry with exponential backoff for HTTP 429 (rate limit) errors.
/// </summary>
public sealed class DeepLXTranslator : ITranslationApi
{
    /// <inheritdoc/>
    public string Name => "DeepLX (本地)";

    private readonly HttpClient _httpClient;
    private readonly TranslationApiConfig _config;

    /// <summary>Maximum concurrent requests to DeepLX. Local free proxies are very rate-limited,
    /// so we serialize requests (1) to avoid 429; higher values risk immediate throttling.</summary>
    private readonly SemaphoreSlim _semaphore = new(1);

    /// <summary>Minimum delay between consecutive requests in milliseconds (serialized via lock).
    /// DeepL's free upstream is rate-limited to roughly 1 request / 3s per IP, so we default to 3000ms
    /// to stay under the limit proactively instead of relying purely on 429 retries.</summary>
    private const int PerRequestDelayMs = 3000;

    /// <summary>Maximum number of retry attempts for transient errors (429, 5xx).</summary>
    private const int MaxRetries = 8;

    /// <summary>Base delay for exponential backoff on retry (milliseconds). 429 typically needs a
    /// longer cool-down than a few seconds, so we start at 5s and grow.</summary>
    private const int BaseRetryDelayMs = 5000;

    /// <summary>Upper bound for a single retry backoff (ms) so a long Retry-After can't block forever.</summary>
    private const int MaxRetryDelayMs = 120000;

    /// <summary>After this many consecutive 429s we enter an extended cool-down to avoid burning
    /// the whole retry budget on a still-throttled server.</summary>
    private const int Consecutive429CooldownThreshold = 3;

    /// <summary>Extended cool-down (ms) applied once <see cref="Consecutive429CooldownThreshold"/> is hit.</summary>
    private const int ExtendedCooldownMs = 60000;

    /// <summary>Lock object guarding the shared request-timing state below.</summary>
    private readonly object _timingLock = new();

    /// <summary>Timestamp (Utc) of the last request, guarded by <see cref="_timingLock"/>.</summary>
    private DateTime _lastRequestTime = DateTime.MinValue;

    /// <summary>Running count of consecutive 429 responses, reset on any success or non-429 error.</summary>
    private int _consecutive429Count = 0;

    /// <summary>Whether we have already probed/started the local server for this instance.</summary>
    private int _serverEnsureAttempted = 0;

    /// <summary>
    /// Creates a new DeepLX translator with the given configuration.
    /// </summary>
    /// <param name="config">
    /// API configuration where <c>BaseUrl</c> stores the DeepLX base URL
    /// (default: http://127.0.0.1:1188). <c>ApiKey</c> is unused (DeepLX is token-free).
    /// </param>
    public DeepLXTranslator(TranslationApiConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _httpClient = new HttpClient
        {
            // Local server; fail fast (15s) instead of hanging on the default 100s timeout.
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    /// <inheritdoc/>
    public async Task<string> TranslateAsync(
        string text,
        string sourceLang,
        string targetLang,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text must not be empty.", nameof(text));

        string baseUrl = string.IsNullOrWhiteSpace(_config.BaseUrl)
            ? "http://127.0.0.1:1188"
            : _config.BaseUrl.TrimEnd('/');

        // Lazily ensure the local DeepLX server is running (auto-start bundled exe on first use).
        EnsureServerRunning(baseUrl);

        var requestBody = new
        {
            text,
            source_lang = sourceLang.ToUpperInvariant(),
            target_lang = targetLang.ToUpperInvariant(),
        };

        string json = JsonSerializer.Serialize(requestBody);

        // Retry loop for transient errors (429, 5xx)
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Enforce per-request delay across all concurrent tasks
            await EnforcePerRequestDelayAsync(cancellationToken);

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            string url = $"{baseUrl}/translate";

            HttpResponseMessage? response = null;
            try
            {
                response = await _httpClient.PostAsync(url, content, cancellationToken);
            }
            catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
            {
                throw new InvalidOperationException(
                    $"DeepLX 服务未运行或无法连接（{baseUrl}）。请确保 DeepLX 已启动。\n\n" +
                    "安装 DeepLX：https://github.com/OwO-Network/DeepLX\n" +
                    $"或修改 URL 为可用的 DeepLX 服务地址。", ex);
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException(
                    $"DeepLX 连接失败：{ex.Message}（{baseUrl}）。请确认 DeepLX 服务是否可以访问。", ex);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Thrown when HttpClient.Timeout (15s) elapses — this is a connection/response
                // timeout, NOT a user cancellation, so surface it as a clear failure instead of
                // letting it bubble up as "test cancelled".
                throw new InvalidOperationException(
                    $"DeepLX 连接超时（{baseUrl}）：服务未在 15 秒内响应。\n\n" +
                    "请确认 DeepLX 服务已启动且地址正确。\n" +
                    "安装 DeepLX：https://github.com/OwO-Network/DeepLX");
            }

            using (response)
            {
                string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                // Handle 429 Too Many Requests with retry
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    _consecutive429Count++;

                    // When throttling persists, switch to an extended cool-down so we don't
                    // burn every retry on a server that is still rate-limited.
                    if (_consecutive429Count >= Consecutive429CooldownThreshold && attempt < MaxRetries)
                    {
                        ErrorLogger.Instance.Warn("DeepLX",
                            $"连续 {_consecutive429Count} 次 429，进入延长冷却 {ExtendedCooldownMs}ms…");
                        await Task.Delay(ExtendedCooldownMs, cancellationToken);
                        continue;
                    }

                    if (attempt < MaxRetries)
                    {
                        // Respect a Retry-After header if present, else exponential backoff.
                        int delayMs = BaseRetryDelayMs * (1 << attempt); // 5s, 10s, 20s, 40s, 80s, 160s…
                        if (response.Headers.RetryAfter is not null)
                        {
                            var ra = response.Headers.RetryAfter.Delta;
                            if (ra is not null)
                                delayMs = (int)ra.Value.TotalMilliseconds;
                            else if (response.Headers.RetryAfter.Date is not null)
                            {
                                var after = (int)(response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow).TotalMilliseconds;
                                if (after > 0) delayMs = after;
                            }
                        }
                        if (delayMs > MaxRetryDelayMs) delayMs = MaxRetryDelayMs;

                        ErrorLogger.Instance.Warn("DeepLX",
                            $"429 限流，第 {attempt + 1}/{MaxRetries} 次重试，等待 {delayMs}ms…");
                        await Task.Delay(delayMs, cancellationToken);
                        continue;
                    }

                    ErrorLogger.Instance.Error("DeepLX",
                        $"429 限流，已重试 {MaxRetries} 次仍失败。");
                    throw new InvalidOperationException(
                        $"DeepLX 返回 429 (请求过于频繁)，已重试 {MaxRetries} 次仍失败。\n\n" +
                        "DeepL 免费接口对每 IP 有严格限速（约 1 请求/3 秒，且按分钟配额）。\n" +
                        "建议：\n" +
                        "1. 等待几分钟让配额恢复后再试\n" +
                        "2. 改用自定义 AI（如 DeepSeek/gpt-4o-mini）避免免费限流\n" +
                        "3. 换用 DeepL 官方付费 API Key");
                }

                // Handle 5xx server errors with retry
                if ((int)response.StatusCode >= 500 && (int)response.StatusCode < 600)
                {
                    if (attempt < MaxRetries)
                    {
                        int delayMs = BaseRetryDelayMs * (1 << attempt);
                        ErrorLogger.Instance.Warn("DeepLX",
                            $"服务器错误 {(int)response.StatusCode}，第 {attempt + 1}/{MaxRetries} 次重试，等待 {delayMs}ms…");
                        await Task.Delay(delayMs, cancellationToken);
                        continue;
                    }
                }

                if (!response.IsSuccessStatusCode)
                {
                    ErrorLogger.Instance.Error("DeepLX", $"HTTP {(int)response.StatusCode} — URL: {url}, 响应: {ErrorLogger.Truncate(responseBody)}");
                    response.EnsureSuccessStatusCode();
                }

                // Parse response: {"code":200,"data":"你好"} or {"code":200,"data":"你好","id":12345}
                using var doc = JsonDocument.Parse(responseBody);

                if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() != 200)
                {
                    string errorMsg = doc.RootElement.TryGetProperty("data", out var dataEl)
                        ? dataEl.GetString() ?? "Unknown error"
                        : "Unknown error";
                    throw new InvalidOperationException($"DeepLX API 错误 (code={codeEl.GetInt32()}): {errorMsg}");
                }

                if (doc.RootElement.TryGetProperty("data", out var dataProperty))
                {
                    // "data" can be either a string or a JsonElement depending on response format
                    if (dataProperty.ValueKind == JsonValueKind.String)
                    {
                        _consecutive429Count = 0; // reset on success
                        ErrorLogger.Instance.Info("DeepLX", "翻译成功");
                        return dataProperty.GetString() ?? string.Empty;
                    }

                    // Fallback: try to get as string anyway
                    _consecutive429Count = 0; // reset on success
                    return dataProperty.GetString() ?? string.Empty;
                }

                throw new InvalidOperationException("DeepLX API 返回了意外的响应格式。");
            }
        }

        // Should never reach here, but compiler needs it
        throw new InvalidOperationException("DeepLX 翻译在多次重试后失败。");
    }

    /// <inheritdoc/>
    public async Task<List<string>> TranslateBatchAsync(
        List<string> texts,
        string sourceLang,
        string targetLang,
        CancellationToken cancellationToken = default)
    {
        if (texts is null || texts.Count == 0)
            throw new ArgumentException("Texts list must not be empty.", nameof(texts));

        // DeepLX has no batch API — translate one by one with concurrency control.
        var results = new string[texts.Count];

        var tasks = texts.Select(async (text, index) =>
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                results[index] = await TranslateAsync(text, sourceLang, targetLang, cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results.ToList();
    }

    /// <summary>
    /// Probes the configured DeepLX endpoint and, if it is not reachable, attempts to auto-start
    /// the bundled local server executable so the user does not have to launch it manually.
    /// Runs at most once per translator instance.
    /// </summary>
    private void EnsureServerRunning(string baseUrl)
    {
        // Only attempt once (thread-safe-ish; worst case we try twice on first concurrent burst).
        if (Interlocked.Exchange(ref _serverEnsureAttempted, 1) != 0)
            return;

        if (!LocalServerHelper.TryParseHostPort(baseUrl, out var host, out var port))
            return;

        if (LocalServerHelper.IsPortOpen(host, port))
            return; // already running

        ErrorLogger.Instance.Warn("DeepLX",
            $"本地服务未运行（{baseUrl}），尝试自动启动 deeplx_windows_amd64.exe…");

        var proc = LocalServerHelper.TryStartBundledServer("deeplx_windows_amd64.exe");
        if (proc is null)
        {
            ErrorLogger.Instance.Warn("DeepLX", "未找到本地服务可执行文件，请手动启动 DeepLX。");
            return;
        }

        // Give the server a moment to bind the port (best-effort; translate will retry on failure).
        // Keep this short — the server is also auto-started at app launch, so it should be up already.
        for (int i = 0; i < 6; i++)
        {
            System.Threading.Thread.Sleep(500);
            if (LocalServerHelper.IsPortOpen(host, port))
            {
                ErrorLogger.Instance.Info("DeepLX", "本地服务已就绪。");
                return;
            }
        }
        ErrorLogger.Instance.Warn("DeepLX", "本地服务启动后端口仍未就绪，将继续尝试连接。");
    }

    /// <summary>
    /// Ensures a minimum delay between consecutive requests to avoid rate limiting.
    /// Serialized via <see cref="_timingLock"/> so concurrent tasks cannot both observe the
    /// same <see cref="_lastRequestTime"/> and fire simultaneously (which previously collapsed
    /// the interval to ~0ms and triggered 429).
    /// </summary>
    private Task EnforcePerRequestDelayAsync(CancellationToken cancellationToken)
    {
        int remaining;
        lock (_timingLock)
        {
            var now = DateTime.UtcNow;
            var elapsed = (int)(now - _lastRequestTime).TotalMilliseconds;
            remaining = PerRequestDelayMs - elapsed;
            if (remaining < 0) remaining = 0;

            // Reserve the slot immediately so the next caller waits from now.
            _lastRequestTime = now.AddMilliseconds(remaining);
        }

        return remaining > 0 ? Task.Delay(remaining, cancellationToken) : Task.CompletedTask;
    }
}
