// Services/DxfLayoutAdjuster.cs
// Adjusts text height and MTEXT boundaries when translated text overflows.
// Phase 4 (V3-4.1): Runs after DwgWriter write-back to scale down text height
// for entities where the translated text is wider than the entity boundary.

using System.Globalization;
using System.Text;
using CADTransLite.Core.Models;
using CoreEntityType = CADTransLite.Core.Models.EntityType;

namespace CADTransLite.Core.Services;

/// <summary>
/// 在 DwgWriter 回写后追加执行，对 TEXT/MTEXT 实体做字高缩放。
/// 当译文视觉宽度超过实体宽度时，自动降低字高（最小缩放比 0.65）
/// 并刷新 MTEXT 的 RectangleWidth（组码 41）。
/// </summary>
public static class DxfLayoutAdjuster
{
    /// <summary>
    /// 最小缩放因子，避免字高过小不可读。
    /// </summary>
    public const double MinScaleFactor = 0.65;

    /// <summary>
    /// Adjusts text height and MTEXT boundaries when translated text overflows.
    /// Returns (adjustedCount, List of log messages).
    /// </summary>
    /// <param name="dxfFilePath">已回写译文的 DXF 文件路径。</param>
    /// <param name="items">翻译条目列表（需包含 TranslatedText）。</param>
    /// <param name="progress">进度报告器。</param>
    /// <returns>(调整实体数, 日志消息列表)</returns>
    public static (int adjustedCount, List<string> log) AdjustLayout(
        string dxfFilePath,
        List<TranslationItem> items,
        IProgress<(int current, int total, string message)>? progress = null)
    {
        var log = new List<string>();

        if (!File.Exists(dxfFilePath))
        {
            log.Add($"[WARN] DXF file not found for layout adjustment: {dxfFilePath}");
            return (0, log);
        }

        // Step 1: Read DXF file as line array
        string[] lines;
        try
        {
            lines = DxfRawParser.ReadDxfFile(dxfFilePath);
        }
        catch (Exception ex)
        {
            log.Add($"[WARN] Cannot read DXF file for layout adjustment: {ex.Message}");
            return (0, log);
        }

        // Step 2: Parse entity info from DXF
        var textEntities = DxfRawParser.ParseTextEntities(dxfFilePath);
        var mtextEntities = DxfRawParser.ParseMTextEntities(dxfFilePath);

        // Build Handle → EntityInfo lookup
        var textByHandle = new Dictionary<string, TextEntityInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in textEntities)
            textByHandle[t.Handle] = t;

        var mtextByHandle = new Dictionary<string, MTextEntityInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mtextEntities)
            mtextByHandle[m.Handle] = m;

        // Step 3: Filter items that have translated text and are TEXT/MTEXT
        var translatableItems = items
            .Where(i => !string.IsNullOrWhiteSpace(i.TranslatedText)
                        && (i.EntityType == CoreEntityType.Text || i.EntityType == CoreEntityType.MText))
            .ToList();

        if (translatableItems.Count == 0)
        {
            log.Add("[INFO] No TEXT/MTEXT items with translations to adjust.");
            return (0, log);
        }

        int adjustedCount = 0;
        int total = translatableItems.Count;
        int processed = 0;

        // Step 4: Expand merged items (same logic as DwgWriter)
        var expandedItems = new List<TranslationItem>();
        foreach (var item in translatableItems)
        {
            if (item.MergedItems.Count > 0)
            {
                foreach (var original in item.MergedItems)
                {
                    var expanded = new TranslationItem
                    {
                        Handle = original.Handle,
                        EntityType = original.EntityType,
                        TranslatedText = item.TranslatedText,
                    };
                    expandedItems.Add(expanded);
                }
            }
            else
            {
                expandedItems.Add(item);
            }
        }

        // Step 5: Adjust each item
        foreach (var item in expandedItems)
        {
            processed++;
            if (processed % 20 == 0)
                progress?.Report((processed, total, $"Adjusting layout {processed}/{total}…"));

            // Calculate visual width of translated text using MTextRebuilder.GetCharWidth
            double visualWidth = 0;
            foreach (char ch in item.TranslatedText!)
            {
                visualWidth += MTextRebuilder.GetCharWidth(ch);
            }

            if (visualWidth <= 0)
                continue;

            switch (item.EntityType)
            {
                case CoreEntityType.Text:
                    if (AdjustTextEntity(lines, textByHandle, item.Handle, visualWidth, log))
                        adjustedCount++;
                    break;

                case CoreEntityType.MText:
                    if (AdjustMTextEntity(lines, mtextByHandle, item.Handle, visualWidth, log))
                        adjustedCount++;
                    break;
            }
        }

        // Step 6: Save modified lines back to DXF file if any adjustments were made
        if (adjustedCount > 0)
        {
            progress?.Report((total, total, "Saving adjusted DXF…"));
            try
            {
                SaveDxfFile(dxfFilePath, lines);
                log.Add($"[INFO] Layout adjustment saved: {adjustedCount} entities adjusted.");
            }
            catch (Exception ex)
            {
                log.Add($"[WARN] Failed to save layout-adjusted DXF: {ex.Message}");
            }
        }

        return (adjustedCount, log);
    }

    // -----------------------------------------------------------------
    // TEXT entity adjustment
    // -----------------------------------------------------------------

    /// <summary>
    /// Adjusts a TEXT entity's text height if the translated text overflows.
    /// TEXT entities have no explicit width boundary, so we use the original
    /// text's visual width × original height as the reference boundary.
    /// </summary>
    private static bool AdjustTextEntity(
        string[] lines,
        Dictionary<string, TextEntityInfo> lookup,
        string handle,
        double translatedVisualWidth,
        List<string> log)
    {
        if (!lookup.TryGetValue(handle, out var info))
            return false;

        if (info.TextLineNumber < 0 || info.TextLineNumber >= lines.Length)
            return false;

        // Find group code 40 (text height) by searching upward from TextLineNumber
        var (heightValue, heightLineNum) = FindGroupCodeValue(lines, info.TextLineNumber, "40");
        if (heightLineNum < 0 || !double.TryParse(heightValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double textHeight))
            return false;

        if (textHeight <= 0)
            return false;

        // For TEXT, we use the original text visual width × text height as reference boundary.
        // Calculate original visual width
        double originalVisualWidth = 0;
        foreach (char ch in info.OriginalText)
        {
            originalVisualWidth += MTextRebuilder.GetCharWidth(ch);
        }

        if (originalVisualWidth <= 0)
            originalVisualWidth = translatedVisualWidth; // fallback

        // The effective entity width = originalVisualWidth × textHeight
        double entityWidth = originalVisualWidth * textHeight;
        double translatedWidth = translatedVisualWidth * textHeight;

        if (translatedWidth <= entityWidth)
            return false; // No overflow, no adjustment needed

        // Calculate scale factor
        double scaleFactor = Math.Max(MinScaleFactor, entityWidth / translatedWidth);
        double newHeight = textHeight * scaleFactor;

        // Update group code 40 value in DXF lines
        lines[heightLineNum] = newHeight.ToString("F6", CultureInfo.InvariantCulture);
        log.Add($"[ADJ] TEXT Handle={handle}: height {textHeight:F4} → {newHeight:F4} (scale={scaleFactor:F3})");

        return true;
    }

    // -----------------------------------------------------------------
    // MTEXT entity adjustment
    // -----------------------------------------------------------------

    /// <summary>
    /// Adjusts an MTEXT entity's text height and RectangleWidth if the translated text overflows.
    /// </summary>
    private static bool AdjustMTextEntity(
        string[] lines,
        Dictionary<string, MTextEntityInfo> lookup,
        string handle,
        double translatedVisualWidth,
        List<string> log)
    {
        if (!lookup.TryGetValue(handle, out var info))
            return false;

        // Find group code 40 (text height) by searching upward from entity start
        // Use the first group 3 line or LastGroup1LineNumber as reference point
        int searchStartLine = info.Group3LineNumbers.Count > 0
            ? info.Group3LineNumbers[0]
            : info.LastGroup1LineNumber;

        if (searchStartLine < 0)
            return false;

        var (heightValue, heightLineNum) = FindGroupCodeValue(lines, searchStartLine, "40");
        if (heightLineNum < 0 || !double.TryParse(heightValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double textHeight))
            return false;

        if (textHeight <= 0)
            return false;

        double entityWidth = info.RectangleWidth;
        if (entityWidth <= 0)
            return false; // No boundary width, cannot adjust

        double translatedWidth = translatedVisualWidth * textHeight;

        if (translatedWidth <= entityWidth)
            return false; // No overflow

        // Calculate scale factor
        double scaleFactor = Math.Max(MinScaleFactor, entityWidth / translatedWidth);
        double newHeight = textHeight * scaleFactor;

        // Update group code 40 (text height)
        lines[heightLineNum] = newHeight.ToString("F6", CultureInfo.InvariantCulture);

        // Update group code 41 (RectangleWidth) to fit the new text
        double newRectWidth = translatedVisualWidth * newHeight;

        var (_, rectWidthLineNum) = FindGroupCodeValue(lines, searchStartLine, "41");
        if (rectWidthLineNum >= 0)
        {
            lines[rectWidthLineNum] = newRectWidth.ToString("F6", CultureInfo.InvariantCulture);
        }

        log.Add($"[ADJ] MTEXT Handle={handle}: height {textHeight:F4} → {newHeight:F4}, rectWidth {entityWidth:F4} → {newRectWidth:F4} (scale={scaleFactor:F3})");

        return true;
    }

    // -----------------------------------------------------------------
    // DXF group code search utility
    // -----------------------------------------------------------------

    /// <summary>
    /// 从 startLine 向上搜索（最多50行），找到组码为指定值的行。
    /// 返回 (组码值所在行的文本, 组码值所在行的行号)。
    /// 行号 i 处为组码行，行号 i+1 处为值行，返回的行号为 i+1。
    /// </summary>
    /// <param name="lines">DXF 文件行数组。</param>
    /// <param name="startLine">开始搜索的行号（从此行向上搜索）。</param>
    /// <param name="groupCode">目标组码字符串（如 "40", "41"）。</param>
    /// <returns>(值文本, 值行号)；未找到返回 ("", -1)。</returns>
    private static (string value, int valueLineNumber) FindGroupCodeValue(
        string[] lines, int startLine, string groupCode)
    {
        // Search upward from startLine, max 50 lines back
        int searchLimit = Math.Max(0, startLine - 50);
        for (int i = startLine; i >= searchLimit; i--)
        {
            if (lines[i].Trim() == groupCode && i + 1 < lines.Length)
            {
                return (lines[i + 1].Trim(), i + 1);
            }
        }
        return (string.Empty, -1);
    }

    // -----------------------------------------------------------------
    // File I/O
    // -----------------------------------------------------------------

    /// <summary>
    /// 将修改后的行数组保存回 DXF 文件。
    /// 使用与 DwgWriter 相同的编码策略。
    /// </summary>
    private static void SaveDxfFile(string filePath, string[] lines)
    {
        try
        {
            File.WriteAllLines(filePath, lines, Encoding.Default);
        }
        catch (Exception)
        {
            File.WriteAllLines(filePath, lines, Encoding.UTF8);
        }
    }
}
