// Services/DeepLTranslator.cs
// DeepL translation API implementation.

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CADTransLite.Core.Interfaces;
using CADTransLite.Core.Models;

namespace CADTransLite.Core.Services;

/// <summary>
/// Translation provider using the DeepL API (Free and Pro).
/// </summary>
public sealed class DeepLTranslator : ITranslationApi
{
    /// <inheritdoc/>
    public string Name => "DeepL";

    private readonly HttpClient _httpClient;
    private readonly TranslationApiConfig _config;

    /// <summary>
    /// Creates a new DeepL translator with the given configuration.
    /// </summary>
    /// <param name="config">API configuration containing the DeepL authentication key.</param>
    public DeepLTranslator(TranslationApiConfig config)
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

        var results = await TranslateBatchAsync(
            new List<string> { text }, sourceLang, targetLang, cancellationToken);
        return results[0];
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

        if (string.IsNullOrWhiteSpace(_config.ApiKey))
            throw new InvalidOperationException("DeepL API Key 未配置。");

        // Determine endpoint: Free API keys end with ":fx"
        string baseUrl = _config.ApiKey.TrimEnd().EndsWith(":fx", StringComparison.OrdinalIgnoreCase)
            ? "https://api-free.deepl.com/v2/translate"
            : "https://api.deepl.com/v2/translate";

        var allTranslations = new List<string>(texts.Count);

        // DeepL supports up to 50 texts per request.
        const int batchSize = 50;

        for (int i = 0; i < texts.Count; i += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = texts.Skip(i).Take(batchSize).ToList();

            var requestBody = new Dictionary<string, object>
            {
                ["text"] = batch,
                ["source_lang"] = sourceLang,
                ["target_lang"] = targetLang,
            };

            string json = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", _config.ApiKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                ErrorLogger.Instance.Error("DeepL", $"HTTP {(int)response.StatusCode} — 请求: batch {i / batchSize + 1}, 响应: {ErrorLogger.Truncate(responseBody, 500)}");
                response.EnsureSuccessStatusCode();
            }

            using var doc = JsonDocument.Parse(responseBody);
            var translations = doc.RootElement.GetProperty("translations");

            foreach (var t in translations.EnumerateArray())
            {
                allTranslations.Add(t.GetProperty("text").GetString() ?? string.Empty);
            }
        }

        ErrorLogger.Instance.Info("DeepL", $"翻译成功 — 输入: {texts.Count} 条, 输出: {allTranslations.Count} 条");
        return allTranslations;
    }
}
