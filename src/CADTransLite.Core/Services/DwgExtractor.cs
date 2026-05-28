// Services/DwgExtractor.cs
// Reads a DXF file using netDxf and extracts all translatable text entities.
// v2.3: Integrates ImportSettings for customizable extraction options.

using System.Linq;
using CADTransLite.Core.Models;
using netDxf;
using netDxf.Entities;
using netDxf.Tables;
using CoreEntityType = CADTransLite.Core.Models.EntityType;

namespace CADTransLite.Core.Services;

/// <summary>
/// Extracts translatable text from a DXF document and optionally merges duplicates.
/// </summary>
public sealed class DwgExtractor
{
    private ImportSettings _importSettings = new();

    /// <summary>
    /// Applies import settings to control which entities are extracted.
    /// </summary>
    public void ApplySettings(ImportSettings settings)
    {
        _importSettings = settings;
    }

    // -----------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------

    /// <summary>
    /// Extracts all text entities from the specified DXF file and merges duplicates.
    /// Respects <see cref="ImportSettings"/>.
    /// </summary>
    /// <param name="filePath">Absolute path to the .dxf file.</param>
    /// <param name="progress">
    /// Optional progress callback. Reports (current, total, message).
    /// </param>
    /// <returns>
    /// A tuple of (mergedItems, rawItemCount).
    /// </returns>
    public (List<TranslationItem> mergedItems, int rawItemCount) ExtractAndMerge(
        string filePath,
        IProgress<(int current, int total, string message)>? progress = null)
    {
        return ExtractAndMerge(filePath, _importSettings, progress);
    }

    /// <summary>
    /// Extracts all text entities from the specified DXF file and merges duplicates.
    /// Uses the provided <paramref name="settings"/> for cleaned-dedup control.
    /// </summary>
    /// <param name="filePath">Absolute path to the .dxf file.</param>
    /// <param name="settings">Import settings controlling dedup behavior.</param>
    /// <param name="progress">
    /// Optional progress callback. Reports (current, total, message).
    /// </param>
    /// <returns>
    /// A tuple of (mergedItems, rawItemCount).
    /// </returns>
    public (List<TranslationItem> mergedItems, int rawItemCount) ExtractAndMerge(
        string filePath,
        ImportSettings settings,
        IProgress<(int current, int total, string message)>? progress = null)
    {
        List<TranslationItem> rawItems = Extract(filePath, progress);
        int rawCount = rawItems.Count;

        progress?.Report((rawCount, rawCount + 1, $"提取 {rawCount} 条，正在合并重复项…"));

        List<TranslationItem> merged = TranslationMerger.Merge(rawItems, settings.EnableCleanedDedup);

        progress?.Report((rawCount + 1, rawCount + 1,
            $"合并完成：{rawCount} → {merged.Count} 条"));

        return (merged, rawCount);
    }

    /// <summary>
    /// Extracts all text entities from the specified DXF file (without merging).
    /// Respects <see cref="ImportSettings"/>.
    /// </summary>
    /// <param name="filePath">Absolute path to the .dxf file.</param>
    /// <param name="progress">
    /// Optional progress callback. Reports (current, total, message).
    /// </param>
    /// <returns>List of <see cref="TranslationItem"/> instances.</returns>
    public List<TranslationItem> Extract(
        string filePath,
        IProgress<(int current, int total, string message)>? progress = null)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"DXF file not found: {filePath}", filePath);

        progress?.Report((0, 100, "Loading DXF document…"));

        DxfDocument doc;
        try
        {
            doc = DxfDocument.Load(filePath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"无法加载 DXF 文档 '{filePath}'：{ex.Message}", ex);
        }

        if (doc is null)
            throw new InvalidOperationException($"DxfDocument.Load 对 '{filePath}' 返回了 null。");

        var items = new List<TranslationItem>();

        // Count total entities for progress reporting.
        int totalTexts = doc.Entities.Texts.Count();
        int totalMTexts = doc.Entities.MTexts.Count();
        int totalInserts = doc.Entities.Inserts.Count();
        int grandTotal = totalTexts + totalMTexts + totalInserts;
        int processed = 0;

        // v3.0: 文本清洗配置
        var cleanerConfig = _importSettings.CleanerConfig;
        bool enableCleaning = _importSettings.EnableTextCleaning;

        progress?.Report((0, grandTotal, $"Found {grandTotal} candidate entities. Extracting…"));

        // ---------------------------------------------------------------
        // 1. TEXT entities (single-line text)
        // ---------------------------------------------------------------
        foreach (var text in doc.Entities.Texts)
        {
            processed++;
            if (processed % 10 == 0)
                progress?.Report((processed, grandTotal, $"Processing TEXT {processed}/{grandTotal}"));

            // Check layer visibility
            if (!IsLayerVisible(text.Layer))
                continue;

            string rawValue = text.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawValue))
                continue;

            // v3.0: 文本清洗过滤
            var item = new TranslationItem
            {
                Handle = text.Handle,
                EntityType = CoreEntityType.Text,
                RawOriginalText = rawValue,
                OriginalText = rawValue,           // TEXT has no format codes
                LayerName = text.Layer?.Name ?? string.Empty,
                CadHandles = new List<string> { text.Handle },
            };

            if (enableCleaning)
            {
                var (cleanedText, wasFiltered, filterReason) = DxfTextCleaner.Clean(rawValue, cleanerConfig);
                item.CleanedText = cleanedText;
                if (wasFiltered)
                {
                    item.Status = "skipped";
                    item.FilterReason = filterReason;
                }
            }

            items.Add(item);
        }

        // ---------------------------------------------------------------
        // 2. MTEXT entities (multi-line text)
        // ---------------------------------------------------------------
        foreach (var mtext in doc.Entities.MTexts)
        {
            processed++;
            if (processed % 10 == 0)
                progress?.Report((processed, grandTotal, $"Processing MTEXT {processed}/{grandTotal}"));

            // Check layer visibility
            if (!IsLayerVisible(mtext.Layer))
                continue;

            string rawValue = mtext.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawValue))
                continue;

            string plainText = MTextCodec.StripFormatCodes(rawValue, out var placeholders);

            if (string.IsNullOrWhiteSpace(plainText))
                continue;

            if (_importSettings.ImportMTextWhole)
            {
                // Extract MTEXT as a whole block
                var mtextItem = new TranslationItem
                {
                    Handle = mtext.Handle,
                    EntityType = CoreEntityType.MText,
                    RawOriginalText = rawValue,
                    OriginalText = plainText,
                    FormatPlaceholders = placeholders,
                    LayerName = mtext.Layer?.Name ?? string.Empty,
                    CadHandles = new List<string> { mtext.Handle },
                };

                if (enableCleaning)
                {
                    var (cleanedText, wasFiltered, filterReason) = DxfTextCleaner.Clean(plainText, cleanerConfig);
                    mtextItem.CleanedText = cleanedText;
                    if (wasFiltered)
                    {
                        mtextItem.Status = "skipped";
                        mtextItem.FilterReason = filterReason;
                    }
                }

                items.Add(mtextItem);
            }
            else if (_importSettings.ImportMTextParagraph)
            {
                // Extract MTEXT by paragraphs (split by \P)
                var paragraphs = SplitByParagraphs(rawValue);
                int paragraphIndex = 0;
                foreach (var paragraph in paragraphs)
                {
                    if (string.IsNullOrWhiteSpace(paragraph))
                        continue;

                    string paraText = MTextCodec.StripFormatCodes(paragraph, out var paraPlaceholders);

                    // Create a composite handle for paragraph identification
                    string paraHandle = $"{mtext.Handle}:P{paragraphIndex}";

                    var paraItem = new TranslationItem
                    {
                        Handle = paraHandle,
                        EntityType = CoreEntityType.MText,
                        RawOriginalText = paragraph,
                        OriginalText = paraText,
                        FormatPlaceholders = paraPlaceholders,
                        LayerName = mtext.Layer?.Name ?? string.Empty,
                        CadHandles = new List<string> { paraHandle },
                    };

                    if (enableCleaning)
                    {
                        var (cleanedText, wasFiltered, filterReason) = DxfTextCleaner.Clean(paraText, cleanerConfig);
                        paraItem.CleanedText = cleanedText;
                        if (wasFiltered)
                        {
                            paraItem.Status = "skipped";
                            paraItem.FilterReason = filterReason;
                        }
                    }

                    items.Add(paraItem);

                    paragraphIndex++;
                }
            }
        }

        // ---------------------------------------------------------------
        // 3. INSERT entities (block references with attributes)
        // ---------------------------------------------------------------
        if (_importSettings.ImportBlockAttributes)
        {
            foreach (var insert in doc.Entities.Inserts)
            {
                processed++;
                if (processed % 10 == 0)
                    progress?.Report((processed, grandTotal, $"Processing INSERT {processed}/{grandTotal}"));

                // Check layer visibility
                if (!IsLayerVisible(insert.Layer))
                    continue;

                if (insert.Attributes is null || !insert.Attributes.Any())
                    continue;

                foreach (var attr in insert.Attributes)
                {
                    string rawValue = attr.Value ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(rawValue))
                        continue;

                    // Composite handle: insertHandle::attrTag
                    string compositeHandle = $"{insert.Handle}::{attr.Tag}";

                    var attrItem = new TranslationItem
                    {
                        Handle = compositeHandle,
                        EntityType = CoreEntityType.Attribute,
                        RawOriginalText = rawValue,
                        OriginalText = rawValue,       // Attributes are plain text
                        LayerName = insert.Layer?.Name ?? string.Empty,
                        CadHandles = new List<string> { compositeHandle },
                        BlockName = insert.Block?.Name,
                        AttributeTag = attr.Tag,
                    };

                    if (enableCleaning)
                    {
                        var (cleanedText, wasFiltered, filterReason) = DxfTextCleaner.Clean(rawValue, cleanerConfig);
                        attrItem.CleanedText = cleanedText;
                        if (wasFiltered)
                        {
                            attrItem.Status = "skipped";
                            attrItem.FilterReason = filterReason;
                        }
                    }

                    items.Add(attrItem);
                }
            }
        }

        // ---------------------------------------------------------------
        // 4. ACAD_TABLE entities (via raw DXF parsing)
        // ---------------------------------------------------------------
        if (_importSettings.ImportAcadTables)
        {
            try
            {
                var tableData = DxfRawParser.ParseAcadTables(filePath);
                foreach (var table in tableData)
                {
                    if (!IsLayerVisibleName(table.LayerName, doc))
                        continue;

                    foreach (var cell in table.Cells)
                    {
                        if (cell.CellType != 1)  // 跳过块类型单元格
                            continue;

                        string rawValue = cell.Text;
                        if (string.IsNullOrWhiteSpace(rawValue))
                            continue;

                        string plainText = MTextCodec.StripFormatCodes(rawValue, out var placeholders);

                        string cellHandle = $"{table.Handle}::R{cell.Row}::C{cell.Column}";

                        var item = new TranslationItem
                        {
                            Handle = cellHandle,
                            EntityType = CoreEntityType.TableCell,
                            RawOriginalText = rawValue,
                            OriginalText = string.IsNullOrWhiteSpace(plainText) ? rawValue : plainText,
                            FormatPlaceholders = placeholders,
                            LayerName = table.LayerName,
                            CadHandles = new List<string> { cellHandle },
                            TableRow = cell.Row,
                            TableColumn = cell.Column,
                        };

                        if (enableCleaning)
                        {
                            var (cleanedText, wasFiltered, filterReason) = DxfTextCleaner.Clean(plainText, cleanerConfig);
                            item.CleanedText = cleanedText;
                            if (wasFiltered)
                            {
                                item.Status = "skipped";
                                item.FilterReason = filterReason;
                            }
                        }

                        items.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLogger.Instance.Warn("DwgExtractor", $"ACAD_TABLE 提取失败: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------
        // 5. MULTILEADER entities (via raw DXF parsing)
        // ---------------------------------------------------------------
        if (_importSettings.ImportMultiLeaders)
        {
            try
            {
                var mleaderData = DxfRawParser.ParseMultiLeaders(filePath);
                foreach (var ml in mleaderData)
                {
                    if (ml.ContentType != 2)  // 跳过块类型和无内容
                        continue;

                    string rawValue = ml.TextContent;
                    if (string.IsNullOrWhiteSpace(rawValue))
                        continue;

                    string plainText = MTextCodec.StripFormatCodes(rawValue, out var placeholders);

                    string mlHandle = $"{ml.Handle}::CTX";

                    var item = new TranslationItem
                    {
                        Handle = mlHandle,
                        EntityType = CoreEntityType.MLeader,
                        RawOriginalText = rawValue,
                        OriginalText = string.IsNullOrWhiteSpace(plainText) ? rawValue : plainText,
                        FormatPlaceholders = placeholders,
                        LayerName = ml.LayerName,
                        CadHandles = new List<string> { mlHandle },
                    };

                    if (enableCleaning)
                    {
                        var (cleanedText, wasFiltered, filterReason) = DxfTextCleaner.Clean(plainText, cleanerConfig);
                        item.CleanedText = cleanedText;
                        if (wasFiltered)
                        {
                            item.Status = "skipped";
                            item.FilterReason = filterReason;
                        }
                    }

                    items.Add(item);
                }
            }
            catch (Exception ex)
            {
                ErrorLogger.Instance.Warn("DwgExtractor", $"MULTILEADER 提取失败: {ex.Message}");
            }
        }

        progress?.Report((grandTotal, grandTotal, $"Extraction complete. {items.Count} items found."));

        return items;
    }

    // -----------------------------------------------------------------
    // Layer visibility helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Checks layer visibility by name (for raw-parsed entities that don't have netDxf Layer objects).
    /// </summary>
    private bool IsLayerVisibleName(string layerName, DxfDocument doc)
    {
        if (string.IsNullOrWhiteSpace(layerName))
            return true;

        var layer = doc.Layers.FirstOrDefault(l =>
            string.Equals(l.Name, layerName, StringComparison.OrdinalIgnoreCase));

        if (layer is null)
            return true;  // Unknown layer → include by default

        return IsLayerVisible(layer);
    }

    /// <summary>
    /// Checks whether a layer is visible based on import settings.
    /// </summary>
    private bool IsLayerVisible(netDxf.Tables.Layer? layer)
    {
        if (layer is null)
            return true;

        // Frozen layer
        if (layer.IsFrozen && !_importSettings.ImportFrozenLayers)
            return false;

        // Locked layer
        if (layer.IsLocked && !_importSettings.ImportLockedLayers)
            return false;

        // Off layer
        if (!layer.IsVisible && !_importSettings.ImportOffLayers)
            return false;

        return true;
    }

    /// <summary>
    /// Splits MText value by paragraph separators (\P).
    /// </summary>
    private static List<string> SplitByParagraphs(string mtextValue)
    {
        // Split by \P (paragraph separator in MText format codes)
        var parts = mtextValue.Split(new[] { @"\P" }, StringSplitOptions.None);
        return parts.ToList();
    }
}
