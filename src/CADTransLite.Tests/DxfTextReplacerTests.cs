// DxfTextReplacerTests.cs
// Unit tests for DxfTextReplacer — DXF text precise replacement for
// ACAD_TABLE and MULTILEADER entities. Phase 2 validation.

using CADTransLite.Core.Models;
using CADTransLite.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CADTransLite.Tests;

/// <summary>
/// DxfTextReplacer unit tests.
/// Covers: line-number-based replacement, composite handle parsing,
/// replacement preserving total line count, MLEADER replacement.
/// </summary>
[TestClass]
public class DxfTextReplacerTests
{
    // -----------------------------------------------------------------------
    // Helper: write a minimal DXF file to a temp path
    // -----------------------------------------------------------------------

    private static string WriteTempDxf(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"DxfReplacerTest_{Guid.NewGuid():N}.dxf");
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
    // ACAD_TABLE cell replacement
    // =======================================================================

    [TestMethod]
    public void Replace_TableCell_R2007_ReplacesCorrectly()
    {
        // Arrange: Create a DXF with a 1x1 ACAD_TABLE (R2007+ format)
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
            "1",
            "92",
            "1",
            "301",
            "CELL",
            "171",
            "1",
            "302",
            "OldText",
            "0",
            "EOF"
        );

        string dxf = DxfSkeleton("AC1021", entities);
        string path = WriteTempDxf(dxf);

        try
        {
            var replacements = new List<(string handle, int row, int col, string newText)>
            {
                ("1A6::R0::C0", 0, 0, "NewText")
            };

            // Act
            var (updated, notFound, log) = DxfTextReplacer.Replace(path, replacements);

            // Assert
            Assert.AreEqual(1, updated, "One replacement should succeed");
            Assert.AreEqual(0, notFound, "No replacements should be not-found");

            // Verify the file content was updated
            string[] lines = File.ReadAllLines(path);
            bool foundNewText = lines.Any(l => l.Trim() == "NewText");
            Assert.IsTrue(foundNewText, "NewText should be present in the file");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Replace_TableCell_R2004_ReplacesCorrectly()
    {
        // Arrange: Create a DXF with a 1x1 ACAD_TABLE (R2004 format)
        string entities = string.Join(Environment.NewLine,
            "0",
            "ACAD_TABLE",
            "5",
            "2B5",
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
            "Original",
            "0",
            "EOF"
        );

        string dxf = DxfSkeleton("AC1018", entities);
        string path = WriteTempDxf(dxf);

        try
        {
            var replacements = new List<(string handle, int row, int col, string newText)>
            {
                ("2B5::R0::C0", 0, 0, "Translated")
            };

            var (updated, notFound, _) = DxfTextReplacer.Replace(path, replacements);

            Assert.AreEqual(1, updated);
            Assert.AreEqual(0, notFound);

            string[] lines = File.ReadAllLines(path);
            Assert.IsTrue(lines.Any(l => l.Trim() == "Translated"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Replace_TableCell_MultipleCells_ReplacesAll()
    {
        // Arrange: 2x2 table with different texts
        string entities = string.Join(Environment.NewLine,
            "0",
            "ACAD_TABLE",
            "5",
            "T1",
            "8",
            "Layer0",
            "100",
            "AcDbTable",
            "91",
            "2",
            "92",
            "2",
            "301", "CELL", "171", "1", "302", "Cell00",
            "301", "CELL", "171", "1", "302", "Cell01",
            "301", "CELL", "171", "1", "302", "Cell10",
            "301", "CELL", "171", "1", "302", "Cell11"
        );

        string dxf = DxfSkeleton("AC1021", entities);
        string path = WriteTempDxf(dxf);

        try
        {
            var replacements = new List<(string handle, int row, int col, string newText)>
            {
                ("T1::R0::C0", 0, 0, "Trans00"),
                ("T1::R0::C1", 0, 1, "Trans01"),
                ("T1::R1::C0", 1, 0, "Trans10"),
                ("T1::R1::C1", 1, 1, "Trans11")
            };

            var (updated, notFound, _) = DxfTextReplacer.Replace(path, replacements);

            Assert.AreEqual(4, updated);
            Assert.AreEqual(0, notFound);

            string content = File.ReadAllText(path);
            Assert.IsTrue(content.Contains("Trans00"));
            Assert.IsTrue(content.Contains("Trans01"));
            Assert.IsTrue(content.Contains("Trans10"));
            Assert.IsTrue(content.Contains("Trans11"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // =======================================================================
    // MULTILEADER text replacement
    // =======================================================================

    [TestMethod]
    public void Replace_MLeader_ReplacesCorrectly()
    {
        // Arrange: MLEADER with text content
        string entities = string.Join(Environment.NewLine,
            "0",
            "MULTILEADER",
            "5",
            "ML1",
            "8",
            "AnnotLayer",
            "172",
            "2",
            "300",
            "CTX",
            "304",
            "Old Note",
            "340",
            "2F",
            "41",
            "50.5",
            "301",
            "END"
        );

        string dxf = DxfSkeleton("AC1021", entities);
        string path = WriteTempDxf(dxf);

        try
        {
            var replacements = new List<(string handle, int row, int col, string newText)>
            {
                ("ML1", -1, -1, "New Note")
            };

            var (updated, notFound, _) = DxfTextReplacer.Replace(path, replacements);

            Assert.AreEqual(1, updated);
            Assert.AreEqual(0, notFound);

            string[] lines = File.ReadAllLines(path);
            Assert.IsTrue(lines.Any(l => l.Trim() == "New Note"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // =======================================================================
    // Composite Handle parsing (TableCell format)
    // =======================================================================

    [TestMethod]
    public void Replace_TableCell_CompositeHandle_ParsesTableHandle()
    {
        // The DxfTextReplacer should parse "1A6::R0::C2" and extract table handle "1A6"
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
            "1",
            "92",
            "3",
            "301", "CELL", "171", "1", "302", "A",
            "301", "CELL", "171", "1", "302", "B",
            "301", "CELL", "171", "1", "302", "C"
        );

        string dxf = DxfSkeleton("AC1021", entities);
        string path = WriteTempDxf(dxf);

        try
        {
            // Replace only column 2 (index 2)
            var replacements = new List<(string handle, int row, int col, string newText)>
            {
                ("1A6::R0::C2", 0, 2, "ReplacedC")
            };

            var (updated, notFound, _) = DxfTextReplacer.Replace(path, replacements);

            Assert.AreEqual(1, updated);
            Assert.AreEqual(0, notFound);

            string[] lines = File.ReadAllLines(path);
            Assert.IsTrue(lines.Any(l => l.Trim() == "ReplacedC"),
                "Column 2 text should be replaced");
        }
        finally
        {
            File.Delete(path);
        }
    }

    // =======================================================================
    // Replacement preserves total line count
    // =======================================================================

    [TestMethod]
    public void Replace_ReplacementDoesNotChangeTotalLineCount()
    {
        string entities = string.Join(Environment.NewLine,
            "0",
            "ACAD_TABLE",
            "5",
            "LC",
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
            "Short",
            "0",
            "EOF"
        );

        string dxf = DxfSkeleton("AC1021", entities);
        string path = WriteTempDxf(dxf);

        try
        {
            int lineCountBefore = File.ReadAllLines(path).Length;

            var replacements = new List<(string handle, int row, int col, string newText)>
            {
                ("LC::R0::C0", 0, 0, "A Much Longer Replacement Text That Should Not Change Line Count")
            };

            DxfTextReplacer.Replace(path, replacements);

            int lineCountAfter = File.ReadAllLines(path).Length;
            Assert.AreEqual(lineCountBefore, lineCountAfter,
                "Replacement should not change total number of lines in the file");
        }
        finally
        {
            File.Delete(path);
        }
    }

    // =======================================================================
    // Not-found cases
    // =======================================================================

    [TestMethod]
    public void Replace_HandleNotFound_ReturnsNotFound()
    {
        string entities = string.Join(Environment.NewLine,
            "0",
            "ACAD_TABLE",
            "5",
            "XX",
            "8",
            "Layer0",
            "100",
            "AcDbTable",
            "91",
            "1",
            "92",
            "1",
            "301", "CELL", "171", "1", "302", "Text"
        );

        string dxf = DxfSkeleton("AC1021", entities);
        string path = WriteTempDxf(dxf);

        try
        {
            var replacements = new List<(string handle, int row, int col, string newText)>
            {
                ("NONEXISTENT::R0::C0", 0, 0, "NewText")
            };

            var (updated, notFound, _) = DxfTextReplacer.Replace(path, replacements);

            Assert.AreEqual(0, updated);
            Assert.AreEqual(1, notFound, "Non-existent handle should be counted as not-found");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Replace_FileNotFound_ReturnsAllNotFound()
    {
        var replacements = new List<(string handle, int row, int col, string newText)>
        {
            ("A", -1, -1, "Text")
        };

        var (updated, notFound, _) = DxfTextReplacer.Replace("C:\\nonexistent.dxf", replacements);

        Assert.AreEqual(0, updated);
        Assert.AreEqual(1, notFound);
    }

    // =======================================================================
    // Empty replacements
    // =======================================================================

    [TestMethod]
    public void Replace_EmptyReplacements_ReturnsZero()
    {
        string path = WriteTempDxf("dummy content");

        try
        {
            var (updated, notFound, _) = DxfTextReplacer.Replace(path, new List<(string, int, int, string)>());
            Assert.AreEqual(0, updated);
            Assert.AreEqual(0, notFound);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Replace_NullReplacements_ReturnsZero()
    {
        string path = WriteTempDxf("dummy content");

        try
        {
            var (updated, notFound, _) = DxfTextReplacer.Replace(path, null!);
            Assert.AreEqual(0, updated);
            Assert.AreEqual(0, notFound);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // =======================================================================
    // Combined ACAD_TABLE + MLEADER replacement
    // =======================================================================

    [TestMethod]
    public void Replace_MixedTableAndMLeader_ReplacesBoth()
    {
        string entities = string.Join(Environment.NewLine,
            "0",
            "ACAD_TABLE",
            "5",
            "TB",
            "8",
            "Layer0",
            "100",
            "AcDbTable",
            "91",
            "1",
            "92",
            "1",
            "301", "CELL", "171", "1", "302", "TableOld",
            "0",
            "MULTILEADER",
            "5",
            "ML",
            "8",
            "Layer0",
            "172",
            "2",
            "300", "CTX",
            "304", "MleaderOld",
            "301", "END"
        );

        string dxf = DxfSkeleton("AC1021", entities);
        string path = WriteTempDxf(dxf);

        try
        {
            var replacements = new List<(string handle, int row, int col, string newText)>
            {
                ("TB::R0::C0", 0, 0, "TableNew"),
                ("ML", -1, -1, "MleaderNew")
            };

            var (updated, notFound, _) = DxfTextReplacer.Replace(path, replacements);

            Assert.AreEqual(2, updated, "Both table cell and MLEADER should be replaced");
            Assert.AreEqual(0, notFound);

            string content = File.ReadAllText(path);
            Assert.IsTrue(content.Contains("TableNew"));
            Assert.IsTrue(content.Contains("MleaderNew"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
