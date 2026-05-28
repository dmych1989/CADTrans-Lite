namespace CADTransLite.Core.Models;

/// <summary>
/// AI 智能过滤配置。
/// </summary>
public sealed class AiFilterConfig
{
    /// <summary>自定义过滤 prompt 模板。空字符串表示使用默认模板。</summary>
    public string FilterPrompt { get; set; } = string.Empty;

    /// <summary>AI 过滤使用的模型名称。空字符串表示复用翻译 API 的 ModelName。</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>是否保护表格表头不被过滤。默认 true。</summary>
    public bool ProtectTableHeaders { get; set; } = true;

    /// <summary>每批发送给 AI 的条目数量。默认 50。</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>源语言代码。</summary>
    public string SourceLang { get; set; } = "EN";

    /// <summary>目标语言代码。</summary>
    public string TargetLang { get; set; } = "ZH";
}
