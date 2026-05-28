// Models/LanguageInfo.cs
// Language metadata and the static list of supported languages.

namespace CADTransLite.Core.Models;

/// <summary>
/// Describes a language supported for translation, including provider-specific codes.
/// </summary>
public sealed class LanguageInfo
{
    /// <summary>ISO 639-1 language code (e.g., "ZH", "EN").</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Display name in the UI (e.g., "中文", "English").</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Whether this language appears in the primary (short) list.</summary>
    public bool IsPrimary { get; init; }

    /// <summary>DeepL language code (e.g., "ZH", "EN", "PT-PT"). Null means fall back to <see cref="Code"/>.</summary>
    public string? DeepLCode { get; init; }

    /// <summary>Baidu Translate language code (e.g., "zh", "en", "jp"). Null means fall back to <see cref="Code"/>.</summary>
    public string? BaiduCode { get; init; }

    /// <summary>Tencent TMT language code (e.g., "zh", "en", "ja"). Null means fall back to <see cref="Code"/>.</summary>
    public string? TencentCode { get; init; }

    /// <summary>Microsoft Translator language code (e.g., "zh-Hans", "en"). Null means fall back to <see cref="Code"/>.</summary>
    public string? MicrosoftCode { get; init; }

    /// <summary>
    /// Returns the provider-specific language code for the given provider name.
    /// Falls back to <see cref="Code"/> if no provider-specific code is configured.
    /// </summary>
    /// <param name="providerName">The provider name (e.g., "百度翻译", "腾讯翻译", "Microsoft Translator", "DeepL", "DeepLX", "自定义AI").</param>
    /// <returns>Provider-specific language code (lowercase for Baidu/Tencent/Microsoft/CustomAI; uppercase for DeepL/DeepLX).</returns>
    public string GetProviderCode(string providerName) => providerName switch
    {
        "百度翻译" => BaiduCode ?? Code.ToLowerInvariant(),
        "腾讯翻译" => TencentCode ?? Code.ToLowerInvariant(),
        "Microsoft Translator" => MicrosoftCode ?? Code.ToLowerInvariant(),
        "DeepL" => DeepLCode ?? Code.ToUpperInvariant(),
        "DeepLX" => DeepLCode ?? Code.ToUpperInvariant(),
        "自定义AI" => Code.ToUpperInvariant(), // Custom AI uses standard codes in prompt
        _ => Code.ToLowerInvariant(),
    };

    /// <inheritdoc/>
    public override string ToString() => $"{DisplayName} ({Code})";
}

/// <summary>
/// Static catalog of all supported languages (6 primary + 7 extended = 13 total).
/// </summary>
public static class SupportedLanguages
{
    /// <summary>All supported languages, primary first.</summary>
    public static readonly IReadOnlyList<LanguageInfo> All = BuildList();

    /// <summary>Primary languages shown by default in the UI.</summary>
    public static IEnumerable<LanguageInfo> Primary => All.Where(l => l.IsPrimary);

    /// <summary>Extended languages shown in the dropdown after primary.</summary>
    public static IEnumerable<LanguageInfo> Extended => All.Where(l => !l.IsPrimary);

    /// <summary>Finds a language by its code, or returns <c>null</c>.</summary>
    public static LanguageInfo? ByCode(string code) =>
        All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

    private static List<LanguageInfo> BuildList() =>
    [
        // ── 6 Primary languages (matching architecture §3.3) ──
        new()
        {
            Code = "ZH", DisplayName = "中文", IsPrimary = true,
            DeepLCode = "ZH", BaiduCode = "zh", TencentCode = "zh", MicrosoftCode = "zh-Hans",
        },
        new()
        {
            Code = "EN", DisplayName = "英文", IsPrimary = true,
            DeepLCode = "EN", BaiduCode = "en", TencentCode = "en", MicrosoftCode = "en",
        },
        new()
        {
            Code = "RU", DisplayName = "俄语", IsPrimary = true,
            DeepLCode = "RU", BaiduCode = "ru", TencentCode = "ru", MicrosoftCode = "ru",
        },
        new()
        {
            Code = "ES", DisplayName = "西班牙语", IsPrimary = true,
            DeepLCode = "ES", BaiduCode = "es", TencentCode = "es", MicrosoftCode = "es",
        },
        new()
        {
            Code = "PT", DisplayName = "葡萄牙语", IsPrimary = true,
            DeepLCode = "PT", BaiduCode = "pt", TencentCode = "pt", MicrosoftCode = "pt",
        },
        new()
        {
            Code = "AR", DisplayName = "阿拉伯语", IsPrimary = true,
            DeepLCode = "AR", BaiduCode = "ara", TencentCode = "ar", MicrosoftCode = "ar",
        },

        // ── 7 Extended languages (matching architecture §3.3) ──
        new()
        {
            Code = "FR", DisplayName = "法语", IsPrimary = false,
            DeepLCode = "FR", BaiduCode = "fra", TencentCode = "fr", MicrosoftCode = "fr",
        },
        new()
        {
            Code = "VI", DisplayName = "越南语", IsPrimary = false,
            DeepLCode = "VI", BaiduCode = "vie", TencentCode = "vi", MicrosoftCode = "vi",
        },
        new()
        {
            Code = "KO", DisplayName = "韩语", IsPrimary = false,
            DeepLCode = "KO", BaiduCode = "kor", TencentCode = "ko", MicrosoftCode = "ko",
        },
        new()
        {
            Code = "JA", DisplayName = "日语", IsPrimary = false,
            DeepLCode = "JA", BaiduCode = "jp", TencentCode = "ja", MicrosoftCode = "ja",
        },
        new()
        {
            Code = "IT", DisplayName = "意大利语", IsPrimary = false,
            DeepLCode = "IT", BaiduCode = "it", TencentCode = "it", MicrosoftCode = "it",
        },
        new()
        {
            Code = "DE", DisplayName = "德语", IsPrimary = false,
            DeepLCode = "DE", BaiduCode = "de", TencentCode = "de", MicrosoftCode = "de",
        },
        new()
        {
            Code = "TH", DisplayName = "泰语", IsPrimary = false,
            DeepLCode = "TH", BaiduCode = "th", TencentCode = "th", MicrosoftCode = "th",
        },
    ];
}
