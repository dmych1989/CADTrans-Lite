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
        string suffix = "_translated",
        bool enableLayoutAdjust = true)
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

        // Build Handle → EntityInfo lookup dictionaries.
        // 注意：无句柄（组码5为空）的实体用行号合成键 "L<行号>"，与提取阶段赋予
        // TranslationItem.Handle 的合成键保持一致，确保回写时能正确定位。
        var textByHandle = new Dictionary<string, TextEntityInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in textEntities)
            textByHandle[string.IsNullOrEmpty(t.Handle) ? "L" + t.TextLineNumber : t.Handle] = t;

        var mtextByHandle = new Dictionary<string, MTextEntityInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mtextEntities)
            mtextByHandle[string.IsNullOrEmpty(m.Handle) ? "L" + m.LastGroup1LineNumber : m.Handle] = m;

        // Attribute key = "{InsertHandle}::{Tag}"；插入句柄为空时用行号合成键。
        var attrByCompositeKey = new Dictionary<string, AttributeEntityInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in attribEntities)
            attrByCompositeKey[string.IsNullOrEmpty(a.InsertHandle) ? "L" + a.TextLineNumber : a.CompositeKey] = a;

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
        var mtextSpecs = new List<MTextReplaceSpec>();

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
                    // A composite handle (insertHandle::tag) always denotes a block attribute,
                    // even if the item was imported as plain Text (e.g. from a 2-column Excel or a
                    // previous session). Route it through the attribute path so it isn't mis-routed
                    // to single-handle TEXT lookup and silently skipped.
                    if (item.Handle.Contains("::") &&
                        ReplaceAttributeValue(lines, attrByCompositeKey, item.Handle, newText, log))
                    {
                        updated++;
                    }
                    else if (ReplaceTextValue(lines, textByHandle, item.Handle, newText, log))
                        updated++;
                    else
                        notFound++;
                    break;

                case CoreEntityType.MText:
                    {
                        var spec = ReplaceMTextValue(mtextByHandle, item, newText, log);
                        if (spec != null) { mtextSpecs.Add(spec); updated++; }
                        else notFound++;
                        break;
                    }

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
        // Step 5.5: 将 MTEXT 的分块替换（组码 3 / 1）拼接进 lines。
        // 必须在单行的 TEXT/ATTRIB 循环之后执行，以免行号在循环内错位。
        // ---------------------------------------------------------------
        if (mtextSpecs.Count > 0)
        {
            mtextSpecs.Sort((a, b) => a.StartLine.CompareTo(b.StartLine));
            var newLines = new List<string>(lines.Length + mtextSpecs.Count * 2);
            int cursor = 0;
            foreach (var spec in mtextSpecs)
            {
                if (spec.StartLine < 0 || spec.EndLine >= lines.Length || spec.StartLine > spec.EndLine)
                {
                    log.Add($"[WARN] MTEXT splice span {spec.StartLine}..{spec.EndLine} out of range; skipped.");
                    continue;
                }
                // 写保护：文本块起始行的前一行必须是组码行，否则说明行号映射错位，跳过以免损坏结构
                if (spec.StartLine > 0 && !DxfRawParser.IsGroupCodeLine(lines[spec.StartLine - 1]))
                {
                    log.Add($"[WARN] MTEXT splice span {spec.StartLine}..{spec.EndLine} 起始行前不是组码行（'{lines[spec.StartLine - 1].Trim()}'），疑似映射错位，已跳过。");
                    continue;
                }
                log.Add(spec.LogMessage);

                // spec.StartLine 指向的是"值行"索引；其前一行 (StartLine-1) 才是组码行。
                // chunks 已经自带新的组码+值，因此前缀拷贝必须停在 StartLine-1 之前，
                // 否则会把原组码行也拷进去，与新生成的组码行重复，导致前一个组码失去对应值 -> DXF 损坏。
                int copyEnd = spec.StartLine - 1;
                for (int i = cursor; i < copyEnd; i++)
                    newLines.Add(lines[i]);

                // 复用原组码行的前导空格，使新组码行格式与原文一致
                string? codeLeader = null;
                if (spec.StartLine - 1 >= 0)
                {
                    string origCodeLine = lines[spec.StartLine - 1];
                    int lead = origCodeLine.Length - origCodeLine.TrimStart().Length;
                    codeLeader = origCodeLine.Substring(0, lead);
                }

                var chunks = spec.Chunks;
                if (chunks.Count == 0)
                {
                    // 防御：译文为空，保留一个(组码 + 空值)以避免结构损坏
                    string origCode = spec.StartLine - 1 >= 0 ? lines[spec.StartLine - 1].Trim() : "1";
                    chunks = new List<(string, string)> { (origCode, "") };
                }

                foreach (var (code, text) in chunks)
                {
                    string safeText = text.Replace("\r\n", "\\P").Replace("\r", "\\P").Replace("\n", "\\P");
                    newLines.Add(codeLeader + code);
                    newLines.Add(safeText);
                }
                cursor = spec.EndLine + 1;
            }
            for (int i = cursor; i < lines.Length; i++)
                newLines.Add(lines[i]);
            lines = newLines.ToArray();
        }

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

        // ---------------------------------------------------------------
        // Step 8: Layout adjustment (Phase 4 - V3-4.1)
        // Adjusts text height and MTEXT boundaries when translated text overflows.
        // ---------------------------------------------------------------
        if (enableLayoutAdjust)
        {
            progress?.Report((95, 100, "Adjusting layout…"));
            try
            {
                var (adjustedCount, adjustLog) = DxfLayoutAdjuster.AdjustLayout(outputPath, mergedItems, progress);
                log.AddRange(adjustLog);
                log.Add($"[INFO] Layout adjustment: {adjustedCount} entities adjusted.");
            }
            catch (Exception ex)
            {
                log.Add($"[WARN] Layout adjustment failed: {ex.Message}");
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

        // 写保护：目标行若是组码行，说明行号映射错位，跳过以免破坏整图结构
        if (DxfRawParser.IsGroupCodeLine(lines[info.TextLineNumber]))
        {
            log.Add($"[WARN] TEXT Handle={handle} 跳过：目标行 {info.TextLineNumber} 是组码行（'{lines[info.TextLineNumber].Trim()}'），疑似映射错位，已跳过以避免损坏文件。");
            return false;
        }

        lines[info.TextLineNumber] = DxfRawParser.SanitizeSingleLineText(newText);
        log.Add($"[OK] TEXT Handle={handle} → \"{TruncateLog(newText)}\" (line {info.TextLineNumber})");
        return true;
    }

    // -----------------------------------------------------------------
    // MTEXT replacement
    // -----------------------------------------------------------------

    /// <summary>
    /// MTEXT 回写规格：不直接改 lines，而是记录要替换的文本块区间与分块后的组码行，
    /// 由调用方在循环结束后统一拼接，避免行号在循环内错位。
    /// </summary>
    private sealed class MTextReplaceSpec
    {
        public int StartLine;            // 文本块起始行（首个组码 3，或组码 1）
        public int EndLine;              // 文本块结束行（最后一个组码 1）
        public List<(string Code, string Text)> Chunks = new();
        public string LogMessage = string.Empty;
    }

    /// <summary>
    /// 计算 MTEXT 实体的文本替换规格（不直接写文件）。
    /// DXF 要求单值行不超过约 256 字符：超长译文必须切成组码 3 / 1 的若干行块，
    /// 否则 CAD 软件会将该图形判为损坏而拒绝打开。
    /// </summary>
    private static MTextReplaceSpec? ReplaceMTextValue(
        Dictionary<string, MTextEntityInfo> lookup,
        TranslationItem item,
        string translatedText,
        List<string> log)
    {
        if (!lookup.TryGetValue(item.Handle, out var info))
        {
            log.Add($"[WARN] MTEXT Handle={item.Handle} not found in raw DXF parse.");
            return null;
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

        if (info.LastGroup1LineNumber < 0)
        {
            log.Add($"[WARN] MTEXT Handle={item.Handle} has no group-1 text line.");
            return null;
        }

        // 按 DXF 规范把超长 MTEXT 切成组码 3 / 1 的若干行块
        var chunks = MTextRebuilder.SplitMTextToDxfChunks(restoredValue, 250);

        int startLine = (info.Group3LineNumbers != null && info.Group3LineNumbers.Count > 0)
            ? info.Group3LineNumbers[0]
            : info.LastGroup1LineNumber;
        int endLine = info.LastGroup1LineNumber;

        var spec = new MTextReplaceSpec
        {
            StartLine = startLine,
            EndLine = endLine,
            LogMessage = $"[OK] MTEXT Handle={item.Handle} → \"{TruncateLog(restoredValue)}\" (chunked into {chunks.Count} DXF line(s))"
        };
        spec.Chunks = chunks;
        return spec;
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

        // 写保护：目标行若是组码行，说明行号映射错位，跳过以免破坏整图结构
        if (DxfRawParser.IsGroupCodeLine(lines[info.TextLineNumber]))
        {
            log.Add($"[WARN] ATTR Handle={compositeHandle} 跳过：目标行 {info.TextLineNumber} 是组码行（'{lines[info.TextLineNumber].Trim()}'），疑似映射错位，已跳过以避免损坏文件。");
            return false;
        }

        lines[info.TextLineNumber] = DxfRawParser.SanitizeSingleLineText(newText);
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
        // 用与读取时一致的编码写回（探测文件真实编码，保证中文不乱码、结构不偏移）
        var enc = DxfRawParser.DetectDxfEncoding(filePath);
        File.WriteAllLines(filePath, lines, enc);
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
        AiFilterDecision = src.AiFilterDecision,
        AiFilterReason = src.AiFilterReason,
    };

    private static string TruncateLog(string text, int maxLen = 60) =>
        text.Length <= maxLen ? text : text[..maxLen] + "…";
}
