// DxfTextCleanerTests.cs
// Unit tests for DxfTextCleaner — text cleaning/filtering pipeline.

using CADTransLite.Core.Models;
using CADTransLite.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CADTransLite.Tests;

/// <summary>
/// DxfTextCleaner 单元测试。
/// 覆盖：空文本过滤、数字/单位识别、符号检测、工程编码检测、
/// 工程标签识别、语言匹配、自定义跳过模式、组合过滤管道。
/// </summary>
[TestClass]
public class DxfTextCleanerTests
{
    private static DxfTextCleanerConfig DefaultConfig => new();

    // -----------------------------------------------------------------------
    // Clean — 主清洗入口
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Clean_NullText_WithFilterEmpty_ReturnsFiltered()
    {
        var config = new DxfTextCleanerConfig { FilterEmpty = true };
        var (cleaned, wasFiltered, reason) = DxfTextCleaner.Clean(null!, config);
        Assert.IsTrue(wasFiltered);
        Assert.AreEqual("Empty", reason);
    }

    [TestMethod]
    public void Clean_EmptyText_WithFilterEmpty_ReturnsFiltered()
    {
        var config = new DxfTextCleanerConfig { FilterEmpty = true };
        var (cleaned, wasFiltered, reason) = DxfTextCleaner.Clean("", config);
        Assert.IsTrue(wasFiltered);
        Assert.AreEqual("Empty", reason);
    }

    [TestMethod]
    public void Clean_EmptyText_WithoutFilterEmpty_ReturnsNotFiltered()
    {
        var config = new DxfTextCleanerConfig { FilterEmpty = false };
        var (cleaned, wasFiltered, reason) = DxfTextCleaner.Clean("", config);
        Assert.IsFalse(wasFiltered);
        Assert.IsNull(reason);
    }

    [TestMethod]
    public void Clean_NormalText_ReturnsNotFiltered()
    {
        var config = DefaultConfig;
        var (cleaned, wasFiltered, reason) = DxfTextCleaner.Clean("Hello World", config);
        Assert.IsFalse(wasFiltered);
        Assert.AreEqual("Hello World", cleaned);
    }

    [TestMethod]
    public void Clean_TrimsWhitespace()
    {
        var config = DefaultConfig;
        var (cleaned, wasFiltered, _) = DxfTextCleaner.Clean("  Hello  ", config);
        Assert.AreEqual("Hello", cleaned);
    }

    // -----------------------------------------------------------------------
    // IsNumber — 数字+单位识别
    // -----------------------------------------------------------------------

    [TestMethod]
    public void IsNumber_PureInteger_ReturnsTrue()
    {
        Assert.IsTrue(DxfTextCleaner.IsNumber("1500"));
    }

    [TestMethod]
    public void IsNumber_PureDecimal_ReturnsTrue()
    {
        Assert.IsTrue(DxfTextCleaner.IsNumber("3.5"));
    }

    [TestMethod]
    public void IsNumber_WithUnit_ReturnsTrue()
    {
        Assert.IsTrue(DxfTextCleaner.IsNumber("1500mm"));
        Assert.IsTrue(DxfTextCleaner.IsNumber("3.5MPa"));
        Assert.IsTrue(DxfTextCleaner.IsNumber("220V"));
    }

    [TestMethod]
    public void IsNumber_Range_ReturnsTrue()
    {
        Assert.IsTrue(DxfTextCleaner.IsNumber("1-10"));
        Assert.IsTrue(DxfTextCleaner.IsNumber("3.5~7.2"));
    }

    [TestMethod]
    public void IsNumber_Ratio_ReturnsTrue()
    {
        Assert.IsTrue(DxfTextCleaner.IsNumber("1:100"));
        Assert.IsTrue(DxfTextCleaner.IsNumber("3/4"));
    }

    [TestMethod]
    public void IsNumber_TextWithNumber_ReturnsFalse()
    {
        Assert.IsFalse(DxfTextCleaner.IsNumber("Room 101"));
        Assert.IsFalse(DxfTextCleaner.IsNumber("Level 3"));
    }

    // -----------------------------------------------------------------------
    // IsCode — 工程编码检测
    // -----------------------------------------------------------------------

    [TestMethod]
    public void IsCode_StandardCode_ReturnsTrue()
    {
        Assert.IsTrue(DxfTextCleaner.IsCode("A-001"));
        Assert.IsTrue(DxfTextCleaner.IsCode("ELEC-123B"));
        Assert.IsTrue(DxfTextCleaner.IsCode("STR-001-2"));
    }

    [TestMethod]
    public void IsCode_CompactCode_ReturnsTrue()
    {
        Assert.IsTrue(DxfTextCleaner.IsCode("EL01234"));
        Assert.IsTrue(DxfTextCleaner.IsCode("HVAC5678X"));
    }

    [TestMethod]
    public void IsCode_DrawingNo_ReturnsTrue()
    {
        Assert.IsTrue(DxfTextCleaner.IsCode("DWG NO"));
        Assert.IsTrue(DxfTextCleaner.IsCode("DWG NO.123"));
    }

    [TestMethod]
    public void IsCode_Revision_ReturnsTrue()
    {
        Assert.IsTrue(DxfTextCleaner.IsCode("REV.1"));
        Assert.IsTrue(DxfTextCleaner.IsCode("REV-2"));
        Assert.IsTrue(DxfTextCleaner.IsCode("REV3"));
    }

    [TestMethod]
    public void IsCode_NormalText_ReturnsFalse()
    {
        Assert.IsFalse(DxfTextCleaner.IsCode("Hello World"));
        Assert.IsFalse(DxfTextCleaner.IsCode("电气设计说明"));
    }

    // -----------------------------------------------------------------------
    // IsEngineeringTag — 设备标签/型号
    // -----------------------------------------------------------------------

    [TestMethod]
    public void IsEngineeringTag_WithTagSymbol_ReturnsTrue()
    {
        Assert.IsTrue(DxfTextCleaner.IsEngineeringTag("A#1"));
        Assert.IsTrue(DxfTextCleaner.IsEngineeringTag("@PUMP"));
    }

    [TestMethod]
    public void IsEngineeringTag_UpperCaseAndDigit_ReturnsTrue()
    {
        Assert.IsTrue(DxfTextCleaner.IsEngineeringTag("AHU01"));
        Assert.IsTrue(DxfTextCleaner.IsEngineeringTag("FCU3"));
    }

    [TestMethod]
    public void IsEngineeringTag_HasLowerCase_ReturnsFalse()
    {
        // Has lowercase letters → likely natural language
        Assert.IsFalse(DxfTextCleaner.IsEngineeringTag("ahu01"));
        Assert.IsFalse(DxfTextCleaner.IsEngineeringTag("Pump01"));
    }

    [TestMethod]
    public void IsEngineeringTag_NormalText_ReturnsFalse()
    {
        Assert.IsFalse(DxfTextCleaner.IsEngineeringTag("Hello"));
        Assert.IsFalse(DxfTextCleaner.IsEngineeringTag("Design Note"));
    }

    // -----------------------------------------------------------------------
    // IsSymbol — 纯符号文本
    // -----------------------------------------------------------------------

    [TestMethod]
    public void IsSymbol_PureSymbols_ReturnsTrue()
    {
        Assert.IsTrue(DxfTextCleaner.IsSymbol("---"));
        Assert.IsTrue(DxfTextCleaner.IsSymbol("+++"));
        Assert.IsTrue(DxfTextCleaner.IsSymbol("///"));
        Assert.IsTrue(DxfTextCleaner.IsSymbol("°"));
        Assert.IsTrue(DxfTextCleaner.IsSymbol("±×÷"));
    }

    [TestMethod]
    public void IsSymbol_MixedWithLetters_ReturnsFalse()
    {
        Assert.IsFalse(DxfTextCleaner.IsSymbol("A-1"));
        Assert.IsFalse(DxfTextCleaner.IsSymbol("3.5mm"));
    }

    // -----------------------------------------------------------------------
    // MatchesLanguage — Unicode 范围语言检测
    // -----------------------------------------------------------------------

    [TestMethod]
    public void MatchesLanguage_Chinese_ReturnsTrue()
    {
        Assert.IsTrue(DxfTextCleaner.MatchesLanguage("电气设计", "zh"));
        Assert.IsTrue(DxfTextCleaner.MatchesLanguage("建筑说明", "zh"));
    }

    [TestMethod]
    public void MatchesLanguage_English_ReturnsTrue()
    {
        Assert.IsTrue(DxfTextCleaner.MatchesLanguage("Hello", "en"));
        Assert.IsTrue(DxfTextCleaner.MatchesLanguage("Design", "en"));
    }

    [TestMethod]
    public void MatchesLanguage_Japanese_ReturnsTrue()
    {
        Assert.IsTrue(DxfTextCleaner.MatchesLanguage("こんにちは", "ja"));
        Assert.IsTrue(DxfTextCleaner.MatchesLanguage("カタカナ", "ja"));
    }

    [TestMethod]
    public void MatchesLanguage_Korean_ReturnsTrue()
    {
        Assert.IsTrue(DxfTextCleaner.MatchesLanguage("안녕하세요", "ko"));
    }

    [TestMethod]
    public void MatchesLanguage_WrongLanguage_ReturnsFalse()
    {
        Assert.IsFalse(DxfTextCleaner.MatchesLanguage("Hello", "zh"));
        Assert.IsFalse(DxfTextCleaner.MatchesLanguage("你好", "en"));
    }

    // -----------------------------------------------------------------------
    // Clean — 组合过滤管道
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Clean_NumberWithUnit_Filtered()
    {
        var config = new DxfTextCleanerConfig { FilterNumber = true };
        var (_, wasFiltered, reason) = DxfTextCleaner.Clean("1500mm", config);
        Assert.IsTrue(wasFiltered);
        Assert.AreEqual("Number", reason);
    }

    [TestMethod]
    public void Clean_EngineeringCode_Filtered()
    {
        var config = new DxfTextCleanerConfig { FilterCode = true };
        var (_, wasFiltered, reason) = DxfTextCleaner.Clean("A-001", config);
        Assert.IsTrue(wasFiltered);
        Assert.AreEqual("Code", reason);
    }

    [TestMethod]
    public void Clean_TargetLangText_Filtered()
    {
        var config = new DxfTextCleanerConfig
        {
            SourceLang = "en",
            TargetLang = "zh",
            FilterTargetLang = true
        };
        var (_, wasFiltered, reason) = DxfTextCleaner.Clean("电气设计", config);
        Assert.IsTrue(wasFiltered);
        Assert.IsTrue(reason!.Contains("TargetLang"));
    }

    [TestMethod]
    public void Clean_NonSourceLangText_Filtered()
    {
        var config = new DxfTextCleanerConfig
        {
            SourceLang = "en",
            TargetLang = "zh",
            FilterNonSourceLang = true
        };
        var (_, wasFiltered, reason) = DxfTextCleaner.Clean("こんにちは", config);
        Assert.IsTrue(wasFiltered);
        Assert.IsTrue(reason!.Contains("NonSourceLang"));
    }

    [TestMethod]
    public void Clean_CustomSkipPattern_Filtered()
    {
        var config = new DxfTextCleanerConfig
        {
            FilterNumber = false,
            FilterCode = false,
            CustomSkipPatterns = new List<string> { @"^NOTE:" }
        };
        var (_, wasFiltered, reason) = DxfTextCleaner.Clean("NOTE: Something", config);
        Assert.IsTrue(wasFiltered);
        Assert.AreEqual("CustomSkip", reason);
    }

    [TestMethod]
    public void Clean_MixedText_NotFiltered()
    {
        // "Room 101" has letters and numbers → not pure number, not pure code
        var config = DefaultConfig;
        var (cleaned, wasFiltered, _) = DxfTextCleaner.Clean("Room 101", config);
        Assert.IsFalse(wasFiltered);
        Assert.AreEqual("Room 101", cleaned);
    }
}
