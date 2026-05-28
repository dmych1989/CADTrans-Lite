// Services/DxfRawParser.cs
// Raw DXF text parser for TEXT, MTEXT, ATTRIB, ACAD_TABLE, and MULTILEADER entities.
// Phase 2+: All entity types are parsed directly from DXF text to enable
// raw line-level replacement, which preserves ALL DXF content (unlike netDxf Save).

using System.Text;
using CADTransLite.Core.Models;

namespace CADTransLite.Core.Services;

/// <summary>
/// 直接读取 DXF 文件文本，解析所有可翻译文本实体类型。
/// 使用原始文本解析实现行级精准替换，避免 netDxf Save 丢失不支持的实体类型。
/// </summary>
public static class DxfRawParser
{
    // -----------------------------------------------------------------
    // DXF version constants
    // -----------------------------------------------------------------
    private const string ACADVER_R2000 = "AC1015";
    private const string ACADVER_R2004 = "AC1018";
    private const string ACADVER_R2007 = "AC1021";
    private const string ACADVER_R2010 = "AC1024";
    private const string ACADVER_R2013 = "AC1027";
    private const string ACADVER_R2018 = "AC1032";

    // -----------------------------------------------------------------
    // Public API — ACAD_TABLE
    // -----------------------------------------------------------------

    /// <summary>
    /// 解析 DXF 文件中的所有 ACAD_TABLE 实体。
    /// </summary>
    /// <param name="filePath">DXF 文件的完整路径。</param>
    /// <returns>解析到的 AcadTableData 列表。</returns>
    public static List<AcadTableData> ParseAcadTables(string filePath)
    {
        var result = new List<AcadTableData>();

        string[] lines;
        try
        {
            lines = ReadDxfFile(filePath);
        }
        catch (Exception ex)
        {
            ErrorLogger.Instance.Warn("DxfRawParser", $"无法读取文件 '{filePath}': {ex.Message}");
            return result;
        }

        string acadVer = DetectAcadVer(lines);
        ErrorLogger.Instance.Info("DxfRawParser", $"DXF 版本 = {acadVer}");

        bool isR2007OrLater = IsR2007OrLater(acadVer);

        // Scan for ACAD_TABLE entities
        int i = 0;
        while (i < lines.Length - 1)
        {
            // Look for group code 0 with value ACAD_TABLE
            if (IsGroupCode(lines, i, 0) && lines[i + 1].Trim() == "ACAD_TABLE")
            {
                var (tableData, nextI) = ParseAcadTableEntity(lines, i, isR2007OrLater);
                if (tableData != null)
                {
                    result.Add(tableData);
                }
                i = nextI;
            }
            else
            {
                i += 2; // Skip this group pair
            }
        }

        ErrorLogger.Instance.Info("DxfRawParser", $"解析到 {result.Count} 个 ACAD_TABLE 实体");
        return result;
    }

    // -----------------------------------------------------------------
    // Public API — MULTILEADER
    // -----------------------------------------------------------------

    /// <summary>
    /// 解析 DXF 文件中的所有 MULTILEADER 实体。
    /// </summary>
    /// <param name="filePath">DXF 文件完整路径。</param>
    /// <returns>解析到的 MultiLeaderData 列表。</returns>
    public static List<MultiLeaderData> ParseMultiLeaders(string filePath)
    {
        var result = new List<MultiLeaderData>();

        string[] lines;
        try
        {
            lines = ReadDxfFile(filePath);
        }
        catch (Exception ex)
        {
            ErrorLogger.Instance.Warn("DxfRawParser", $"无法读取文件 '{filePath}': {ex.Message}");
            return result;
        }

        // Scan for MULTILEADER entities
        int i = 0;
        while (i < lines.Length - 1)
        {
            if (IsGroupCode(lines, i, 0) && lines[i + 1].Trim() == "MULTILEADER")
            {
                var (mlData, nextI) = ParseMultiLeaderEntity(lines, i);
                if (mlData != null)
                {
                    result.Add(mlData);
                }
                i = nextI;
            }
            else
            {
                i += 2;
            }
        }

        ErrorLogger.Instance.Info("DxfRawParser", $"解析到 {result.Count} 个 MULTILEADER 实体");
        return result;
    }

    // -----------------------------------------------------------------
    // DXF version detection
    // -----------------------------------------------------------------

    /// <summary>
    /// 从 DXF 文件的 HEADER 段检测 $ACADVER 变量。
    /// DXF 格式: 组码9 → "$ACADVER", 组码1 → 版本字符串
    /// </summary>
    private static string DetectAcadVer(string[] lines)
    {
        for (int i = 0; i < lines.Length - 3; i++)
        {
            // Look for the value "$ACADVER" on a group-code value line
            if (lines[i].Trim() == "$ACADVER")
            {
                // Next group pair should be: group code 1, value = version string
                if (i + 2 < lines.Length && int.TryParse(lines[i + 1].Trim(), out int gc) && gc == 1)
                {
                    return lines[i + 2].Trim();
                }
            }
        }
        return string.Empty;
    }

    /// <summary>
    /// 判断 DXF 版本是否为 R2007 或更高。
    /// </summary>
    private static bool IsR2007OrLater(string acadVer)
    {
        if (string.IsNullOrEmpty(acadVer))
            return true; // Unknown version → assume modern format

        // Compare lexicographically: AC1021 (R2007) and above
        return string.Compare(acadVer, ACADVER_R2007, StringComparison.Ordinal) >= 0;
    }

    // -----------------------------------------------------------------
    // Group code utility — GetGroupValue (IsGroupCode is now public above)
    // -----------------------------------------------------------------

    /// <summary>
    /// 从指定位置开始读取一个组码对的值。
    /// lines[index] = 组码，lines[index+1] = 值。
    /// </summary>
    private static string GetGroupValue(string[] lines, int index)
    {
        if (index + 1 >= lines.Length)
            return string.Empty;
        return lines[index + 1].Trim();
    }

    // -----------------------------------------------------------------
    // ACAD_TABLE entity parsing
    // -----------------------------------------------------------------

    /// <summary>
    /// 解析单个 ACAD_TABLE 实体，返回解析数据和下一次扫描起始位置。
    /// </summary>
    private static (AcadTableData? data, int nextIndex) ParseAcadTableEntity(
        string[] lines, int startIndex, bool isR2007OrLater)
    {
        try
        {
            var table = new AcadTableData();

            // Collect all group pairs belonging to this entity
            // until we hit the next group code 0 entity marker
            var groupPairs = new List<(int groupCode, string value, int lineNumber)>();
            int i = startIndex + 2; // Skip past "0" / "ACAD_TABLE"

            while (i < lines.Length - 1)
            {
                if (int.TryParse(lines[i].Trim(), out int code))
                {
                    // If we hit group code 0, this entity is done
                    if (code == 0)
                        break;

                    string value = GetGroupValue(lines, i);
                    groupPairs.Add((code, value, i + 1)); // lineNumber = value line number (1-based)
                    i += 2;
                }
                else
                {
                    // Malformed line, skip
                    i++;
                }
            }

            // Extract common properties
            table.Handle = groupPairs.FirstOrDefault(p => p.groupCode == 5).value ?? string.Empty;
            table.LayerName = groupPairs.FirstOrDefault(p => p.groupCode == 8).value ?? string.Empty;

            // nRows = group code 91, nCols = group code 92
            int nRows = 0, nCols = 0;
            var rowPair = groupPairs.FirstOrDefault(p => p.groupCode == 91);
            var colPair = groupPairs.FirstOrDefault(p => p.groupCode == 92);
            if (int.TryParse(rowPair.value, out nRows)) table.Rows = nRows;
            if (int.TryParse(colPair.value, out nCols)) table.Columns = nCols;

            if (nRows == 0 || nCols == 0)
            {
                ErrorLogger.Instance.Warn("DxfRawParser", $"ACAD_TABLE Handle={table.Handle} 行列数为 0，跳过");
                return (null, i);
            }

            // Parse cells
            if (isR2007OrLater)
            {
                ParseCellsR2007(groupPairs, table);
            }
            else
            {
                ParseCellsR2004(groupPairs, table);
            }

            return (table, i);
        }
        catch (Exception ex)
        {
            ErrorLogger.Instance.Warn("DxfRawParser", $"解析 ACAD_TABLE 失败: {ex.Message}");
            // Try to advance past this entity
            int j = startIndex + 2;
            while (j < lines.Length - 1)
            {
                if (IsGroupCode(lines, j, 0))
                    break;
                j += 2;
            }
            return (null, j);
        }
    }

    /// <summary>
    /// R2007+ 格式：按组码 301 分割单元格，文本在组码 302。
    /// </summary>
    private static void ParseCellsR2007(
        List<(int groupCode, string value, int lineNumber)> groupPairs,
        AcadTableData table)
    {
        // Find all indices where group code 301 appears — each marks a new cell
        var cellStartIndices = new List<int>();
        for (int i = 0; i < groupPairs.Count; i++)
        {
            if (groupPairs[i].groupCode == 301)
            {
                cellStartIndices.Add(i);
            }
        }

        // If no 301 codes found, there are no text cells
        if (cellStartIndices.Count == 0)
            return;

        // Process each cell segment
        int cellIndex = 0;
        for (int seg = 0; seg < cellStartIndices.Count; seg++)
        {
            int startIdx = cellStartIndices[seg];
            int endIdx = (seg + 1 < cellStartIndices.Count)
                ? cellStartIndices[seg + 1]
                : groupPairs.Count;

            // Extract cell type (group code 171) within this segment
            int cellType = 0;
            string cellText = string.Empty;
            int textLineNumber = -1;

            for (int k = startIdx; k < endIdx; k++)
            {
                var (gc, val, ln) = groupPairs[k];

                if (gc == 171)
                {
                    int.TryParse(val, out cellType);
                }
                else if (gc == 302)
                {
                    cellText = val;
                    textLineNumber = ln;
                }
            }

            // Calculate row/column (row-major order)
            int row = cellIndex / table.Columns;
            int col = cellIndex % table.Columns;

            if (row < table.Rows && col < table.Columns)
            {
                var cell = new TableCellData
                {
                    Row = row,
                    Column = col,
                    CellType = cellType,
                    Text = cellText,
                    TextLineNumber = textLineNumber,
                };
                table.Cells.Add(cell);
            }

            cellIndex++;
        }
    }

    /// <summary>
    /// R2004 格式：按组码 171 分割单元格，文本在组码 1（短文本）或 3+1（长文本分块）。
    /// </summary>
    private static void ParseCellsR2004(
        List<(int groupCode, string value, int lineNumber)> groupPairs,
        AcadTableData table)
    {
        // Find all indices where group code 171 appears — each marks a new cell
        var cellStartIndices = new List<int>();
        for (int i = 0; i < groupPairs.Count; i++)
        {
            if (groupPairs[i].groupCode == 171)
            {
                cellStartIndices.Add(i);
            }
        }

        if (cellStartIndices.Count == 0)
            return;

        // Process each cell segment
        int cellIndex = 0;
        for (int seg = 0; seg < cellStartIndices.Count; seg++)
        {
            int startIdx = cellStartIndices[seg];
            int endIdx = (seg + 1 < cellStartIndices.Count)
                ? cellStartIndices[seg + 1]
                : groupPairs.Count;

            int cellType = 0;
            var textChunks = new List<(string text, int lineNumber)>();
            int primaryTextLineNumber = -1;

            for (int k = startIdx; k < endIdx; k++)
            {
                var (gc, val, ln) = groupPairs[k];

                if (gc == 171)
                {
                    int.TryParse(val, out cellType);
                }
                else if (gc == 1)
                {
                    // Group code 1: text content (≤250 chars)
                    textChunks.Add((val, ln));
                    if (primaryTextLineNumber == -1)
                        primaryTextLineNumber = ln;
                }
                else if (gc == 3)
                {
                    // Group code 3: text content chunk (>250 chars, prefix chunks)
                    textChunks.Add((val, ln));
                }
            }

            // Combine text chunks: code 3 chunks first, then code 1 chunk as tail
            string cellText = string.Concat(textChunks.Select(t => t.text));

            int row = cellIndex / table.Columns;
            int col = cellIndex % table.Columns;

            if (row < table.Rows && col < table.Columns)
            {
                var cell = new TableCellData
                {
                    Row = row,
                    Column = col,
                    CellType = cellType,
                    Text = cellText,
                    TextLineNumber = primaryTextLineNumber,
                };
                table.Cells.Add(cell);
            }

            cellIndex++;
        }
    }

    // -----------------------------------------------------------------
    // MULTILEADER entity parsing
    // -----------------------------------------------------------------

    /// <summary>
    /// 解析单个 MULTILEADER 实体，返回解析数据和下一次扫描起始位置。
    /// </summary>
    private static (MultiLeaderData? data, int nextIndex) ParseMultiLeaderEntity(
        string[] lines, int startIndex)
    {
        try
        {
            var ml = new MultiLeaderData();

            // Collect all group pairs belonging to this entity
            var groupPairs = new List<(int groupCode, string value, int lineNumber)>();
            int i = startIndex + 2; // Skip past "0" / "MULTILEADER"

            while (i < lines.Length - 1)
            {
                if (int.TryParse(lines[i].Trim(), out int code))
                {
                    if (code == 0)
                        break;

                    string value = GetGroupValue(lines, i);
                    groupPairs.Add((code, value, i + 1)); // lineNumber = value line number
                    i += 2;
                }
                else
                {
                    i++;
                }
            }

            // Extract common properties
            ml.Handle = groupPairs.FirstOrDefault(p => p.groupCode == 5).value ?? string.Empty;
            ml.LayerName = groupPairs.FirstOrDefault(p => p.groupCode == 8).value ?? string.Empty;

            // ContentType = group code 172
            var contentTypePair = groupPairs.FirstOrDefault(p => p.groupCode == 172);
            if (int.TryParse(contentTypePair.value, out int ct))
                ml.ContentType = ct;

            // If ContentType != 2 (not text), return early (no text to extract)
            if (ml.ContentType != 2)
            {
                return (ml, i);
            }

            // For text-type MULTILEADER, extract context data
            // Context data is enclosed between group code 300 (start) and 301 (end)
            bool inContext = false;
            for (int k = 0; k < groupPairs.Count; k++)
            {
                var (gc, val, ln) = groupPairs[k];

                if (gc == 300)
                {
                    inContext = true;
                    continue;
                }

                if (gc == 301)
                {
                    inContext = false;
                    continue;
                }

                if (inContext)
                {
                    if (gc == 304)
                    {
                        ml.TextContent = val;
                        ml.TextLineNumber = ln;
                    }
                    else if (gc == 340)
                    {
                        ml.TextStyleHandle = val;
                    }
                    else if (gc == 41)
                    {
                        double.TryParse(val, out double bw);
                        ml.TextBoundaryWidth = bw;
                    }
                }
            }

            return (ml, i);
        }
        catch (Exception ex)
        {
            ErrorLogger.Instance.Warn("DxfRawParser", $"解析 MULTILEADER 失败: {ex.Message}");
            int j = startIndex + 2;
            while (j < lines.Length - 1)
            {
                if (IsGroupCode(lines, j, 0))
                    break;
                j += 2;
            }
            return (null, j);
        }
    }

    // -----------------------------------------------------------------
    // Public API — TEXT entities
    // -----------------------------------------------------------------

    /// <summary>
    /// 解析 DXF 文件中的所有 TEXT 实体。
    /// 返回每个 TEXT 实体的 Handle、图层、文本内容和行号定位信息。
    /// </summary>
    public static List<TextEntityInfo> ParseTextEntities(string filePath)
    {
        var result = new List<TextEntityInfo>();

        string[] lines;
        try
        {
            lines = ReadDxfFile(filePath);
        }
        catch (Exception ex)
        {
            ErrorLogger.Instance.Warn("DxfRawParser", $"无法读取文件 '{filePath}': {ex.Message}");
            return result;
        }

        int i = 0;
        while (i < lines.Length - 1)
        {
            if (IsGroupCode(lines, i, 0))
            {
                string entityType = lines[i + 1].Trim();
                if (entityType == "TEXT")
                {
                    var (info, nextI) = ParseTextEntity(lines, i);
                    if (info != null)
                        result.Add(info);
                    i = nextI;
                }
                else
                {
                    i += 2;
                }
            }
            else
            {
                i += 2;
            }
        }

        ErrorLogger.Instance.Info("DxfRawParser", $"解析到 {result.Count} 个 TEXT 实体");
        return result;
    }

    // -----------------------------------------------------------------
    // Public API — MTEXT entities
    // -----------------------------------------------------------------

    /// <summary>
    /// 解析 DXF 文件中的所有 MTEXT 实体。
    /// MTEXT 文本可能跨越多个组码 3（续行）和最后一个组码 1，
    /// 本方法记录所有文本行的行号以便回写时精准替换。
    /// </summary>
    public static List<MTextEntityInfo> ParseMTextEntities(string filePath)
    {
        var result = new List<MTextEntityInfo>();

        string[] lines;
        try
        {
            lines = ReadDxfFile(filePath);
        }
        catch (Exception ex)
        {
            ErrorLogger.Instance.Warn("DxfRawParser", $"无法读取文件 '{filePath}': {ex.Message}");
            return result;
        }

        int i = 0;
        while (i < lines.Length - 1)
        {
            if (IsGroupCode(lines, i, 0))
            {
                string entityType = lines[i + 1].Trim();
                if (entityType == "MTEXT")
                {
                    var (info, nextI) = ParseMTextEntity(lines, i);
                    if (info != null)
                        result.Add(info);
                    i = nextI;
                }
                else
                {
                    i += 2;
                }
            }
            else
            {
                i += 2;
            }
        }

        ErrorLogger.Instance.Info("DxfRawParser", $"解析到 {result.Count} 个 MTEXT 实体");
        return result;
    }

    // -----------------------------------------------------------------
    // Public API — ATTRIB entities
    // -----------------------------------------------------------------

    /// <summary>
    /// 解析 DXF 文件中的所有 ATTRIB 实体（嵌套在 INSERT 内）。
    /// 返回每个 ATTRIB 的 Handle、InsertHandle、Tag 和行号定位信息。
    /// 组合键 = "{InsertHandle}::{Tag}"，与 TranslationItem.Handle 格式一致。
    /// </summary>
    public static List<AttributeEntityInfo> ParseAttributeEntities(string filePath)
    {
        var result = new List<AttributeEntityInfo>();

        string[] lines;
        try
        {
            lines = ReadDxfFile(filePath);
        }
        catch (Exception ex)
        {
            ErrorLogger.Instance.Warn("DxfRawParser", $"无法读取文件 '{filePath}': {ex.Message}");
            return result;
        }

        string currentInsertHandle = string.Empty;

        int i = 0;
        while (i < lines.Length - 1)
        {
            if (IsGroupCode(lines, i, 0))
            {
                string entityType = lines[i + 1].Trim();

                if (entityType == "INSERT")
                {
                    var (insHandle, nextI) = ParseInsertHandle(lines, i);
                    currentInsertHandle = insHandle;
                    i = nextI;
                }
                else if (entityType == "ATTRIB")
                {
                    var (attrInfo, nextI) = ParseAttributeEntity(lines, i, currentInsertHandle);
                    if (attrInfo != null)
                        result.Add(attrInfo);
                    i = nextI;
                }
                else if (entityType == "SEQEND")
                {
                    // SEQEND marks the end of an INSERT's attribute sequence
                    currentInsertHandle = string.Empty;
                    i += 2;
                }
                else
                {
                    i += 2;
                }
            }
            else
            {
                i += 2;
            }
        }

        ErrorLogger.Instance.Info("DxfRawParser", $"解析到 {result.Count} 个 ATTRIB 实体");
        return result;
    }

    // -----------------------------------------------------------------
    // Public API — File reading (shared with DxfTextReplacer)
    // -----------------------------------------------------------------

    /// <summary>
    /// 读取 DXF 文件为行数组。先用系统默认编码尝试，失败则用 UTF-8。
    /// </summary>
    public static string[] ReadDxfFile(string filePath)
    {
        try
        {
            return File.ReadAllLines(filePath, Encoding.Default);
        }
        catch (Exception)
        {
            return File.ReadAllLines(filePath, Encoding.UTF8);
        }
    }

    // -----------------------------------------------------------------
    // Public API — Group code utilities (shared)
    // -----------------------------------------------------------------

    /// <summary>
    /// 检查指定位置的行是否为指定的组码。
    /// </summary>
    public static bool IsGroupCode(string[] lines, int index, int expectedCode)
    {
        if (index < 0 || index >= lines.Length)
            return false;
        return int.TryParse(lines[index].Trim(), out int code) && code == expectedCode;
    }

    // -----------------------------------------------------------------
    // TEXT entity parsing
    // -----------------------------------------------------------------

    private static (TextEntityInfo? info, int nextIndex) ParseTextEntity(string[] lines, int startIndex)
    {
        try
        {
            var info = new TextEntityInfo();
            int i = startIndex + 2; // Skip past "0" / "TEXT"

            while (i < lines.Length - 1)
            {
                if (int.TryParse(lines[i].Trim(), out int code))
                {
                    if (code == 0) break;

                    string value = GetGroupValue(lines, i);

                    switch (code)
                    {
                        case 5:  // Handle
                            info.Handle = value;
                            break;
                        case 7:  // Text style name
                            info.TextStyleName = value;
                            break;
                        case 8:  // Layer name
                            info.LayerName = value;
                            break;
                        case 1:  // Text value
                            info.OriginalText = value;
                            info.TextLineNumber = i + 1; // Value line number (0-based)
                            break;
                    }

                    i += 2;
                }
                else
                {
                    i++;
                }
            }

            return string.IsNullOrEmpty(info.Handle) ? (null, i) : (info, i);
        }
        catch (Exception ex)
        {
            ErrorLogger.Instance.Warn("DxfRawParser", $"解析 TEXT 失败: {ex.Message}");
            return (null, AdvanceToNextEntity(lines, startIndex));
        }
    }

    // -----------------------------------------------------------------
    // MTEXT entity parsing
    // -----------------------------------------------------------------

    private static (MTextEntityInfo? info, int nextIndex) ParseMTextEntity(string[] lines, int startIndex)
    {
        try
        {
            var info = new MTextEntityInfo();
            var textChunks = new List<(string text, int lineNumber, int groupCode)>();
            int i = startIndex + 2; // Skip past "0" / "MTEXT"

            while (i < lines.Length - 1)
            {
                if (int.TryParse(lines[i].Trim(), out int code))
                {
                    if (code == 0) break;

                    string value = GetGroupValue(lines, i);

                    switch (code)
                    {
                        case 5:  // Handle
                            info.Handle = value;
                            break;
                        case 7:  // Text style name
                            info.TextStyleName = value;
                            break;
                        case 8:  // Layer name
                            info.LayerName = value;
                            break;
                        case 41: // Rectangle width
                            double.TryParse(value, out double rw);
                            info.RectangleWidth = rw;
                            break;
                        case 1:  // Text value (final chunk)
                            textChunks.Add((value, i + 1, 1));
                            break;
                        case 3:  // Text continuation chunk
                            textChunks.Add((value, i + 1, 3));
                            break;
                    }

                    i += 2;
                }
                else
                {
                    i++;
                }
            }

            // Build the full original text and track line numbers
            if (textChunks.Count > 0)
            {
                info.OriginalText = string.Concat(textChunks.Select(t => t.text));

                // Group code 3 lines need to be cleared on replacement
                info.Group3LineNumbers = textChunks
                    .Where(t => t.groupCode == 3)
                    .Select(t => t.lineNumber)
                    .ToList();

                // The LAST group code 1 line is where we put the new value
                var lastGroup1 = textChunks.LastOrDefault(t => t.groupCode == 1);
                info.LastGroup1LineNumber = lastGroup1.lineNumber;
            }

            return string.IsNullOrEmpty(info.Handle) ? (null, i) : (info, i);
        }
        catch (Exception ex)
        {
            ErrorLogger.Instance.Warn("DxfRawParser", $"解析 MTEXT 失败: {ex.Message}");
            return (null, AdvanceToNextEntity(lines, startIndex));
        }
    }

    // -----------------------------------------------------------------
    // INSERT entity parsing (for tracking current InsertHandle)
    // -----------------------------------------------------------------

    private static (string insertHandle, int nextIndex) ParseInsertHandle(string[] lines, int startIndex)
    {
        string handle = string.Empty;
        int i = startIndex + 2;

        // Only scan the first few group pairs for the Handle
        int scanLimit = Math.Min(i + 20, lines.Length - 1);
        while (i < scanLimit)
        {
            if (int.TryParse(lines[i].Trim(), out int code))
            {
                if (code == 0) break;
                if (code == 5)
                {
                    handle = GetGroupValue(lines, i);
                    break;
                }
                i += 2;
            }
            else
            {
                i++;
            }
        }

        // Don't advance past the INSERT — we need to parse its ATTRIB children
        return (handle, startIndex + 2);
    }

    // -----------------------------------------------------------------
    // ATTRIB entity parsing
    // -----------------------------------------------------------------

    private static (AttributeEntityInfo? info, int nextIndex) ParseAttributeEntity(
        string[] lines, int startIndex, string insertHandle)
    {
        try
        {
            var info = new AttributeEntityInfo
            {
                InsertHandle = insertHandle,
            };

            int i = startIndex + 2; // Skip past "0" / "ATTRIB"

            while (i < lines.Length - 1)
            {
                if (int.TryParse(lines[i].Trim(), out int code))
                {
                    if (code == 0) break;

                    string value = GetGroupValue(lines, i);

                    switch (code)
                    {
                        case 5:  // ATTRIB entity's own Handle
                            info.Handle = value;
                            break;
                        case 2:  // Attribute Tag
                            info.Tag = value;
                            break;
                        case 7:  // Text style name
                            info.TextStyleName = value;
                            break;
                        case 8:  // Layer name
                            info.LayerName = value;
                            break;
                        case 1:  // Text value
                            info.OriginalText = value;
                            info.TextLineNumber = i + 1;
                            break;
                    }

                    i += 2;
                }
                else
                {
                    i++;
                }
            }

            return string.IsNullOrEmpty(info.Tag) ? (null, i) : (info, i);
        }
        catch (Exception ex)
        {
            ErrorLogger.Instance.Warn("DxfRawParser", $"解析 ATTRIB 失败: {ex.Message}");
            return (null, AdvanceToNextEntity(lines, startIndex));
        }
    }

    // -----------------------------------------------------------------
    // Utility: advance past current entity to next group code 0
    // -----------------------------------------------------------------

    private static int AdvanceToNextEntity(string[] lines, int startIndex)
    {
        int j = startIndex + 2;
        while (j < lines.Length - 1)
        {
            if (IsGroupCode(lines, j, 0))
                break;
            j += 2;
        }
        return j;
    }
}
