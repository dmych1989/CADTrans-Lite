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
    /// Full list of column configurations for the 11-column rich format.
    /// Order: Handle, 类型, 图层, 块名, 属性标签, 表格位置, 原文, 清洗文本, 译文, 状态, 备注
    /// </summary>
    public static readonly IReadOnlyList<ExcelFormatConfig> RichColumns = new List<ExcelFormatConfig>
    {
        new() { ColumnIndex = 1,  HeaderText = "Handle",      Width = 12,  IsMetadata = true,  IsEditable = false },
        new() { ColumnIndex = 2,  HeaderText = "类型",        Width = 10,  IsMetadata = true,  IsEditable = false },
        new() { ColumnIndex = 3,  HeaderText = "图层",        Width = 15,  IsMetadata = true,  IsEditable = false },
        new() { ColumnIndex = 4,  HeaderText = "块名",        Width = 15,  IsMetadata = true,  IsEditable = false },
        new() { ColumnIndex = 5,  HeaderText = "属性标签",    Width = 12,  IsMetadata = true,  IsEditable = false },
        new() { ColumnIndex = 6,  HeaderText = "表格位置",    Width = 10,  IsMetadata = true,  IsEditable = false },
        new() { ColumnIndex = 7,  HeaderText = "原文",        Width = 60,  IsMetadata = false, IsEditable = true  },
        new() { ColumnIndex = 8,  HeaderText = "清洗文本",    Width = 40,  IsMetadata = true,  IsEditable = false },
        new() { ColumnIndex = 9,  HeaderText = "译文",        Width = 60,  IsMetadata = false, IsEditable = true  },
        new() { ColumnIndex = 10, HeaderText = "状态",        Width = 10,  IsMetadata = true,  IsEditable = false },
        new() { ColumnIndex = 11, HeaderText = "备注",        Width = 20,  IsMetadata = false, IsEditable = true  },
    };
}
