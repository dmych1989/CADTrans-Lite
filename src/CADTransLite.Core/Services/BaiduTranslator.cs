// Services/BaiduTranslator.cs
// Baidu Translate API implementation.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CADTransLite.Core.Interfaces;
using CADTransLite.Core.Models;

namespace CADTransLite.Core.Services;

/// <summary>
/// Translation provider using the Baidu Translate API.
/// Note: Baidu does not support batch translation; texts are translated one-by-one with concurrency.
/// </summary>
public sealed class BaiduTranslator : ITranslationApi
{
    /// <inheritdoc/>
    public string Name => "百度翻译";

    private readonly HttpClient _httpClient;
    private readonly TranslationApiConfig _config;

    /// <summary>
    /// Creates a new Baidu translator with the given configuration.
    /// </summary>
    /// <param name="config">API configuration containing AppId and SecretKey.</param>
    public BaiduTranslator(TranslationApiConfig config)
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

        return await TranslateSingleAsync(text, sourceLang, targetLang, cancellationToken);
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

        if (string.IsNullOrWhiteSpace(_config.AppId) || string.IsNullOrWhiteSpace(_config.SecretKey))
            throw new InvalidOperationException("百度翻译 AppId 或 SecretKey 未配置。");

        // Baidu does not support batch — translate one by one with limited concurrency and rate limiting.
        var results = new string[texts.Count];
        var semaphore = new SemaphoreSlim(1); // 降低并发到1，避免54003错误
        
        var tasks = new List<Task>();
        for (int i = 0; i < texts.Count; i++)
        {
            int index = i; // capture loop variable
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    // 每个请求之间添加延迟，避免触发访问频率限制
                    if (index > 0)
                    {
                        await Task.Delay(300, cancellationToken);
                    }
                    
                    cancellationToken.ThrowIfCancellationRequested();
                    results[index] = await TranslateSingleAsync(texts[index], sourceLang, targetLang, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);
        return results.ToList();
    }

    /// <summary>
    /// Translates a single text using Baidu Translate API.
    /// Retries on rate-limit errors (54003) with exponential backoff.
    /// </summary>
    private async Task<string> TranslateSingleAsync(
        string text,
        string sourceLang,
        string targetLang,
        CancellationToken cancellationToken = default)
    {
        const int maxRetries = 3;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string salt = Guid.NewGuid().ToString("N")[..16];
            string sign = ComputeSign(_config.AppId, text, salt, _config.SecretKey);

            var queryParams = new Dictionary<string, string>
            {
                ["q"] = text,
                ["from"] = sourceLang,
                ["to"] = targetLang,
                ["appid"] = _config.AppId,
                ["salt"] = salt,
                ["sign"] = sign,
            };

            string url = "https://fanyi-api.baidu.com/api/trans/vip/translate?"
                         + string.Join("&", queryParams.Select(kv =>
                             $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

            using var response = await _httpClient.GetAsync(url, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                ErrorLogger.Instance.Error("Baidu", $"HTTP {(int)response.StatusCode} — 响应: {ErrorLogger.Truncate(responseBody)}");
                response.EnsureSuccessStatusCode();
            }
            using var doc = JsonDocument.Parse(responseBody);

            // Check for API error.
            if (doc.RootElement.TryGetProperty("error_code", out var errorCode))
            {
                string errorMsg = doc.RootElement.TryGetProperty("error_msg", out var errorMsgEl)
                    ? errorMsgEl.GetString() ?? "Unknown error"
                    : "Unknown error";
                string code = errorCode.GetString() ?? "";

                // Rate limit (54003) — retry with backoff
                if (code == "54003" && attempt < maxRetries - 1)
                {
                    int delayMs = (int)Math.Pow(2, attempt + 1) * 1000; // 2s, 4s, 8s...
                    ErrorLogger.Instance.Error("Baidu", $"速率限制 (54003)，第 {attempt + 1} 次重试，等待 {delayMs}ms...");
                    await Task.Delay(delayMs, cancellationToken);
                    continue;
                }

                throw new InvalidOperationException($"百度翻译 API 错误 ({errorCode}): {errorMsg}");
            }

            ErrorLogger.Instance.Info("Baidu", "翻译成功");
            var result = doc.RootElement.GetProperty("trans_result");
            var firstEntry = result.EnumerateArray().First();
            return firstEntry.GetProperty("dst").GetString() ?? string.Empty;
        }

        throw new InvalidOperationException("百度翻译 API 错误 (54003): 重试多次后仍然超出访问频率限制，请稍后再试。");
    }

    /// <summary>
    /// Computes the Baidu API signature: MD5(appid + query + salt + secretKey).
    /// </summary>
    private static string ComputeSign(string appId, string query, string salt, string secretKey)
    {
        string raw = appId + query + salt + secretKey;
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
