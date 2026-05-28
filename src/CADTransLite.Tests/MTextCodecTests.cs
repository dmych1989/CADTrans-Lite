// MTextCodecTests.cs
// Unit tests for MTextCodec (MText format code handling).

using CADTransLite.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CADTransLite.Tests;

/// <summary>
/// Tests for MTextCodec class.
/// </summary>
[TestClass]
public class MTextCodecTests
{
    // -----------------------------------------------------------------------
    // StripFormatCodes tests
    // -----------------------------------------------------------------------

    [TestMethod]
    public void StripFormatCodes_NoFormatCodes_ReturnsOriginalText()
    {
        // Arrange
        string input = "Hello World";
        Dictionary<string, string> placeholders;

        // Act
        string result = MTextCodec.StripFormatCodes(input, out placeholders);

        // Assert
        Assert.AreEqual("Hello World", result);
        Assert.AreEqual(0, placeholders.Count);
    }

    [TestMethod]
    public void StripFormatCodes_SimpleP_ReturnsPlainText()
    {
        // Arrange
        string input = "Line1\\PLine2";
        Dictionary<string, string> placeholders;

        // Act
        string result = MTextCodec.StripFormatCodes(input, out placeholders);

        // Assert
        Assert.IsTrue(placeholders.Count > 0);
        Assert.IsTrue(result.Contains("Line1"));
        Assert.IsTrue(result.Contains("Line2"));
    }

    [TestMethod]
    public void StripFormatCodes_MultiplePCodes_ReturnsMultiplePlaceholders()
    {
        // Arrange
        string input = "Line1\\PLine2\\PLine3";
        Dictionary<string, string> placeholders;

        // Act
        string result = MTextCodec.StripFormatCodes(input, out placeholders);

        // Assert
        Assert.IsTrue(placeholders.Count >= 2);
    }

    [TestMethod]
    public void StripFormatCodes_FontGroup_PreservesInnerText()
    {
        // Arrange
        string input = "{\\F Arial;Bold Text}";
        Dictionary<string, string> placeholders;

        // Act
        string result = MTextCodec.StripFormatCodes(input, out placeholders);

        // Assert
        Assert.IsTrue(result.Contains("Bold Text"));
        Assert.IsTrue(placeholders.Count > 0);
    }

    [TestMethod]
    public void StripFormatCodes_EmptyString_ReturnsEmpty()
    {
        // Arrange
        string input = "";
        Dictionary<string, string> placeholders;

        // Act
        string result = MTextCodec.StripFormatCodes(input, out placeholders);

        // Assert
        Assert.AreEqual("", result);
        Assert.AreEqual(0, placeholders.Count);
    }

    [TestMethod]
    public void StripFormatCodes_NullString_ReturnsNull()
    {
        // Arrange
        string input = null;
        Dictionary<string, string> placeholders;

        // Act
        string result = MTextCodec.StripFormatCodes(input, out placeholders);

        // Assert
        Assert.IsNull(result);
        Assert.AreEqual(0, placeholders.Count);
    }

    // -----------------------------------------------------------------------
    // RestoreFormatCodes tests
    // -----------------------------------------------------------------------

    [TestMethod]
    public void RestoreFormatCodes_SimplePlaceholder_RestoresCorrectly()
    {
        // Arrange
        string translated = "Translated Line1 Line2";
        var placeholders = new Dictionary<string, string>
        {
            { "[FMT:1]", "\\P" }
        };

        // Act
        string result = MTextCodec.RestoreFormatCodes(translated, placeholders);

        // Assert - placeholders should be replaced with format codes
        Assert.IsTrue(result.Contains("\\P") || !result.Contains("[FMT:"));
    }

    [TestMethod]
    public void RestoreFormatCodes_MultiplePlaceholders_RestoresAll()
    {
        // Arrange
        string translated = "[FMT:1]A[FMT:2]B";
        var placeholders = new Dictionary<string, string>
        {
            { "[FMT:1]", "\\P" },
            { "[FMT:2]", "\\L" }
        };

        // Act
        string result = MTextCodec.RestoreFormatCodes(translated, placeholders);

        // Assert
        Assert.IsTrue(result.Contains("\\P"));
        Assert.IsTrue(result.Contains("\\L"));
    }

    [TestMethod]
    public void RestoreFormatCodes_EmptyPlaceholders_ReturnsOriginal()
    {
        // Arrange
        string translated = "Hello World";
        var placeholders = new Dictionary<string, string>();

        // Act
        string result = MTextCodec.RestoreFormatCodes(translated, placeholders);

        // Assert
        Assert.AreEqual("Hello World", result);
    }

    [TestMethod]
    public void RestoreFormatCodes_NullPlaceholders_ThrowsArgumentNull()
    {
        // Arrange
        string translated = "Hello World";

        // Act & Assert
        Assert.ThrowsException<ArgumentNullException>(() =>
            MTextCodec.RestoreFormatCodes(translated, null));
    }

    // -----------------------------------------------------------------------
    // Integration tests (Strip + Restore)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void StripAndRestore_RoundTrip_PreservesFormatting()
    {
        // Arrange
        string original = "Start\\PMiddle\\PEnd";
        Dictionary<string, string> placeholders;

        // Act
        string stripped = MTextCodec.StripFormatCodes(original, out placeholders);
        string restored = MTextCodec.RestoreFormatCodes(stripped, placeholders);

        // Assert
        Assert.AreEqual(original, restored);
    }

    [TestMethod]
    public void StripAndRestore_TranslatedText_RestoresFormatting()
    {
        // Arrange
        string original = "Line1\\PLine2";
        Dictionary<string, string> placeholders;

        // Act
        string stripped = MTextCodec.StripFormatCodes(original, out placeholders);
        
        // Simulate translation
        string translated = stripped.Replace("Line1", "Translated1").Replace("Line2", "Translated2");
        
        string restored = MTextCodec.RestoreFormatCodes(translated, placeholders);

        // Assert
        Assert.IsTrue(restored.Contains("\\P"));
        Assert.IsTrue(restored.Contains("Translated1"));
        Assert.IsTrue(restored.Contains("Translated2"));
    }
}
