// Services/CleanedTextDeduplicator.cs
// Post-merge deduplication: groups items by (EntityType, CleanedText)
// so that items with the same cleaned text share a single translation.
// Phase 3: runs after TranslationMerger.Merge when EnableCleanedDedup is true.

using CADTransLite.Core.Models;
using CoreEntityType = CADTransLite.Core.Models.EntityType;

namespace CADTransLite.Core.Services;

/// <summary>
/// Cleaned-text deduplication service. After <see cref="TranslationMerger.Merge"/> has
/// combined items with the same (EntityType, OriginalText, RawOriginalText), this service
/// performs a second pass to further deduplicate by (EntityType, CleanedText).
/// </summary>
public static class CleanedTextDeduplicator
{
    /// <summary>
    /// Deduplication key: items with the same (EntityType, CleanedText) are grouped.
    /// CleanedText must be non-null and non-empty to participate in dedup.
    /// </summary>
    private sealed record DedupKey(EntityType EntityType, string CleanedText);

    /// <summary>
    /// Performs cleaned-text deduplication on already-merged items.
    /// Items with null or empty CleanedText are not deduplicated (kept as-is).
    /// The first item in each group becomes the representative; its CadHandles and
    /// MergedItems absorb those of subsequent duplicates.
    /// </summary>
    /// <param name="mergedItems">Output from <see cref="TranslationMerger.Merge"/>.</param>
    /// <returns>Deduplicated list of items.</returns>
    public static List<TranslationItem> Deduplicate(List<TranslationItem> mergedItems)
    {
        if (mergedItems is null || mergedItems.Count == 0)
            return new List<TranslationItem>();

        var groups = new Dictionary<DedupKey, List<TranslationItem>>();
        var noDedupItems = new List<TranslationItem>(); // Items that don't participate in dedup

        foreach (var item in mergedItems)
        {
            // Items without CleanedText are not deduplicated
            if (string.IsNullOrWhiteSpace(item.CleanedText))
            {
                noDedupItems.Add(item);
                continue;
            }

            var key = new DedupKey(item.EntityType, item.CleanedText);
            if (!groups.TryGetValue(key, out var group))
            {
                group = new List<TranslationItem>();
                groups[key] = group;
            }
            group.Add(item);
        }

        var result = new List<TranslationItem>(groups.Count + noDedupItems.Count);

        foreach (var (_, group) in groups)
        {
            if (group.Count == 1)
            {
                // No duplicates — keep as-is
                result.Add(group[0]);
                continue;
            }

            // Merge: first item is the representative
            var representative = group[0];

            // Collect all CadHandles from the group
            var allHandles = new List<string>();
            if (representative.CadHandles is not null)
                allHandles.AddRange(representative.CadHandles);

            // Collect all MergedItems from the group.
            // The representative must also be included in MergedItems for DwgWriter
            // to properly expand it. If the representative's MergedItems is empty
            // (i.e., it was not merged in step 1), we add the representative itself.
            var allMergedItems = new List<TranslationItem>();
            if (representative.MergedItems.Count > 0)
            {
                // Representative was already merged in step 1 — its MergedItems
                // already contain all original items including itself
                allMergedItems.AddRange(representative.MergedItems);
            }
            else
            {
                // Representative was NOT merged in step 1 — add it as a MergedItem
                // so DwgWriter can expand it properly
                allMergedItems.Add(representative);
            }

            for (int i = 1; i < group.Count; i++)
            {
                var duplicate = group[i];

                // Merge CadHandles
                if (duplicate.CadHandles is not null)
                    allHandles.AddRange(duplicate.CadHandles);

                // Merge MergedItems — if the duplicate was merged in step 1,
                // its MergedItems contain the original items; otherwise, add
                // the duplicate itself
                if (duplicate.MergedItems.Count > 0)
                {
                    allMergedItems.AddRange(duplicate.MergedItems);
                }
                else
                {
                    allMergedItems.Add(duplicate);
                }
            }

            // Update the representative with merged handles and items
            representative.CadHandles = allHandles;
            representative.MergedItems = allMergedItems;

            ErrorLogger.Instance.Info("CleanedTextDeduplicator",
                $"去重：{group.Count} 项合并为 1 项 (EntityType={representative.EntityType}, " +
                $"CleanedText=\"{Truncate(representative.CleanedText ?? "", 30)}\")");

            result.Add(representative);
        }

        // Add items that were not deduplicated
        result.AddRange(noDedupItems);

        ErrorLogger.Instance.Info("CleanedTextDeduplicator",
            $"清洗后去重完成：{mergedItems.Count} → {result.Count} 项");

        return result;
    }

    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..maxLen] + "…";
}
