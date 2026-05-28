// Services/TranslationService.cs
// Orchestrates translation using a configured ITranslationApi provider.
// v2.1: 50 items per batch, 200ms inter-batch delay, CancellationToken support.

using CADTransLite.Core.Interfaces;
using CADTransLite.Core.Models;

namespace CADTransLite.Core.Services;

/// <summary>
/// Coordinates the translation of <see cref="TranslationItem"/> lists using
/// a pluggable <see cref="ITranslationApi"/> provider.
/// Batches items in groups of 50 with a 200ms delay between batches to avoid rate limiting.
/// </summary>
public sealed class TranslationService
{
    private readonly ITranslationApi _api;

    /// <summary>Maximum number of texts per batch.</summary>
    private const int BatchSize = 50;

    /// <summary>Delay between batches in milliseconds to avoid rate limiting.</summary>
    private const int InterBatchDelayMs = 200;

    /// <summary>
    /// Creates a new translation service with the specified API provider.
    /// </summary>
    /// <param name="api">The translation API to use.</param>
    public TranslationService(ITranslationApi api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    /// <summary>
    /// Translates all items in <paramref name="items"/> that do not already have
    /// a <see cref="TranslationItem.TranslatedText"/> value.
    /// Items are processed in batches of 50 with a 200ms delay between batches.
    /// Translation results are written directly to each item's <see cref="TranslationItem.TranslatedText"/>.
    /// </summary>
    /// <param name="items">Merged translation items (from <see cref="TranslationMerger"/>).</param>
    /// <param name="sourceLang">Provider-specific source language code.</param>
    /// <param name="targetLang">Provider-specific target language code.</param>
    /// <param name="progress">Optional progress callback.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public async Task TranslateItemsAsync(
        List<TranslationItem> items,
        string sourceLang,
        string targetLang,
        IProgress<(int current, int total, string message)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0)
            return;

        // Collect texts that need translation.
        var indicesToTranslate = new List<int>();
        var textsToTranslate = new List<string>();

        for (int i = 0; i < items.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(items[i].TranslatedText))
            {
                indicesToTranslate.Add(i);
                // Use clean text for the translation API (same as what the user sees in Excel).
                // Strip MText format codes but keep paragraph breaks (\P → newline).
                textsToTranslate.Add(GetTextForTranslation(items[i]));
            }
        }

        if (textsToTranslate.Count == 0)
        {
            progress?.Report((items.Count, items.Count, "所有条目已有翻译，无需调用 API。"));
            return;
        }

        int totalTexts = textsToTranslate.Count;
        progress?.Report((0, totalTexts, $"正在通过 {_api.Name} 翻译 {totalTexts} 条文本…"));

        // Process in batches of 50 with inter-batch delay.
        int totalBatches = (totalTexts + BatchSize - 1) / BatchSize;
        int overallDone = 0;

        for (int batchIndex = 0; batchIndex < totalBatches; batchIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Inter-batch delay (skip for the first batch).
            if (batchIndex > 0)
            {
                await Task.Delay(InterBatchDelayMs, cancellationToken);
            }

            int batchStart = batchIndex * BatchSize;
            int batchCount = Math.Min(BatchSize, totalTexts - batchStart);

            var batchTexts = textsToTranslate.GetRange(batchStart, batchCount);
            var batchIndices = indicesToTranslate.GetRange(batchStart, batchCount);

            // Call the translation API for this batch.
            List<string> translations = await _api.TranslateBatchAsync(
                batchTexts, sourceLang, targetLang, cancellationToken);

            // Apply translations back to items.
            for (int j = 0; j < batchIndices.Count; j++)
            {
                int itemIndex = batchIndices[j];
                items[itemIndex].TranslatedText = j < translations.Count
                    ? translations[j]
                    : items[itemIndex].OriginalText;
            }

            overallDone += batchCount;
            progress?.Report((overallDone, totalTexts,
                $"已翻译 {overallDone}/{totalTexts} 条（批次 {batchIndex + 1}/{totalBatches}）"));
        }

        progress?.Report((totalTexts, totalTexts, "翻译完成。"));
    }

    /// <summary>
    /// Produces clean, human-readable text from a <see cref="TranslationItem"/>
    /// suitable for sending to a translation API.
    /// Uses <see cref="MTextCodec.StripForTranslation"/> to remove MText format
    /// codes while preserving paragraph breaks as newlines.
    /// </summary>
    private static string GetTextForTranslation(TranslationItem item)
    {
        // Prefer RawOriginalText (what the user sees in the Excel "原文" column)
        string source = !string.IsNullOrEmpty(item.RawOriginalText)
            ? item.RawOriginalText
            : item.OriginalText;

        // If the source still contains binary placeholders from StripFormatCodes,
        // fall back to OriginalText after stripping placeholders.
        if (source.Contains(MTextCodec.PlaceholderPrefix))
        {
            source = item.OriginalText;
            // Strip binary placeholder tokens, converting \P codes to newlines
            foreach (var kvp in item.FormatPlaceholders)
            {
                if (kvp.Value == @"\P" || kvp.Value == @"\p")
                    source = source.Replace(kvp.Key, "\n");
                else
                    source = source.Replace(kvp.Key, "");
            }
            return source.Trim();
        }

        // Clean MText format codes for translation
        return MTextCodec.StripForTranslation(source);
    }
}
