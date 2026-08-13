namespace CADTransLite.Core.Models;

/// <summary>
/// 术语表配置。
/// </summary>
public sealed class GlossaryConfig
{
    /// <summary>术语表 JSON 文件路径。空字符串表示使用默认路径。</summary>
    public string GlossaryPath { get; set; } = string.Empty;

    /// <summary>是否启用术语表替换。</summary>
    public bool EnableGlossary { get; set; } = false;

    /// <summary>是否自动匹配大小写（当 GlossaryEntry.IsCaseSensitive == false 时生效）。</summary>
    public bool AutoMatchCase { get; set; } = true;

    /// <summary>术语条目列表。</summary>
    public List<GlossaryEntry> Entries { get; set; } = new();
}
