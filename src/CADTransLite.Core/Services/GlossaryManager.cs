// Services/GlossaryManager.cs
// Manages glossary persistence (JSON) and term replacement logic.
// v3.0 Phase 4: Glossary system for consistent professional terminology.

using CADTransLite.Core.Models;
using System.Text.Json;

namespace CADTransLite.Core.Services;

/// <summary>
/// Static service for glossary management: loading, saving, and applying
/// terminology replacements to translation results.
/// Glossary entries map source-language terms to target-language equivalents,
/// ensuring consistent professional terminology across translations.
/// </summary>
public static class GlossaryManager
{
    /// <summary>术语表默认存储目录。</summary>
    public static string GlossaryDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CADTransLite", "glossary");

    /// <summary>JSON serialization options with camelCase naming and indented formatting.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// 加载术语表 JSON 文件。如果文件不存在，返回空列表。
    /// </summary>
    /// <param name="glossaryPath">术语表 JSON 文件的完整路径。</param>
    /// <returns>术语条目列表；文件不存在时返回空列表。</returns>
    public static List<GlossaryEntry> LoadGlossary(string glossaryPath)
    {
        if (string.IsNullOrWhiteSpace(glossaryPath) || !File.Exists(glossaryPath))
            return new List<GlossaryEntry>();

        string json = File.ReadAllText(glossaryPath);
        if (string.IsNullOrWhiteSpace(json))
            return new List<GlossaryEntry>();

        var entries = JsonSerializer.Deserialize<List<GlossaryEntry>>(json, JsonOptions);
        return entries ?? new List<GlossaryEntry>();
    }

    /// <summary>
    /// 保存术语表到 JSON 文件。自动创建目录。
    /// </summary>
    /// <param name="entries">要保存的术语条目列表。</param>
    /// <param name="glossaryPath">目标 JSON 文件路径。</param>
    public static void SaveGlossary(List<GlossaryEntry> entries, string glossaryPath)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(glossaryPath);

        // Ensure directory exists
        string? dir = Path.GetDirectoryName(glossaryPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(entries, JsonOptions);
        File.WriteAllText(glossaryPath, json);
    }

    /// <summary>
    /// 对翻译结果应用术语替换。
    /// 遍历每个有 TranslatedText 的 TranslationItem，检查原文是否匹配术语条目，
    /// 并在匹配时将译文替换为目标术语。
    /// <para>
    /// 替换策略（安全优先，避免误替换）：
    /// <list type="bullet">
    ///   <item>原文 == 源术语（精确匹配）：直接将译文替换为目标术语</item>
    ///   <item>原文包含源术语（子串匹配）：检查译文中是否存在未翻译的源术语，如有则替换为目标术语；
    ///         否则检查译文是否已包含目标术语，若已包含则跳过，若未包含则不替换</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="items">翻译条目列表。</param>
    /// <param name="entries">术语条目列表。</param>
    /// <param name="sourceLang">当前源语言代码（如 "EN"）。</param>
    /// <param name="targetLang">当前目标语言代码（如 "ZH"）。</param>
    /// <param name="progress">可选进度回调。</param>
    /// <returns>替换次数。</returns>
    public static int ApplyGlossary(
        List<TranslationItem> items,
        List<GlossaryEntry> entries,
        string sourceLang,
        string targetLang,
        IProgress<(int current, int total, string message)>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(entries);

        if (items.Count == 0 || entries.Count == 0)
            return 0;

        // Step 1: Filter entries matching the current language pair.
        // An entry matches if its SourceLang is empty or equals sourceLang,
        // AND its TargetLang is empty or equals targetLang.
        var matchingEntries = entries
            .Where(e => (string.IsNullOrEmpty(e.SourceLang) ||
                        e.SourceLang.Equals(sourceLang, StringComparison.OrdinalIgnoreCase)) &&
                       (string.IsNullOrEmpty(e.TargetLang) ||
                        e.TargetLang.Equals(targetLang, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (matchingEntries.Count == 0)
            return 0;

        // Step 2: Sort by SourceTerm length descending (longer terms first to avoid
        // short terms truncating longer ones, e.g., "bolt" vs "bolt hole").
        matchingEntries.Sort((a, b) => b.SourceTerm.Length.CompareTo(a.SourceTerm.Length));

        // Step 3: Apply replacements
        int replacedCount = 0;
        int totalItems = items.Count;

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];

            // Skip items without translated text
            if (string.IsNullOrWhiteSpace(item.TranslatedText))
                continue;

            string originalText = item.OriginalText ?? string.Empty;
            string translatedText = item.TranslatedText;

            foreach (var entry in matchingEntries)
            {
                // Skip regex entries (V1 placeholder, not yet supported)
                if (entry.IsRegex)
                    continue;

                if (string.IsNullOrEmpty(entry.SourceTerm))
                    continue;

                bool containsSourceTerm = entry.IsCaseSensitive
                    ? originalText.Contains(entry.SourceTerm, StringComparison.Ordinal)
                    : originalText.Contains(entry.SourceTerm, StringComparison.OrdinalIgnoreCase);

                if (!containsSourceTerm)
                    continue;

                bool exactMatch = entry.IsCaseSensitive
                    ? string.Equals(originalText, entry.SourceTerm, StringComparison.Ordinal)
                    : string.Equals(originalText, entry.SourceTerm, StringComparison.OrdinalIgnoreCase);

                if (exactMatch)
                {
                    // Case 1: Original text exactly matches the source term.
                    // Directly replace the entire translated text with the target term.
                    item.TranslatedText = entry.TargetTerm;
                    replacedCount++;
                }
                else
                {
                    // Case 2: Original text contains the source term as a substring.
                    // Check if the source term appears untranslated in the translated text.
                    // If so, replace it with the target term.
                    bool sourceTermInTranslation = entry.IsCaseSensitive
                        ? translatedText.Contains(entry.SourceTerm, StringComparison.Ordinal)
                        : translatedText.Contains(entry.SourceTerm, StringComparison.OrdinalIgnoreCase);

                    if (sourceTermInTranslation)
                    {
                        // The source term was left untranslated in the target text.
                        // Replace it with the target term.
                        item.TranslatedText = entry.IsCaseSensitive
                            ? item.TranslatedText.Replace(entry.SourceTerm, entry.TargetTerm, StringComparison.Ordinal)
                            : ReplaceIgnoreCase(item.TranslatedText, entry.SourceTerm, entry.TargetTerm);
                        translatedText = item.TranslatedText;
                        replacedCount++;
                    }
                    else
                    {
                        // Check if the target term is already present in the translation.
                        // If yes, the term is correctly translated — skip.
                        // If no, we can't reliably locate the translated equivalent,
                        // so we skip to avoid false replacements.
                        bool targetTermInTranslation = entry.IsCaseSensitive
                            ? translatedText.Contains(entry.TargetTerm, StringComparison.Ordinal)
                            : translatedText.Contains(entry.TargetTerm, StringComparison.OrdinalIgnoreCase);

                        // Target term not present and source term not found in translation.
                        // The term may have been translated to a different word.
                        // We skip to avoid incorrect replacements.
                    }
                }
            }

            // Report progress every 50 items
            if (progress != null && i % 50 == 0)
            {
                progress.Report((i, totalItems, $"正在应用术语表 {i}/{totalItems}"));
            }
        }

        return replacedCount;
    }

    /// <summary>
    /// 获取默认术语表路径。
    /// </summary>
    /// <returns>默认术语表文件的完整路径。</returns>
    public static string GetDefaultGlossaryPath()
    {
        return Path.Combine(GlossaryDirectory, "default.json");
    }

    /// <summary>
    /// 确保默认术语表目录和文件存在。如果文件不存在则创建空术语表。
    /// </summary>
    public static void EnsureDefaultGlossary()
    {
        if (!Directory.Exists(GlossaryDirectory))
            Directory.CreateDirectory(GlossaryDirectory);

        string defaultPath = GetDefaultGlossaryPath();
        if (!File.Exists(defaultPath))
        {
            SaveGlossary(new List<GlossaryEntry>(), defaultPath);
        }
    }

    /// <summary>
    /// Case-insensitive string replacement.
    /// Finds all occurrences of <paramref name="search"/> in <paramref name="text"/>
    /// (ignoring case) and replaces them with <paramref name="replacement"/>.
    /// Preserves the original casing of the surrounding text.
    /// </summary>
    private static string ReplaceIgnoreCase(string text, string search, string replacement)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(search))
            return text;

        int index = text.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return text;

        // Use a StringBuilder for efficient concatenation when there are multiple replacements
        var result = new System.Text.StringBuilder(text.Length - search.Length + replacement.Length);
        int lastPos = 0;

        while (index >= 0)
        {
            result.Append(text, lastPos, index - lastPos);
            result.Append(replacement);
            lastPos = index + search.Length;
            index = text.IndexOf(search, lastPos, StringComparison.OrdinalIgnoreCase);
        }

        result.Append(text, lastPos, text.Length - lastPos);
        return result.ToString();
    }
}
