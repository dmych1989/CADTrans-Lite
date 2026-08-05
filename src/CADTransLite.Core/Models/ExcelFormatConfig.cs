// Models/ExcelFormatConfig.cs
// Excel multi-column export format layout definition.
// Phase 3: defines the 11-column rich format for translation metadata.

namespace CADTransLite.Core.Models;

/// <summary>
/// Excel multi-column export format column layout definition.
/// Each instance describes one column in the 11-column rich Excel format.
/// </summary>
public sealed class ExcelFormatConfig
{
    /// <summary>Column index (1-based, EPPlus convention).</summary>
    public int ColumnIndex { get; init; }

    /// <summary>Header text displayed in row 1.</summary>
    public string HeaderText { get; init; } = string.Empty;

    /// <summary>Column width in character units.</summary>
    public double Width { get; init; }

    /// <summary>Whether this is a metadata column (gray background, read-only hint comment).</summary>
    public bool IsMetadata { get; init; }

    /// <summary>Whether this is an editable data column.</summary>
    public bool IsEditable { get; init; }

    /// <summary>
    /// Whether this column is hidden in the exported Excel.
    /// Hidden columns are kept in the file (e.g. Handle) so that write-back can still
    /// match by handle; they are simply not shown to the translator for a cleaner view.
    /// </summary>
    public bool IsHidden { get; init; }

    /// <summary>
    /// Full list of column configurations for the rich format.
    /// 列清洗后仅保留 3 列：id / 原文 / 翻译（与历史 _all.xlsx 数据表布局一致）。
    /// 其余 9 列（Handle/类型/图层/块名/属性标签/表格位置/清洗文本/状态/备注）
    /// 不再导出——直接从文件中删除而非隐藏。
    /// </summary>
    public static readonly IReadOnlyList<ExcelFormatConfig> RichColumns = new List<ExcelFormatConfig>
    {
        new() { ColumnIndex = 1, HeaderText = "id",   Width = 24, IsMetadata = true,  IsEditable = false, IsHidden = false },
        new() { ColumnIndex = 2, HeaderText = "原文", Width = 60, IsMetadata = false, IsEditable = true,  IsHidden = false },
        new() { ColumnIndex = 3, HeaderText = "翻译", Width = 60, IsMetadata = false, IsEditable = true,  IsHidden = false },
    };
}
