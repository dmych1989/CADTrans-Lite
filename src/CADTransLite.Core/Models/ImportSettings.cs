// Models/ImportSettings.cs
// Configuration for CAD text extraction options.

namespace CADTransLite.Core.Models;

/// <summary>
/// Import settings for CAD text extraction.
/// Controls which entity types and layers are processed.
/// </summary>
public sealed class ImportSettings
{
    /// <summary>
    /// Whether to explode proxy entities and extract text from them.
    /// </summary>
    public bool ImportProxyObjects { get; set; } = true;

    /// <summary>
    /// Whether to extract text from block attribute definitions.
    /// </summary>
    public bool ImportBlockAttributes { get; set; } = true;

    /// <summary>
    /// Whether to extract dimension text (measurement values and custom text).
    /// </summary>
    public bool ImportDimensionText { get; set; } = true;

    /// <summary>
    /// Whether to extract MText as individual paragraphs (split by \P).
    /// Mutually exclusive with <see cref="ImportMTextWhole"/>.
    /// </summary>
    public bool ImportMTextParagraph { get; set; } = true;

    /// <summary>
    /// Whether to extract MText as a single whole block.
    /// Mutually exclusive with <see cref="ImportMTextParagraph"/>.
    /// </summary>
    public bool ImportMTextWhole { get; set; } = true;

    /// <summary>
    /// Whether to extract text from frozen (invisible) layers.
    /// </summary>
    public bool ImportFrozenLayers { get; set; } = false;

    /// <summary>
    /// Whether to extract text from locked layers.
    /// </summary>
    public bool ImportLockedLayers { get; set; } = false;

    /// <summary>
    /// Whether to extract text from turned-off layers.
    /// </summary>
    public bool ImportOffLayers { get; set; } = false;

    // ────────────────────────────────────────────────────────────────
    // v3.0 — Text cleaning configuration
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 文本清洗过滤器的配置。控制哪些类型的内容会被自动跳过。
    /// </summary>
    public DxfTextCleanerConfig CleanerConfig { get; set; } = new();

    /// <summary>
    /// 是否启用文本清洗过滤。启用后，提取阶段会自动跳过
    /// 无需翻译的内容（纯数字、符号、工程编码等）。
    /// </summary>
    public bool EnableTextCleaning { get; set; } = true;

    // ────────────────────────────────────────────────────────────────
    // v3.0 Phase 2 — ACAD_TABLE / MULTILEADER extraction
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 是否提取 ACAD_TABLE 单元格文本。
    /// </summary>
    public bool ImportAcadTables { get; set; } = true;

    /// <summary>
    /// 是否提取 MULTILEADER 文本。
    /// </summary>
    public bool ImportMultiLeaders { get; set; } = true;

    // ────────────────────────────────────────────────────────────────
    // v3.0 Phase 3 — Excel format & deduplication
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 是否启用清洗后去重。启用后，合并阶段会在第一步合并之后，
    /// 按 (EntityType, CleanedText) 进行二次去重。
    /// 默认 false。
    /// </summary>
    public bool EnableCleanedDedup { get; set; } = false;

    /// <summary>
    /// 是否使用多列富元数据 Excel 格式导出。
    /// false = 传统 2 列格式（向后兼容）。
    /// true = 11 列格式（Handle/类型/图层/原文/清洗/译文/状态等）。
    /// 默认 true。
    /// </summary>
    public bool UseRichExcelFormat { get; set; } = true;

    // ────────────────────────────────────────────────────────────────
    // v3.0 Phase 4 — Feature toggles
    // ────────────────────────────────────────────────────────────────

    /// <summary>是否启用布局自适应。默认 true。</summary>
    public bool EnableLayoutAdjust { get; set; } = true;

    /// <summary>是否启用 AI 智能过滤。默认 false。</summary>
    public bool EnableAiFilter { get; set; } = false;

    /// <summary>是否启用术语表替换。默认 false。</summary>
    public bool EnableGlossary { get; set; } = false;
}
