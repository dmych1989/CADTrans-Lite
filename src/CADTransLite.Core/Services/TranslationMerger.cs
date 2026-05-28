// Services/TranslationMerger.cs
// Merges TranslationItems with identical (EntityType, OriginalText, RawOriginalText)
// to reduce the number of rows in the Excel sheet.

using CADTransLite.Core.Models;
using CoreEntityType = CADTransLite.Core.Models.EntityType;

namespace CADTransLite.Core.Services;

/// <summary>
/// Merges translation items that share the same entity type, original text, and raw original text.
/// Merged items retain all original handles via <see cref="TranslationItem.CadHandles"/> and
/// preserve the individual items via <see cref="TranslationItem.MergedItems"/> for later expansion.
/// </summary>
public static class TranslationMerger
{
    /// <summary>
    /// Merge key: items with the same (EntityType, OriginalText, RawOriginalText) are grouped.
    /// </summary>
    private sealed record MergeKey(
        EntityType EntityType,
        string OriginalText,
        string RawOriginalText);

    /// <summary>
    /// Merges a list of raw extracted items into a reduced list where duplicate texts
    /// are combined into a single row.
    /// </summary>
    /// <param name="rawItems">Items produced by <see cref="DwgExtractor.Extract"/>.</param>
    /// <returns>A merged list of items with <see cref="TranslationItem.IsMerged"/> and
    /// <see cref="TranslationItem.MergedItems"/> populated.</returns>
    public static List<TranslationItem> Merge(List<TranslationItem> rawItems)
    {
        return Merge(rawItems, enableCleanedDedup: false);
    }

    /// <summary>
    /// Merges a list of raw extracted items into a reduced list where duplicate texts
    /// are combined into a single row. Optionally applies cleaned-text deduplication
    /// after the initial merge.
    /// </summary>
    /// <param name="rawItems">Items produced by <see cref="DwgExtractor.Extract"/>.</param>
    /// <param name="enableCleanedDedup">
    /// If true, performs a second deduplication pass by (EntityType, CleanedText)
    /// after the initial merge by (EntityType, OriginalText, RawOriginalText).
    /// </param>
    /// <returns>A merged (and optionally deduplicated) list of items.</returns>
    public static List<TranslationItem> Merge(List<TranslationItem> rawItems, bool enableCleanedDedup)
    {
        if (rawItems is null || rawItems.Count == 0)
            return new List<TranslationItem>();

        // Step 1: Merge by (EntityType, OriginalText, RawOriginalText) — always executed.
        var groups = new Dictionary<MergeKey, List<TranslationItem>>();

        foreach (var item in rawItems)
        {
            var key = new MergeKey(item.EntityType, item.OriginalText, item.RawOriginalText);
            if (!groups.TryGetValue(key, out var group))
            {
                group = new List<TranslationItem>();
                groups[key] = group;
            }
            group.Add(item);
        }

        // Build merged result.
        var result = new List<TranslationItem>(groups.Count);

        foreach (var (_, group) in groups)
        {
            // Use the first item as the template.
            var template = group[0];

            var mergedItem = new TranslationItem
            {
                Handle = template.Handle,
                EntityType = template.EntityType,
                RawOriginalText = template.RawOriginalText,
                OriginalText = template.OriginalText,
                FormatPlaceholders = new Dictionary<string, string>(template.FormatPlaceholders),
                LayerName = template.LayerName,
                CadHandles = group.Select(g => g.Handle).ToList(),
                MergedItems = new List<TranslationItem>(group),
                // v3.0 Phase 2 fields — carry forward from template
                BlockName = template.BlockName,
                AttributeTag = template.AttributeTag,
                TableRow = template.TableRow,
                TableColumn = template.TableColumn,
                FilterReason = template.FilterReason,
                CleanedText = template.CleanedText,
                Status = template.Status,
                Remark = template.Remark,
            };

            result.Add(mergedItem);
        }

        // Step 2: Optional cleaned-text deduplication.
        if (enableCleanedDedup)
        {
            result = CleanedTextDeduplicator.Deduplicate(result);
        }

        return result;
    }
}
