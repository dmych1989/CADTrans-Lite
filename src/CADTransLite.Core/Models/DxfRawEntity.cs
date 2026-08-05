// Models/DxfRawEntity.cs
// Raw DXF entity models for ACAD_TABLE, MULTILEADER, TEXT, MTEXT, and ATTRIB parsing.
// Phase 2+: These models capture data for raw DXF text replacement,
// avoiding netDxf Save which loses unsupported entity types.

namespace CADTransLite.Core.Models;

/// <summary>
/// ACAD_TABLE 实体的原始解析数据。
/// </summary>
public sealed class AcadTableData
{
    /// <summary>实体句柄（组码 5）。</summary>
    public string Handle { get; set; } = string.Empty;

    /// <summary>所在图层名（组码 8）。</summary>
    public string LayerName { get; set; } = string.Empty;

    /// <summary>表格行数（组码 91）。</summary>
    public int Rows { get; set; }

    /// <summary>表格列数（组码 92）。</summary>
    public int Columns { get; set; }

    /// <summary>单元格数据列表，按行优先顺序排列。</summary>
    public List<TableCellData> Cells { get; set; } = new();
}

/// <summary>
/// ACAD_TABLE 单元格数据。
/// </summary>
public sealed class TableCellData
{
    /// <summary>单元格行索引。</summary>
    public int Row { get; set; }

    /// <summary>单元格列索引。</summary>
    public int Column { get; set; }

    /// <summary>单元格类型：1=文本, 2=块。</summary>
    public int CellType { get; set; }

    /// <summary>单元格文本内容。</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 文本在 DXF 文件中的行号位置，用于回写时精确定位。
    /// R2004: 组码 1 所在行号; R2007+: 组码 302 所在行号。
    /// </summary>
    public int TextLineNumber { get; set; } = -1;
}

/// <summary>
/// MULTILEADER 实体的原始解析数据。
/// </summary>
public sealed class MultiLeaderData
{
    /// <summary>实体句柄（组码 5）。</summary>
    public string Handle { get; set; } = string.Empty;

    /// <summary>所在图层名（组码 8）。</summary>
    public string LayerName { get; set; } = string.Empty;

    /// <summary>内容类型：2=文本, 1=块, 0=无。</summary>
    public int ContentType { get; set; }

    /// <summary>
    /// 组码 304 的文本内容（MTEXT 格式码字符串）。
    /// </summary>
    public string TextContent { get; set; } = string.Empty;

    /// <summary>文字样式句柄（组码 340）。</summary>
    public string TextStyleHandle { get; set; } = string.Empty;

    /// <summary>文字边界宽度（组码 41）。</summary>
    public double TextBoundaryWidth { get; set; }

    /// <summary>
    /// 组码 304 在 DXF 文件中的行号位置，用于回写时精确定位。
    /// </summary>
    public int TextLineNumber { get; set; } = -1;
}

// ────────────────────────────────────────────────────────────────────
// v3.1: TEXT / MTEXT / ATTRIB raw entity models
// Used by DxfRawTextWriter for raw DXF text replacement,
// which preserves ALL DXF content (unlike netDxf Save).
// ────────────────────────────────────────────────────────────────────

/// <summary>
/// TEXT 实体的原始解析数据。
/// 用于行级精准替换，避免 netDxf Save 丢失内容。
/// </summary>
public sealed class TextEntityInfo
{
    /// <summary>实体句柄（组码 5）。</summary>
    public string Handle { get; set; } = string.Empty;

    /// <summary>所在图层名（组码 8）。</summary>
    public string LayerName { get; set; } = string.Empty;

    /// <summary>原始文本内容（组码 1）。</summary>
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>文字样式名（组码 7），未找到则为空。</summary>
    public string TextStyleName { get; set; } = string.Empty;

    /// <summary>组码 1 值在 DXF 文件中的行号（0-based），用于回写定位。</summary>
    public int TextLineNumber { get; set; } = -1;
}

/// <summary>
/// MTEXT 实体的原始解析数据。
/// MTEXT 文本可能跨越多个组码 3（续行）和最后一个组码 1。
/// </summary>
public sealed class MTextEntityInfo
{
    /// <summary>实体句柄（组码 5）。</summary>
    public string Handle { get; set; } = string.Empty;

    /// <summary>所在图层名（组码 8）。</summary>
    public string LayerName { get; set; } = string.Empty;

    /// <summary>完整原始文本（所有组码 3 + 组码 1 拼接）。</summary>
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>文字样式名（组码 7），未找到则为空。</summary>
    public string TextStyleName { get; set; } = string.Empty;

    /// <summary>矩形宽度（组码 41），0 表示未指定。</summary>
    public double RectangleWidth { get; set; }

    /// <summary>所有组码 3 值的行号列表（0-based），用于回写时清空续行。</summary>
    public List<int> Group3LineNumbers { get; set; } = new();

    /// <summary>最后一个组码 1 值的行号（0-based），用于回写时设置新文本。</summary>
    public int LastGroup1LineNumber { get; set; } = -1;
}

/// <summary>
/// ATTRIB 实体的原始解析数据。
/// ATTRIB 嵌套在 INSERT 实体内，需要用 InsertHandle + Tag 组合定位。
/// </summary>
public sealed class AttributeEntityInfo
{
    /// <summary>ATTRIB 实体自身的句柄（组码 5）。</summary>
    public string Handle { get; set; } = string.Empty;

    /// <summary>父 INSERT 实体的句柄。</summary>
    public string InsertHandle { get; set; } = string.Empty;

    /// <summary>属性标签（组码 2）。</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>所在图层名（组码 8）。</summary>
    public string LayerName { get; set; } = string.Empty;

    /// <summary>原始文本内容（组码 1）。</summary>
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>文字样式名（组码 7），未找到则为空。</summary>
    public string TextStyleName { get; set; } = string.Empty;

    /// <summary>组码 1 值在 DXF 文件中的行号（0-based），用于回写定位。</summary>
    public int TextLineNumber { get; set; } = -1;

    /// <summary>组合键 "{InsertHandle}::{Tag}"，与 TranslationItem.Handle 格式一致。</summary>
    public string CompositeKey => $"{InsertHandle}::{Tag}";
}
