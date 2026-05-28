// Services/CustomAiTranslator.cs
// Custom AI translation service (OpenAI-compatible API format).
// Supports any API that follows the OpenAI chat completions format.

using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CADTransLite.Core.Interfaces;
using CADTransLite.Core.Models;

namespace CADTransLite.Core.Services;

/// <summary>
/// Translation API using a custom OpenAI-compatible endpoint.
/// Supports any service that follows the OpenAI chat completions format,
/// including Azure OpenAI, local models, and third-party compatible APIs.
/// </summary>
public sealed class CustomAiTranslator : ITranslationApi
{
    private readonly TranslationApiSettings _settings;
    private readonly HttpClient _httpClient;

    public string Name => $"自定义AI ({_settings.ModelName})";

    public CustomAiTranslator(TranslationApiSettings settings)
    {
        _settings = settings;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization",
            $"Bearer {_settings.ApiKey}");
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    /// <inheritdoc/>
    public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var prompt = BuildPrompt(text, sourceLang, targetLang);

        var requestBody = new
        {
            model = _settings.ModelName,
            messages = new[] { new { role = "user", content = prompt } },
            temperature = 0.3,
            max_tokens = 4000,
        };

        var content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        try
        {
            var response = await _httpClient.PostAsync(
                $"{_settings.BaseUrl.TrimEnd('/')}/chat/completions",
                content,
                cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                ErrorLogger.Instance.Error("CustomAI", $"HTTP {(int)response.StatusCode} — Model: {_settings.ModelName}, 响应: {ErrorLogger.Truncate(responseBody)}");
                throw new HttpRequestException(
                    $"API 请求失败: {response.StatusCode}\n{responseBody}");
            }

            var result = JsonSerializer.Deserialize<OpenAiChatResponse>(responseBody);

            string translated = result?.Choices?[0]?.Message?.Content ?? text;

            // Clean up the response (remove quotes if the model wraps the result)
            translated = translated.Trim().TrimMatchingQuotes('"').Trim();

            ErrorLogger.Instance.Info("CustomAI", $"翻译成功 — Model: {_settings.ModelName}");
            return translated;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"自定义AI翻译请求失败：{ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<List<string>> TranslateBatchAsync(List<string> texts, string sourceLang, string targetLang, CancellationToken cancellationToken = default)
    {
        var results = new List<string>(texts.Count);
        foreach (var text in texts)
        {
            results.Add(await TranslateAsync(text, sourceLang, targetLang, cancellationToken));
        }
        return results;
    }

    /// <summary>
    /// Builds a translation prompt for the AI model.
    /// </summary>
    private static string BuildPrompt(string text, string sourceLang, string targetLang)
    {
        return $"Translate the following text from {sourceLang} to {targetLang}. " +
               "Only return the translated text, without any explanations, quotes, or formatting. " +
               "Preserve all line breaks (use \\n). " +
               $"\\n\\nText to translate:\\n{text}";
    }
}

/// <summary>
/// OpenAI-compatible chat completions response structure.
/// </summary>
file sealed class OpenAiChatResponse
{
    [JsonPropertyName("choices")]
    public List<OpenAiChoice>? Choices { get; set; }
}

file sealed class OpenAiChoice
{
    [JsonPropertyName("message")]
    public OpenAiMessage? Message { get; set; }
}

file sealed class OpenAiMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

/// <summary>
/// Extension methods for string trimming.
/// </summary>
file static partial class StringExtensions
{
    public static string TrimMatchingQuotes(this string input, char quote)
    {
        if (input.Length >= 2 &&
            input[0] == quote &&
            input[^1] == quote)
        {
            return input[1..^1];
        }
        return input;
    }
}
