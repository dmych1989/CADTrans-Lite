// Services/TencentTranslator.cs
// Tencent Cloud TMT (Machine Translation) API implementation.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CADTransLite.Core.Interfaces;
using CADTransLite.Core.Models;

namespace CADTransLite.Core.Services;

/// <summary>
/// Translation provider using the Tencent Cloud TMT API (TextTranslate).
/// Uses TC3-HMAC-SHA256 signature method.
/// </summary>
public sealed class TencentTranslator : ITranslationApi
{
    /// <inheritdoc/>
    public string Name => "腾讯翻译";

    private readonly HttpClient _httpClient;
    private readonly TranslationApiConfig _config;

    private const string Service = "tmt";
    private const string Host = "tmt.tencentcloudapi.com";
    private const string Action = "TextTranslate";
    private const string Version = "2018-03-21";

    /// <summary>
    /// Creates a new Tencent translator with the given configuration.
    /// </summary>
    /// <param name="config">API configuration containing AppId (SecretId) and SecretKey.</param>
    public TencentTranslator(TranslationApiConfig config)
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
            throw new InvalidOperationException("腾讯翻译 SecretId 或 SecretKey 未配置。");

        // Tencent TMT TextTranslate supports single text at a time.
        // Use concurrency for batch translation.
        var results = new string[texts.Count];
        var semaphore = new SemaphoreSlim(5);

        var tasks = texts.Select(async (text, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                results[index] = await TranslateSingleAsync(text, sourceLang, targetLang, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results.ToList();
    }

    /// <summary>
    /// Translates a single text using the Tencent Cloud TMT TextTranslate API.
    /// </summary>
    private async Task<string> TranslateSingleAsync(
        string text,
        string sourceLang,
        string targetLang,
        CancellationToken cancellationToken = default)
    {
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string date = DateTime.UtcNow.ToString("yyyy-MM-dd");

        // Build request body.
        var bodyObj = new Dictionary<string, object>
        {
            ["SourceText"] = text,
            ["Source"] = sourceLang,
            ["Target"] = targetLang,
            ["ProjectId"] = 0,
        };
        string payload = JsonSerializer.Serialize(bodyObj);

        // Build canonical request.
        string contentType = "application/json; charset=utf-8";
        string hashedPayload = Sha256Hex(payload);

        string canonicalHeaders = $"content-type:{contentType}\nhost:{Host}\n";
        string signedHeaders = "content-type;host";
        string canonicalRequest = $"POST\n/\n\n{canonicalHeaders}\n{signedHeaders}\n{hashedPayload}";

        // Build string to sign.
        string credentialScope = $"{date}/{Service}/tc3_request";
        string hashedCanonicalRequest = Sha256Hex(canonicalRequest);
        string stringToSign = $"TC3-HMAC-SHA256\n{timestamp}\n{credentialScope}\n{hashedCanonicalRequest}";

        // Calculate signature.
        byte[] secretDate = HmacSha256(Encoding.UTF8.GetBytes($"TC3{_config.SecretKey}"), date);
        byte[] secretService = HmacSha256(secretDate, Service);
        byte[] secretSigning = HmacSha256(secretService, "tc3_request");
        string signature = HmacSha256Hex(secretSigning, stringToSign);

        // Build authorization header.
        string authorization = $"TC3-HMAC-SHA256 Credential={_config.AppId}/{credentialScope}, " +
                               $"SignedHeaders={signedHeaders}, Signature={signature}";

        // Send request.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{Host}/");
        request.Headers.Add("Authorization", authorization);
        request.Headers.Add("X-TC-Action", Action);
        request.Headers.Add("X-TC-Version", Version);
        request.Headers.Add("X-TC-Timestamp", timestamp);
        request.Headers.Add("X-TC-Region", "ap-beijing");
        request.Headers.Add("Host", Host);
        request.Content = new StringContent(payload, Encoding.UTF8, contentType);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            ErrorLogger.Instance.Error("Tencent", $"HTTP {(int)response.StatusCode} — 响应: {ErrorLogger.Truncate(responseBody)}");
            response.EnsureSuccessStatusCode();
        }

        using var doc = JsonDocument.Parse(responseBody);

        // Check for API error.
        if (doc.RootElement.TryGetProperty("Response", out var resp))
        {
            if (resp.TryGetProperty("Error", out var error))
            {
                string code = error.TryGetProperty("Code", out var c) ? c.GetString() ?? "" : "";
                string message = error.TryGetProperty("Message", out var m) ? m.GetString() ?? "" : "";
                throw new InvalidOperationException($"腾讯翻译 API 错误 ({code}): {message}");
            }

            if (resp.TryGetProperty("TargetText", out var targetText))
            {
                ErrorLogger.Instance.Info("Tencent", "翻译成功");
                return targetText.GetString() ?? string.Empty;
            }
        }

        throw new InvalidOperationException("腾讯翻译 API 返回了意外的响应格式。");
    }

    // ────────────────────────────────────────────────────────────────
    // Crypto helpers
    // ────────────────────────────────────────────────────────────────

    private static string Sha256Hex(string data) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();

    private static byte[] HmacSha256(byte[] key, string data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    private static string HmacSha256Hex(byte[] key, string data)
    {
        byte[] hash = HmacSha256(key, data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
