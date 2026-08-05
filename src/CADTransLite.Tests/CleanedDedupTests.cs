// CleanedDedupTests.cs
// Phase 3 unit tests for CleanedTextDeduplicator.
// Tests: same CleanedText dedup, CadHandles merge, MergedItems merge,
// null/empty CleanedText handling, cross-EntityType isolation.

using CADTransLite.Core.Models;
using CADTransLite.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace CADTransLite.Tests;

/// <summary>
/// Tests for CleanedTextDeduplicator — Phase 3 cleaned-text deduplication.
/// </summary>
[TestClass]
public class CleanedDedupTests
{
    // -----------------------------------------------------------------------
    // Basic deduplication
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Deduplicate_SameCleanedText_MergesIntoOne()
    {
        // Arrange — two items with same EntityType and CleanedText
        var items = new List<TranslationItem>
        {
            new()
            {
                Handle = "H1",
                EntityType = EntityType.Text,
                OriginalText = "Hello World",
                RawOriginalText = "Hello World",
                CleanedText = "hello world",
                CadHandles = new List<string> { "H1" },
                MergedItems = new List<TranslationItem>(),
            },
            new()
            {
                Handle = "H2",
                EntityType = EntityType.Text,
                OriginalText = "HELLO WORLD",
                RawOriginalText = "HELLO WORLD",
                CleanedText = "hello world",
                CadHandles = new List<string> { "H2" },
                MergedItems = new List<TranslationItem>(),
            },
        };

        // Act
        var result = CleanedTextDeduplicator.Deduplicate(items);

        // Assert
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("H1", result[0].Handle); // First item is representative
    }

    [TestMethod]
    public void Deduplicate_CadHandles_MergedCorrectly()
    {
        // Arrange
        var items = new List<TranslationItem>
        {
            new()
            {
                Handle = "H1",
                EntityType = EntityType.MText,
                OriginalText = "Text A",
                RawOriginalText = "Text A",
                CleanedText = "text a",
                CadHandles = new List<string> { "H1" },
                MergedItems = new List<TranslationItem>(),
            },
            new()
            {
                Handle = "H2",
                EntityType = EntityType.MText,
                OriginalText = "Text A!", // Different OriginalText
                RawOriginalText = "Text A!",
                CleanedText = "text a", // Same CleanedText
                CadHandles = new List<string> { "H2" },
                MergedItems = new List<TranslationItem>(),
            },
            new()
            {
                Handle = "H3",
                EntityType = EntityType.MText,
                OriginalText = "TEXT A",
                RawOriginalText = "TEXT A",
                CleanedText = "text a", // Same CleanedText
                CadHandles = new List<string> { "H3" },
                MergedItems = new List<TranslationItem>(),
            },
        };

        // Act
        var result = CleanedTextDeduplicator.Deduplicate(items);

        // Assert
        Assert.AreEqual(1, result.Count);
        var merged = result[0];
        Assert.AreEqual(3, merged.CadHandles!.Count);
        Assert.IsTrue(merged.CadHandles.Contains("H1"));
        Assert.IsTrue(merged.CadHandles.Contains("H2"));
        Assert.IsTrue(merged.CadHandles.Contains("H3"));
    }

    [TestMethod]
    public void Deduplicate_MergedItems_AbsorbedCorrectly()
    {
        // Arrange
        var items = new List<TranslationItem>
        {
            new()
            {
                Handle = "H1",
                EntityType = EntityType.Text,
                OriginalText = "Hello",
                RawOriginalText = "Hello",
                CleanedText = "hello",
                CadHandles = new List<string> { "H1" },
                MergedItems = new List<TranslationItem>(),
            },
            new()
            {
                Handle = "H2",
                EntityType = EntityType.Text,
                OriginalText = "Hello!",
                RawOriginalText = "Hello!",
                CleanedText = "hello",
                CadHandles = new List<string> { "H2" },
                MergedItems = new List<TranslationItem>(),
            },
        };

        // Act
        var result = CleanedTextDeduplicator.Deduplicate(items);

        // Assert
        Assert.AreEqual(1, result.Count);
        // MergedItems should contain: representative (since it wasn't merged in step 1)
        // + duplicate H2 (since it wasn't merged in step 1 either)
        Assert.AreEqual(2, result[0].MergedItems.Count);
    }

    // -----------------------------------------------------------------------
    // Cross-EntityType isolation
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Deduplicate_DifferentEntityType_NotMerged()
    {
        // Arrange — same CleanedText but different EntityType
        var items = new List<TranslationItem>
        {
            new()
            {
                Handle = "H1",
                EntityType = EntityType.Text,
                OriginalText = "Hello",
                RawOriginalText = "Hello",
                CleanedText = "hello",
                CadHandles = new List<string> { "H1" },
                MergedItems = new List<TranslationItem>(),
            },
            new()
            {
                Handle = "H2",
                EntityType = EntityType.MText,
                OriginalText = "Hello",
                RawOriginalText = "Hello",
                CleanedText = "hello",
                CadHandles = new List<string> { "H2" },
                MergedItems = new List<TranslationItem>(),
            },
        };

        // Act
        var result = CleanedTextDeduplicator.Deduplicate(items);

        // Assert — should NOT merge across entity types
        Assert.AreEqual(2, result.Count);
    }

    // -----------------------------------------------------------------------
    // Null / empty CleanedText handling
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Deduplicate_NullCleanedText_NotDeduplicated()
    {
        // Arrange — two items with null CleanedText
        var items = new List<TranslationItem>
        {
            new()
            {
                Handle = "H1",
                EntityType = EntityType.Text,
                OriginalText = "Text1",
                RawOriginalText = "Text1",
                CleanedText = null,
                CadHandles = new List<string> { "H1" },
                MergedItems = new List<TranslationItem>(),
            },
            new()
            {
                Handle = "H2",
                EntityType = EntityType.Text,
                OriginalText = "Text2",
                RawOriginalText = "Text2",
                CleanedText = null,
                CadHandles = new List<string> { "H2" },
                MergedItems = new List<TranslationItem>(),
            },
        };

        // Act
        var result = CleanedTextDeduplicator.Deduplicate(items);

        // Assert — null CleanedText items should not be deduplicated
        Assert.AreEqual(2, result.Count);
    }

    [TestMethod]
    public void Deduplicate_EmptyCleanedText_NotDeduplicated()
    {
        // Arrange
        var items = new List<TranslationItem>
        {
            new()
            {
                Handle = "H1",
                EntityType = EntityType.Text,
                OriginalText = "A",
                RawOriginalText = "A",
                CleanedText = "",
                CadHandles = new List<string> { "H1" },
                MergedItems = new List<TranslationItem>(),
            },
            new()
            {
                Handle = "H2",
                EntityType = EntityType.Text,
                OriginalText = "B",
                RawOriginalText = "B",
                CleanedText = "",
                CadHandles = new List<string> { "H2" },
                MergedItems = new List<TranslationItem>(),
            },
        };

        // Act
        var result = CleanedTextDeduplicator.Deduplicate(items);

        // Assert — empty CleanedText items should not be deduplicated
        Assert.AreEqual(2, result.Count);
    }

    // -----------------------------------------------------------------------
    // Mixed scenario
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Deduplicate_MixedScenario_CorrectResult()
    {
        // Arrange
        var items = new List<TranslationItem>
        {
            // Group 1: same (Text, "hello")
            new()
            {
                Handle = "H1", EntityType = EntityType.Text,
                OriginalText = "Hello", RawOriginalText = "Hello",
                CleanedText = "hello", CadHandles = new List<string> { "H1" },
                MergedItems = new List<TranslationItem>(),
            },
            new()
            {
                Handle = "H2", EntityType = EntityType.Text,
                OriginalText = "Hello!", RawOriginalText = "Hello!",
                CleanedText = "hello", CadHandles = new List<string> { "H2" },
                MergedItems = new List<TranslationItem>(),
            },
            // Group 2: same (Text, "world") — single item, no dedup
            new()
            {
                Handle = "H3", EntityType = EntityType.Text,
                OriginalText = "World", RawOriginalText = "World",
                CleanedText = "world", CadHandles = new List<string> { "H3" },
                MergedItems = new List<TranslationItem>(),
            },
            // Group 3: null CleanedText — no dedup
            new()
            {
                Handle = "H4", EntityType = EntityType.Text,
                OriginalText = "123", RawOriginalText = "123",
                CleanedText = null, CadHandles = new List<string> { "H4" },
                MergedItems = new List<TranslationItem>(),
            },
            // Group 4: different EntityType, same CleanedText as Group 1
            new()
            {
                Handle = "H5", EntityType = EntityType.Attribute,
                OriginalText = "Hello", RawOriginalText = "Hello",
                CleanedText = "hello", CadHandles = new List<string> { "H5" },
                MergedItems = new List<TranslationItem>(),
            },
        };

        // Act
        var result = CleanedTextDeduplicator.Deduplicate(items);

        // Assert
        // Group 1 → 1 item (H1 + H2 merged)
        // Group 2 → 1 item (H3)
        // Group 3 → 1 item (H4, no dedup)
        // Group 4 → 1 item (H5, different EntityType)
        Assert.AreEqual(4, result.Count);

        // Verify the merged item has both handles
        var mergedItem = result.First(i => i.Handle == "H1");
        Assert.AreEqual(2, mergedItem.CadHandles!.Count);
        Assert.IsTrue(mergedItem.CadHandles.Contains("H1"));
        Assert.IsTrue(mergedItem.CadHandles.Contains("H2"));
    }

    // -----------------------------------------------------------------------
    // Edge cases
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Deduplicate_EmptyList_ReturnsEmptyList()
    {
        // Act
        var result = CleanedTextDeduplicator.Deduplicate(new List<TranslationItem>());

        // Assert
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Deduplicate_SingleItem_ReturnsSameItem()
    {
        // Arrange
        var items = new List<TranslationItem>
        {
            new()
            {
                Handle = "H1", EntityType = EntityType.Text,
                OriginalText = "Hello", RawOriginalText = "Hello",
                CleanedText = "hello", CadHandles = new List<string> { "H1" },
                MergedItems = new List<TranslationItem>(),
            },
        };

        // Act
        var result = CleanedTextDeduplicator.Deduplicate(items);

        // Assert
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("H1", result[0].Handle);
    }
}
