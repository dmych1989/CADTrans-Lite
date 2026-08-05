// Services/MicrosoftTranslator.cs
// Microsoft Translator API implementation.
// Supports batch translation (up to 100 items per request).

using System.Text;
using System.Text.Json;
using CADTransLite.Core.Interfaces;
using CADTransLite.Core.Models;

namespace CADTransLite.Core.Services;

/// <summary>
/// Translation provider using the Microsoft Translator API (v3.0).
/// Supports batch translation with up to 100 items per request.
/// </summary>
public sealed class MicrosoftTranslator : ITranslationApi
{
    /// <inheritdoc/>
    public string Name => "微软翻译";

    private readonly HttpClient _httpClient;
    private readonly TranslationApiConfig _config;
    private readonly SemaphoreSlim _semaphore = new(5);

    private const string BaseUrl = "https://api.cognitive.microsofttranslator.com/translate?api-version=3.0";

    /// <summary>
    /// Creates a new Microsoft Translator instance with the given configuration.
    /// </summary>
    /// <param name="config">
    /// API configuration where:
    /// <c>ApiKey</c> = Microsoft subscription key,
    /// <c>Region</c> = Azure region (e.g., "eastasia", "global").
    /// </param>
    public MicrosoftTranslator(TranslationApiConfig config)
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
            throw new InvalidOperationException("微软翻译 API Key 未配置。");

        var allTranslations = new List<string>(texts.Count);

        // Microsoft Translator supports up to 100 items per request.
        const int batchSize = 100;

        for (int i = 0; i < texts.Count; i += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = texts.Skip(i).Take(batchSize).ToList();

            // Build request body: [{"text":"hello"},{"text":"world"}]
            var requestBody = batch.Select(t => new { text = t }).ToArray();
            string json = JsonSerializer.Serialize(requestBody);

            string url = $"{BaseUrl}&from={Uri.EscapeDataString(sourceLang)}&to={Uri.EscapeDataString(targetLang)}";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Ocp-Apim-Subscription-Key", _config.ApiKey);

            // Add region header if configured.
            if (!string.IsNullOrWhiteSpace(_config.Region))
                request.Headers.Add("Ocp-Apim-Subscription-Region", _config.Region);

            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                ErrorLogger.Instance.Error("Microsoft", $"HTTP {(int)response.StatusCode} — batch {i / batchSize + 1}, 响应: {ErrorLogger.Truncate(responseBody)}");
                response.EnsureSuccessStatusCode();
            }

            // Parse response: [{"translations":[{"text":"你好","to":"zh-Hans"}]},...]
            using var doc = JsonDocument.Parse(responseBody);
            var rootArray = doc.RootElement.EnumerateArray();

            foreach (var item in rootArray)
            {
                var translations = item.GetProperty("translations");
                var firstTranslation = translations.EnumerateArray().First();
                string translatedText = firstTranslation.GetProperty("text").GetString() ?? string.Empty;
                allTranslations.Add(translatedText);
            }
        }

        ErrorLogger.Instance.Info("Microsoft", $"翻译成功 — 输入: {texts.Count} 条, 输出: {allTranslations.Count} 条");
        return allTranslations;
    }
}
