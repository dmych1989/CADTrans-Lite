// DxfStyleManagerTests.cs
// Unit tests for DxfStyleManager — Unicode font style management.

using CADTransLite.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CADTransLite.Tests;

/// <summary>
/// DxfStyleManager 单元测试。
/// 覆盖：Unicode 脚本检测、字体样式映射、Unicode 编码。
/// 注意：EnsureUnicodeStyle 需要 DxfDocument，在集成测试中验证。
/// </summary>
[TestClass]
public class DxfStyleManagerTests
{
    // -----------------------------------------------------------------------
    // DetectScript — Unicode 脚本类型检测
    // -----------------------------------------------------------------------

    [TestMethod]
    public void DetectScript_AsciiOnly_ReturnsLatin()
    {
        Assert.AreEqual("Latin", DxfStyleManager.DetectScript("Hello World"));
        Assert.AreEqual("Latin", DxfStyleManager.DetectScript("Design Note 123"));
    }

    [TestMethod]
    public void DetectScript_Chinese_ReturnsCJK()
    {
        Assert.AreEqual("CJK", DxfStyleManager.DetectScript("电气设计"));
        Assert.AreEqual("CJK", DxfStyleManager.DetectScript("建筑说明"));
    }

    [TestMethod]
    public void DetectScript_Japanese_ReturnsJapanese()
    {
        Assert.AreEqual("Japanese", DxfStyleManager.DetectScript("こんにちは"));
        Assert.AreEqual("Japanese", DxfStyleManager.DetectScript("カタカナ"));
    }

    [TestMethod]
    public void DetectScript_Korean_ReturnsKorean()
    {
        Assert.AreEqual("Korean", DxfStyleManager.DetectScript("안녕하세요"));
    }

    [TestMethod]
    public void DetectScript_Arabic_ReturnsArabic()
    {
        Assert.AreEqual("Arabic", DxfStyleManager.DetectScript("مرحبا"));
    }

    [TestMethod]
    public void DetectScript_Thai_ReturnsThai()
    {
        Assert.AreEqual("Thai", DxfStyleManager.DetectScript("สวัสดี"));
    }

    [TestMethod]
    public void DetectScript_Greek_ReturnsGreek()
    {
        Assert.AreEqual("Greek", DxfStyleManager.DetectScript("Γειά"));
    }

    [TestMethod]
    public void DetectScript_Devanagari_ReturnsDevanagari()
    {
        Assert.AreEqual("Devanagari", DxfStyleManager.DetectScript("नमस्ते"));
    }

    [TestMethod]
    public void DetectScript_MixedCJKAndLatin_ReturnsCJK()
    {
        // First non-Latin script wins
        Assert.AreEqual("CJK", DxfStyleManager.DetectScript("Hello 你好"));
    }

    [TestMethod]
    public void DetectScript_EmptyOrNull_ReturnsLatin()
    {
        Assert.AreEqual("Latin", DxfStyleManager.DetectScript(""));
        Assert.AreEqual("Latin", DxfStyleManager.DetectScript(null!));
    }

    // -----------------------------------------------------------------------
    // EncodeDxfUnicode — 非 ASCII 编码为 \U+XXXX
    // -----------------------------------------------------------------------

    [TestMethod]
    public void EncodeDxfUnicode_AsciiOnly_ReturnsSame()
    {
        Assert.AreEqual("Hello", DxfStyleManager.EncodeDxfUnicode("Hello"));
        Assert.AreEqual("ABC 123", DxfStyleManager.EncodeDxfUnicode("ABC 123"));
    }

    [TestMethod]
    public void EncodeDxfUnicode_Chinese_Encoded()
    {
        // '你' = U+4F60
        string result = DxfStyleManager.EncodeDxfUnicode("你");
        Assert.AreEqual("\\U+4F60", result);
    }

    [TestMethod]
    public void EncodeDxfUnicode_MixedText_PartiallyEncoded()
    {
        // "A你" → "A\\U+4F60"
        string result = DxfStyleManager.EncodeDxfUnicode("A你");
        Assert.IsTrue(result.StartsWith("A"));
        Assert.IsTrue(result.Contains("\\U+4F60"));
    }

    [TestMethod]
    public void EncodeDxfUnicode_EmptyOrNull_ReturnsSame()
    {
        Assert.AreEqual("", DxfStyleManager.EncodeDxfUnicode(""));
        Assert.AreEqual(null, DxfStyleManager.EncodeDxfUnicode(null!));
    }

    // -----------------------------------------------------------------------
    // Script → Font mapping (implicit via DetectScript)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void DetectScript_CJKExtensionA_ReturnsCJK()
    {
        // CJK Extension A: U+3400..U+4DBF
        char extChar = '\u3444';
        Assert.AreEqual("CJK", DxfStyleManager.DetectScript(extChar.ToString()));
    }

    [TestMethod]
    public void DetectScript_HangulJamo_ReturnsKorean()
    {
        // Hangul Jamo: U+1100..U+11FF
        char jamoChar = '\u1100';
        Assert.AreEqual("Korean", DxfStyleManager.DetectScript(jamoChar.ToString()));
    }
}
