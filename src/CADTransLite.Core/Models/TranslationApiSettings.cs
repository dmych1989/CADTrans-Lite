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

    // ────────────────────────────────────────────────────────────────
    // LibreTranslate (Local HTTP service) Settings
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether to use LibreTranslate (local HTTP service) for translation.
    /// </summary>
    public bool EnableLibreTranslate { get; set; } = false;

    /// <summary>
    /// LibreTranslate service URL.
    /// Default: http://127.0.0.1:5000
    /// </summary>
    public string LibreTranslateUrl { get; set; } = "http://127.0.0.1:5000";

    // ────────────────────────────────────────────────────────────────
    // NLLB (本地离线) Settings — 集成 RTranslator 的 NLLB-Distilled-600M 离线翻译模型
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether to use NLLB (本地离线) for translation.
    /// 离线神经机器翻译，基于 Meta NLLB-200-Distilled-600M，无需联网，隐私安全。
    /// </summary>
    public bool EnableNllb { get; set; } = false;

    /// <summary>
    /// NLLB 本地服务 URL（bundled python http server）。
    /// Default: http://127.0.0.1:5002
    /// </summary>
    public string NllbUrl { get; set; } = "http://127.0.0.1:5002";

    // ────────────────────────────────────────────────────────────────
    // v3.0 Phase 4 — AI filter settings
    // ────────────────────────────────────────────────────────────────

    /// <summary>AI 过滤自定义 prompt 模板。空字符串表示使用默认模板。</summary>
    public string AiFilterPrompt { get; set; } = string.Empty;

    /// <summary>AI 过滤使用的模型名称。空字符串表示复用 ModelName。</summary>
    public string AiFilterModelName { get; set; } = string.Empty;
}
