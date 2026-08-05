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
                    $"[HTTP {(int)response.StatusCode}] {BuildStatusHint(response.StatusCode, responseBody)}\n响应体: {responseBody}");
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
            string networkHint = BuildNetworkHint(ex);
            throw new InvalidOperationException(
                $"自定义AI翻译请求失败：{ex.Message}{networkHint}", ex);
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
    /// 调用 OpenAI 兼容的 /models 接口，获取可用模型 id 列表。
    /// 兼容标准格式 { "data": [ { "id": "..." }, ... ] }，也兼容直接返回数组的接口。
    /// </summary>
    public static async Task<List<string>> ListModelsAsync(
        string apiKey, string baseUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Base URL 未配置");

        var url = $"{baseUrl.TrimEnd('/')}/models";
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        if (!string.IsNullOrWhiteSpace(apiKey))
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        var response = await client.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"API 请求失败: {response.StatusCode}\n{body}");

        var models = new List<string>();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in dataEl.EnumerateArray())
                if (item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                    models.Add(idEl.GetString()!);
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
                if (item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                    models.Add(idEl.GetString()!);
        }

        models.Sort();
        return models;
    }

    /// <summary>
    /// 根据 HTTP 状态码（及响应体）给出排查提示（用于把 404/401/429/5xx 等翻译成人话）。
    /// body 用于识别“5xx 包裹鉴权错误”这类转发网关（one-api 等）的典型报错。
    /// </summary>
    private static string BuildStatusHint(System.Net.HttpStatusCode statusCode, string body = "")
    {
        bool bodySuggestsAuth = body.Contains("NoAuth", StringComparison.OrdinalIgnoreCase)
            || body.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
            || body.Contains("invalid api", StringComparison.OrdinalIgnoreCase)
            || body.Contains("鉴权", StringComparison.OrdinalIgnoreCase)
            || body.Contains("11200");

        return statusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                => "API Key 无效、缺失或未被该接口授权（鉴权失败，请检查 API Key 与接口要求）",
            System.Net.HttpStatusCode.NotFound
                => "模型名称不存在或接口路径错误（请用「获取模型列表」核对正确的模型名，并确认 Base URL 指向 /v1 这类接口基地址）",
            System.Net.HttpStatusCode.BadRequest
                => "请求参数错误（通常是模型名不被接受或消息格式不符）",
            (System.Net.HttpStatusCode)429
                => "请求过于频繁，触发限流（请稍后重试，或更换不限流的接口）",
            >= System.Net.HttpStatusCode.InternalServerError
                => bodySuggestsAuth
                    ? "服务端返回 5xx，且响应体提示鉴权失败（如 one-api/转发网关的 AppldNoAuthError、错误码 11200）：通常是 API Key 错误或该渠道未授权，请核对 Key 与渠道配置"
                    : "服务端内部错误（接口方故障，或返回的不是 OpenAI 兼容格式）",
            _ => "接口返回了非成功状态码",
        };
    }

    /// <summary>
    /// 根据连接层异常（无法连通 / DNS / 超时）给出排查提示。
    /// </summary>
    private static string BuildNetworkHint(HttpRequestException ex)
    {
        var msg = (ex.InnerException?.Message ?? ex.Message);
        if (msg.Contains("actively refused") || msg.Contains("refused", StringComparison.OrdinalIgnoreCase))
            return "（目标地址拒绝连接：请检查 Base URL 是否正确、服务是否已启动）";
        if (msg.Contains("resolve", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("No such host", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("name or service", StringComparison.OrdinalIgnoreCase))
            return "（无法解析主机：请检查 Base URL 域名是否正确、网络是否可达）";
        if (msg.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return "（连接/读取超时：服务响应过慢或地址有误）";
        return "";
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
