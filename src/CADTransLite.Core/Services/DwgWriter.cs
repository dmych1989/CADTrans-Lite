// Services/DwgWriter.cs
// Applies translated text to DXF files using raw DXF line-level replacement.
// v3.1: Replaced netDxf Load/Save with raw text replacement to preserve ALL
// DXF content. netDxf's Save drops entities it doesn't support (ACAD_TABLE,
// MULTILEADER, etc.), causing empty output files. Raw replacement avoids this
// by modifying the original file's text lines directly.

using System.Text;
using CADTransLite.Core.Models;
using CoreEntityType = CADTransLite.Core.Models.EntityType;

namespace CADTransLite.Core.Services;

/// <summary>
/// Writes translated text back into a DXF document using raw line-level replacement.
/// This approach copies the original DXF file and modifies text values at specific
/// line numbers, preserving ALL content including entity types that netDxf cannot
/// serialize (ACAD_TABLE, MULTILEADER, custom entities, etc.).
/// </summary>
public sealed class DwgWriter
{
    // -----------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------

    /// <summary>
    /// Applies translated text from <paramref name="mergedItems"/> to the DXF file at
    /// <paramref name="sourceFilePath"/> and saves the result as a new file.
    /// Uses raw DXF line-level replacement to preserve ALL original content.
    /// </summary>
    public (string outputFilePath, List<string> log) WriteBack(
        string sourceFilePath,
        List<TranslationItem> mergedItems,
        IProgress<(int current, int total, string message)>? progress = null,
        string suffix = "_translated")
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException($"找不到源 DXF 文件：{sourceFilePath}", sourceFilePath);

        var log = new List<string>();
        progress?.Report((0, 100, "Preparing DXF file…"));

        // ---------------------------------------------------------------
        // Step 1: Copy original DXF to output path (preserves ALL content)
        // ---------------------------------------------------------------
        string dir = Path.GetDirectoryName(sourceFilePath) ?? string.Empty;
        string nameWithoutExt = Path.GetFileNameWithoutExtension(sourceFilePath);
        string ext = Path.GetExtension(sourceFilePath);
        string outputPath = Path.Combine(dir, $"{nameWithoutExt}{suffix}{ext}");

        try
        {
            File.Copy(sourceFilePath, outputPath, overwrite: true);
            log.Add($"[INFO] Copied source to output: {outputPath}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"无法复制 DXF 文件到 '{outputPath}'：{ex.Message}", ex);
        }

        // ---------------------------------------------------------------
        // Step 2: Read the copied DXF file as line array
        // ---------------------------------------------------------------
        string[] lines;
        try
        {
            lines = DxfRawParser.ReadDxfFile(outputPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"无法读取 DXF 文件 '{outputPath}'：{ex.Message}", ex);
        }

        log.Add($"[INFO] Read {lines.Length} lines from output file.");

        // ---------------------------------------------------------------
        // Step 3: Parse all entity types from raw DXF
        // ---------------------------------------------------------------
        progress?.Report((5, 100, "Parsing DXF entities…"));

        var textEntities = DxfRawParser.ParseTextEntities(outputPath);
        var mtextEntities = DxfRawParser.ParseMTextEntities(outputPath);
        var attribEntities = DxfRawParser.ParseAttributeEntities(outputPath);

        // Build Handle → EntityInfo lookup dictionaries
        var textByHandle = new Dictionary<string, TextEntityInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in textEntities)
            textByHandle[t.Handle] = t;

        var mtextByHandle = new Dictionary<string, MTextEntityInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mtextEntities)
            mtextByHandle[m.Handle] = m;

        // Attribute key = "{InsertHandle}::{Tag}" (same format as TranslationItem.Handle)
        var attrByCompositeKey = new Dictionary<string, AttributeEntityInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in attribEntities)
            attrByCompositeKey[a.CompositeKey] = a;

        log.Add($"[INFO] Indexed {textByHandle.Count} TEXT, {mtextByHandle.Count} MTEXT, {attrByCompositeKey.Count} ATTRIB entities from raw DXF.");

        // ---------------------------------------------------------------
        // Step 4: Expand merged items into a flat list
        // ---------------------------------------------------------------
        var tableCellReplacements = new List<(string handle, int row, int col, string newText)>();
        var mleaderReplacements = new List<(string handle, int row, int col, string newText)>();

        var expandedItems = new List<TranslationItem>();
        foreach (var item in mergedItems)
        {
            if (item.MergedItems.Count > 0)
            {
                foreach (var original in item.MergedItems)
                {
                    var expanded = CloneItem(original);
                    expanded.TranslatedText = item.TranslatedText;
                    expandedItems.Add(expanded);
                }
            }
            else
            {
                expandedItems.Add(item);
            }
        }

        // ---------------------------------------------------------------
        // Step 5: Apply translations via raw line replacement
        // ---------------------------------------------------------------
        int total = expandedItems.Count;
        int processed = 0;
        int updated = 0;
        int skipped = 0;
        int notFound = 0;

        foreach (var item in expandedItems)
        {
            processed++;
            if (processed % 10 == 0)
                progress?.Report((10 + (80 * processed / total), 100, $"Writing back {processed}/{total}…"));

            // Skip items without translation — keep original text
            if (string.IsNullOrWhiteSpace(item.TranslatedText))
            {
                skipped++;
                continue;
            }

            string newText = item.TranslatedText!;

            switch (item.EntityType)
            {
                case CoreEntityType.Text:
                    if (ReplaceTextValue(lines, textByHandle, item.Handle, newText, log))
                        updated++;
                    else
                        notFound++;
                    break;

                case CoreEntityType.MText:
                    if (ReplaceMTextValue(lines, mtextByHandle, item, newText, log))
                        updated++;
                    else
                        notFound++;
                    break;

                case CoreEntityType.Attribute:
                    if (ReplaceAttributeValue(lines, attrByCompositeKey, item.Handle, newText, log))
                        updated++;
                    else
                        notFound++;
                    break;

                case CoreEntityType.TableCell:
                    tableCellReplacements.Add((item.Handle, item.TableRow, item.TableColumn, newText));
                    updated++;
                    log.Add($"[OK] TABLECELL Handle={item.Handle} R{item.TableRow}C{item.TableColumn} → \"{TruncateLog(newText)}\"");
                    break;

                case CoreEntityType.MLeader:
                    {
                        string mlHandle = item.Handle.EndsWith("::CTX") ? item.Handle[..^5] : item.Handle;
                        mleaderReplacements.Add((mlHandle, -1, -1, newText));
                        updated++;
                        log.Add($"[OK] MLEADER Handle={mlHandle} → \"{TruncateLog(newText)}\"");
                    }
                    break;

                default:
                    skipped++;
                    log.Add($"[SKIP] Unknown entity type for Handle={item.Handle}");
                    break;
            }
        }

        log.Add($"[INFO] Write-back summary: {updated} updated, {skipped} skipped, {notFound} not found.");

        // ---------------------------------------------------------------
        // Step 6: Save modified lines back to DXF file
        // ---------------------------------------------------------------
        progress?.Report((90, 100, "Saving DXF file…"));

        try
        {
            SaveDxfFile(outputPath, lines);
            log.Add($"[INFO] Saved output: {outputPath}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"无法保存 DXF 文件到 '{outputPath}'：{ex.Message}", ex);
        }

        // ---------------------------------------------------------------
        // Step 7: Apply ACAD_TABLE / MLEADER text replacements
        // These use DxfTextReplacer which operates on the saved file's raw lines
        // ---------------------------------------------------------------
        if (tableCellReplacements.Count > 0 || mleaderReplacements.Count > 0)
        {
            progress?.Report((92, 100, "Replacing TABLE/MLEADER text…"));
            try
            {
                var allReplacements = tableCellReplacements.Concat(mleaderReplacements).ToList();
                var (replaceUpdated, replaceNotFound, replaceLog) = DxfTextReplacer.Replace(outputPath, allReplacements);
                log.AddRange(replaceLog);
                log.Add($"[INFO] DXF text replacement: {replaceUpdated} updated, {replaceNotFound} not found.");
            }
            catch (Exception ex)
            {
                log.Add($"[WARN] DXF text replacement failed: {ex.Message}");
            }
        }

        progress?.Report((100, 100, "Done"));
        return (outputPath, log);
    }

    // -----------------------------------------------------------------
    // TEXT replacement
    // -----------------------------------------------------------------

    /// <summary>
    /// 替换 TEXT 实体的文本值（组码 1）。
    /// </summary>
    private static bool ReplaceTextValue(
        string[] lines,
        Dictionary<string, TextEntityInfo> lookup,
        string handle,
        string newText,
        List<string> log)
    {
        if (!lookup.TryGetValue(handle, out var info))
        {
            log.Add($"[WARN] TEXT Handle={handle} not found in raw DXF parse.");
            return false;
        }

        if (info.TextLineNumber < 0 || info.TextLineNumber >= lines.Length)
        {
            log.Add($"[WARN] TEXT Handle={handle} text line number {info.TextLineNumber} out of range.");
            return false;
        }

        lines[info.TextLineNumber] = newText;
        log.Add($"[OK] TEXT Handle={handle} → \"{TruncateLog(newText)}\" (line {info.TextLineNumber})");
        return true;
    }

    // -----------------------------------------------------------------
    // MTEXT replacement
    // -----------------------------------------------------------------

    /// <summary>
    /// 替换 MTEXT 实体的文本值。
    /// MTEXT 文本可能跨越多个组码 3（续行）和最后一个组码 1。
    /// 替换策略：清空所有组码 3 行，将完整新文本写入最后一个组码 1 行。
    /// </summary>
    private static bool ReplaceMTextValue(
        string[] lines,
        Dictionary<string, MTextEntityInfo> lookup,
        TranslationItem item,
        string translatedText,
        List<string> log)
    {
        if (!lookup.TryGetValue(item.Handle, out var info))
        {
            log.Add($"[WARN] MTEXT Handle={item.Handle} not found in raw DXF parse.");
            return false;
        }

        // Reconstruct the formatted MText value
        string restoredValue;
        try
        {
            restoredValue = MTextRebuilder.RebuildMtextContent(
                item.RawOriginalText, translatedText, info.RectangleWidth);
        }
        catch
        {
            // Fallback: use MTextCodec to restore format codes
            restoredValue = MTextCodec.RestoreFormatCodes(translatedText, item.FormatPlaceholders);
        }

        // Clear all group code 3 continuation lines (set to empty string)
        foreach (int lineNum in info.Group3LineNumbers)
        {
            if (lineNum >= 0 && lineNum < lines.Length)
                lines[lineNum] = string.Empty;
        }

        // Write the new value to the last group code 1 line
        if (info.LastGroup1LineNumber < 0 || info.LastGroup1LineNumber >= lines.Length)
        {
            log.Add($"[WARN] MTEXT Handle={item.Handle} group-1 line number {info.LastGroup1LineNumber} out of range.");
            return false;
        }

        lines[info.LastGroup1LineNumber] = restoredValue;
        log.Add($"[OK] MTEXT Handle={item.Handle} → \"{TruncateLog(restoredValue)}\" (line {info.LastGroup1LineNumber}, cleared {info.Group3LineNumbers.Count} continuation lines)");
        return true;
    }

    // -----------------------------------------------------------------
    // Attribute replacement
    // -----------------------------------------------------------------

    /// <summary>
    /// 替换 ATTRIB 实体的文本值（组码 1）。
    /// Handle 格式为 "{insertHandle}::{attrTag}"。
    /// </summary>
    private static bool ReplaceAttributeValue(
        string[] lines,
        Dictionary<string, AttributeEntityInfo> lookup,
        string compositeHandle,
        string newText,
        List<string> log)
    {
        if (!lookup.TryGetValue(compositeHandle, out var info))
        {
            log.Add($"[WARN] ATTR Handle={compositeHandle} not found in raw DXF parse.");
            return false;
        }

        if (info.TextLineNumber < 0 || info.TextLineNumber >= lines.Length)
        {
            log.Add($"[WARN] ATTR Handle={compositeHandle} text line number {info.TextLineNumber} out of range.");
            return false;
        }

        lines[info.TextLineNumber] = newText;
        log.Add($"[OK] ATTR Handle={compositeHandle} → \"{TruncateLog(newText)}\" (line {info.TextLineNumber})");
        return true;
    }

    // -----------------------------------------------------------------
    // File I/O
    // -----------------------------------------------------------------

    /// <summary>
    /// 将修改后的行数组保存回 DXF 文件。
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

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static TranslationItem CloneItem(TranslationItem src) => new()
    {
        Handle = src.Handle,
        EntityType = src.EntityType,
        RawOriginalText = src.RawOriginalText,
        OriginalText = src.OriginalText,
        TranslatedText = src.TranslatedText,
        FormatPlaceholders = new Dictionary<string, string>(src.FormatPlaceholders),
        LayerName = src.LayerName,
        ExcelRowIndex = src.ExcelRowIndex,
        CadHandles = src.CadHandles is null ? null : new List<string>(src.CadHandles),
        MergedItems = new List<TranslationItem>(src.MergedItems),
        BlockName = src.BlockName,
        AttributeTag = src.AttributeTag,
        TableRow = src.TableRow,
        TableColumn = src.TableColumn,
        FilterReason = src.FilterReason,
        CleanedText = src.CleanedText,
        Status = src.Status,
        Remark = src.Remark,
    };

    private static string TruncateLog(string text, int maxLen = 60) =>
        text.Length <= maxLen ? text : text[..maxLen] + "…";
}
