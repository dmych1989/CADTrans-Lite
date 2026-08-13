// MTextRebuilderTests.cs
// Unit tests for MTextRebuilder — MTEXT write-back enhancement.

using CADTransLite.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CADTransLite.Tests;

/// <summary>
/// MTextRebuilder 单元测试。
/// 覆盖：骨架提取、可见文字替换、视觉宽度换行、完整重建流程。
/// </summary>
[TestClass]
public class MTextRebuilderTests
{
    // -----------------------------------------------------------------------
    // ReplaceVisibleText — 替换可见文字（public 接口）
    // Note: ExtractSkeleton is internal; tested indirectly via RebuildMtextContent.
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ReplaceVisibleText_SinglePlaceholder_ReplacesAll()
    {
        // Build a skeleton with one visible text segment
        string skeleton = "\x02VT\x02Hello\x03";
        string result = MTextRebuilder.ReplaceVisibleText(skeleton, "你好");
        Assert.AreEqual("你好", result);
    }

    [TestMethod]
    public void ReplaceVisibleText_MultiplePlaceholders_DistributesTranslation()
    {
        // Build a skeleton with two visible text segments separated by \P
        string skeleton = "\x02VT\x02Hello\x03\\P\x02VT\x02World\x03";
        string result = MTextRebuilder.ReplaceVisibleText(skeleton, "你好\\P世界");
        Assert.IsTrue(result.Contains("你好"));
        Assert.IsTrue(result.Contains("世界"));
        Assert.IsTrue(result.Contains("\\P"));
    }

    [TestMethod]
    public void ReplaceVisibleText_EmptyTranslation_ReturnsSkeleton()
    {
        string skeleton = "\x02VT\x02Hello\x03";
        string result = MTextRebuilder.ReplaceVisibleText(skeleton, "");
        Assert.AreEqual(skeleton, result);
    }

    [TestMethod]
    public void ReplaceVisibleText_EscapesSpecialChars()
    {
        string skeleton = "\x02VT\x02Hello\x03";
        string result = MTextRebuilder.ReplaceVisibleText(skeleton, "A\\B{C}");
        // Should contain escaped versions
        Assert.IsTrue(result.Contains(@"\\"));  // \ escaped to \\
        Assert.IsTrue(result.Contains(@"\{"));  // { escaped to \{
        Assert.IsTrue(result.Contains(@"\}"));  // } escaped to \}
    }

    [TestMethod]
    public void ReplaceVisibleText_NoPlaceholder_ReturnsSkeleton()
    {
        // No \x02VT\x02...\x03 markers → return as-is
        string result = MTextRebuilder.ReplaceVisibleText("\\C6;Red", "你好");
        Assert.AreEqual("\\C6;Red", result);
    }

    // -----------------------------------------------------------------------
    // WrapPlainText — 视觉宽度换行
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WrapPlainText_ShortText_NoWrapping()
    {
        // entityWidth=50, text is much shorter than that
        string result = MTextRebuilder.WrapPlainText("Hello", 50);
        Assert.AreEqual("Hello", result);
    }

    [TestMethod]
    public void WrapPlainText_LongAsciiText_WrapsAtWidth()
    {
        // entityWidth=5 means roughly 9 ASCII chars per line (5/0.55≈9)
        string text = "ABCDEFGHIJKLMNO"; // 15 chars
        string result = MTextRebuilder.WrapPlainText(text, 5);
        Assert.IsTrue(result.Contains("\\P")); // Should have inserted line breaks
    }

    [TestMethod]
    public void WrapPlainText_CJKText_WrapsEarlier()
    {
        // CJK width=1.0, entityWidth=3 means 3 CJK chars per line
        string text = "你好世界再见"; // 6 CJK chars
        string result = MTextRebuilder.WrapPlainText(text, 3);
        Assert.IsTrue(result.Contains("\\P"));
    }

    [TestMethod]
    public void WrapPlainText_ExistingParagraphBreaks_Preserved()
    {
        string text = "Hello\\PWorld";
        string result = MTextRebuilder.WrapPlainText(text, 50);
        Assert.IsTrue(result.Contains("\\P"));
    }

    [TestMethod]
    public void WrapPlainText_ZeroWidth_NoWrapping()
    {
        string text = "Very long text that would normally wrap";
        string result = MTextRebuilder.WrapPlainText(text, 0);
        Assert.AreEqual(text, result); // No wrapping when width=0
    }

    [TestMethod]
    public void WrapPlainText_NullOrEmpty_ReturnsSame()
    {
        Assert.IsNull(MTextRebuilder.WrapPlainText(null!, 10));
        Assert.AreEqual("", MTextRebuilder.WrapPlainText("", 10));
    }

    // -----------------------------------------------------------------------
    // RebuildMtextContent — 完整重建流程
    // -----------------------------------------------------------------------

    [TestMethod]
    public void RebuildMtextContent_PlainText_ReturnsTranslation()
    {
        string result = MTextRebuilder.RebuildMtextContent("Hello", "你好", 0);
        Assert.AreEqual("你好", result);
    }

    [TestMethod]
    public void RebuildMtextContent_NullOriginal_ReturnsTranslation()
    {
        string result = MTextRebuilder.RebuildMtextContent(null!, "你好", 0);
        Assert.AreEqual("你好", result);
    }

    [TestMethod]
    public void RebuildMtextContent_NullTranslation_ReturnsOriginal()
    {
        string result = MTextRebuilder.RebuildMtextContent("Hello", null!, 0);
        Assert.AreEqual("Hello", result);
    }

    [TestMethod]
    public void RebuildMtextContent_WithWidth_ContainsWrap()
    {
        // Original has no format codes, translation is CJK with width limit
        string result = MTextRebuilder.RebuildMtextContent(
            "Simple open storage room",
            "简易开敞式储藏室",
            3); // 3 CJK chars per line
        Assert.IsTrue(result.Contains("\\P"));
    }

    // -----------------------------------------------------------------------
    // Character width constants verification
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WrapPlainText_WidthCalculation_CJKWiderThanAscii()
    {
        // Same entityWidth, CJK text should wrap more frequently than ASCII
        string ascii = "ABCDEFGHIJ"; // 10 ASCII chars
        string cjk = "你好世界你好世界你好世界你好世界"; // 16 CJK chars

        string asciiResult = MTextRebuilder.WrapPlainText(ascii, 5);
        string cjkResult = MTextRebuilder.WrapPlainText(cjk, 5);

        int asciiBreaks = CountOccurrences(asciiResult, "\\P");
        int cjkBreaks = CountOccurrences(cjkResult, "\\P");

        // CJK should have more line breaks than ASCII for the same width
        Assert.IsTrue(cjkBreaks >= asciiBreaks,
            $"CJK breaks ({cjkBreaks}) should be >= ASCII breaks ({asciiBreaks})");
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }
}
