// MergerIntegrationTests.cs
// Phase 3 integration tests for the full merge → dedup pipeline.
// Tests: TranslationMerger.Merge with enableCleanedDedup flag,
// end-to-end merge + dedup + DwgWriter expansion compatibility.

using CADTransLite.Core.Models;
using CADTransLite.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace CADTransLite.Tests;

/// <summary>
/// Integration tests for the Phase 3 merge + dedup pipeline.
/// </summary>
[TestClass]
public class MergerIntegrationTests
{
    // -----------------------------------------------------------------------
    // Merge without dedup (existing behavior preserved)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Merge_NoDedup_SameAsOriginal()
    {
        // Arrange
        var rawItems = new List<TranslationItem>
        {
            new()
            {
                Handle = "H1", EntityType = EntityType.Text,
                OriginalText = "Hello", RawOriginalText = "Hello",
                CleanedText = "hello",
                CadHandles = new List<string> { "H1" },
            },
            new()
            {
                Handle = "H2", EntityType = EntityType.Text,
                OriginalText = "Hello", RawOriginalText = "Hello",
                CleanedText = "hello",
                CadHandles = new List<string> { "H2" },
            },
        };

        // Act
        var result = TranslationMerger.Merge(rawItems, enableCleanedDedup: false);

        // Assert — standard merge: same OriginalText → 1 item, 2 handles
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(2, result[0].CadHandles!.Count);
    }

    // -----------------------------------------------------------------------
    // Merge with dedup
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Merge_WithDedup_SecondPassApplied()
    {
        // Arrange — two items with same EntityType but different OriginalText
        // but same CleanedText (after merge, they won't merge in step 1,
        // but should deduplicate in step 2)
        var rawItems = new List<TranslationItem>
        {
            new()
            {
                Handle = "H1", EntityType = EntityType.Text,
                OriginalText = "Hello", RawOriginalText = "Hello",
                CleanedText = "hello",
                CadHandles = new List<string> { "H1" },
            },
            new()
            {
                Handle = "H2", EntityType = EntityType.Text,
                OriginalText = "Hello!", RawOriginalText = "Hello!",
                CleanedText = "hello", // Same CleanedText, different OriginalText
                CadHandles = new List<string> { "H2" },
            },
        };

        // Act
        var result = TranslationMerger.Merge(rawItems, enableCleanedDedup: true);

        // Assert — step 1 doesn't merge (different OriginalText),
        // step 2 deduplicates by CleanedText → 1 item
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(2, result[0].CadHandles!.Count);
    }

    [TestMethod]
    public void Merge_WithDedup_ThreeItemsSameCleanedText()
    {
        // Arrange — three items: first two have same OriginalText (merge in step 1),
        // third has different OriginalText but same CleanedText (dedup in step 2)
        var rawItems = new List<TranslationItem>
        {
            new()
            {
                Handle = "H1", EntityType = EntityType.MText,
                OriginalText = "Door", RawOriginalText = "Door",
                CleanedText = "door",
                CadHandles = new List<string> { "H1" },
            },
            new()
            {
                Handle = "H2", EntityType = EntityType.MText,
                OriginalText = "Door", RawOriginalText = "Door",
                CleanedText = "door",
                CadHandles = new List<string> { "H2" },
            },
            new()
            {
                Handle = "H3", EntityType = EntityType.MText,
                OriginalText = "DOOR", RawOriginalText = "DOOR",
                CleanedText = "door",
                CadHandles = new List<string> { "H3" },
            },
        };

        // Act
        var result = TranslationMerger.Merge(rawItems, enableCleanedDedup: true);

        // Assert — step 1 merges H1+H2 → 1 item; step 2 deduplicates with H3 → 1 item
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(3, result[0].CadHandles!.Count);
        Assert.IsTrue(result[0].CadHandles.Contains("H1"));
        Assert.IsTrue(result[0].CadHandles.Contains("H2"));
        Assert.IsTrue(result[0].CadHandles.Contains("H3"));
    }

    // -----------------------------------------------------------------------
    // DwgWriter expansion compatibility
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Merge_WithDedup_MergedItemsCanBeExpanded()
    {
        // Arrange
        var rawItems = new List<TranslationItem>
        {
            new()
            {
                Handle = "H1", EntityType = EntityType.Text,
                OriginalText = "Wall", RawOriginalText = "Wall",
                CleanedText = "wall",
                CadHandles = new List<string> { "H1" },
            },
            new()
            {
                Handle = "H2", EntityType = EntityType.Text,
                OriginalText = "WALL", RawOriginalText = "WALL",
                CleanedText = "wall",
                CadHandles = new List<string> { "H2" },
            },
        };

        // Act
        var result = TranslationMerger.Merge(rawItems, enableCleanedDedup: true);

        // Assert — result has MergedItems that DwgWriter can expand
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(2, result[0].MergedItems.Count);

        // Simulate DwgWriter expansion: each MergedItem gets the shared translation
        var expanded = new List<TranslationItem>();
        result[0].TranslatedText = "墙";
        foreach (var merged in result[0].MergedItems)
        {
            var expandedItem = new TranslationItem
            {
                Handle = merged.Handle,
                EntityType = merged.EntityType,
                RawOriginalText = merged.RawOriginalText,
                OriginalText = merged.OriginalText,
                TranslatedText = result[0].TranslatedText,
            };
            expanded.Add(expandedItem);
        }

        Assert.AreEqual(2, expanded.Count);
        var handles = expanded.Select(e => e.Handle).ToList();
        Assert.IsTrue(handles.Contains("H1"));
        Assert.IsTrue(handles.Contains("H2"));
        Assert.AreEqual("墙", expanded[0].TranslatedText);
        Assert.AreEqual("墙", expanded[1].TranslatedText);
    }

    // -----------------------------------------------------------------------
    // Empty input handling
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Merge_WithDedup_EmptyInput_ReturnsEmptyList()
    {
        // Act
        var result = TranslationMerger.Merge(new List<TranslationItem>(), enableCleanedDedup: true);

        // Assert
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Merge_WithDedup_NullInput_ReturnsEmptyList()
    {
        // Act
        var result = TranslationMerger.Merge(null!, enableCleanedDedup: true);

        // Assert
        Assert.AreEqual(0, result.Count);
    }
}
