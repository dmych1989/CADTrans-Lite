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

    /// <summary>Maximum concurrent requests to DeepLX (reduced from 5 to 2 to avoid 429).</summary>
    private readonly SemaphoreSlim _semaphore = new(2);

    /// <summary>Minimum delay between individual requests in milliseconds.</summary>
    private const int PerRequestDelayMs = 300;

    /// <summary>Maximum number of retry attempts for transient errors (429, 5xx).</summary>
    private const int MaxRetries = 3;

    /// <summary>Base delay for exponential backoff on retry (milliseconds).</summary>
    private const int BaseRetryDelayMs = 1000;

    /// <summary>Timestamp of the last request, used to enforce per-request delay.</summary>
    private DateTime _lastRequestTime = DateTime.MinValue;

    /// <summary>
    /// Creates a new DeepLX translator with the given configuration.
    /// </summary>
    /// <param name="config">
    /// API configuration where <c>ApiKey</c> stores the DeepLX base URL
    /// (default: http://127.0.0.1:1188).
    /// </param>
    public DeepLXTranslator(TranslationApiConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _httpClient = new HttpClient();
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

            using (response)
            {
                string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                // Handle 429 Too Many Requests with retry
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    if (attempt < MaxRetries)
                    {
                        int delayMs = BaseRetryDelayMs * (1 << attempt); // 1s, 2s, 4s
                        ErrorLogger.Instance.Warn("DeepLX",
                            $"429 限流，第 {attempt + 1}/{MaxRetries} 次重试，等待 {delayMs}ms…");
                        await Task.Delay(delayMs, cancellationToken);
                        continue;
                    }

                    ErrorLogger.Instance.Error("DeepLX",
                        $"429 限流，已重试 {MaxRetries} 次仍失败。请降低并发或增大请求间隔。");
                    throw new InvalidOperationException(
                        $"DeepLX 返回 429 (请求过于频繁)，已重试 {MaxRetries} 次仍失败。\n\n" +
                        "建议：\n" +
                        "1. 减少一次翻译的文本数量\n" +
                        "2. 检查 DeepLX 服务端限流设置\n" +
                        "3. 等待几分钟后重试");
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
                        ErrorLogger.Instance.Info("DeepLX", "翻译成功");
                        return dataProperty.GetString() ?? string.Empty;
                    }

                    // Fallback: try to get as string anyway
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
    /// Ensures a minimum delay between consecutive requests to avoid rate limiting.
    /// This is enforced across all concurrent tasks via a shared timestamp.
    /// </summary>
    private async Task EnforcePerRequestDelayAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var elapsed = (int)(now - _lastRequestTime).TotalMilliseconds;
        var remaining = PerRequestDelayMs - elapsed;

        if (remaining > 0)
        {
            await Task.Delay(remaining, cancellationToken);
        }

        _lastRequestTime = DateTime.UtcNow;
    }
}
