// ExcelHandlerTests.cs
// Unit tests for ExcelHandler (Export/Import).
// Updated for 2-column format: A=原文, B=译文 (no ID column).

using CADTransLite.Core.Models;
using CADTransLite.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeOpenXml;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CADTransLite.Tests;

/// <summary>
/// Tests for ExcelHandler class.
/// v3.1+ uses a two-column layout: A=原文 (cleaned RawOriginalText), B=译文 (Translation).
/// </summary>
[TestClass]
public class ExcelHandlerTests
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
        {
            File.Delete(_testExcelPath);
        }
    }

    // -----------------------------------------------------------------------
    // Export tests
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Export_CreatesFile()
    {
        // Arrange
        var items = CreateSampleItems(3);

        // Act
        _handler.Export(items, _testExcelPath);

        // Assert
        Assert.IsTrue(File.Exists(_testExcelPath));
        var fileInfo = new FileInfo(_testExcelPath);
        Assert.IsTrue(fileInfo.Length > 0);
    }

    [TestMethod]
    public void Export_HeadersCorrect()
    {
        // Arrange
        var items = CreateSampleItems(2);

        // Act
        _handler.Export(items, _testExcelPath);

        // Assert — 2-column format: A=原文, B=译文
        using var package = new ExcelPackage(new FileInfo(_testExcelPath));
        var ws = package.Workbook.Worksheets.First();

        Assert.AreEqual("原文", ws.Cells[1, 1].Value);
        Assert.AreEqual("译文", ws.Cells[1, 2].Value);
        // No column C header
        Assert.IsNull(ws.Cells[1, 3].Value);
    }

    [TestMethod]
    public void Export_RowCountCorrect()
    {
        // Arrange
        var items = CreateSampleItems(5);

        // Act
        _handler.Export(items, _testExcelPath);

        // Assert
        using var package = new ExcelPackage(new FileInfo(_testExcelPath));
        var ws = package.Workbook.Worksheets.First();
        int dataRows = ws.Dimension.Rows - 1; // Subtract header
        Assert.AreEqual(5, dataRows);
    }

    [TestMethod]
    public void Export_TwoColumnLayout()
    {
        // Arrange
        var items = CreateSampleItems(1);

        // Act
        _handler.Export(items, _testExcelPath);

        // Assert — verify 2 columns only, data in columns A and B
        using var package = new ExcelPackage(new FileInfo(_testExcelPath));
        var ws = package.Workbook.Worksheets.First();

        Assert.IsNotNull(ws.Cells[2, 1].Value); // Column A = 原文 (data)
        Assert.IsNotNull(ws.Cells[2, 2].Value); // Column B = 译文 (data, empty string)
        Assert.IsNull(ws.Cells[2, 3].Value);    // No column C
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentException))]
    public void Export_EmptyItems_ThrowsArgumentException()
    {
        // Arrange
        var items = new List<TranslationItem>();

        // Act
        _handler.Export(items, _testExcelPath);
    }

    // -----------------------------------------------------------------------
    // Import tests
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Import_ValidFile_ReturnsCorrectData()
    {
        // Arrange
        var items = CreateSampleItems(3);
        _handler.Export(items, _testExcelPath);

        // Act
        var (importedItems, error) = _handler.Import(_testExcelPath, items);

        // Assert
        Assert.IsNull(error);
        Assert.IsNotNull(importedItems);
        Assert.AreEqual(3, importedItems!.Count);
    }

    [TestMethod]
    public void Import_RowCountMismatch_DegradedMatch()
    {
        // Arrange — export 3 items, import with 2-item originalItems
        // Previously this would error; now it degrades to sequential matching
        var items = CreateSampleItems(3);
        _handler.Export(items, _testExcelPath);

        // Add translations in the Excel file
        using (var package = new ExcelPackage(new FileInfo(_testExcelPath)))
        {
            var ws = package.Workbook.Worksheets.First();
            ws.Cells[2, 2].Value = "翻译1";
            ws.Cells[3, 2].Value = "翻译2";
            ws.Cells[4, 2].Value = "翻译3";
            package.Save();
        }

        // Modify: create a different count
        var differentItems = CreateSampleItems(2);

        // Act — should now succeed with degraded matching instead of erroring
        var (importedItems, error) = _handler.Import(_testExcelPath, differentItems);

        // Assert — degraded matching returns results (no error)
        Assert.IsNull(error, "Row count mismatch should now degrade to sequential matching, not error.");
        Assert.IsNotNull(importedItems);
        Assert.AreEqual(2, importedItems!.Count);
        // First 2 items should get translations from Excel rows 2 and 3
        Assert.AreEqual("翻译1", importedItems[0].TranslatedText);
        Assert.AreEqual("翻译2", importedItems[1].TranslatedText);
    }

    [TestMethod]
    public void Import_OriginalTextModified_ReturnsError()
    {
        // Arrange
        var items = CreateSampleItems(2);
        _handler.Export(items, _testExcelPath);

        // Modify column A (原文) in the Excel file — column 1 in 2-col format
        using (var package = new ExcelPackage(new FileInfo(_testExcelPath)))
        {
            var ws = package.Workbook.Worksheets.First();
            ws.Cells[2, 1].Value = "Modified original text";
            package.Save();
        }

        // Act
        var (importedItems, error) = _handler.Import(_testExcelPath, items);

        // Assert
        Assert.IsNotNull(error);
        Assert.IsTrue(error.Contains("原文") || error.Contains("Original"));
    }

    [TestMethod]
    public void Import_EmptyTranslation_KeepsOriginal()
    {
        // Arrange
        var items = CreateSampleItems(2);
        items[0].TranslatedText = null;           // item[0]: no translation
        items[1].TranslatedText = "Should be kept"; // item[1]: has translation
        _handler.Export(items, _testExcelPath);

        // Clear column B (译文) for first row — column 2 in 2-col format
        using (var package = new ExcelPackage(new FileInfo(_testExcelPath)))
        {
            var ws = package.Workbook.Worksheets.First();
            ws.Cells[2, 2].Value = "";
            package.Save();
        }

        // Act
        var (importedItems, error) = _handler.Import(_testExcelPath, items);

        // Assert
        Assert.IsNull(error);
        Assert.IsNotNull(importedItems);
        // First item has empty translation → TranslatedText should be null
        Assert.IsNull(importedItems![0].TranslatedText);
        // Second item has non-empty translation → should be preserved
        Assert.AreEqual("Should be kept", importedItems[1].TranslatedText);
    }

    [TestMethod]
    public void Import_WithTranslation_ReturnsTranslated()
    {
        // Arrange
        var items = CreateSampleItems(2);
        _handler.Export(items, _testExcelPath);

        // Add translations in column B (译文) — column 2 in 2-col format
        using (var package = new ExcelPackage(new FileInfo(_testExcelPath)))
        {
            var ws = package.Workbook.Worksheets.First();
            ws.Cells[2, 2].Value = "Translated 1";
            ws.Cells[3, 2].Value = "Translated 2";
            package.Save();
        }

        // Act
        var (importedItems, error) = _handler.Import(_testExcelPath, items);

        // Assert
        Assert.IsNull(error);
        Assert.IsNotNull(importedItems);
        Assert.AreEqual("Translated 1", importedItems![0].TranslatedText);
        Assert.AreEqual("Translated 2", importedItems[1].TranslatedText);
    }

    [TestMethod]
    public void Import_NonExistentFile_ReturnsError()
    {
        // Arrange
        var items = CreateSampleItems(1);
        string nonExistent = Path.Combine(Path.GetTempPath(), "nonexistent.xlsx");

        // Act
        var (importedItems, error) = _handler.Import(nonExistent, items);

        // Assert
        Assert.IsNotNull(error);
        Assert.IsTrue(error.Contains("not found") || error.Contains("找不到"));
    }

    // -----------------------------------------------------------------------
    // Helper methods
    // -----------------------------------------------------------------------

    private static List<TranslationItem> CreateSampleItems(int count)
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
                LayerName = "TestLayer",
                ExcelRowIndex = i + 2, // 1-based, header = 1
                CadHandles = new List<string> { $"Handle{i:X6}" }
            });
        }
        return items;
    }
}
