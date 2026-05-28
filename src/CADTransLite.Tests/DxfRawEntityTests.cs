// DxfRawEntityTests.cs
// Unit tests for DxfRawEntity data models — AcadTableData, TableCellData, MultiLeaderData.
// Phase 2: Validates data model field completeness and default values.

using CADTransLite.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CADTransLite.Tests;

/// <summary>
/// DxfRawEntity model unit tests.
/// Validates field completeness, default values, and Handle naming conventions.
/// </summary>
[TestClass]
public class DxfRawEntityTests
{
    // =======================================================================
    // AcadTableData
    // =======================================================================

    [TestMethod]
    public void AcadTableData_DefaultValues_AreCorrect()
    {
        var table = new AcadTableData();

        Assert.AreEqual(string.Empty, table.Handle);
        Assert.AreEqual(string.Empty, table.LayerName);
        Assert.AreEqual(0, table.Rows);
        Assert.AreEqual(0, table.Columns);
        Assert.IsNotNull(table.Cells);
        Assert.AreEqual(0, table.Cells.Count);
    }

    [TestMethod]
    public void AcadTableData_SetProperties_StoresCorrectly()
    {
        var table = new AcadTableData
        {
            Handle = "1A6",
            LayerName = "MyLayer",
            Rows = 3,
            Columns = 4,
            Cells = new List<TableCellData>
            {
                new() { Row = 0, Column = 0, CellType = 1, Text = "Cell00" },
                new() { Row = 0, Column = 1, CellType = 2, Text = "" },
            }
        };

        Assert.AreEqual("1A6", table.Handle);
        Assert.AreEqual("MyLayer", table.LayerName);
        Assert.AreEqual(3, table.Rows);
        Assert.AreEqual(4, table.Columns);
        Assert.AreEqual(2, table.Cells.Count);
    }

    // =======================================================================
    // TableCellData
    // =======================================================================

    [TestMethod]
    public void TableCellData_DefaultValues_AreCorrect()
    {
        var cell = new TableCellData();

        Assert.AreEqual(0, cell.Row);
        Assert.AreEqual(0, cell.Column);
        Assert.AreEqual(0, cell.CellType);
        Assert.AreEqual(string.Empty, cell.Text);
        Assert.AreEqual(-1, cell.TextLineNumber, "TextLineNumber should default to -1 (not set)");
    }

    [TestMethod]
    public void TableCellData_SetProperties_StoresCorrectly()
    {
        var cell = new TableCellData
        {
            Row = 2,
            Column = 3,
            CellType = 1,
            Text = "{\\H1.5x;Hello}",
            TextLineNumber = 42
        };

        Assert.AreEqual(2, cell.Row);
        Assert.AreEqual(3, cell.Column);
        Assert.AreEqual(1, cell.CellType);
        Assert.AreEqual("{\\H1.5x;Hello}", cell.Text);
        Assert.AreEqual(42, cell.TextLineNumber);
    }

    [TestMethod]
    public void TableCellData_CellType_Text_Is1()
    {
        // CellType = 1 means text cell
        var cell = new TableCellData { CellType = 1 };
        Assert.AreEqual(1, cell.CellType);
    }

    [TestMethod]
    public void TableCellData_CellType_Block_Is2()
    {
        // CellType = 2 means block cell
        var cell = new TableCellData { CellType = 2 };
        Assert.AreEqual(2, cell.CellType);
    }

    // =======================================================================
    // MultiLeaderData
    // =======================================================================

    [TestMethod]
    public void MultiLeaderData_DefaultValues_AreCorrect()
    {
        var ml = new MultiLeaderData();

        Assert.AreEqual(string.Empty, ml.Handle);
        Assert.AreEqual(string.Empty, ml.LayerName);
        Assert.AreEqual(0, ml.ContentType);
        Assert.AreEqual(string.Empty, ml.TextContent);
        Assert.AreEqual(string.Empty, ml.TextStyleHandle);
        Assert.AreEqual(0.0, ml.TextBoundaryWidth);
        Assert.AreEqual(-1, ml.TextLineNumber, "TextLineNumber should default to -1 (not set)");
    }

    [TestMethod]
    public void MultiLeaderData_SetProperties_StoresCorrectly()
    {
        var ml = new MultiLeaderData
        {
            Handle = "1A7",
            LayerName = "Annotations",
            ContentType = 2,
            TextContent = "{\\H2.0;Note text}",
            TextStyleHandle = "2F",
            TextBoundaryWidth = 50.5,
            TextLineNumber = 99
        };

        Assert.AreEqual("1A7", ml.Handle);
        Assert.AreEqual("Annotations", ml.LayerName);
        Assert.AreEqual(2, ml.ContentType);
        Assert.AreEqual("{\\H2.0;Note text}", ml.TextContent);
        Assert.AreEqual("2F", ml.TextStyleHandle);
        Assert.AreEqual(50.5, ml.TextBoundaryWidth);
        Assert.AreEqual(99, ml.TextLineNumber);
    }

    [TestMethod]
    public void MultiLeaderData_ContentType_Text_Is2()
    {
        var ml = new MultiLeaderData { ContentType = 2 };
        Assert.AreEqual(2, ml.ContentType);
    }

    [TestMethod]
    public void MultiLeaderData_ContentType_Block_Is1()
    {
        var ml = new MultiLeaderData { ContentType = 1 };
        Assert.AreEqual(1, ml.ContentType);
    }

    [TestMethod]
    public void MultiLeaderData_ContentType_None_Is0()
    {
        var ml = new MultiLeaderData { ContentType = 0 };
        Assert.AreEqual(0, ml.ContentType);
    }

    // =======================================================================
    // Handle naming convention validation
    // =======================================================================

    [TestMethod]
    public void TableCellHandle_FollowsConvention()
    {
        // TableCell Handle format: {tableHandle}::R{row}::C{col}
        string tableHandle = "1A6";
        int row = 0, col = 2;
        string cellHandle = $"{tableHandle}::R{row}::C{col}";

        Assert.AreEqual("1A6::R0::C2", cellHandle);
        Assert.IsTrue(cellHandle.Contains("::R"));
        Assert.IsTrue(cellHandle.Contains("::C"));
    }

    [TestMethod]
    public void MLeaderHandle_FollowsConvention()
    {
        // MLeader Handle format: {mleaderHandle}::CTX
        string mleaderHandle = "1A7";
        string compositeHandle = $"{mleaderHandle}::CTX";

        Assert.AreEqual("1A7::CTX", compositeHandle);
        Assert.IsTrue(compositeHandle.EndsWith("::CTX"));
    }

    [TestMethod]
    public void MLeaderHandle_CompositeParse_ExtractsBaseHandle()
    {
        // Simulating DwgWriter's handle parsing: "1A7::CTX" → "1A7"
        string composite = "1A7::CTX";
        string baseHandle = composite.EndsWith("::CTX")
            ? composite[..^5]
            : composite;

        Assert.AreEqual("1A7", baseHandle);
    }

    [TestMethod]
    public void TableCellHandle_CompositeParse_ExtractsTableHandle()
    {
        // Simulating DxfTextReplacer's handle parsing: "1A6::R0::C2" → "1A6"
        string composite = "1A6::R0::C2";
        int rIdx = composite.IndexOf("::R", StringComparison.Ordinal);
        string tableHandle = rIdx > 0 ? composite[..rIdx] : composite;

        Assert.AreEqual("1A6", tableHandle);
    }

    // =======================================================================
    // ImportSettings — Phase 2 fields
    // =======================================================================

    [TestMethod]
    public void ImportSettings_ImportAcadTables_DefaultIsTrue()
    {
        var settings = new ImportSettings();
        Assert.IsTrue(settings.ImportAcadTables);
    }

    [TestMethod]
    public void ImportSettings_ImportMultiLeaders_DefaultIsTrue()
    {
        var settings = new ImportSettings();
        Assert.IsTrue(settings.ImportMultiLeaders);
    }

    [TestMethod]
    public void ImportSettings_SetToFalse_StoresCorrectly()
    {
        var settings = new ImportSettings
        {
            ImportAcadTables = false,
            ImportMultiLeaders = false
        };

        Assert.IsFalse(settings.ImportAcadTables);
        Assert.IsFalse(settings.ImportMultiLeaders);
    }

    // =======================================================================
    // EntityType enum — Phase 2 additions
    // =======================================================================

    [TestMethod]
    public void EntityType_TableCell_Exists()
    {
        Assert.IsTrue(Enum.IsDefined(typeof(EntityType), EntityType.TableCell));
    }

    [TestMethod]
    public void EntityType_MLeader_Exists()
    {
        Assert.IsTrue(Enum.IsDefined(typeof(EntityType), EntityType.MLeader));
    }

    // =======================================================================
    // TranslationItem — Phase 2 fields
    // =======================================================================

    [TestMethod]
    public void TranslationItem_TableRow_DefaultIsMinus1()
    {
        var item = new TranslationItem();
        Assert.AreEqual(-1, item.TableRow);
    }

    [TestMethod]
    public void TranslationItem_TableColumn_DefaultIsMinus1()
    {
        var item = new TranslationItem();
        Assert.AreEqual(-1, item.TableColumn);
    }

    [TestMethod]
    public void TranslationItem_TableCell_SetsRowAndColumn()
    {
        var item = new TranslationItem
        {
            Handle = "1A6::R2::C3",
            EntityType = EntityType.TableCell,
            TableRow = 2,
            TableColumn = 3,
        };

        Assert.AreEqual("1A6::R2::C3", item.Handle);
        Assert.AreEqual(EntityType.TableCell, item.EntityType);
        Assert.AreEqual(2, item.TableRow);
        Assert.AreEqual(3, item.TableColumn);
    }

    [TestMethod]
    public void TranslationItem_MLeader_UsesCompositeHandle()
    {
        var item = new TranslationItem
        {
            Handle = "1A7::CTX",
            EntityType = EntityType.MLeader,
            TableRow = -1,
            TableColumn = -1,
        };

        Assert.AreEqual("1A7::CTX", item.Handle);
        Assert.AreEqual(EntityType.MLeader, item.EntityType);
        Assert.AreEqual(-1, item.TableRow);
        Assert.AreEqual(-1, item.TableColumn);
    }
}
