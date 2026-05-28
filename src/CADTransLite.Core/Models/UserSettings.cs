// Models/UserSettings.cs
// Unified settings model that aggregates all user-configurable settings.
// Persisted via SettingsManager to %APPDATA%/CADTransLite/settings.json.

namespace CADTransLite.Core.Models;

/// <summary>
/// Root unified settings model for CADTrans Lite.
/// Contains all user-configurable settings in a single object for easy persistence.
/// </summary>
public sealed class UserSettings
{
    /// <summary>
    /// Path to the ODA File Converter executable.
    /// Default: <see cref="OdaSettings.DefaultExecutablePath"/>.
    /// </summary>
    public string OdaPath { get; set; } = OdaSettings.DefaultExecutablePath;

    /// <summary>
    /// Import settings for CAD text extraction.
    /// Controls which entity types and layers are processed.
    /// </summary>
    public ImportSettings Import { get; set; } = new();

    /// <summary>
    /// Suffix added to output file names.
    /// Default: "_纯翻译"
    /// Example: "building.dxf" → "building_纯翻译.dxf"
    /// </summary>
    public string ExportSuffix { get; set; } = "_纯翻译";

    /// <summary>
    /// Translation API settings (Custom AI, DeepL, Baidu, Tencent, Microsoft, DeepLX).
    /// </summary>
    public TranslationApiSettings TranslationApi { get; set; } = new();

    /// <summary>
    /// ISO 639-1 code of the last-used source language.
    /// Default: "EN"
    /// </summary>
    public string SourceLanguageCode { get; set; } = "EN";

    /// <summary>
    /// ISO 639-1 code of the last-used target language.
    /// Default: "ZH"
    /// </summary>
    public string TargetLanguageCode { get; set; } = "ZH";

    /// <summary>
    /// Last selected translation provider name.
    /// Default: "百度翻译"
    /// </summary>
    public string SelectedProvider { get; set; } = "百度翻译";
}
