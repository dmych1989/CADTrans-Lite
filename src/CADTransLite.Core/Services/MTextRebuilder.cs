// Services/MTextRebuilder.cs
// Rebuilds MTEXT content after translation: preserves formatting skeleton,
// injects translated text, and applies visual-width line wrapping.

using System.Text;
using System.Text.RegularExpressions;

namespace CADTransLite.Core.Services;

/// <summary>
/// MTEXT 回写增强器。
/// 解析原始 MTEXT 的格式命令骨架，将译文注入可见文字位置，
/// 并按视觉宽度自动换行。
/// </summary>
public static class MTextRebuilder
{
    // -----------------------------------------------------------------
    // 字符宽度映射常量
    // -----------------------------------------------------------------

    /// <summary>空格的相对宽度因子</summary>
    private const double WidthSpace = 0.35;

    /// <summary>ASCII 字符的相对宽度因子</summary>
    private const double WidthAscii = 0.55;

    /// <summary>CJK 字符的相对宽度因子</summary>
    private const double WidthCjk = 1.0;

    // -----------------------------------------------------------------
    // 正则模式
    // -----------------------------------------------------------------

    /// <summary>
    /// 匹配 MTEXT 格式命令。
    /// 涵盖：{\H...;}、{\C...;}、{\F...;}、\P、\W...;、\A...;、\T...;、\Q...;、
    /// \S...;、\O、\o、\L、\l、\K、\k、\~、{、}
    /// </summary>
    private static readonly Regex ReMtextCommand = new(
        @"\{\\[HCFWATQSp][^;]*;|\\[WHATQCSplLoOkK][^;]*;|\\P|\\~|[{}]",
        RegexOptions.Compiled);

    /// <summary>
    /// 匹配 \P 段落分隔符（独立出现或带前缀空格）。
    /// </summary>
    private static readonly Regex ReParagraphBreak = new(
        @"\\P",
        RegexOptions.Compiled);

    // -----------------------------------------------------------------
    // RebuildMtextContent — 主入口
    // -----------------------------------------------------------------

    /// <summary>
    /// 重建 MTEXT 内容：解析原始格式骨架，注入译文，按需换行。
    /// </summary>
    /// <param name="originalRaw">原始 MTEXT 的 raw 值（含格式码）。</param>
    /// <param name="translatedText">翻译后的纯文本。</param>
    /// <param name="entityWidth">
    /// MTEXT 实体的 RectangleWidth。&lt;=0 表示不自动换行。
    /// </param>
    /// <returns>重建后的 MTEXT 字符串，可直接写回实体。</returns>
    public static string RebuildMtextContent(string originalRaw, string translatedText, double entityWidth)
    {
        if (string.IsNullOrWhiteSpace(originalRaw))
            return translatedText ?? string.Empty;

        if (string.IsNullOrWhiteSpace(translatedText))
            return originalRaw;

        // 步骤1：提取原始 MTEXT 的格式骨架
        string skeleton = ExtractSkeleton(originalRaw);

        // 步骤2：将译文注入骨架中的可见文字位置
        string rebuilt = ReplaceVisibleText(skeleton, translatedText);

        // 步骤3：如果实体宽度有效，按视觉宽度换行
        if (entityWidth > 0)
        {
            rebuilt = WrapPlainText(rebuilt, entityWidth);
        }

        return rebuilt;
    }

    // -----------------------------------------------------------------
    // ExtractSkeleton — 提取格式骨架
    // -----------------------------------------------------------------

    /// <summary>
    /// 从原始 MTEXT 中提取格式骨架。
    /// 将所有可见文字替换为占位符，保留所有格式命令。
    /// 占位符格式：\x02VT\x02\x03（与 MTextCodec 不冲突）。
    /// </summary>
    internal static string ExtractSkeleton(string originalRaw)
    {
        if (string.IsNullOrEmpty(originalRaw))
            return originalRaw;

        // 将所有格式命令替换为临时标记，剩余部分即为可见文字
        var sb = new StringBuilder(originalRaw.Length);
        int lastEnd = 0;

        foreach (Match m in ReMtextCommand.Matches(originalRaw))
        {
            // 可见文字段（在两个格式命令之间的内容）
            if (m.Index > lastEnd)
            {
                string visiblePart = originalRaw[lastEnd..m.Index];
                if (!string.IsNullOrEmpty(visiblePart))
                {
                    // 用占位符标记可见文字的位置
                    sb.Append("\x02VT\x02");
                    sb.Append(visiblePart);
                    sb.Append("\x03");
                }
            }

            // 格式命令原样保留
            sb.Append(m.Value);
            lastEnd = m.Index + m.Length;
        }

        // 尾部可见文字
        if (lastEnd < originalRaw.Length)
        {
            string visiblePart = originalRaw[lastEnd..];
            if (!string.IsNullOrEmpty(visiblePart))
            {
                sb.Append("\x02VT\x02");
                sb.Append(visiblePart);
                sb.Append("\x03");
            }
        }

        return sb.ToString();
    }

    // -----------------------------------------------------------------
    // ReplaceVisibleText — 替换可见文字
    // -----------------------------------------------------------------

    /// <summary>
    /// 将骨架中的可见文字占位符替换为译文。
    /// 保留所有 MTEXT 格式命令。
    /// </summary>
    /// <param name="originalSkeleton">含占位符的格式骨架。</param>
    /// <param name="translatedText">翻译后的纯文本。</param>
    /// <returns>替换后的 MTEXT 字符串。</returns>
    public static string ReplaceVisibleText(string originalSkeleton, string translatedText)
    {
        if (string.IsNullOrEmpty(originalSkeleton))
            return translatedText ?? string.Empty;

        if (string.IsNullOrEmpty(translatedText))
            return originalSkeleton;

        // 转义译文中的 MTEXT 特殊字符
        string escaped = EscapeMTextSpecial(translatedText);

        // 将 \n 转为 \P（MTEXT 段落分隔）
        escaped = escaped.Replace("\n", @"\P");

        // 查找骨架中可见文字占位符的数量
        string placeholderStart = "\x02VT\x02";
        string placeholderEnd = "\x03";

        int placeholderCount = 0;
        int searchIdx = 0;
        while ((searchIdx = originalSkeleton.IndexOf(placeholderStart, searchIdx, StringComparison.Ordinal)) >= 0)
        {
            placeholderCount++;
            searchIdx += placeholderStart.Length;
        }

        if (placeholderCount == 0)
        {
            // 无占位符，直接返回原文骨架（格式命令包裹）
            return originalSkeleton;
        }

        // 如果只有一个可见文字段，整体替换
        if (placeholderCount == 1)
        {
            int startIdx = originalSkeleton.IndexOf(placeholderStart, StringComparison.Ordinal);
            int endIdx = originalSkeleton.IndexOf(placeholderEnd, startIdx + placeholderStart.Length, StringComparison.Ordinal);

            if (startIdx >= 0 && endIdx >= 0)
            {
                string before = originalSkeleton[..startIdx];
                string after = originalSkeleton[(endIdx + placeholderEnd.Length)..];
                return before + escaped + after;
            }
        }

        // 多个可见文字段：将译文按原始段落数分配
        // 先将译文按 \P 分段
        string[] translatedParagraphs = escaped.Split(new[] { @"\P" }, StringSplitOptions.None);

        var result = new StringBuilder(originalSkeleton.Length + escaped.Length);
        int paraIdx = 0;
        int pos = 0;

        while (pos < originalSkeleton.Length)
        {
            int startIdx = originalSkeleton.IndexOf(placeholderStart, pos, StringComparison.Ordinal);
            if (startIdx < 0)
            {
                // 剩余部分无占位符，直接追加
                result.Append(originalSkeleton[pos..]);
                break;
            }

            // 追加占位符之前的内容
            result.Append(originalSkeleton[pos..startIdx]);

            // 找到占位符结束位置
            int endIdx = originalSkeleton.IndexOf(placeholderEnd, startIdx + placeholderStart.Length, StringComparison.Ordinal);
            if (endIdx < 0)
            {
                // 格式异常，追加剩余并退出
                result.Append(originalSkeleton[startIdx..]);
                break;
            }

            // 用对应段落的译文替换
            if (paraIdx < translatedParagraphs.Length)
            {
                result.Append(translatedParagraphs[paraIdx]);
            }
            paraIdx++;

            pos = endIdx + placeholderEnd.Length;
        }

        return result.ToString();
    }

    // -----------------------------------------------------------------
    // WrapPlainText — 按视觉宽度换行
    // -----------------------------------------------------------------

    /// <summary>
    /// 按视觉宽度对 MTEXT 文本执行自动换行。
    /// 字符宽度映射：空格=0.35, ASCII=0.55, CJK=1.0。
    /// 超过 <paramref name="entityWidth"/> 时插入 \P 换行符。
    /// 保持原有 \P 换行符。
    /// </summary>
    /// <param name="text">MTEXT 文本（含格式码）。</param>
    /// <param name="entityWidth">实体宽度（以字符宽度为单位）。</param>
    /// <returns>换行后的 MTEXT 文本。</returns>
    public static string WrapPlainText(string text, double entityWidth)
    {
        if (string.IsNullOrEmpty(text) || entityWidth <= 0)
            return text;

        // 以 \P 为分隔符拆分段落
        string[] paragraphs = ReParagraphBreak.Split(text);
        var result = new StringBuilder(text.Length + paragraphs.Length * 10);

        for (int i = 0; i < paragraphs.Length; i++)
        {
            if (i > 0)
                result.Append(@"\P");

            string wrapped = WrapSingleParagraph(paragraphs[i], entityWidth);
            result.Append(wrapped);
        }

        return result.ToString();
    }

    /// <summary>
    /// 对单个段落（无 \P）按视觉宽度换行。
    /// 只处理可见文字，跳过格式命令。
    /// </summary>
    private static string WrapSingleParagraph(string paragraph, double entityWidth)
    {
        if (string.IsNullOrEmpty(paragraph))
            return paragraph;

        var result = new StringBuilder(paragraph.Length + 20);
        double currentLineWidth = 0;
        int pos = 0;

        while (pos < paragraph.Length)
        {
            // 检测是否为格式命令
            if (paragraph[pos] == '\\' || paragraph[pos] == '{' || paragraph[pos] == '}')
            {
                // 提取完整的格式命令
                int cmdEnd = FindCommandEnd(paragraph, pos);
                if (cmdEnd > pos)
                {
                    string cmd = paragraph[pos..cmdEnd];
                    result.Append(cmd);
                    pos = cmdEnd;
                    continue;
                }
            }

            // 处理可见字符
            char ch = paragraph[pos];
            double charWidth = GetCharWidth(ch);

            if (currentLineWidth + charWidth > entityWidth && currentLineWidth > 0)
            {
                // 超出宽度，插入换行
                result.Append(@"\P");
                currentLineWidth = 0;
            }

            result.Append(ch);
            currentLineWidth += charWidth;
            pos++;
        }

        return result.ToString();
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// 获取字符的相对宽度。
    /// </summary>
    internal static double GetCharWidth(char ch)
    {
        if (char.IsWhiteSpace(ch))
            return WidthSpace;

        if (ch <= 127)
            return WidthAscii;

        // CJK 字符
        if ((ch >= '\u4E00' && ch <= '\u9FFF') ||
            (ch >= '\u3400' && ch <= '\u4DBF') ||
            (ch >= '\uAC00' && ch <= '\uD7AF') ||
            (ch >= '\u1100' && ch <= '\u11FF') ||
            (ch >= '\u3040' && ch <= '\u309F') ||
            (ch >= '\u30A0' && ch <= '\u30FF'))
            return WidthCjk;

        // 其他 Unicode 字符，按 CJK 宽度处理（保守估计）
        return WidthCjk;
    }

    /// <summary>
    /// 查找 MTEXT 格式命令的结束位置。
    /// 返回命令后的第一个字符索引。
    /// </summary>
    private static int FindCommandEnd(string text, int start)
    {
        if (start >= text.Length)
            return start;

        // 花括号组：{ ... }
        if (text[start] == '{')
        {
            int depth = 1;
            int i = start + 1;
            while (i < text.Length && depth > 0)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}') depth--;
                i++;
            }
            return i;
        }

        // 闭合花括号
        if (text[start] == '}')
            return start + 1;

        // 反斜杠命令
        if (text[start] == '\\' && start + 1 < text.Length)
        {
            char next = text[start + 1];

            // 简单命令：\P, \~, \O, \o, \L, \l, \K, \k
            if ("Pp~OoLlKkNn".Contains(next))
                return start + 2;

            // 带参数命令：\H..., \W..., \A..., \T..., \Q..., \C..., \S..., \F...
            if ("HWATQCSFhwatqcsf".Contains(next))
            {
                int semicolon = text.IndexOf(';', start);
                if (semicolon >= 0)
                    return semicolon + 1;
            }
        }

        // 无法识别，前进一个字符
        return start + 1;
    }

    /// <summary>
    /// 转义 MTEXT 特殊字符：\ → \\, { → \{, } → \}。
    /// 仅对非格式命令的纯文本执行转义。
    /// </summary>
    private static string EscapeMTextSpecial(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var sb = new StringBuilder(text.Length * 2);
        foreach (char ch in text)
        {
            switch (ch)
            {
                case '\\':
                    sb.Append(@"\\");
                    break;
                case '{':
                    sb.Append(@"\{");
                    break;
                case '}':
                    sb.Append(@"\}");
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }
}
