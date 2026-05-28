// Models/DxfTextCleanerConfig.cs
// Configuration for the DxfTextCleaner text filtering pipeline.

namespace CADTransLite.Core.Models;

/// <summary>
/// DxfTextCleaner 的配置项，控制哪些过滤器启用以及语言设定。
/// </summary>
public sealed class DxfTextCleanerConfig
{
    /// <summary>源语言代码（默认 en）。</summary>
    public string SourceLang { get; set; } = "en";

    /// <summary>目标语言代码（默认 zh）。</summary>
    public string TargetLang { get; set; } = "zh";

    /// <summary>是否过滤空白文本。</summary>
    public bool FilterEmpty { get; set; } = true;

    /// <summary>是否过滤纯数字+单位文本。</summary>
    public bool FilterNumber { get; set; } = true;

    /// <summary>是否过滤纯符号文本。</summary>
    public bool FilterSymbol { get; set; } = true;

    /// <summary>是否过滤工程编码文本。</summary>
    public bool FilterCode { get; set; } = true;

    /// <summary>是否过滤已为目标语言的文本。</summary>
    public bool FilterTargetLang { get; set; } = true;

    /// <summary>是否过滤非源语言文本。</summary>
    public bool FilterNonSourceLang { get; set; } = false;

    /// <summary>自定义跳过模式列表（正则表达式字符串）。</summary>
    public List<string> CustomSkipPatterns { get; set; } = new();
}
