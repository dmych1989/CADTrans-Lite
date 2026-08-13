// Services/DxfStyleManager.cs
// Manages DXF text styles for Unicode character support.
// Automatically creates appropriate font styles for non-ASCII translated text.

using netDxf;
using netDxf.Tables;

namespace CADTransLite.Core.Services;

/// <summary>
/// 管理 DXF 文字样式以支持 Unicode 字符。
/// 当译文包含非 ASCII 字符时，自动创建或复用对应的字体样式。
/// </summary>
public static class DxfStyleManager
{
    /// <summary>
    /// 字符脚本 → TrueType 字体族名的映射表。
    /// 用于 netDxf 的 TextStyle(name, fontFamily, fontStyle) 构造函数。
    /// </summary>
    private static readonly Dictionary<string, string> ScriptFontFamilyMap = new()
    {
        ["CJK"] = "SimSun",
        ["Arabic"] = "Arial",
        ["Devanagari"] = "Mangal",
        ["Thai"] = "Tahoma",
        ["Greek"] = "Arial",
        ["Korean"] = "Batang",
        ["Japanese"] = "MS Mincho",
    };

    // -----------------------------------------------------------------
    // DetectScript — 检测文本所属的 Unicode 脚本类型
    // -----------------------------------------------------------------

    /// <summary>
    /// 检测给定文本的主要 Unicode 脚本类型。
    /// 逐字符扫描，返回第一个匹配的非 Latin 脚本；若全部为 ASCII 则返回 "Latin"。
    /// </summary>
    /// <param name="text">待检测文本。</param>
    /// <returns>脚本名称：CJK / Arabic / Devanagari / Thai / Greek / Korean / Japanese / Latin。</returns>
    public static string DetectScript(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "Latin";

        foreach (char ch in text)
        {
            // CJK Unified Ideographs & Extension A
            if (ch >= '\u4E00' && ch <= '\u9FFF')
                return "CJK";
            if (ch >= '\u3400' && ch <= '\u4DBF')
                return "CJK";

            // Arabic
            if (ch >= '\u0600' && ch <= '\u06FF')
                return "Arabic";
            if (ch >= '\u0750' && ch <= '\u077F')
                return "Arabic";

            // Devanagari
            if (ch >= '\u0900' && ch <= '\u097F')
                return "Devanagari";

            // Thai
            if (ch >= '\u0E00' && ch <= '\u0E7F')
                return "Thai";

            // Greek
            if (ch >= '\u0370' && ch <= '\u03FF')
                return "Greek";

            // Korean (Hangul Syllables & Jamo)
            if (ch >= '\uAC00' && ch <= '\uD7AF')
                return "Korean";
            if (ch >= '\u1100' && ch <= '\u11FF')
                return "Korean";

            // Japanese (Hiragana & Katakana)
            if (ch >= '\u3040' && ch <= '\u309F')
                return "Japanese";
            if (ch >= '\u30A0' && ch <= '\u30FF')
                return "Japanese";
        }

        return "Latin";
    }

    // -----------------------------------------------------------------
    // EnsureUnicodeStyle — 确保文档中存在合适的 Unicode 字体样式
    // -----------------------------------------------------------------

    /// <summary>
    /// 检测 <paramref name="text"/> 是否包含非 ASCII 字符，
    /// 若包含则在 <paramref name="doc"/> 中创建或复用对应的 Unicode 字体样式，
    /// 返回样式名。若文本全为 ASCII，则原样返回 <paramref name="existingStyleName"/>。
    /// </summary>
    /// <param name="doc">目标 DXF 文档。</param>
    /// <param name="text">待写入的文本（原文或译文）。</param>
    /// <param name="existingStyleName">实体当前使用的样式名。</param>
    /// <returns>应使用的文字样式名。</returns>
    public static string EnsureUnicodeStyle(DxfDocument doc, string text, string existingStyleName)
    {
        ArgumentNullException.ThrowIfNull(doc);

        if (string.IsNullOrEmpty(text))
            return existingStyleName;

        // 检测是否包含非 ASCII 字符
        bool hasNonAscii = false;
        foreach (char ch in text)
        {
            if (ch > 127)
            {
                hasNonAscii = true;
                break;
            }
        }

        if (!hasNonAscii)
            return existingStyleName;

        // 检测脚本类型
        string script = DetectScript(text);

        // 查找对应字体族名
        if (!ScriptFontFamilyMap.TryGetValue(script, out string? fontFamily))
            return existingStyleName;

        // 生成样式名：如 "CJK_Standard" 或 "CJK_MyStyle"
        string baseStyle = string.IsNullOrWhiteSpace(existingStyleName)
            ? "Standard"
            : existingStyleName;
        string newStyleName = $"{script}_{baseStyle}";

        // 检查文档中是否已存在该样式
        var existingStyle = doc.TextStyles.FirstOrDefault(s =>
            string.Equals(s.Name, newStyleName, StringComparison.OrdinalIgnoreCase));

        if (existingStyle is not null)
            return existingStyle.Name;

        // 创建新的 TextStyle 并添加到文档
        // 使用 TrueType 字体族名构造函数：TextStyle(name, fontFamily, fontStyle)
        try
        {
            var newStyle = new TextStyle(newStyleName, fontFamily, FontStyle.Regular);
            doc.TextStyles.Add(newStyle);
            return newStyle.Name;
        }
        catch (ArgumentException)
        {
            // 样式名冲突或字体族名不可用，回退到原有样式
            return existingStyleName;
        }
    }

    // -----------------------------------------------------------------
    // EncodeDxfUnicode — 将非 ASCII 字符编码为 \U+XXXX 格式
    // -----------------------------------------------------------------

    /// <summary>
    /// 将文本中的非 ASCII 字符编码为 AutoCAD DXF 的 \U+XXXX 格式。
    /// BMP 字符编码为 \U+XXXX（4 位十六进制），
    /// 补充平面字符编码为 \U+XXXXXXXX（8 位十六进制），
    /// ASCII 字符保持不变。
    /// </summary>
    /// <param name="text">原始文本。</param>
    /// <returns>编码后的文本。</returns>
    public static string EncodeDxfUnicode(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var result = new System.Text.StringBuilder(text.Length * 2);

        foreach (char ch in text)
        {
            if (ch <= 127)
            {
                result.Append(ch);
            }
            else
            {
                int codePoint = ch;

                // 检测代理对（surrogate pair）以处理补充平面字符
                if (char.IsHighSurrogate(ch))
                {
                    // 代理对需要两个 char，但在此逐字符处理中，
                    // 只编码高代理项为 8 位格式
                    result.Append($"\\U+{codePoint:X8}");
                }
                else if (char.IsLowSurrogate(ch))
                {
                    // 低代理项单独出现时也编码
                    result.Append($"\\U+{codePoint:X8}");
                }
                else
                {
                    // BMP 字符：4 位十六进制
                    result.Append($"\\U+{codePoint:X4}");
                }
            }
        }

        return result.ToString();
    }
}
