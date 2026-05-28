// Interfaces/ITranslationApi.cs
// Common interface for all translation service providers.

using CADTransLite.Core.Models;

namespace CADTransLite.Core.Interfaces;

/// <summary>
/// Defines the contract for a machine-translation provider.
/// Implementations: DeepL, Baidu, Tencent.
/// </summary>
public interface ITranslationApi
{
    /// <summary>
    /// Human-readable name of the translation provider (e.g., "DeepL", "百度翻译").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Translates a single text from <paramref name="sourceLang"/> to
    /// <paramref name="targetLang"/>.
    /// </summary>
    /// <param name="text">The text to translate.</param>
    /// <param name="sourceLang">Provider-specific source language code.</param>
    /// <param name="targetLang">Provider-specific target language code.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The translated text.</returns>
    Task<string> TranslateAsync(
        string text,
        string sourceLang,
        string targetLang,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Translates a batch of texts from <paramref name="sourceLang"/> to
    /// <paramref name="targetLang"/>.
    /// </summary>
    /// <param name="texts">Texts to translate. Must not be empty.</param>
    /// <param name="sourceLang">
    /// Provider-specific source language code (from <see cref="LanguageInfo"/>).
    /// </param>
    /// <param name="targetLang">
    /// Provider-specific target language code (from <see cref="LanguageInfo"/>).
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>
    /// A list of translated texts, in the same order as <paramref name="texts"/>.
    /// </returns>
    Task<List<string>> TranslateBatchAsync(
        List<string> texts,
        string sourceLang,
        string targetLang,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration settings for a translation API provider.
/// </summary>
public sealed class TranslationApiConfig
{
    /// <summary>API key or secret key for the provider.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Additional identifier (e.g., Baidu AppId, Tencent SecretId).</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>Additional secret (e.g., Baidu SecretKey, Tencent SecretKey).</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Azure region (for Microsoft Translator).</summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>Base URL (for custom AI or DeepLX).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Model name (for custom AI).</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>Whether the provider is properly configured (has required credentials).</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey)
                                || (!string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(SecretKey));
}
