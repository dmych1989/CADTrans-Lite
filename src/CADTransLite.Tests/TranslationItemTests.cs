// TranslationItemTests.cs
// Unit tests for TranslationItem model.

using CADTransLite.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace CADTransLite.Tests;

/// <summary>
/// Tests for TranslationItem class.
/// </summary>
[TestClass]
public class TranslationItemTests
{
    // -----------------------------------------------------------------------
    // IdString property tests
    // -----------------------------------------------------------------------

    [TestMethod]
    public void IdString_SingleHandle_ReturnsCorrectFormat()
    {
        // Arrange
        var item = new TranslationItem
        {
            CadHandles = new List<string> { "2D07D5" }
        };

        // Act
        string idString = item.IdString;

        // Assert
        Assert.IsTrue(idString.Contains("2D07D5"));
    }

    [TestMethod]
    public void IdString_MultipleHandles_ReturnsCommaSeparated()
    {
        // Arrange
        var item = new TranslationItem
        {
            CadHandles = new List<string> { "2D07D5", "2E9C72", "2E9D68" }
        };

        // Act
        string idString = item.IdString;

        // Assert
        Assert.IsTrue(idString.Contains(","));
        Assert.IsTrue(idString.Contains("2D07D5"));
    }

    [TestMethod]
    public void IdString_EmptyHandles_ReturnsEmptyString()
    {
        // Arrange
        var item = new TranslationItem
        {
            CadHandles = new List<string>()
        };

        // Act
        string idString = item.IdString;

        // Assert
        Assert.AreEqual("", idString);
    }

    [TestMethod]
    public void IdString_NullHandles_ReturnsEmptyString()
    {
        // Arrange
        var item = new TranslationItem
        {
            CadHandles = null
        };

        // Act
        string idString = item.IdString;

        // Assert
        Assert.AreEqual("", idString);
    }

    [TestMethod]
    public void IdString_SingleHandleWithPrefix_ReturnsFormatted()
    {
        // Arrange
        var item = new TranslationItem
        {
            CadHandles = new List<string> { "2D07D5" }
        };

        // Act
        string idString = item.IdString;

        // Assert
        // Should be "@_2D07D5_&f0" format
        Assert.IsTrue(idString.Contains("@_"));
        Assert.IsTrue(idString.Contains("_&f0"));
    }

    // -----------------------------------------------------------------------
    // FormatPlaceholders property tests
    // -----------------------------------------------------------------------

    [TestMethod]
    public void FormatPlaceholders_DefaultValue_IsEmptyDictionary()
    {
        // Arrange
        var item = new TranslationItem();

        // Act & Assert
        Assert.IsNotNull(item.FormatPlaceholders);
        Assert.AreEqual(0, item.FormatPlaceholders.Count);
    }

    [TestMethod]
    public void FormatPlaceholders_SetValue_StoresCorrectly()
    {
        // Arrange
        var item = new TranslationItem
        {
            FormatPlaceholders = new Dictionary<string, string>
            {
                { "[FMT:1]", "\\P" },
                { "[FMT:2]", "\\L" }
            }
        };

        // Act & Assert
        Assert.AreEqual(2, item.FormatPlaceholders.Count);
        Assert.AreEqual("\\P", item.FormatPlaceholders["[FMT:1]"]);
        Assert.AreEqual("\\L", item.FormatPlaceholders["[FMT:2]"]);
    }

    [TestMethod]
    public void FormatPlaceholders_MTextEntity_CanStoreCodes()
    {
        // Arrange
        var item = new TranslationItem
        {
            EntityType = EntityType.MText,
            RawOriginalText = "Line1\\PLine2",
            OriginalText = "Line1 Line2",
            FormatPlaceholders = new Dictionary<string, string>
            {
                { "[FMT:1]", "\\P" }
            }
        };

        // Act & Assert
        Assert.AreEqual(EntityType.MText, item.EntityType);
        Assert.IsTrue(item.FormatPlaceholders.Count > 0);
    }

    // -----------------------------------------------------------------------
    // TranslatedText property tests
    // -----------------------------------------------------------------------

    [TestMethod]
    public void TranslatedText_DefaultValue_IsNull()
    {
        // Arrange
        var item = new TranslationItem
        {
            OriginalText = "Sample text"
        };

        // Act & Assert
        Assert.IsNull(item.TranslatedText);
        Assert.AreEqual("Sample text", item.OriginalText);
    }

    [TestMethod]
    public void TranslatedText_SetValue_StoresCorrectly()
    {
        // Arrange
        var item = new TranslationItem
        {
            OriginalText = "Sample text",
            TranslatedText = "Translated text"
        };

        // Act & Assert
        Assert.AreEqual("Translated text", item.TranslatedText);
    }

    [TestMethod]
    public void TranslatedText_SetToNull_KeepsOriginal()
    {
        // Arrange
        var item = new TranslationItem
        {
            OriginalText = "Sample text",
            TranslatedText = null
        };

        // Act & Assert
        Assert.IsNull(item.TranslatedText);
        Assert.AreEqual("Sample text", item.OriginalText);
    }

    // -----------------------------------------------------------------------
    // CadHandles property tests (merge support)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void CadHandles_DefaultValue_IsNull()
    {
        // Arrange
        var item = new TranslationItem();

        // Act & Assert
        Assert.IsNull(item.CadHandles);
    }

    [TestMethod]
    public void CadHandles_SetValue_StoresAllHandles()
    {
        // Arrange
        var item = new TranslationItem
        {
            CadHandles = new List<string> { "2D07D5", "2E9C72", "2E9D68" }
        };

        // Act & Assert
        Assert.IsNotNull(item.CadHandles);
        Assert.AreEqual(3, item.CadHandles.Count);
        Assert.AreEqual("2D07D5", item.CadHandles[0]);
    }

    [TestMethod]
    public void CadHandles_EmptyList_Allowed()
    {
        // Arrange
        var item = new TranslationItem
        {
            CadHandles = new List<string>()
        };

        // Act & Assert
        Assert.IsNotNull(item.CadHandles);
        Assert.AreEqual(0, item.CadHandles.Count);
    }
}
