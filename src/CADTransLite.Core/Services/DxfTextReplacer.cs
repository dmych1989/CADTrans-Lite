// Services/DxfTextReplacer.cs
// DXF text replacement engine for ACAD_TABLE and MULTILEADER entities.
// Phase 2: Performs precise line-number-based text replacement on DXF files
// after netDxf has saved its output (since netDxf cannot handle these entity types).

using System.Text;
using CADTransLite.Core.Models;

namespace CADTransLite.Core.Services;

/// <summary>
/// DXF 文本精准替换器，用于 ACAD_TABLE 和 MULTILEADER 的回写。
/// 通过行号精确定位组码值行进行替换，避免字符串搜索替换导致的误替换。
/// </summary>
public static class DxfTextReplacer
{
    // -----------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------

    /// <summary>
    /// 在 DXF 文件中执行文本替换。
    /// </summary>
    /// <param name="dxfFilePath">DXF 文件路径（netDxf 已保存的输出文件）。</param>
    /// <param name="replacements">
    /// 替换列表。每项为 (handle, row, col, newText)。
    /// ACAD_TABLE: handle=tableHandle, row/col=单元格坐标。
    /// MLEADER: handle=mleaderHandle, row=-1, col=-1。
    /// </param>
    /// <param name="progress">可选进度回调。</param>
    /// <returns>
    /// (updatedCount, notFoundCount, logEntries)。
    /// </returns>
    public static (int updatedCount, int notFoundCount, List<string> log) Replace(
        string dxfFilePath,
        List<(string handle, int row, int col, string newText)> replacements,
        IProgress<(int current, int total, string message)>? progress = null)
    {
        var log = new List<string>();
        int updated = 0;
        int notFound = 0;

        if (replacements == null || replacements.Count == 0)
            return (0, 0, log);

        if (!File.Exists(dxfFilePath))
        {
            log.Add($"[WARN] DXF 文件不存在: {dxfFilePath}");
            return (0, replacements.Count, log);
        }

        // Read entire DXF file as lines
        string[] lines;
        try
        {
            lines = ReadDxfFile(dxfFilePath);
        }
        catch (Exception ex)
        {
            log.Add($"[WARN] 无法读取 DXF 文件: {ex.Message}");
            return (0, replacements.Count, log);
        }

        // Build lookup from raw parser for line number resolution
        var tableData = DxfRawParser.ParseAcadTables(dxfFilePath);
        var mleaderData = DxfRawParser.ParseMultiLeaders(dxfFilePath);

        // Build handle → entity lookups
        var tableByHandle = new Dictionary<string, AcadTableData>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tableData)
        {
            if (!string.IsNullOrEmpty(t.Handle))
                tableByHandle[t.Handle] = t;
        }

        var mleaderByHandle = new Dictionary<string, MultiLeaderData>(StringComparer.OrdinalIgnoreCase);
        foreach (var ml in mleaderData)
        {
            if (!string.IsNullOrEmpty(ml.Handle))
                mleaderByHandle[ml.Handle] = ml;
        }

        // Process each replacement
        int total = replacements.Count;
        int processed = 0;

        foreach (var (handle, row, col, newText) in replacements)
        {
            processed++;
            progress?.Report((processed, total, $"Replacing text {processed}/{total}…"));

            bool replaced = false;

            // ACAD_TABLE: handle is the table handle, row/col specify the cell
            if (row >= 0 && col >= 0)
            {
                replaced = ReplaceTableCell(lines, tableByHandle, handle, row, col, newText, log);
            }
            // MULTILEADER: row=-1, col=-1
            else if (row < 0 && col < 0)
            {
                replaced = ReplaceMLeaderText(lines, mleaderByHandle, handle, newText, log);
            }

            if (replaced)
                updated++;
            else
                notFound++;
        }

        // Save modified file
        try
        {
            SaveDxfFile(dxfFilePath, lines);
            log.Add($"[INFO] DXF text replacement saved: {updated} updated, {notFound} not found.");
        }
        catch (Exception ex)
        {
            log.Add($"[WARN] 无法保存 DXF 文件: {ex.Message}");
            return (0, replacements.Count, log);
        }

        return (updated, notFound, log);
    }

    // -----------------------------------------------------------------
    // ACAD_TABLE cell replacement
    // -----------------------------------------------------------------

    /// <summary>
    /// 替换 ACAD_TABLE 中指定单元格的文本。
    /// 通过行号精确定位组码 1/302 的值行进行替换。
    /// </summary>
    private static bool ReplaceTableCell(
        string[] lines,
        Dictionary<string, AcadTableData> tableByHandle,
        string handle,
        int row,
        int col,
        string newText,
        List<string> log)
    {
        // The handle from DwgWriter is in format "{tableHandle}::R{row}::C{col}"
        // Extract the table handle from the composite handle
        string tableHandle = handle;
        int rIdx = handle.IndexOf("::R", StringComparison.Ordinal);
        if (rIdx > 0)
            tableHandle = handle[..rIdx];

        if (!tableByHandle.TryGetValue(tableHandle, out var table))
        {
            log.Add($"[WARN] TABLE Handle={tableHandle} (from composite={handle}) not found in raw parse.");
            return false;
        }

        // Find the matching cell
        var cell = table.Cells.FirstOrDefault(c => c.Row == row && c.Column == col);
        if (cell == null)
        {
            log.Add($"[WARN] TABLE Handle={tableHandle} R{row}C{col} cell not found.");
            return false;
        }

        if (cell.TextLineNumber < 0 || cell.TextLineNumber >= lines.Length)
        {
            log.Add($"[WARN] TABLE Handle={tableHandle} R{row}C{col} text line number {cell.TextLineNumber} out of range.");
            return false;
        }

        // Replace the value at the specific line number
        // TextLineNumber is the 0-based index of the value line in the lines array
        lines[cell.TextLineNumber] = newText;
        log.Add($"[OK] TABLE Handle={tableHandle} R{row}C{col} → \"{TruncateLog(newText)}\" (line {cell.TextLineNumber})");
        return true;
    }

    // -----------------------------------------------------------------
    // MULTILEADER text replacement
    // -----------------------------------------------------------------

    /// <summary>
    /// 替换 MULTILEADER 的文本内容。
    /// 通过行号精确定位组码 304 的值行进行替换。
    /// </summary>
    private static bool ReplaceMLeaderText(
        string[] lines,
        Dictionary<string, MultiLeaderData> mleaderByHandle,
        string handle,
        string newText,
        List<string> log)
    {
        if (!mleaderByHandle.TryGetValue(handle, out var ml))
        {
            log.Add($"[WARN] MLEADER Handle={handle} not found in raw parse.");
            return false;
        }

        if (ml.TextLineNumber < 0 || ml.TextLineNumber >= lines.Length)
        {
            log.Add($"[WARN] MLEADER Handle={handle} text line number {ml.TextLineNumber} out of range.");
            return false;
        }

        // Replace the value at the specific line number
        lines[ml.TextLineNumber] = newText;
        log.Add($"[OK] MLEADER Handle={handle} → \"{TruncateLog(newText)}\" (line {ml.TextLineNumber})");
        return true;
    }

    // -----------------------------------------------------------------
    // File I/O utilities
    // -----------------------------------------------------------------

    /// <summary>
    /// 读取 DXF 文件为行数组。
    /// </summary>
    private static string[] ReadDxfFile(string filePath)
        => DxfRawParser.ReadDxfFile(filePath);

    /// <summary>
    /// 将修改后的行数组保存回 DXF 文件。
    /// </summary>
    private static void SaveDxfFile(string filePath, string[] lines)
    {
        // Detect the original encoding by trying Default first
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

    private static string TruncateLog(string text, int maxLen = 60) =>
        text.Length <= maxLen ? text : text[..maxLen] + "…";
}
