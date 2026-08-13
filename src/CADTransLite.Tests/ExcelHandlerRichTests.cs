// ExcelHandlerRichTests.cs
// Phase 3 integration tests for ExcelHandler multi-column export/import.
// Tests: 3-column export (id/原文/翻译), Handle-based import, row deletion, original text modification,
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
/// Tests for ExcelHandler Phase 3: 3-column (id/原文/翻译) export/import
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
    public void RichExport_HasThreeColumns()
    {
        // Arrange
        var items = CreateRichSampleItems(2);
        var settings = new ImportSettings { UseRichExcelFormat = true };

        // Act
        _handler.Export(items, _testExcelPath, settings);

        // Assert — 仅 3 列：id / 原文 / 翻译
        using var package = new ExcelPackage(new FileInfo(_testExcelPath));
        var ws = package.Workbook.Worksheets.First();

        // Verify headers
        Assert.AreEqual("id", ws.Cells[1, 1].Value);
        Assert.AreEqual("原文", ws.Cells[1, 2].Value);
        Assert.AreEqual("翻译", ws.Cells[1, 3].Value);
        // No column 4
        Assert.IsNull(ws.Cells[1, 4].Value);
    }

    [TestMethod]
    public void RichExport_DataCorrect()
    {
        // Arrange
        var items = CreateRichSampleItems(1);
        items[0].Handle = "1A3F";
        items[0].OriginalText = "Hello World";   // 原文列使用 OriginalText
        items[0].RawOriginalText = "Hello World";
        items[0].LayerName = "Layer1";
        items[0].BlockName = "BlockA";
        items[0].Status = "pending";

        var settings = new ImportSettings { UseRichExcelFormat = true };

        // Act
        _handler.Export(items, _testExcelPath, settings);

        // Assert — 仅 3 列：id / 原文 / 翻译
        using var package = new ExcelPackage(new FileInfo(_testExcelPath));
        var ws = package.Workbook.Worksheets.First();

        Assert.AreEqual("1A3F", ws.Cells[2, 1].Value);          // id (Handle)
        Assert.AreEqual("Hello World", ws.Cells[2, 2].Value);   // 原文
        Assert.AreEqual("", ws.Cells[2, 3].Value);              // 翻译（无）
        Assert.IsNull(ws.Cells[2, 4].Value);                    // 第 4 列不存在
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
            ws.Cells[2, 3].Value = "翻译1";
            ws.Cells[3, 3].Value = "翻译2";
            ws.Cells[4, 3].Value = "翻译3";
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

        // Modify original text (column B)
        using (var package = new ExcelPackage(new FileInfo(_testExcelPath)))
        {
            var ws = package.Workbook.Worksheets.First();
            ws.Cells[2, 2].Value = "Modified original";
            ws.Cells[2, 3].Value = "Some translation";
            package.Save();
        }

        // Act — should NOT error, just warn
        var (importedItems, error) = _handler.Import(_testExcelPath, items);

        // Assert
        Assert.IsNull(error, "Rich format should allow original text modification (warn only).");
        Assert.IsNotNull(importedItems);
        Assert.AreEqual("Some translation", importedItems![0].TranslatedText);
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
            ws.Cells[4, 3].Value = "New translation for item 3";
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
