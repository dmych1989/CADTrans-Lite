namespace CADTransLite.Core.Models;

/// <summary>
/// 术语表条目：定义源语言术语到目标语言术语的映射。
/// </summary>
public sealed class GlossaryEntry
{
    /// <summary>源语言术语（需要被替换的文本）。</summary>
    public string SourceTerm { get; set; } = string.Empty;

    /// <summary>目标语言术语（替换后的文本）。</summary>
    public string TargetTerm { get; set; } = string.Empty;

    /// <summary>源语言代码（如 "EN", "ZH"），空字符串表示适用于所有源语言。</summary>
    public string SourceLang { get; set; } = string.Empty;

    /// <summary>目标语言代码（如 "ZH", "EN"），空字符串表示适用于所有目标语言。</summary>
    public string TargetLang { get; set; } = string.Empty;

    /// <summary>是否区分大小写匹配。默认 false。</summary>
    public bool IsCaseSensitive { get; set; } = false;

    /// <summary>是否使用正则表达式匹配。V1 暂不支持，预留字段。默认 false。</summary>
    public bool IsRegex { get; set; } = false;

    /// <summary>术语分类标签（如 "机械", "电气", "建筑"），用于分组管理。</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>备注说明。</summary>
    public string Remark { get; set; } = string.Empty;
}
