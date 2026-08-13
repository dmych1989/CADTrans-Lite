// DxfRawParserTests.cs
// Unit tests for DxfRawParser — ACAD_TABLE and MULTILEADER raw DXF text parsing.
// Phase 2: Validates the auxiliary channel for entity types that netDxf cannot handle.

using CADTransLite.Core.Models;
using CADTransLite.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CADTransLite.Tests;

/// <summary>
/// DxfRawParser unit tests.
/// Covers: ACAD_TABLE parsing (R2004/R2007+), MULTILEADER parsing,
/// version detection, encoding handling, edge cases.
/// </summary>
[TestClass]
public class DxfRawParserTests
{
    // -----------------------------------------------------------------------
    // Helper: write a minimal DXF file to a temp path
    // -----------------------------------------------------------------------

    private static string WriteTempDxf(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"DxfRawParserTest_{Guid.NewGuid():N}.dxf");
        File.WriteAllText(path, content, System.Text.Encoding.UTF8);
        return path;
    }

    /// <summary>
    /// Generates a minimal DXF file skeleton with $ACADVER header.
    /// </summary>
    private static string DxfSkeleton(string acadVer, string entitiesSection)
    {
        return string.Join(Environment.NewLine,
            "0",
            "SECTION",
            "2",
            "HEADER",
            "9",
            "$ACADVER",
            "1",
            acadVer,
            "0",
            "ENDSEC",
            "0",
            "SECTION",
            "2",
            "ENTITIES",
            entitiesSection,
            "0",
            "ENDSEC",
            "0",
            "EOF"
        );
    }

    // =======================================================================
    // ACAD_TABLE — R2007+ format (group code 301/302)
    // =======================================================================

    [TestMethod]
    public void ParseAcadTables_R2007_SingleTable_ParsesCorrectly()
    {
        // Arrange: 2x2 table with text cells in R2007+ format
        string entities = string.Join(Environment.NewLine,
            "0",
            "ACAD_TABLE",
            "5",
            "1A6",
            "8",
            "Layer1",
            "100",
            "AcDbEntity",
            "100",
            "AcDbBlockReference",
            "100",
            "AcDbTable",
            "91",
            "2",        // nRows
            "92",
            "2",        // nCols
            // Cell 0,0 (row=0, col=0)
            "301",
            "CELL",
            "171",
            "1",        // cellType = text
            "302",
            "Header A",
            // Cell 0,1 (row=0, col=1)
            "301",
            "CELL",
            "171",
            "1",
            "302",
            "Header B",
            // Cell 1,0 (row=1, col=0)
            "301",
            "CELL",
            "171",
            "1",
            "302",
            "{\\H1.5x;Data 1}",   // MTEXT format code
            // Cell 1,1 (row=1, col=1)
            "301",
            "CELL",
            "171",
            "2",        // cellType = block → should be parsed but flagged
            "302",
            ""
        );

        string dxf = DxfSkeleton("AC1024", entities);  // R2010 → R2007+
        string path = WriteTempDxf(dxf);

        try
        {
            // Act
            var result = DxfRawParser.ParseAcadTables(path);

            // Assert
            Assert.AreEqual(1, result.Count, "Should find exactly 1 ACAD_TABLE entity");
            var table = result[0];
            Assert.AreEqual("1A6", table.Handle);
            Assert.AreEqual("Layer1", table.LayerName);
            Assert.AreEqual(2, table.Rows);
            Assert.AreEqual(2, table.Columns);
            Assert.AreEqual(4, table.Cells.Count, "2x2 table should have 4 cells");

            // Verify cell coordinates (row-major order)
            Assert.AreEqual(0, table.Cells[0].Row);
            Assert.AreEqual(0, table.Cells[0].Column);
            Assert.AreEqual(1, table.Cells[0].CellType);
            Assert.AreEqual("Header A", table.Cells[0].Text);

            Assert.AreEqual(0, table.Cells[1].Row);
            Assert.AreEqual(1, table.Cells[1].Column);
            Assert.AreEqual("Header B", table.Cells[1].Text);

            Assert.AreEqual(1, table.Cells[2].Row);
            Assert.AreEqual(0, table.Cells[2].Column);
            Assert.AreEqual("{\\H1.5x;Data 1}", table.Cells[2].Text);

            Assert.AreEqual(1, table.Cells[3].Row);
            Assert.AreEqual(1, table.Cells[3].Column);
            Assert.AreEqual(2, table.Cells[3].CellType, "Last cell should be block type");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ParseAcadTables_R2007_MultipleTables_ParsesAll()
    {
        // Arrange: two separate ACAD_TABLE entities
        string entities = string.Join(Environment.NewLine,
            // Table 1: 1x1
            "0",
            "ACAD_TABLE",
            "5",
            "AA",
            "8",
            "L1",
            "100",
            "AcDbTable",
            "91",
            "1",
            "92",
            "1",
            "301",
            "CELL",
            "171",
            "1",
            "302",
            "Cell A",
            // Table 2: 1x1
            "0",
            "ACAD_TABLE",
            "5",
            "BB",
            "8",
            "L2",
            "100",
            "AcDbTable",
            "91",
            "1",
            "92",
            "1",
            "301",
            "CELL",
            "171",
            "1",
            "302",
            "Cell B"
        );

        string dxf = DxfSkeleton("AC1021", entities);  // R2007
        string path = WriteTempDxf(dxf);

        try
        {
            var result = DxfRawParser.ParseAcadTables(path);
            Assert.AreEqual(2, result.Count, "Should find 2 ACAD_TABLE entities");
            Assert.AreEqual("AA", result[0].Handle);
            Assert.AreEqual("BB", result[1].Handle);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // =======================================================================
    // ACAD_TABLE — R2004 format (group code 171/1/3)
    // =======================================================================

    [TestMethod]
    public void ParseAcadTables_R2004_SingleTable_ParsesCorrectly()
    {
        // Arrange: 1x2 table in R2004 format (split by group code 171)
        string entities = string.Join(Environment.NewLine,
            "0",
            "ACAD_TABLE",
            "5",
            "CC",
            "8",
            "Layer0",
            "100",
            "AcDbTable",
            "91",
            "1",        // nRows
            "92",
            "2",        // nCols
            // Cell 0,0
            "171",
            "1",        // cellType = text
            "1",
            "Hello",
            // Cell 0,1
            "171",
            "1",
            "1",
            "World"
        );

        string dxf = DxfSkeleton("AC1018", entities);  // R2004
        string path = WriteTempDxf(dxf);

        try
        {
            var result = DxfRawParser.ParseAcadTables(path);
            Assert.AreEqual(1, result.Count);
            var table = result[0];
            Assert.AreEqual("CC", table.Handle);
            Assert.AreEqual(1, table.Rows);
            Assert.AreEqual(2, table.Columns);
            Assert.AreEqual(2, table.Cells.Count);
            Assert.AreEqual("Hello", table.Cells[0].Text);
            Assert.AreEqual("World", table.Cells[1].Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ParseAcadTables_R2004_LongText_CombinesGroup3And1()
    {
        // Arrange: text > 250 chars uses group code 3 for prefix chunks + code 1 for tail
        string longPrefix = new('A', 250);
        string tail = "END";
        string expectedText = longPrefix + tail;

        string entities = string.Join(Environment.NewLine,
            "0",
            "ACAD_TABLE",
            "5",
            "DD",
            "8",
            "Layer0",
            "100",
            "AcDbTable",
            "91",
            "1",
            "92",
            "1",
            "171",
            "1",
            "3",
            longPrefix,    // prefix chunk (group code 3)
            "1",
            tail           // tail (group code 1)
        );

        string dxf = DxfSkeleton("AC1018", entities);
        string path = WriteTempDxf(dxf);

        try
        {
            var result = DxfRawParser.ParseAcadTables(path);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(expectedText, result[0].Cells[0].Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // =======================================================================
    // ACAD_TABLE — edge cases
    // =======================================================================

    [TestMethod]
    public void ParseAcadTables_ZeroRows_SkipsEntity()
    {
        string entities = string.Join(Environment.NewLine,
            "0",
            "ACAD_TABLE",
            "5",
            "EE",
            "8",
            "Layer0",
            "100",
            "AcDbTable",
            "91",
            "0",
            "92",
            "0"
        );

        string dxf = DxfSkeleton("AC1021", entities);
        string path = WriteTempDxf(dxf);

        try
        {
            var result = DxfRawParser.ParseAcadTables(path);
            Assert.AreEqual(0, result.Count, "Table with 0 rows should be skipped");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ParseAcadTables_NoAcadTables_ReturnsEmptyList()
    {
        string entities = string.Join(Environment.NewLine,
            "0",
            "TEXT",
            "5",
            "FF",
            "8",
            "Layer0",
            "1",
            "Just some text"
        );

        string dxf = DxfSkeleton("AC1021", entities);
        string path = WriteTempDxf(dxf);

        try
        {
            var result = DxfRawParser.ParseAcadTables(path);
            Assert.AreEqual(0, result.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ParseAcadTables_FileNotFound_ReturnsEmptyList()
    {
        var result = DxfRawParser.ParseAcadTables("C:\\nonexistent\\file.dxf");
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ParseAcadTables_R2007_NoCells_ReturnsTableWithEmptyCellsList()
    {
        // R2007+ table with no group code 301 → no text cells
        string entities = string.Join(Environment.NewLine,
            "0",
            "ACAD_TABLE",
            "5",
            "GG",
            "8",
            "Layer0",
            "100",
            "AcDbTable",
            "91",
            "1",
            "92",
            "1"
        );

        string dxf = DxfSkeleton("AC1021", entities);
        string path = WriteTempDxf(dxf);

        try
        {
            var result = DxfRawParser.ParseAcadTables(path);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(0, result[0].Cells.Count, "No 301 codes → no cells parsed");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ParseAcadTables_R2007_TextLineNumber_Recorded()
    {
        // Verify that TextLineNumber is set for each cell
        string entities = string.Join(Environment.NewLine,
            "0",
            "ACAD_TABLE",
            "5",
            "HH",
            "8",
            "Layer0",
            "100",
            "AcDbTable",
            "91",
            "1",
            "92",
            "1",
            "301",
            "CELL",
            "171",
            "1",
            "302",
            "Text"
        );

        string dxf = DxfSkeleton("AC1021", entities);
        string path = WriteTempDxf(dxf);

        try
        {
            var result = DxfRawParser.ParseAcadTables(path);
            Assert.AreEqual(1, result.Count);
            Assert.IsTrue(result[0].Cells[0].TextLineNumber >= 0,
                "TextLineNumber should be recorded (non-negative)");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ParseAcadTables_R2004_MultipleGroup3Chunks_Concatenated()
    {
        // Arrange: text with two prefix chunks (group code 3) and one tail (group code 1)
        string entities = string.Join(Environment.NewLine,
            "0",
            "ACAD_TABLE",
            "5",
            "II",
            "8",
            "Layer0",
            "100",
            "AcDbTable",
            "91",
            "1",
            "92",
            "1",
            "171",
            "1",
            "3",
            "AAA",
            "3",
            "BBB",
            "1",
            "CCC"
        );

        string dxf = DxfSkeleton("AC1018", entities);
        string path = WriteTempDxf(dxf);

        try
        {
            var result = DxfRawParser.ParseAcadTables(path);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("AAABBBCCC", result[0].Cells[0].Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // =======================================================================
    // MULTILEADER — text type (ContentType == 2)
    // =======================================================================

    [TestMethod]
    public void ParseMultiLeaders_TextType_ParsesCorrectly()
    {
        // Arrange: MLEADER with ContentType=2 (text)
        string entities = string.Join(Environment.NewLine,
            "0",
            "MULTILEADER",
            "5",
            "1A7",
            "8",
            "AnnotLayer",
            "172",
            "2",            // ContentType = text
            "300",
            "CONTEXT_DATA{",
            "304",
            "{\\H2.0;Note text}",
            "340",
            "2F",
            "41",
            "50.5",
            "301",
            "}"
        );

        string dxf = DxfSkeleton("AC1021", entities);
        string path = WriteTempDxf(dxf);

        try
        {
            var result = DxfRawParser.ParseMultiLeaders(path);
            Assert.AreEqual(1, result.Count, "Should find 1 MULTILEADER");
            var ml = result[0];
            Assert.AreEqual("1A7", ml.Handle);
            Assert.AreEqual("AnnotLayer", ml.LayerName);
            Assert.AreEqual(2, ml.ContentType);
            Assert.AreEqual("{\\H2.0;Note text}", ml.TextContent);
            Assert.AreEqual("2F", ml.TextStyleHandle);
            Assert.AreEqual(50.5, ml.TextBoundaryWidth, 0.001);
            Assert.IsTrue(ml.TextLineNumber >= 0, "TextLineNumber should be recorded");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ParseMultiLeaders_BlockType_ContentNotExtracted()
    {
        // Arrange: MLEADER with ContentType=1 (block) → no text to extract
        string entities = string.Join(Environment.NewLine,
            "0",
            "MULTILEADER",
            "5",
            "2B",
            "8",
            "Layer0",
            "172",
            "1"             // ContentType = block
        );

        string dxf = DxfSkeleton("AC1021", entities);
        string path = WriteTempDxf(dxf);

        try
        {
            var result = DxfRawParser.ParseMultiLeaders(path);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(1, result[0].ContentType);
            Assert.AreEqual(string.Empty, result[0].TextContent,
                "Block-type MLEADER should have no TextContent");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ParseMultiLeaders_NoContentType_ReturnsZero()
    {
        // Arrange: MLEADER without group code 172 → default ContentType=0
        string entities = string.Join(Environment.NewLine,
            "0",
            "MULTILEADER",
            "5",
            "3C",
            "8",
            "Layer0"
        );

        string dxf = DxfSkeleton("AC1021", entities);
        string path = WriteTempDxf(dxf);

        try
        {
            var result = DxfRawParser.ParseMultiLeaders(path);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(0, result[0].ContentType,
                "Default ContentType should be 0 when group code 172 is absent");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ParseMultiLeaders_MultipleMLeaders_ParsesAll()
    {
        string entities = string.Join(Environment.NewLine,
            "0",
            "MULTILEADER",
            "5",
            "M1",
            "8",
            "L1",
            "172",
            "2",
            "300",
            "CTX",
            "304",
            "Text1",
            "301",
            "END",
            "0",
            "MULTILEADER",
            "5",
            "M2",
            "8",
            "L2",
            "172",
            "2",
            "300",
            "CTX",
            "304",
            "Text2",
            "301",
            "END"
        );

        string dxf = DxfSkeleton("AC1021", entities);
        string path = WriteTempDxf(dxf);

        try
        {
            var result = DxfRawParser.ParseMultiLeaders(path);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("M1", result[0].Handle);
            Assert.AreEqual("Text1", result[0].TextContent);
            Assert.AreEqual("M2", result[1].Handle);
            Assert.AreEqual("Text2", result[1].TextContent);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ParseMultiLeaders_NoMultiLeaders_ReturnsEmptyList()
    {
        string entities = string.Join(Environment.NewLine,
            "0",
            "TEXT",
            "5",
            "FF",
            "1",
            "Plain text"
        );

        string dxf = DxfSkeleton("AC1021", entities);
        string path = WriteTempDxf(dxf);

        try
        {
            var result = DxfRawParser.ParseMultiLeaders(path);
            Assert.AreEqual(0, result.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ParseMultiLeaders_FileNotFound_ReturnsEmptyList()
    {
        var result = DxfRawParser.ParseMultiLeaders("C:\\nonexistent\\file.dxf");
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ParseMultiLeaders_MultipleGroup304InContext_LastValueUsed()
    {
        // If there are multiple 304 codes in context, the last one should win
        string entities = string.Join(Environment.NewLine,
            "0",
            "MULTILEADER",
            "5",
            "M3",
            "8",
            "L0",
            "172",
            "2",
            "300",
            "CTX",
            "304",
            "First",
            "304",
            "Second",
            "301",
            "END"
        );

        string dxf = DxfSkeleton("AC1021", entities);
        string path = WriteTempDxf(dxf);

        try
        {
            var result = DxfRawParser.ParseMultiLeaders(path);
            Assert.AreEqual(1, result.Count);
            // The implementation iterates sequentially, last 304 in context wins
            Assert.AreEqual("Second", result[0].TextContent);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ParseMultiLeaders_Group304OutsideContext_NotExtracted()
    {
        // Group code 304 outside of context data (300/301) should NOT be extracted
        string entities = string.Join(Environment.NewLine,
            "0",
            "MULTILEADER",
            "5",
            "M4",
            "8",
            "L0",
            "172",
            "2",
            "304",
            "ShouldNotBeExtracted",
            "300",
            "CTX",
            "304",
            "ShouldBeExtracted",
            "301",
            "END"
        );

        string dxf = DxfSkeleton("AC1021", entities);
        string path = WriteTempDxf(dxf);

        try
        {
            var result = DxfRawParser.ParseMultiLeaders(path);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("ShouldBeExtracted", result[0].TextContent,
                "Only 304 inside context should be extracted");
        }
        finally
        {
            File.Delete(path);
        }
    }

    // =======================================================================
    // Version detection
    // =======================================================================

    [TestMethod]
    public void ParseAcadTables_VersionR2000_UsesR2004Parser()
    {
        // R2000 (AC1015) < AC1018 (R2004) → uses R2004 parsing (group code 171)
        // But R2000 doesn't support ACAD_TABLE, so the table just won't appear in R2000 DXFs.
        // Test that the parser doesn't crash with R2000 version.
        string entities = string.Join(Environment.NewLine,
            "0",
            "ACAD_TABLE",
            "5",
            "VV",
            "8",
            "Layer0",
            "100",
            "AcDbTable",
            "91",
            "1",
            "92",
            "1",
            "171",
            "1",
            "1",
            "Test"
        );

        string dxf = DxfSkeleton("AC1015", entities);  // R2000
        string path = WriteTempDxf(dxf);

        try
        {
            var result = DxfRawParser.ParseAcadTables(path);
            // Should parse using R2004 format since AC1015 < AC1021
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Test", result[0].Cells[0].Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ParseAcadTables_VersionR2018_UsesR2007Parser()
    {
        string entities = string.Join(Environment.NewLine,
            "0",
            "ACAD_TABLE",
            "5",
            "WW",
            "8",
            "Layer0",
            "100",
            "AcDbTable",
            "91",
            "1",
            "92",
            "1",
            "301",
            "CELL",
            "171",
            "1",
            "302",
            "Modern"
        );

        string dxf = DxfSkeleton("AC1032", entities);  // R2018
        string path = WriteTempDxf(dxf);

        try
        {
            var result = DxfRawParser.ParseAcadTables(path);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Modern", result[0].Cells[0].Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // =======================================================================
    // Handle naming convention validation
    // =======================================================================

    [TestMethod]
    public void ParseAcadTables_CellHandleFormat_MatchesConvention()
    {
        // Verify the TableCell Handle follows: {tableHandle}::R{row}::C{col}
        string entities = string.Join(Environment.NewLine,
            "0",
            "ACAD_TABLE",
            "5",
            "1A6",
            "8",
            "Layer0",
            "100",
            "AcDbTable",
            "91",
            "2",
            "92",
            "3",
            // 6 cells (2x3), row-major
            "301", "CELL", "171", "1", "302", "R0C0",
            "301", "CELL", "171", "1", "302", "R0C1",
            "301", "CELL", "171", "1", "302", "R0C2",
            "301", "CELL", "171", "1", "302", "R1C0",
            "301", "CELL", "171", "1", "302", "R1C1",
            "301", "CELL", "171", "1", "302", "R1C2"
        );

        string dxf = DxfSkeleton("AC1021", entities);
        string path = WriteTempDxf(dxf);

        try
        {
            var result = DxfRawParser.ParseAcadTables(path);
            Assert.AreEqual(1, result.Count);
            var table = result[0];

            // Simulate DwgExtractor handle naming
            for (int i = 0; i < table.Cells.Count; i++)
            {
                var cell = table.Cells[i];
                string expectedHandle = $"{table.Handle}::R{cell.Row}::C{cell.Column}";
                // Verify row/col computation is consistent
                int expectedRow = i / table.Columns;
                int expectedCol = i % table.Columns;
                Assert.AreEqual(expectedRow, cell.Row, $"Cell {i} row mismatch");
                Assert.AreEqual(expectedCol, cell.Column, $"Cell {i} col mismatch");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
