// Models/TranslationItem.cs
// Data model representing a single extractable text entity from a DXF/DWG file.

namespace CADTransLite.Core.Models;

/// <summary>
/// Represents the type of CAD text entity.
/// </summary>
public enum EntityType
{
    /// <summary>Single-line text (TEXT command).</summary>
    Text,

    /// <summary>Multi-line text (MTEXT command).</summary>
    MText,

    /// <summary>Block attribute (ATTDEF/ATTRIB).</summary>
    Attribute,

    /// <summary>ACAD_TABLE 单元格文本。</summary>
    TableCell,

    /// <summary>多重引线标注文本。</summary>
    MLeader,
}

/// <summary>
/// A single translatable text item extracted from a DXF document.
/// Supports merging: multiple items with the same (EntityType, OriginalText, RawOriginalText)
/// are merged into one row in the Excel sheet, with all handles tracked in <see cref="CadHandles"/>.
/// </summary>
public sealed class TranslationItem
{
    /// <summary>
    /// The DXF entity handle that uniquely identifies this entity within the document.
    /// For attributes, the handle is formatted as "{insertHandle}::{attrTag}".
    /// </summary>
    public string Handle { get; set; } = string.Empty;

    /// <summary>Type of the source entity.</summary>
    public EntityType EntityType { get; set; }

    /// <summary>
    /// Raw original text as it appears in the DXF file.
    /// For MText entities this contains the raw format-code string (e.g. "{\H1.5x;Hello}").
    /// </summary>
    public string RawOriginalText { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable original text with format codes stripped / replaced by placeholders.
    /// This is the text that will be written to column B of the Excel sheet.
    /// </summary>
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>
    /// Translated text provided by the user (column C of the Excel sheet).
    /// Null or whitespace means "keep original".
    /// </summary>
    public string? TranslatedText { get; set; }

    /// <summary>
    /// Stores the format-code placeholder map produced by MTextCodec.StripFormatCodes
    /// so that RestoreFormatCodes can reconstruct the final MText value.
    /// Only populated for MText entities.
    /// </summary>
    public Dictionary<string, string> FormatPlaceholders { get; set; } = new();

    /// <summary>
    /// Layer name of the source entity (informational).
    /// </summary>
    public string LayerName { get; set; } = string.Empty;

    /// <summary>
    /// Row index in the Excel sheet (1-based, header = 1).
    /// Populated during export so that re-import can match rows to items.
    /// </summary>
    public int ExcelRowIndex { get; set; }

    // ────────────────────────────────────────────────────────────────
    // v2.1 — Merge support properties
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// List of CAD entity handles for this item.
    /// For un-merged items this contains exactly one handle (same as <see cref="Handle"/>).
    /// For merged items this contains all handles that share the same text.
    /// Null means "not yet populated" (default state).
    /// </summary>
    public List<string>? CadHandles { get; set; } = null;

    /// <summary>
    /// Formatted id string for the Excel sheet (column A).
    /// Format: <c>@_Handle1_&amp;f0,@_Handle2_&amp;f0</c> — multiple handles comma-separated.
    /// For attributes the handle format is <c>insertHandle::attrTag</c>.
    /// Returns empty string when <see cref="CadHandles"/> is null or empty.
    /// </summary>
    public string IdString => CadHandles is null || CadHandles.Count == 0
        ? string.Empty
        : string.Join(",", CadHandles.Select(h => $"@_{h}_&f0"));

    /// <summary>
    /// When this item is the result of a merge, <see cref="MergedItems"/> contains
    /// the original un-merged items so that <see cref="DwgWriter"/> can expand
    /// the merged row and write back each original entity individually.
    /// </summary>
    public List<TranslationItem> MergedItems { get; set; } = new();

    /// <summary>
    /// Whether this item represents a merge of two or more original items.
    /// </summary>
    public bool IsMerged => CadHandles is not null && CadHandles.Count > 1;

    // ────────────────────────────────────────────────────────────────
    // v3.0 — Extended properties for table, leader, filtering, etc.
    // ────────────────────────────────────────────────────────────────

    /// <summary>所属块名（仅对 Attribute / TableCell 有效）。</summary>
    public string? BlockName { get; set; }

    /// <summary>ATTRIB 的 Tag 标识（仅对 Attribute 有效）。</summary>
    public string? AttributeTag { get; set; }

    /// <summary>ACAD_TABLE 行索引，-1 表示非表格单元格。</summary>
    public int TableRow { get; set; } = -1;

    /// <summary>ACAD_TABLE 列索引，-1 表示非表格单元格。</summary>
    public int TableColumn { get; set; } = -1;

    /// <summary>
    /// 表格单元格定位字符串，格式 "R{row}:C{col}"。
    /// 非表格实体返回空字符串。
    /// </summary>
    public string TableCellRef
    {
        get
        {
            if (TableRow < 0 || TableColumn < 0)
                return string.Empty;
            return $"R{TableRow}:C{TableColumn}";
        }
    }

    /// <summary>文本被过滤器跳过的原因；null 表示未被过滤。</summary>
    public string? FilterReason { get; set; }

    /// <summary>清洗后的文本（去除前后空白、规范化空白等）。</summary>
    public string? CleanedText { get; set; }

    /// <summary>处理状态：translated / skipped / error 等。</summary>
    public string? Status { get; set; }

    /// <summary>备注字段，用于记录额外信息。</summary>
    public string? Remark { get; set; }

    /// <inheritdoc/>
    public override string ToString() =>
        $"[{EntityType}] Handle={Handle} Text=\"{OriginalText}\"";
}
