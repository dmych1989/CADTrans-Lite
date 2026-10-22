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
    public string OdaPath { get; set; } = OdaSettings.ResolveExecutablePath();

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

    // ────────────────────────────────────────────────────────────────
    // v3.0 Phase 4 — Layout adjust, Glossary, AI filter, DWG version
    // ────────────────────────────────────────────────────────────────

    /// <summary>是否启用布局自适应（翻译后文字过长时自动缩放字高）。默认 true。</summary>
    public bool EnableLayoutAdjust { get; set; } = true;

    /// <summary>是否启用术语表替换。默认 false。</summary>
    public bool EnableGlossary { get; set; } = false;

    /// <summary>术语表 JSON 文件路径。空字符串表示使用默认路径。</summary>
    public string GlossaryPath { get; set; } = string.Empty;

    /// <summary>是否启用 AI 智能过滤。默认 false。</summary>
    public bool EnableAiFilter { get; set; } = false;

    /// <summary>AI 过滤自定义 prompt 模板。空字符串表示使用默认模板。</summary>
    public string AiFilterPrompt { get; set; } = string.Empty;

    /// <summary>AI 过滤使用的模型名称。空字符串表示复用翻译 API 的 ModelName。</summary>
    public string AiFilterModelName { get; set; } = string.Empty;

    /// <summary>DWG 输出版本代码。默认 "ACAD2018"。</summary>
    public string OutputDwgVersion { get; set; } = "ACAD2018";

    /// <summary>
    /// 本地模型根目录（自定义）。留空表示使用程序自带的 tools 目录。
    /// 若指定，三个本地离线引擎的模型将从该目录下的统一结构加载：
    ///   &lt;目录&gt;/bergamot（Bergamot）
    ///   &lt;目录&gt;/argos_packages（LibreTranslate / Argos）
    ///   &lt;目录&gt;/models/nllb-200-distilled-600M（NLLB）
    /// 这样在别的电脑上把各渠道模型包解压到此处即可，无需把模型放进程序目录。
    /// </summary>
    public string LocalModelRoot { get; set; } = string.Empty;
}
