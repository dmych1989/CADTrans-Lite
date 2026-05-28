// ExcelHandlerRichTests.cs
// Phase 3 integration tests for ExcelHandler multi-column export/import.
// Tests: 11-column export, Handle-based import, row deletion, original text modification,
// format auto-detection, backward compatibility with 2-column format.

using CADTransLite.Core.Models;
using CADTransLite.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeOpenXml;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CADTransLite.Tests;

/// <summary>
/// Tests for ExcelHandler Phase 3: multi-column (11-column) export/import
/// and backward compatibility with 2-column format.
/// </summary>
[TestClass]
public class ExcelHandlerRichTests
{
    private ExcelHandler _handler = null!;
    private string _testExcelPath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _handler = new ExcelHandler();
        _testExcelPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_testExcelPath))
            File.Delete(_testExcelPath);
    }

    // -----------------------------------------------------------------------
    // Rich format export tests
    // -----------------------------------------------------------------------

    [TestMethod]
    public void RichExport_CreatesFile()
    {
        // Arrange
        var items = CreateRichSampleItems(3);
        var settings = new ImportSettings { UseRichExcelFormat = true };

        // Act
        _handler.Export(items, _testExcelPath, settings);

        // Assert
        Assert.IsTrue(File.Exists(_testExcelPath));
        var fileInfo = new FileInfo(_testExcelPath);
        Assert.IsTrue(fileInfo.Length > 0);
    }

    [TestMethod]
    public void RichExport_Has11Columns()
    {
        // Arrange
        var items = CreateRichSampleItems(2);
        var settings = new ImportSettings { UseRichExcelFormat = true };

        // Act
        _handler.Export(items, _testExcelPath, settings);

        // Assert
        using var package = new ExcelPackage(new FileInfo(_testExcelPath));
        var ws = package.Workbook.Worksheets.First();

        // Verify headers
        Assert.AreEqual("Handle", ws.Cells[1, 1].Value);
        Assert.AreEqual("类型", ws.Cells[1, 2].Value);
        Assert.AreEqual("图层", ws.Cells[1, 3].Value);
        Assert.AreEqual("块名", ws.Cells[1, 4].Value);
        Assert.AreEqual("属性标签", ws.Cells[1, 5].Value);
        Assert.AreEqual("表格位置", ws.Cells[1, 6].Value);
        Assert.AreEqual("原文", ws.Cells[1, 7].Value);
        Assert.AreEqual("清洗文本", ws.Cells[1, 8].Value);
        Assert.AreEqual("译文", ws.Cells[1, 9].Value);
        Assert.AreEqual("状态", ws.Cells[1, 10].Value);
        Assert.AreEqual("备注", ws.Cells[1, 11].Value);
        // No column 12
        Assert.IsNull(ws.Cells[1, 12].Value);
    }

    [TestMethod]
    public void RichExport_DataCorrect()
    {
        // Arrange
        var items = CreateRichSampleItems(1);
        items[0].Handle = "1A3F";
        items[0].EntityType = EntityType.MText;
        items[0].LayerName = "Layer1";
        items[0].BlockName = "BlockA";
        items[0].AttributeTag = null;
        items[0].CleanedText = "Hello World";
        items[0].Status = "pending";
        items[0].Remark = null;

        var settings = new ImportSettings { UseRichExcelFormat = true };

        // Act
        _handler.Export(items, _testExcelPath, settings);

        // Assert
        using var package = new ExcelPackage(new FileInfo(_testExcelPath));
        var ws = package.Workbook.Worksheets.First();

        Assert.AreEqual("1A3F", ws.Cells[2, 1].Value);
        Assert.AreEqual("MText", ws.Cells[2, 2].Value);
        Assert.AreEqual("Layer1", ws.Cells[2, 3].Value);
        Assert.AreEqual("BlockA", ws.Cells[2, 4].Value);
        Assert.AreEqual("", ws.Cells[2, 5].Value);     // AttributeTag = null → empty
        Assert.AreEqual("", ws.Cells[2, 6].Value);     // TableCellRef = empty (not a table)
        // Column G = OriginalText (stripped)
        Assert.IsNotNull(ws.Cells[2, 7].Value);
        Assert.AreEqual("Hello World", ws.Cells[2, 8].Value);
        Assert.AreEqual("", ws.Cells[2, 9].Value);     // No translation
        Assert.AreEqual("pending", ws.Cells[2, 10].Value);
        Assert.AreEqual("", ws.Cells[2, 11].Value);    // No remark
    }

    [TestMethod]
    public void RichExport_TableCellRef()
    {
        // Arrange
        var item = new TranslationItem
        {
            Handle = "T1::R2::C3",
            EntityType = EntityType.TableCell,
            RawOriginalText = "Cell text",
            OriginalText = "Cell text",
            LayerName = "Layer0",
            CadHandles = new List<string> { "T1::R2::C3" },
            TableRow = 2,
            TableColumn = 3,
            Status = "pending",
        };

        var settings = new ImportSettings { UseRichExcelFormat = true };

        // Act
        _handler.Export(new List<TranslationItem> { item }, _testExcelPath, settings);

        // Assert
        using var package = new ExcelPackage(new FileInfo(_testExcelPath));
        var ws = package.Workbook.Worksheets.First();
        Assert.AreEqual("R2:C3", ws.Cells[2, 6].Value);
    }

    // -----------------------------------------------------------------------
    // Rich format import tests — Handle matching
    // -----------------------------------------------------------------------

    [TestMethod]
    public void RichImport_HandleMatch_Success()
    {
        // Arrange
        var items = CreateRichSampleItems(3);
        var settings = new ImportSettings { UseRichExcelFormat = true };
        _handler.Export(items, _testExcelPath, settings);

        // Add translations in the Excel file
        using (var package = new ExcelPackage(new FileInfo(_testExcelPath)))
        {
            var ws = package.Workbook.Worksheets.First();
            ws.Cells[2, 9].Value = "翻译1";
            ws.Cells[3, 9].Value = "翻译2";
            ws.Cells[4, 9].Value = "翻译3";
            package.Save();
        }

        // Act
        var (importedItems, error) = _handler.Import(_testExcelPath, items);

        // Assert
        Assert.IsNull(error);
        Assert.IsNotNull(importedItems);
        Assert.AreEqual(3, importedItems!.Count);
        Assert.AreEqual("翻译1", importedItems[0].TranslatedText);
        Assert.AreEqual("翻译2", importedItems[1].TranslatedText);
        Assert.AreEqual("翻译3", importedItems[2].TranslatedText);
    }

    [TestMethod]
    public void RichImport_DeletedRow_Skipped()
    {
        // Arrange
        var items = CreateRichSampleItems(3);
        var settings = new ImportSettings { UseRichExcelFormat = true };
        _handler.Export(items, _testExcelPath, settings);

        // Delete row 2 (first data row) by removing it
        using (var package = new ExcelPackage(new FileInfo(_testExcelPath)))
        {
            var ws = package.Workbook.Worksheets.First();
            ws.DeleteRow(2); // Delete the first data row
            package.Save();
        }

        // Act — import with original items
        var (importedItems, error) = _handler.Import(_testExcelPath, items);

        // Assert — should succeed (rich format allows row deletion)
        // Handle matching will skip the deleted row's handle
        Assert.IsNull(error);
        Assert.IsNotNull(importedItems);
    }

    [TestMethod]
    public void RichImport_OriginalTextModified_WarnOnly()
    {
        // Arrange
        var items = CreateRichSampleItems(2);
        var settings = new ImportSettings { UseRichExcelFormat = true };
        _handler.Export(items, _testExcelPath, settings);

        // Modify original text (column G)
        using (var package = new ExcelPackage(new FileInfo(_testExcelPath)))
        {
            var ws = package.Workbook.Worksheets.First();
            ws.Cells[2, 7].Value = "Modified original";
            ws.Cells[2, 9].Value = "Some translation";
            package.Save();
        }

        // Act — should NOT error, just warn
        var (importedItems, error) = _handler.Import(_testExcelPath, items);

        // Assert
        Assert.IsNull(error, "Rich format should allow original text modification (warn only).");
        Assert.IsNotNull(importedItems);
        Assert.AreEqual("Some translation", importedItems![0].TranslatedText);
    }

    [TestMethod]
    public void RichImport_StatusAndRemarkRead()
    {
        // Arrange
        var items = CreateRichSampleItems(1);
        var settings = new ImportSettings { UseRichExcelFormat = true };
        _handler.Export(items, _testExcelPath, settings);

        // Set status and remark
        using (var package = new ExcelPackage(new FileInfo(_testExcelPath)))
        {
            var ws = package.Workbook.Worksheets.First();
            ws.Cells[2, 9].Value = "翻译";
            ws.Cells[2, 10].Value = "translated";
            ws.Cells[2, 11].Value = "手动翻译";
            package.Save();
        }

        // Act
        var (importedItems, error) = _handler.Import(_testExcelPath, items);

        // Assert
        Assert.IsNull(error);
        Assert.IsNotNull(importedItems);
        Assert.AreEqual("翻译", importedItems![0].TranslatedText);
        Assert.AreEqual("translated", importedItems[0].Status);
        Assert.AreEqual("手动翻译", importedItems[0].Remark);
    }

    // -----------------------------------------------------------------------
    // Format auto-detection tests
    // -----------------------------------------------------------------------

    [TestMethod]
    public void IsRichFormat_RichExcel_ReturnsTrue()
    {
        // Arrange
        var items = CreateRichSampleItems(2);
        var settings = new ImportSettings { UseRichExcelFormat = true };
        _handler.Export(items, _testExcelPath, settings);

        // Act
        bool result = ExcelHandler.IsRichFormat(_testExcelPath);

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsRichFormat_LegacyExcel_ReturnsFalse()
    {
        // Arrange
        var items = CreateRichSampleItems(2);
        _handler.Export(items, _testExcelPath); // Legacy 2-column

        // Act
        bool result = ExcelHandler.IsRichFormat(_testExcelPath);

        // Assert
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsRichFormat_NonExistentFile_ReturnsFalse()
    {
        // Act
        bool result = ExcelHandler.IsRichFormat("nonexistent_file.xlsx");

        // Assert
        Assert.IsFalse(result);
    }

    // -----------------------------------------------------------------------
    // Backward compatibility: legacy 2-column export/import still works
    // -----------------------------------------------------------------------

    [TestMethod]
    public void LegacyExport_Import_RoundTrip()
    {
        // Arrange
        var items = CreateRichSampleItems(3);
        var settings = new ImportSettings { UseRichExcelFormat = false };

        // Act — export in legacy format
        _handler.Export(items, _testExcelPath, settings);

        // Add translations
        using (var package = new ExcelPackage(new FileInfo(_testExcelPath)))
        {
            var ws = package.Workbook.Worksheets.First();
            ws.Cells[2, 2].Value = "翻译1";
            ws.Cells[3, 2].Value = "翻译2";
            ws.Cells[4, 2].Value = "翻译3";
            package.Save();
        }

        var (importedItems, error) = _handler.Import(_testExcelPath, items);

        // Assert
        Assert.IsNull(error);
        Assert.IsNotNull(importedItems);
        Assert.AreEqual(3, importedItems!.Count);
        Assert.AreEqual("翻译1", importedItems[0].TranslatedText);
        Assert.AreEqual("翻译2", importedItems[1].TranslatedText);
        Assert.AreEqual("翻译3", importedItems[2].TranslatedText);
    }

    [TestMethod]
    public void LegacyImport_OriginalTextModified_ReturnsError()
    {
        // Arrange
        var items = CreateRichSampleItems(2);
        _handler.Export(items, _testExcelPath);

        // Modify column A (原文)
        using (var package = new ExcelPackage(new FileInfo(_testExcelPath)))
        {
            var ws = package.Workbook.Worksheets.First();
            ws.Cells[2, 1].Value = "Modified original text";
            package.Save();
        }

        // Act
        var (importedItems, error) = _handler.Import(_testExcelPath, items);

        // Assert — legacy format should reject modified original text
        Assert.IsNotNull(error);
        Assert.IsTrue(error.Contains("原文") || error.Contains("Original"));
    }

    // -----------------------------------------------------------------------
    // Export round-trip: rich export → rich import
    // -----------------------------------------------------------------------

    [TestMethod]
    public void RichExport_Import_RoundTrip()
    {
        // Arrange
        var items = CreateRichSampleItems(3);
        items[0].TranslatedText = "Already translated";
        items[1].Status = "skipped";
        items[2].Remark = "Needs review";

        var settings = new ImportSettings { UseRichExcelFormat = true };

        // Act
        _handler.Export(items, _testExcelPath, settings);

        // Modify translation in Excel
        using (var package = new ExcelPackage(new FileInfo(_testExcelPath)))
        {
            var ws = package.Workbook.Worksheets.First();
            ws.Cells[4, 9].Value = "New translation for item 3";
            package.Save();
        }

        var (importedItems, error) = _handler.Import(_testExcelPath, items);

        // Assert
        Assert.IsNull(error);
        Assert.IsNotNull(importedItems);
        Assert.AreEqual(3, importedItems!.Count);
        Assert.AreEqual("Already translated", importedItems[0].TranslatedText);
        Assert.AreEqual("skipped", importedItems[1].Status);
        Assert.AreEqual("New translation for item 3", importedItems[2].TranslatedText);
        Assert.AreEqual("Needs review", importedItems[2].Remark);
    }

    // -----------------------------------------------------------------------
    // Helper methods
    // -----------------------------------------------------------------------

    private static List<TranslationItem> CreateRichSampleItems(int count)
    {
        var items = new List<TranslationItem>();
        for (int i = 0; i < count; i++)
        {
            items.Add(new TranslationItem
            {
                Handle = $"Handle{i:X6}",
                EntityType = EntityType.Text,
                OriginalText = $"Original text {i + 1}",
                RawOriginalText = $"Raw text {i + 1}",
                TranslatedText = null,
                FormatPlaceholders = new Dictionary<string, string>(),
                LayerName = $"Layer{i}",
                ExcelRowIndex = i + 2,
                CadHandles = new List<string> { $"Handle{i:X6}" },
                CleanedText = $"cleaned {i + 1}",
                Status = "pending",
            });
        }
        return items;
    }
}
