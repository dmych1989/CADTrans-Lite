// Models/TranslationApiSettings.cs
// Configuration for all translation API providers.
// Supports Custom AI, DeepL, Baidu, Tencent, Microsoft Translator, and DeepLX.

namespace CADTransLite.Core.Models;

/// <summary>
/// Translation API configuration.
/// Supports custom AI models (OpenAI-compatible), DeepL, Baidu, Tencent, Microsoft Translator, and DeepLX.
/// </summary>
public sealed class TranslationApiSettings
{
    // ────────────────────────────────────────────────────────────────
    // Custom AI Model Settings
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether to use a custom AI model for translation.
    /// </summary>
    public bool EnableCustomAI { get; set; } = false;

    /// <summary>
    /// API key for the custom AI model.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Base URL for the AI API (OpenAI-compatible format).
    /// Default: https://api.openai.com/v1
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>
    /// Model name to use for translation.
    /// Examples: gpt-4o-mini, gpt-4o, deepseek-chat, claude-3-haiku
    /// </summary>
    public string ModelName { get; set; } = "gpt-4o-mini";

    // ────────────────────────────────────────────────────────────────
    // DeepL Translate API Settings
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether to use DeepL API for translation.
    /// </summary>
    public bool EnableDeepL { get; set; } = false;

    /// <summary>
    /// DeepL API authentication key.
    /// Free API keys end with ":fx", Pro keys do not.
    /// </summary>
    public string DeepLApiKey { get; set; } = string.Empty;

    // ────────────────────────────────────────────────────────────────
    // Baidu Translate API Settings
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether to use Baidu Translate API for translation.
    /// </summary>
    public bool EnableBaiduTranslate { get; set; } = false;

    /// <summary>
    /// Baidu Translate API App ID.
    /// Apply at: https://fanyi-api.baidu.com/
    /// </summary>
    public string BaiduAppId { get; set; } = string.Empty;

    /// <summary>
    /// Baidu Translate API App Key.
    /// </summary>
    public string BaiduAppKey { get; set; } = string.Empty;

    // ────────────────────────────────────────────────────────────────
    // Tencent Cloud TMT Settings
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether to use Tencent Cloud TMT for translation.
    /// </summary>
    public bool EnableTencentTranslate { get; set; } = false;

    /// <summary>
    /// Tencent Cloud SecretId.
    /// Apply at: https://console.cloud.tencent.com/tmt
    /// </summary>
    public string TencentSecretId { get; set; } = string.Empty;

    /// <summary>
    /// Tencent Cloud SecretKey.
    /// </summary>
    public string TencentSecretKey { get; set; } = string.Empty;

    // ────────────────────────────────────────────────────────────────
    // Microsoft Translator Settings
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether to use Microsoft Translator for translation.
    /// </summary>
    public bool EnableMicrosoftTranslate { get; set; } = false;

    /// <summary>
    /// Microsoft Translator API key (Ocp-Apim-Subscription-Key).
    /// Apply at: https://learn.microsoft.com/en-us/azure/ai-services/translator/
    /// </summary>
    public string MicrosoftApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Microsoft Translator region (Ocp-Apim-Subscription-Region).
    /// Examples: "eastasia", "global", "westeurope"
    /// </summary>
    public string MicrosoftRegion { get; set; } = string.Empty;

    // ────────────────────────────────────────────────────────────────
    // DeepLX (Local) Settings
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether to use DeepLX (local DeepL proxy) for translation.
    /// </summary>
    public bool EnableDeepLX { get; set; } = false;

    /// <summary>
    /// DeepLX service URL.
    /// Default: http://127.0.0.1:1188
    /// </summary>
    public string DeepLXUrl { get; set; } = "http://127.0.0.1:1188";
}
