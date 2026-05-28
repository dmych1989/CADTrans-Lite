// Services/ExcelHandler.cs
// Exports TranslationItem lists to Excel and re-imports them.
// Phase 3: supports both 2-column legacy and 11-column rich format.
// Auto-detects format on import; Handle-based matching for rich format.

using CADTransLite.Core.Models;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;

namespace CADTransLite.Core.Services;

/// <summary>
/// Exports and imports translation data as .xlsx files using EPPlus 7.
/// Supports two formats:
///   - Legacy 2-column: A=原文, B=译文
///   - Rich 11-column: Handle/类型/图层/块名/属性标签/表格位置/原文/清洗文本/译文/状态/备注
/// Format is controlled by <see cref="ImportSettings.UseRichExcelFormat"/>.
/// Import auto-detects format via <see cref="IsRichFormat"/>.
/// </summary>
public sealed class ExcelHandler
{
    // ────────────────────────────────────────────────────────────────────
    // Legacy 2-column constants
    // ────────────────────────────────────────────────────────────────────
    private const int ColLegacyOriginal = 1;       // A — 原文 (RawOriginalText)
    private const int ColLegacyTranslated = 2;     // B — 译文 (Translation)

    private const string HeaderLegacyOriginal = "原文";
    private const string HeaderLegacyTranslated = "译文";

    // ────────────────────────────────────────────────────────────────────
    // Rich 11-column constants (1-based, EPPlus convention)
    // ────────────────────────────────────────────────────────────────────
    private const int ColHandle       = 1;   // A — Handle
    private const int ColEntityType   = 2;   // B — 类型
    private const int ColLayerName    = 3;   // C — 图层
    private const int ColBlockName    = 4;   // D — 块名
    private const int ColAttributeTag = 5;   // E — 属性标签
    private const int ColTableCellRef = 6;   // F — 表格位置
    private const int ColOriginalText = 7;   // G — 原始文本
    private const int ColCleanedText  = 8;   // H — 清洗文本
    private const int ColTranslated   = 9;   // I — 译文
    private const int ColStatus       = 10;  // J — 状态
    private const int ColRemark       = 11;  // K — 备注

    private const int RichColumnCount = 11;

    // Row at which data starts (row 1 is the header).
    private const int DataStartRow = 2;

    // Colors
    private static readonly Color MetaBgColor   = Color.FromArgb(255, 240, 240, 240);  // Light gray
    private static readonly Color CleanBgColor   = Color.FromArgb(255, 245, 245, 245);  // Lighter gray for cleaned text
    private static readonly Color TransBgColor   = Color.FromArgb(255, 255, 230, 153);  // Light yellow

    // -----------------------------------------------------------------------
    // Static constructor — configure EPPlus license once.
    // -----------------------------------------------------------------------
    static ExcelHandler()
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    // -----------------------------------------------------------------------
    // Public API — Export
    // -----------------------------------------------------------------------

    /// <summary>
    /// Exports a list of <see cref="TranslationItem"/>s to an Excel file.
    /// Format is determined by <paramref name="settings"/>.<see cref="ImportSettings.UseRichExcelFormat"/>.
    /// </summary>
    public void Export(List<TranslationItem> items, string outputPath, ImportSettings settings)
    {
        if (settings.UseRichExcelFormat)
            ExportRichFormat(items, outputPath);
        else
            ExportLegacyFormat(items, outputPath);
    }

    /// <summary>
    /// Legacy export signature (2-column format). Kept for backward compatibility.
    /// </summary>
    public void Export(List<TranslationItem> items, string outputPath)
    {
        ExportLegacyFormat(items, outputPath);
    }

    // -----------------------------------------------------------------------
    // Public API — Import
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reads translated text from an Excel file. Auto-detects 2-column vs 11-column format.
    /// Rich format: Handle-based matching with row-index fallback.
    /// Legacy format: row-index matching with original-text validation.
    /// </summary>
    public (List<TranslationItem>? items, string? error) Import(
        string excelPath,
        List<TranslationItem> originalItems)
    {
        if (!File.Exists(excelPath))
            return (null, $"找不到 Excel 文件：{excelPath}");

        using var package = new ExcelPackage(new FileInfo(excelPath));
        var ws = package.Workbook.Worksheets.FirstOrDefault();
        if (ws is null)
            return (null, "Excel 工作簿中没有工作表。");

        int excelRows = (ws.Dimension?.Rows ?? 1) - 1;

        // Auto-detect format
        bool isRich = IsRichFormatWorksheet(ws);

        if (isRich)
        {
            // Rich format always needs originalItems for matching
            if (originalItems is null || originalItems.Count == 0)
                return (null, "多列格式导入需要提供原始提取条目。");

            return ImportRichFormat(ws, originalItems, excelRows);
        }
        else
        {
            return ImportLegacyFormat(ws, originalItems, excelRows);
        }
    }

    // -----------------------------------------------------------------------
    // Public API — Translation-only export (DocuTranslate-style)
    // -----------------------------------------------------------------------

    /// <summary>
    /// 导出纯翻译对照表：仅2列(原文/译文)，只含已翻译条目，按CleanedText去重。
    /// 类似 DocuTranslate 的 get_translated_terms_csv_document。
    /// </summary>
    public void ExportTranslationOnly(List<TranslationItem> items, string outputPath)
    {
        if (items is null || items.Count == 0)
            throw new ArgumentException("没有可导出的翻译条目。", nameof(items));

        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Filter: only items with non-empty TranslatedText
        var translatedItems = items
            .Where(i => !string.IsNullOrWhiteSpace(i.TranslatedText))
            .ToList();

        if (translatedItems.Count == 0)
            throw new ArgumentException("没有已翻译的条目可导出。请先翻译后再使用此功能。", nameof(items));

        // Deduplicate by CleanedText ?? OriginalText (keep first occurrence)
        var seen = new HashSet<string>();
        var dedupedItems = new List<TranslationItem>();
        foreach (var item in translatedItems)
        {
            string key = (item.CleanedText ?? item.OriginalText ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(key))
                continue;
            if (seen.Add(key))
                dedupedItems.Add(item);
        }

        using var package = new ExcelPackage();
        var ws = package.Workbook.Worksheets.Add("TranslationOnly");

        // Header row
        ws.Cells[1, ColLegacyOriginal].Value = HeaderLegacyOriginal;
        ws.Cells[1, ColLegacyTranslated].Value = HeaderLegacyTranslated;
        StyleHeader(ws.Cells[1, ColLegacyOriginal]);
        StyleHeader(ws.Cells[1, ColLegacyTranslated]);

        // Data rows
        for (int i = 0; i < dedupedItems.Count; i++)
        {
            int row = DataStartRow + i;
            var item = dedupedItems[i];

            // Column A: Clean, human-readable original text
            string rawText = string.IsNullOrEmpty(item.RawOriginalText)
                ? item.OriginalText
                : item.RawOriginalText;
            string cleanText = MTextCodec.StripForTranslation(rawText);
            ws.Cells[row, ColLegacyOriginal].Value = cleanText;

            // Column B: Translated text (also strip format codes)
            string translatedClean = MTextCodec.StripForTranslation(item.TranslatedText!);
            ws.Cells[row, ColLegacyTranslated].Value = translatedClean;

            // Enable text wrapping on both columns
            ws.Cells[row, ColLegacyOriginal].Style.WrapText = true;
            ws.Cells[row, ColLegacyTranslated].Style.WrapText = true;

            // Alignment: top + left
            ws.Cells[row, ColLegacyOriginal].Style.VerticalAlignment = ExcelVerticalAlignment.Top;
            ws.Cells[row, ColLegacyOriginal].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            ws.Cells[row, ColLegacyTranslated].Style.VerticalAlignment = ExcelVerticalAlignment.Top;
            ws.Cells[row, ColLegacyTranslated].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;

            // Light-yellow background on translation column
            ws.Cells[row, ColLegacyTranslated].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[row, ColLegacyTranslated].Style.Fill.BackgroundColor
                .SetColor(Color.FromArgb(255, 255, 230, 153));
        }

        // Column widths
        ws.Column(ColLegacyOriginal).Width = 75;
        ws.Column(ColLegacyTranslated).Width = 75;

        // Freeze header row
        ws.View.FreezePanes(DataStartRow, 1);

        // No cell protection
        ws.Protection.IsProtected = false;

        package.SaveAs(new FileInfo(outputPath));
    }

    // -----------------------------------------------------------------------
    // Public API — Format detection
    // -----------------------------------------------------------------------

    /// <summary>
    /// Detects whether an Excel file uses the 11-column rich format.
    /// Checks if the first header cell is "Handle".
    /// </summary>
    public static bool IsRichFormat(string excelPath)
    {
        if (!File.Exists(excelPath))
            return false;

        try
        {
            using var package = new ExcelPackage(new FileInfo(excelPath));
            var ws = package.Workbook.Worksheets.FirstOrDefault();
            if (ws is null) return false;
            return IsRichFormatWorksheet(ws);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks a worksheet's first header cell to determine format.
    /// </summary>
    private static bool IsRichFormatWorksheet(ExcelWorksheet ws)
    {
        string firstHeader = ws.Cells[1, 1].GetValue<string>()?.Trim() ?? string.Empty;
        return string.Equals(firstHeader, "Handle", StringComparison.OrdinalIgnoreCase);
    }

    // =======================================================================
    // RICH FORMAT (11-column)
    // =======================================================================

    private void ExportRichFormat(List<TranslationItem> items, string outputPath)
    {
        if (items is null || items.Count == 0)
            throw new ArgumentException("没有可导出的翻译条目。", nameof(items));

        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var package = new ExcelPackage();
        var ws = package.Workbook.Worksheets.Add("Translations");

        // ── Header row ──────────────────────────────────────────────────
        var columns = ExcelFormatConfig.RichColumns;
        for (int c = 0; c < columns.Count; c++)
        {
            var col = columns[c];
            ws.Cells[1, col.ColumnIndex].Value = col.HeaderText;
            StyleHeader(ws.Cells[1, col.ColumnIndex]);
        }

        // ── Data rows ───────────────────────────────────────────────────
        for (int i = 0; i < items.Count; i++)
        {
            int row = DataStartRow + i;
            var item = items[i];

            // Column A: Handle — use the item Handle directly
            ws.Cells[row, ColHandle].Value = item.Handle;

            // Column B: EntityType
            ws.Cells[row, ColEntityType].Value = item.EntityType.ToString();

            // Column C: LayerName
            ws.Cells[row, ColLayerName].Value = item.LayerName;

            // Column D: BlockName (optional)
            ws.Cells[row, ColBlockName].Value = item.BlockName ?? string.Empty;

            // Column E: AttributeTag (optional)
            ws.Cells[row, ColAttributeTag].Value = item.AttributeTag ?? string.Empty;

            // Column F: TableCellRef (optional)
            ws.Cells[row, ColTableCellRef].Value = item.TableCellRef;

            // Column G: OriginalText — MTextCodec.StripForTranslation result
            string rawText = string.IsNullOrEmpty(item.RawOriginalText)
                ? item.OriginalText
                : item.RawOriginalText;
            string cleanText = MTextCodec.StripForTranslation(rawText);
            ws.Cells[row, ColOriginalText].Value = cleanText;

            // Column H: CleanedText (optional, further cleaned)
            ws.Cells[row, ColCleanedText].Value = item.CleanedText ?? string.Empty;

            // Column I: TranslatedText (also strip format codes)
            string translatedClean = string.IsNullOrEmpty(item.TranslatedText)
                ? string.Empty
                : MTextCodec.StripForTranslation(item.TranslatedText);
            ws.Cells[row, ColTranslated].Value = translatedClean;

            // Column J: Status
            string status = item.Status ?? string.Empty;
            if (string.IsNullOrEmpty(status))
                status = "pending";
            ws.Cells[row, ColStatus].Value = status;

            // Column K: Remark
            ws.Cells[row, ColRemark].Value = item.Remark ?? string.Empty;

            // ── Cell styling ────────────────────────────────────────────
            // Metadata columns: gray background
            StyleMetadataCell(ws.Cells[row, ColHandle]);
            StyleMetadataCell(ws.Cells[row, ColEntityType]);
            StyleMetadataCell(ws.Cells[row, ColLayerName]);
            StyleMetadataCell(ws.Cells[row, ColBlockName]);
            StyleMetadataCell(ws.Cells[row, ColAttributeTag]);
            StyleMetadataCell(ws.Cells[row, ColTableCellRef]);

            // Cleaned text column: light gray background (read-only reference)
            StyleCleanedCell(ws.Cells[row, ColCleanedText]);

            // Original text: white background, top-left
            StyleEditableCell(ws.Cells[row, ColOriginalText]);

            // Translated text: light-yellow background
            StyleTranslatedCell(ws.Cells[row, ColTranslated]);

            // Status: conditional formatting-like coloring
            StyleStatusCell(ws.Cells[row, ColStatus], status);

            // Remark: white background
            StyleEditableCell(ws.Cells[row, ColRemark]);

            // Record ExcelRowIndex for reference
            item.ExcelRowIndex = row;
        }

        // ── Handle column: add comment "请勿修改" ─────────────────────
        for (int i = 0; i < items.Count; i++)
        {
            int row = DataStartRow + i;
            ws.Cells[row, ColHandle].AddComment("此列为标识列，请勿修改。修改后系统将按行号降级匹配。", "CADTrans");
        }

        // ── Column widths ───────────────────────────────────────────────
        for (int c = 0; c < columns.Count; c++)
        {
            ws.Column(columns[c].ColumnIndex).Width = columns[c].Width;
        }

        // ── Freeze panes: freeze header row + first 6 metadata columns ─
        ws.View.FreezePanes(DataStartRow, ColOriginalText);

        // No cell protection
        ws.Protection.IsProtected = false;

        package.SaveAs(new FileInfo(outputPath));
    }

    private (List<TranslationItem>? items, string? error) ImportRichFormat(
        ExcelWorksheet ws, List<TranslationItem> originalItems, int dataRows)
    {
        // Build handle → excel-row mapping from the Excel file
        var handleToRow = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int r = 0; r < dataRows; r++)
        {
            int row = DataStartRow + r;
            string? handleVal = ws.Cells[row, ColHandle].GetValue<string>()?.Trim();
            if (!string.IsNullOrEmpty(handleVal))
                handleToRow[handleVal] = row;
        }

        var result = originalItems.Select(CloneItem).ToList();
        int warnCount = 0;

        // Track which result indices have been matched by Handle
        var matchedResultIndices = new HashSet<int>();
        // Track which Excel rows have been consumed by Handle matching
        var matchedExcelRows = new HashSet<int>();

        // Pass 1: Handle-based matching
        for (int i = 0; i < result.Count; i++)
        {
            var item = result[i];

            // Try to match by Handle in the CadHandles list
            string? matchedHandle = null;
            if (item.CadHandles is not null)
            {
                foreach (var h in item.CadHandles)
                {
                    if (handleToRow.ContainsKey(h))
                    {
                        matchedHandle = h;
                        break;
                    }
                }
            }

            // Also try item.Handle directly
            if (matchedHandle is null && handleToRow.ContainsKey(item.Handle))
                matchedHandle = item.Handle;

            if (matchedHandle is not null && handleToRow.TryGetValue(matchedHandle, out int excelRow))
            {
                ApplyRichRowData(ws, excelRow, item);
                matchedResultIndices.Add(i);
                matchedExcelRows.Add(excelRow);
            }
        }

        // Pass 2: Row-index fallback for items without Handle match
        var unmatchedIndices = new List<int>();
        for (int i = 0; i < result.Count; i++)
        {
            if (!matchedResultIndices.Contains(i))
                unmatchedIndices.Add(i);
        }

        if (unmatchedIndices.Count > 0)
        {
            // Find unused Excel rows (rows not matched by Handle)
            var unusedRows = new List<int>();
            for (int r = 0; r < dataRows; r++)
            {
                int row = DataStartRow + r;
                if (!matchedExcelRows.Contains(row))
                    unusedRows.Add(row);
            }

            // Match unmatched items to unused rows by order
            int matchCount = Math.Min(unmatchedIndices.Count, unusedRows.Count);
            for (int j = 0; j < matchCount; j++)
            {
                ApplyRichRowData(ws, unusedRows[j], result[unmatchedIndices[j]]);
                warnCount++;
            }

            if (matchCount < unmatchedIndices.Count)
            {
                ErrorLogger.Instance.Warn("ExcelHandler",
                    $"行号降级匹配：{unmatchedIndices.Count - matchCount} 项无匹配行，已跳过。");
            }
        }

        if (warnCount > 0)
            ErrorLogger.Instance.Info("ExcelHandler",
                $"导入完成：{warnCount} 项使用行号降级匹配。");

        return (result, null);
    }

    /// <summary>
    /// Applies data from a rich-format Excel row to a TranslationItem.
    /// </summary>
    private static void ApplyRichRowData(ExcelWorksheet ws, int row, TranslationItem item)
    {
        // Read translated text (column I)
        string cellTranslated = ws.Cells[row, ColTranslated].GetValue<string>() ?? string.Empty;
        item.TranslatedText = string.IsNullOrWhiteSpace(cellTranslated)
            ? null
            : cellTranslated.Trim();

        // Read status (column J) — optional update
        string cellStatus = ws.Cells[row, ColStatus].GetValue<string>()?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(cellStatus))
            item.Status = cellStatus;

        // Read remark (column K) — optional update
        string cellRemark = ws.Cells[row, ColRemark].GetValue<string>()?.Trim() ?? string.Empty;
        item.Remark = string.IsNullOrEmpty(cellRemark) ? null : cellRemark;

        // Warn if original text (column G) was modified by user
        string cellOriginal = ws.Cells[row, ColOriginalText].GetValue<string>()?.Trim() ?? string.Empty;
        string expectedRaw = string.IsNullOrEmpty(item.RawOriginalText)
            ? item.OriginalText
            : item.RawOriginalText;
        string expected = MTextCodec.StripForTranslation(expectedRaw);
        if (!string.Equals(cellOriginal, expected.Trim(), StringComparison.Ordinal))
        {
            ErrorLogger.Instance.Warn("ExcelHandler",
                $"第 {row} 行原文被修改：期望 \"{TruncateLog(expected)}\"，" +
                $"实际 \"{TruncateLog(cellOriginal)}\"。多列格式下仅警告，不报错。");
        }

        item.ExcelRowIndex = row;
    }

    // =======================================================================
    // LEGACY FORMAT (2-column)
    // =======================================================================

    private void ExportLegacyFormat(List<TranslationItem> items, string outputPath)
    {
        if (items is null || items.Count == 0)
            throw new ArgumentException("没有可导出的翻译条目。", nameof(items));

        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var package = new ExcelPackage();
        var ws = package.Workbook.Worksheets.Add("Translations");

        // Header row
        ws.Cells[1, ColLegacyOriginal].Value = HeaderLegacyOriginal;
        ws.Cells[1, ColLegacyTranslated].Value = HeaderLegacyTranslated;
        StyleHeader(ws.Cells[1, ColLegacyOriginal]);
        StyleHeader(ws.Cells[1, ColLegacyTranslated]);

        // Data rows
        for (int i = 0; i < items.Count; i++)
        {
            int row = DataStartRow + i;
            var item = items[i];

            // Column A: Clean, human-readable text
            string rawText = string.IsNullOrEmpty(item.RawOriginalText)
                ? item.OriginalText
                : item.RawOriginalText;
            string cleanText = MTextCodec.StripForTranslation(rawText);
            ws.Cells[row, ColLegacyOriginal].Value = cleanText;

            // Column B: translated text
            string translatedClean = string.IsNullOrEmpty(item.TranslatedText)
                ? string.Empty
                : MTextCodec.StripForTranslation(item.TranslatedText);
            ws.Cells[row, ColLegacyTranslated].Value = translatedClean;

            // Enable text wrapping on both columns
            ws.Cells[row, ColLegacyOriginal].Style.WrapText = true;
            ws.Cells[row, ColLegacyTranslated].Style.WrapText = true;

            // Alignment: top + left
            ws.Cells[row, ColLegacyOriginal].Style.VerticalAlignment = ExcelVerticalAlignment.Top;
            ws.Cells[row, ColLegacyOriginal].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            ws.Cells[row, ColLegacyTranslated].Style.VerticalAlignment = ExcelVerticalAlignment.Top;
            ws.Cells[row, ColLegacyTranslated].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;

            // Light-yellow background on translation column
            ws.Cells[row, ColLegacyTranslated].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[row, ColLegacyTranslated].Style.Fill.BackgroundColor
                .SetColor(Color.FromArgb(255, 255, 230, 153));

            item.ExcelRowIndex = row;
        }

        // Column widths
        ws.Column(ColLegacyOriginal).Width = 75;
        ws.Column(ColLegacyTranslated).Width = 75;

        // Freeze header row
        ws.View.FreezePanes(DataStartRow, 1);

        // No cell protection
        ws.Protection.IsProtected = false;

        package.SaveAs(new FileInfo(outputPath));
    }

    private (List<TranslationItem>? items, string? error) ImportLegacyFormat(
        ExcelWorksheet ws, List<TranslationItem>? originalItems, int dataRows)
    {
        // Standalone mode: read 2 columns into new items
        if (originalItems is null)
        {
            var standaloneItems = new List<TranslationItem>();
            for (int i = 0; i < dataRows; i++)
            {
                int row = DataStartRow + i;
                string cellOriginal = ws.Cells[row, ColLegacyOriginal].GetValue<string>() ?? string.Empty;
                string cellTranslated = ws.Cells[row, ColLegacyTranslated].GetValue<string>() ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(cellOriginal))
                {
                    standaloneItems.Add(new TranslationItem
                    {
                        OriginalText = cellOriginal.Trim(),
                        TranslatedText = string.IsNullOrWhiteSpace(cellTranslated)
                            ? null
                            : cellTranslated.Trim(),
                    });
                }
            }
            return (standaloneItems, null);
        }

        // Validation mode: match against originalItems
        if (dataRows != originalItems.Count)
        {
            // Degraded matching: when row counts don't match (e.g., user switched
            // from 11-column to 2-column format, or manually added/deleted rows),
            // match as many rows as possible by order instead of rejecting outright.
            ErrorLogger.Instance.Warn("ExcelHandler",
                $"行数不匹配：Excel 有 {dataRows} 行数据，原始文件有 {originalItems.Count} 项。" +
                "将按顺序降级匹配。");

            var degradedResult = originalItems.Select(CloneItem).ToList();
            int matchCount = Math.Min(dataRows, degradedResult.Count);

            for (int i = 0; i < matchCount; i++)
            {
                int row = DataStartRow + i;
                string cellTranslated = ws.Cells[row, ColLegacyTranslated].GetValue<string>() ?? string.Empty;
                degradedResult[i].TranslatedText = string.IsNullOrWhiteSpace(cellTranslated)
                    ? null
                    : cellTranslated.Trim();
            }

            return (degradedResult, null);
        }

        var result = originalItems.Select(CloneItem).ToList();

        for (int i = 0; i < result.Count; i++)
        {
            int row = DataStartRow + i;

            // Validate original text (column A) has not been tampered with
            string cellOriginal = ws.Cells[row, ColLegacyOriginal].GetValue<string>() ?? string.Empty;
            string expectedRaw = string.IsNullOrEmpty(result[i].RawOriginalText)
                ? result[i].OriginalText
                : result[i].RawOriginalText;
            string expected = MTextCodec.StripForTranslation(expectedRaw);

            if (!string.Equals(cellOriginal.Trim(), expected.Trim(), StringComparison.Ordinal))
            {
                return (null,
                    $"第 {row} 行的原文被修改。" +
                    $"期望: \"{TruncateLog(expected)}\" " +
                    $"实际: \"{TruncateLog(cellOriginal)}\"。请不要修改原文列。");
            }

            // Read translation from column B
            string cellTranslated = ws.Cells[row, ColLegacyTranslated].GetValue<string>() ?? string.Empty;
            result[i].TranslatedText = string.IsNullOrWhiteSpace(cellTranslated)
                ? null
                : cellTranslated.Trim();
        }

        return (result, null);
    }

    // =======================================================================
    // Cell styling helpers
    // =======================================================================

    private static void StyleHeader(ExcelRange cell)
    {
        cell.Style.Font.Bold = true;
        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
        cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 189, 189, 189));
        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        cell.Style.Border.Bottom.Style = ExcelBorderStyle.Medium;
    }

    private static void StyleMetadataCell(ExcelRange cell)
    {
        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
        cell.Style.Fill.BackgroundColor.SetColor(MetaBgColor);
        cell.Style.VerticalAlignment = ExcelVerticalAlignment.Top;
        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
        cell.Style.WrapText = true;
    }

    private static void StyleCleanedCell(ExcelRange cell)
    {
        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
        cell.Style.Fill.BackgroundColor.SetColor(CleanBgColor);
        cell.Style.VerticalAlignment = ExcelVerticalAlignment.Top;
        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
        cell.Style.WrapText = true;
    }

    private static void StyleEditableCell(ExcelRange cell)
    {
        // White background (default)
        cell.Style.VerticalAlignment = ExcelVerticalAlignment.Top;
        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
        cell.Style.WrapText = true;
    }

    private static void StyleTranslatedCell(ExcelRange cell)
    {
        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
        cell.Style.Fill.BackgroundColor.SetColor(TransBgColor);
        cell.Style.VerticalAlignment = ExcelVerticalAlignment.Top;
        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
        cell.Style.WrapText = true;
    }

    private static void StyleStatusCell(ExcelRange cell, string status)
    {
        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        cell.Style.VerticalAlignment = ExcelVerticalAlignment.Top;

        switch (status.ToLowerInvariant())
        {
            case "translated":
                cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 200, 230, 201)); // Light green
                break;
            case "skipped":
                cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 255, 224, 178)); // Light orange
                break;
            case "error":
                cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 255, 205, 210)); // Light red
                break;
            default: // pending
                cell.Style.Fill.BackgroundColor.SetColor(MetaBgColor);
                break;
        }
    }

    // =======================================================================
    // Clone helper
    // =======================================================================

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
        // v3.0 Phase 2 fields
        BlockName = src.BlockName,
        AttributeTag = src.AttributeTag,
        TableRow = src.TableRow,
        TableColumn = src.TableColumn,
        FilterReason = src.FilterReason,
        CleanedText = src.CleanedText,
        Status = src.Status,
        Remark = src.Remark,
    };

    private static string TruncateLog(string text, int maxLen = 40) =>
        text.Length <= maxLen ? text : text[..maxLen] + "…";
}
