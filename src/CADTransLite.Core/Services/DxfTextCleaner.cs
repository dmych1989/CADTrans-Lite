// Services/DxfTextCleaner.cs
// Text cleaning and filtering pipeline for DXF text extraction.
// Filters out non-translatable content: numbers, symbols, codes, tags, etc.

using System.Linq;
using System.Text.RegularExpressions;
using CADTransLite.Core.Models;

namespace CADTransLite.Core.Services;

/// <summary>
/// DXF 文本清洗过滤器。
/// 在提取阶段对每条文本执行过滤判断，跳过无需翻译的内容。
/// </summary>
public static class DxfTextCleaner
{
    // -----------------------------------------------------------------
    // 预编译正则 — 工程编码模式
    // -----------------------------------------------------------------

    /// <summary>标准编码：A-001, ELEC-123B, STR-001-2</summary>
    private static readonly Regex ReCodeStandard = new(
        @"^[A-Z]{1,8}-\d{1,6}[A-Z]?(?:-\d+)?$",
        RegexOptions.Compiled);

    /// <summary>紧凑编码：EL01234, HVAC5678X</summary>
    private static readonly Regex ReCodeCompact = new(
        @"^[A-Z]{2,5}\d{2,6}[A-Z]?$",
        RegexOptions.Compiled);

    /// <summary>层次编码：A.1.2, STR.3.1.4</summary>
    private static readonly Regex ReCodeHierarchical = new(
        @"^[A-Z]{1,4}\.\d{1,3}(?:\.\d{1,3})?$",
        RegexOptions.Compiled);

    /// <summary>图纸编号：DWG NO, DWG  NO.123</summary>
    private static readonly Regex ReCodeDrawingNo = new(
        @"^DWG\s*NO",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>版本号：REV.1, REV-2, REV3</summary>
    private static readonly Regex ReCodeRevision = new(
        @"^REV[.-]?\d+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>参数带单位：1500mm, 3.5MPa, 220V</summary>
    private static readonly Regex ReCodeParamWithUnit = new(
        @"^\d+[.,]?\d*\s*(mm|cm|m|kg|kpa|mpa|pa|v|kv|a|hz|kw|w|%|deg|°)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>自定义跳过模式缓存</summary>
    private static List<Regex>? _customPatterns;

    // -----------------------------------------------------------------
    // 单位 & 符号集合
    // -----------------------------------------------------------------

    private static readonly HashSet<string> EngineeringUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "mm", "cm", "m", "kg", "kpa", "mpa", "pa", "v", "kv", "a", "hz", "kw", "w", "%", "deg", "°",
    };

    /// <summary>常见工程符号字符集</summary>
    private static readonly HashSet<char> SymbolChars = new()
    {
        ' ', '-', '+', '=', '_', '/', '\\', '|', '*', '#', '@', '&', '^', '~',
        '(', ')', '[', ']', '{', '}', '<', '>', ':', ';', ',', '.', '!', '?',
        '"', '\'', '`', '$', '£', '€', '¥', '°', '±', '×', '÷', '≤', '≥',
        '≈', '≠', '∞', '∑', '∏', '√', '∫', '∂', '∇', 'π', 'Ω', 'µ',
        'Δ', 'Φ', 'λ', 'θ', 'ω',
    };

    // -----------------------------------------------------------------
    // Clean — 主清洗入口
    // -----------------------------------------------------------------

    /// <summary>
    /// 对文本执行清洗过滤管道。
    /// 按顺序应用所有启用的过滤器；任何一个匹配即返回 wasFiltered=true。
    /// </summary>
    /// <param name="text">原始提取文本。</param>
    /// <param name="config">清洗配置。</param>
    /// <returns>
    /// 元组：(清洗后文本, 是否被过滤, 过滤原因)。
    /// 若被过滤，<paramref name="cleanedText"/> 仍返回清洗后的值。
    /// </returns>
    public static (string cleanedText, bool wasFiltered, string? filterReason) Clean(
        string text, DxfTextCleanerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrEmpty(text))
        {
            return config.FilterEmpty
                ? (string.Empty, true, "Empty")
                : (string.Empty, false, null);
        }

        // 基础清洗：去除前后空白，规范化内部空白
        string cleaned = text.Trim();

        // ── 1. 空白过滤 ──
        if (config.FilterEmpty && string.IsNullOrWhiteSpace(cleaned))
            return (cleaned, true, "Empty");

        // ── 2. 自定义跳过模式 ──
        if (config.CustomSkipPatterns.Count > 0)
        {
            EnsureCustomPatterns(config.CustomSkipPatterns);
            if (_customPatterns is not null)
            {
                foreach (var pat in _customPatterns)
                {
                    if (pat.IsMatch(cleaned))
                        return (cleaned, true, "CustomSkip");
                }
            }
        }

        // ── 3. 纯符号过滤 ──
        if (config.FilterSymbol && IsSymbol(cleaned))
            return (cleaned, true, "Symbol");

        // ── 4. 纯数字+单位过滤 ──
        if (config.FilterNumber && IsNumber(cleaned))
            return (cleaned, true, "Number");

        // ── 5. 工程编码过滤 ──
        if (config.FilterCode && IsCode(cleaned))
            return (cleaned, true, "Code");

        // ── 6. 工程标签/型号过滤 ──
        if (config.FilterCode && IsEngineeringTag(cleaned))
            return (cleaned, true, "EngineeringTag");

        // ── 7. 目标语言过滤（已为目标语言的文本无需翻译） ──
        if (config.FilterTargetLang && MatchesLanguage(cleaned, config.TargetLang))
            return (cleaned, true, $"TargetLang({config.TargetLang})");

        // ── 8. 非源语言过滤 ──
        if (config.FilterNonSourceLang && !MatchesLanguage(cleaned, config.SourceLang))
            return (cleaned, true, $"NonSourceLang({config.SourceLang})");

        return (cleaned, false, null);
    }

    // -----------------------------------------------------------------
    // IsNumber — 识别数字+单位、范围、比值
    // -----------------------------------------------------------------

    /// <summary>
    /// 判断文本是否为纯数字、数字+工程单位、数值范围或比值。
    /// 匹配示例：1500, 3.5mm, 1-10, 3.5~7.2, 1:100, 3/4。
    /// </summary>
    public static bool IsNumber(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string t = text.Trim();

        // 纯数字（整数或小数）
        if (double.TryParse(t, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _))
            return true;

        // 数字 + 工程单位
        var match = ReCodeParamWithUnit.Match(t);
        if (match.Success)
            return true;

        // 简单数字+单位（非正则版本，更宽松）
        int lastSpace = t.LastIndexOf(' ');
        if (lastSpace > 0)
        {
            string numPart = t[..lastSpace].TrimEnd();
            string unitPart = t[(lastSpace + 1)..].Trim();
            if (double.TryParse(numPart, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _) &&
                EngineeringUnits.Contains(unitPart))
                return true;
        }

        // 数字无空格 + 单位：如 1500mm
        foreach (string unit in EngineeringUnits)
        {
            if (t.EndsWith(unit, StringComparison.OrdinalIgnoreCase) && t.Length > unit.Length)
            {
                string numPart = t[..^unit.Length];
                if (double.TryParse(numPart, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out _))
                    return true;
            }
        }

        // 范围：1-10, 3.5~7.2
        char[] rangeSeparators = { '-', '~', '～' };
        foreach (char sep in rangeSeparators)
        {
            int idx = t.IndexOf(sep);
            if (idx > 0 && idx < t.Length - 1)
            {
                string left = t[..idx].Trim();
                string right = t[(idx + 1)..].Trim();
                if (double.TryParse(left, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out _) &&
                    double.TryParse(right, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out _))
                    return true;
            }
        }

        // 比值：1:100, 3/4
        int colonIdx = t.IndexOf(':');
        if (colonIdx > 0 && colonIdx < t.Length - 1)
        {
            string left = t[..colonIdx].Trim();
            string right = t[(colonIdx + 1)..].Trim();
            if (double.TryParse(left, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _) &&
                double.TryParse(right, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
                return true;
        }

        int slashIdx = t.IndexOf('/');
        if (slashIdx > 0 && slashIdx < t.Length - 1)
        {
            string left = t[..slashIdx].Trim();
            string right = t[(slashIdx + 1)..].Trim();
            if (double.TryParse(left, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _) &&
                double.TryParse(right, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
                return true;
        }

        return false;
    }

    // -----------------------------------------------------------------
    // IsCode — 8种工程编码正则
    // -----------------------------------------------------------------

    /// <summary>
    /// 判断文本是否为工程编码（图纸编号、版本号、部件号等）。
    /// </summary>
    public static bool IsCode(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string t = text.Trim();

        // 标准编码：A-001, ELEC-123B
        if (ReCodeStandard.IsMatch(t))
            return true;

        // 紧凑编码：EL01234
        if (ReCodeCompact.IsMatch(t))
            return true;

        // 层次编码：A.1.2
        if (ReCodeHierarchical.IsMatch(t))
            return true;

        // 图纸编号：DWG NO...
        if (ReCodeDrawingNo.IsMatch(t))
            return true;

        // 版本号：REV.1, REV-2
        if (ReCodeRevision.IsMatch(t))
            return true;

        // 参数带单位（也属于编码范畴）
        if (ReCodeParamWithUnit.IsMatch(t))
            return true;

        return false;
    }

    // -----------------------------------------------------------------
    // IsEngineeringTag — 设备标签/型号
    // -----------------------------------------------------------------

    /// <summary>
    /// 判断文本是否为设备标签或型号。
    /// 条件：必须包含至少一个大写字母和至少一个数字，
    /// 或者包含常见标签符号（#、@）。
    /// </summary>
    public static bool IsEngineeringTag(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string t = text.Trim();

        // 包含标签符号
        if (t.Contains('#') || t.Contains('@'))
            return true;

        // 必须同时包含大写字母和数字
        bool hasUpper = false;
        bool hasDigit = false;
        bool hasLower = false;

        foreach (char ch in t)
        {
            if (char.IsUpper(ch)) hasUpper = true;
            if (char.IsDigit(ch)) hasDigit = true;
            if (char.IsLower(ch)) hasLower = true;
        }

        // 只有包含大写字母和数字，且没有小写字母时才判定为工程标签
        // 有小写字母更可能是自然语言
        return hasUpper && hasDigit && !hasLower;
    }

    // -----------------------------------------------------------------
    // IsSymbol — 纯符号文本
    // -----------------------------------------------------------------

    /// <summary>
    /// 判断文本去除空格后是否仅由符号字符组成。
    /// </summary>
    public static bool IsSymbol(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (char ch in text)
        {
            if (char.IsWhiteSpace(ch))
                continue;
            if (!SymbolChars.Contains(ch))
                return false;
        }

        return true;
    }

    // -----------------------------------------------------------------
    // MatchesLanguage — Unicode 范围检测
    // -----------------------------------------------------------------

    /// <summary>
    /// 判断文本是否主要由指定语言的 Unicode 范围组成。
    /// 支持语言：zh, en, ja, ko。
    /// 只要文本中包含该语言对应的 Unicode 字符即返回 true。
    /// </summary>
    /// <param name="text">待检测文本。</param>
    /// <param name="lang">语言代码：zh / en / ja / ko。</param>
    /// <returns>文本是否包含指定语言的字符。</returns>
    public static bool MatchesLanguage(string text, string lang)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(lang))
            return false;

        switch (lang.ToLowerInvariant())
        {
            case "zh":
                // 中文：CJK Unified Ideographs
                foreach (char ch in text)
                {
                    if ((ch >= '\u4E00' && ch <= '\u9FFF') ||
                        (ch >= '\u3400' && ch <= '\u4DBF'))
                        return true;
                }
                return false;

            case "en":
                // 英文：ASCII 范围
                foreach (char ch in text)
                {
                    if (ch >= 'A' && ch <= 'Z' || ch >= 'a' && ch <= 'z')
                        return true;
                }
                return false;

            case "ja":
                // 日文：平假名、片假名、CJK
                foreach (char ch in text)
                {
                    if ((ch >= '\u3040' && ch <= '\u309F') ||
                        (ch >= '\u30A0' && ch <= '\u30FF') ||
                        (ch >= '\u4E00' && ch <= '\u9FFF'))
                        return true;
                }
                return false;

            case "ko":
                // 韩文：谚文音节、韩文字母
                foreach (char ch in text)
                {
                    if ((ch >= '\uAC00' && ch <= '\uD7AF') ||
                        (ch >= '\u1100' && ch <= '\u11FF'))
                        return true;
                }
                return false;

            default:
                return false;
        }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// 确保自定义跳过模式已编译。若配置列表变化则重新编译。
    /// </summary>
    private static void EnsureCustomPatterns(List<string> patterns)
    {
        _customPatterns = new List<Regex>(patterns.Count);
        foreach (string pattern in patterns)
        {
            try
            {
                _customPatterns.Add(new Regex(pattern, RegexOptions.Compiled));
            }
            catch (ArgumentException)
            {
                // 无效正则，跳过
            }
        }
    }
}
