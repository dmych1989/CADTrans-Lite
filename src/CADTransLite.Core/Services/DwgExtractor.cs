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
    public (List<TranslationItem> mergedItems, int rawItemCount, string? loadWarning) ExtractAndMerge(
        string filePath,
        IProgress<(int current, int total, string message)>? progress = null)
    {
        return ExtractAndMerge(filePath, _importSettings, progress);
    }

    /// <summary>
    /// Extracts all text entities from the specified DXF file and merges duplicates.
    /// Uses the provided <paramref name="settings"/> for extraction filtering AND dedup control.
    /// </summary>
    /// <param name="filePath">Absolute path to the .dxf file.</param>
    /// <param name="settings">Import settings controlling extraction + dedup behavior.</param>
    /// <param name="progress">
    /// Optional progress callback. Reports (current, total, message).
    /// </param>
    /// <returns>
    /// A tuple of (mergedItems, rawItemCount, loadWarning).
    /// <paramref name="loadWarning"/> is non-null when netDxf failed to load the file
    /// (timeout/exception) and extraction fell back to the raw parser, or when the document
    /// loaded but contained zero candidate entities.
    /// </returns>
    public (List<TranslationItem> mergedItems, int rawItemCount, string? loadWarning) ExtractAndMerge(
        string filePath,
        ImportSettings settings,
        IProgress<(int current, int total, string message)>? progress = null)
    {
        // 让调用方传入的导入设置真正生效（否则 Extract 始终使用内部默认值，
        // 导致 UI 中的 MTEXT 整段/分段、图层等开关形同虚设）。
        ApplySettings(settings);

        var (rawItems, loadWarning) = Extract(filePath, progress);
        int rawCount = rawItems.Count;

        progress?.Report((rawCount, rawCount + 1, $"提取 {rawCount} 条，正在合并重复项…"));

        List<TranslationItem> merged = TranslationMerger.Merge(rawItems, settings.EnableCleanedDedup);

        progress?.Report((rawCount + 1, rawCount + 1,
            $"合并完成：{rawCount} → {merged.Count} 条"));

        return (merged, rawCount, loadWarning);
    }

    /// <summary>
    /// Extracts all text entities from the specified DXF file (without merging).
    /// Respects <see cref="ImportSettings"/>.
    /// </summary>
    /// <param name="filePath">Absolute path to the .dxf file.</param>
    /// <param name="progress">
    /// Optional progress callback. Reports (current, total, message).
    /// </param>
    /// <returns>
    /// A tuple of (items, loadWarning). <paramref name="loadWarning"/> is non-null when netDxf
    /// failed to load the file and extraction fell back to the raw parser, or when the document
    /// loaded successfully but contained zero candidate text entities.
    /// </returns>
    public (List<TranslationItem> items, string? loadWarning) Extract(
        string filePath,
        IProgress<(int current, int total, string message)>? progress = null)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"DXF file not found: {filePath}", filePath);

        progress?.Report((0, 100, "Loading DXF document…"));

        var (doc, loadTimedOut, loadError) = LoadDxfWithTimeout(filePath);
        string? loadWarning = null;
        if (doc is null)
        {
            var reason = loadTimedOut ? "加载超时（疑似死循环/阻塞）" : "加载抛异常";
            loadWarning = $"netDxf {reason}（{(loadError ?? "未知原因")}），已改用原始解析器兜底提取。";
            progress?.Report((0, 100, $"netDxf {reason}，改用原始解析器兜底提取…"));
        }

        // 图层可见性状态：netDxf 路径从 DxfDocument 取，兜底路径从原始 DXF 解析。
        var layerStates = doc is not null
            ? BuildLayerStatesFromDoc(doc)
            : ParseRawLayerStates(filePath);

        var items = new List<TranslationItem>();

        int totalTexts = 0, totalMTexts = 0, totalInserts = 0;
        if (doc is not null)
        {
            totalTexts = doc.Entities.Texts.Count();
            totalMTexts = doc.Entities.MTexts.Count();
            totalInserts = doc.Entities.Inserts.Count();
        }
        int grandTotal = totalTexts + totalMTexts + totalInserts;
        int processed = 0;

        // v3.0: 文本清洗配置
        var cleanerConfig = _importSettings.CleanerConfig;
        bool enableCleaning = _importSettings.EnableTextCleaning;

        if (doc is null)
        {
            // netDxf 无法解析该文件。先排除二进制文件，再尝试用 DxfRawParser 兜底提取。
            // 注：回写（WriteBack）全程基于原始句柄/行替换，并不依赖 netDxf，因此兜底提取可直接复用。
            if (IsBinaryDwg(filePath))
                throw new InvalidOperationException(
                    "该文件实际是二进制 DWG 格式，netDxf 不支持读取。请先用 ODA File Converter 将其转换为 DXF 后重试。");
            if (IsBinaryDxf(filePath))
                throw new InvalidOperationException(
                    "该 DXF 是二进制格式，netDxf 仅支持 ASCII DXF。请在 AutoCAD 中「另存为」时选择「AutoCAD R2000 DXF（ASCII）」后重试。");

            ErrorLogger.Instance.Warn("DwgExtractor",
                $"netDxf 无法解析 '{filePath}'，已改用 DxfRawParser 兜底提取（部分复杂实体可能未被识别）。");
            (items, grandTotal) = ExtractViaRawParser(filePath, enableCleaning, cleanerConfig, layerStates, progress, ref processed);
        }
        else
        {
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

        } // netDxf 提取分支结束；以下 TABLE / MULTILEADER 段两种路径都会执行

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
                    if (!IsLayerVisibleName(table.LayerName, layerStates))
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

        // netDxf 成功加载，但文档内没有任何候选文字实体：通常意味着文件确实无文字，
        // 或文字嵌在块定义(BLOCK)内部而未被展开。单独给出诊断提示，避免与“被过滤”混淆。
        if (doc is not null && grandTotal == 0)
            loadWarning = "netDxf 已成功加载该 DXF，但未发现任何 TEXT / MTEXT / INSERT 候选实体（共 0 个）。" +
                          "文件可能不含可提取的文字，或文字位于块定义(BLOCK)内部未被展开。";

        return (items, loadWarning);
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
    /// 按图层状态字典判断图层是否可见（兜底路径，无 DxfDocument）。
    /// </summary>
    private bool IsLayerVisibleName(string layerName, Dictionary<string, (bool frozen, bool locked, bool off)> states)
    {
        if (string.IsNullOrWhiteSpace(layerName))
            return true;
        if (!states.TryGetValue(layerName, out var st))
            return true; // 未知图层 → 默认包含
        if (st.frozen && !_importSettings.ImportFrozenLayers) return false;
        if (st.locked && !_importSettings.ImportLockedLayers) return false;
        if (st.off && !_importSettings.ImportOffLayers) return false;
        return true;
    }

    /// <summary>
    /// 从 DxfDocument 收集图层可见性状态。
    /// </summary>
    private static Dictionary<string, (bool frozen, bool locked, bool off)> BuildLayerStatesFromDoc(DxfDocument doc)
    {
        var map = new Dictionary<string, (bool, bool, bool)>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in doc.Layers)
            map[l.Name] = (l.IsFrozen, l.IsLocked, !l.IsVisible);
        return map;
    }

    /// <summary>
    /// 从原始 DXF 解析 LAYER 表，得到图层可见性状态（兜底路径使用）。
    /// 组码 70 位标志：bit1=冻结，bit4=锁定；组码 62 颜色为负表示关闭。
    /// </summary>
    private static Dictionary<string, (bool frozen, bool locked, bool off)> ParseRawLayerStates(string filePath)
    {
        var map = new Dictionary<string, (bool, bool, bool)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var lines = DxfRawParser.ReadDxfFile(filePath);
            bool inLayerTable = false;
            for (int i = 0; i < lines.Length - 1; i++)
            {
                if (lines[i].Trim() != "0") continue;
                var tag = lines[i + 1].Trim();
                if (tag == "TABLE" && i + 3 < lines.Length && lines[i + 2].Trim() == "2" && lines[i + 3].Trim() == "LAYER")
                    inLayerTable = true;
                else if (tag == "ENDTAB")
                    inLayerTable = false;
                else if (inLayerTable && tag == "LAYER")
                {
                    string name = string.Empty; int flags = 0; int color = 0;
                    int j = i + 2;
                    while (j < lines.Length - 1 && lines[j].Trim() != "0")
                    {
                        var code = lines[j].Trim();
                        var val = lines[j + 1].Trim();
                        if (code == "2") name = val;
                        else if (code == "70") int.TryParse(val, out flags);
                        else if (code == "62") int.TryParse(val, out color);
                        j += 2;
                    }
                    if (!string.IsNullOrEmpty(name))
                        map[name] = ((flags & 1) != 0, (flags & 4) != 0, color < 0);
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLogger.Instance.Warn("DwgExtractor", $"解析图层状态失败: {ex.Message}");
        }
        return map;
    }

    // netDxf 加载超时阈值（毫秒）。遇到畸形/高版本实体时 netDxf 会死循环且不抛异常、永不返回，
    // 普通 try/catch 无法捕获；超过该时长即放弃并回退到 DxfRawParser。
    private const int NetDxfLoadTimeoutMs = 30000;

    /// <summary>
    /// 在带超时的独立后台线程中加载 DXF。
    /// netDxf 遇到某些畸形/高版本实体时会进入死循环或阻塞，且 Release 构建下不抛异常、永不返回，
    /// 普通 try/catch 无法捕获。这里改用后台线程 + Join(timeout) 兜底：超时即放弃 netDxf，
    /// 返回 null，由 Extract 回退到 DxfRawParser 继续提取。
    /// 注意：超时后该后台线程会被“遗弃”（.NET Core 已移除 Thread.Abort），在进程退出前可能持续占用一个 CPU 核；
    /// 相比整个应用卡死，这是可接受的降级。
    /// </summary>
    private static (DxfDocument? doc, bool timedOut, string? error) LoadDxfWithTimeout(string filePath)
    {
        DxfDocument? result = null;
        Exception? caught = null;
        var thread = new System.Threading.Thread(() =>
        {
            try { result = DxfDocument.Load(filePath); }
            catch (Exception ex) { caught = ex; }
        });
        thread.IsBackground = true;
        thread.Start();

        if (!thread.Join(NetDxfLoadTimeoutMs))
        {
            ErrorLogger.Instance.Warn("DwgExtractor",
                $"netDxf 加载 '{filePath}' 超过 {NetDxfLoadTimeoutMs}ms 仍未返回（疑似死循环/阻塞），已放弃并改用原始解析器兜底。");
            return (null, true, $"加载超过 {NetDxfLoadTimeoutMs}ms 仍未返回（疑似死循环/阻塞）");
        }

        if (caught is not null)
        {
            var detail = $"{caught.GetType().FullName}: {caught.Message}";
            if (caught.InnerException is not null)
                detail += $" | Inner: {caught.InnerException.GetType().FullName}: {caught.InnerException.Message}";
            ErrorLogger.Instance.Warn("DwgExtractor", $"netDxf 加载 '{filePath}' 时发生异常：{detail}");
            ErrorLogger.Instance.Warn("DwgExtractor", $"netDxf 异常堆栈：{caught.StackTrace}");
            return (null, false, detail);
        }

        // netDxf 对某些无法解析的 DXF 会静默返回 null（不抛异常），需单独说明，
        // 否则上层只会看到"未知原因"，无从判断是格式不兼容还是文件损坏。
        if (result is null)
            return (null, false, "DxfDocument.Load 静默返回 null（netDxf 2022.11.2 无法解析该 DXF 的格式/版本，例如高版本或二进制 DXF）");

        return (result, false, null);
    }

    /// <summary>
    /// 判断文件是否为二进制 DWG（netDxf 不支持读取）。
    /// </summary>
    private static bool IsBinaryDwg(string filePath)
    {
        try
        {
            using var fs = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
            var hdr = new byte[6];
            if (fs.Read(hdr, 0, 6) < 6) return false;
            var s = System.Text.Encoding.ASCII.GetString(hdr);
            return s.StartsWith("AC10") || s.StartsWith("AC1.");
        }
        catch { return false; }
    }

    /// <summary>
    /// 判断文件是否为二进制 DXF（netDxf 仅支持 ASCII DXF）。
    /// </summary>
    private static bool IsBinaryDxf(string filePath)
    {
        try
        {
            using var fs = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
            var hdr = new byte[22];
            if (fs.Read(hdr, 0, 22) < 22) return false;
            var s = System.Text.Encoding.ASCII.GetString(hdr);
            return s.StartsWith("AutoCAD Binary DXF");
        }
        catch { return false; }
    }

    /// <summary>
    /// 当 netDxf 无法解析 DXF 时（DxfDocument.Load 返回 null 或抛解析异常），
    /// 改用 DxfRawParser 直接从文本提取 TEXT / MTEXT / ATTRIB。
    /// 回写端基于原始句柄/行替换，与 netDxf 无关，因此兜底提取可直接复用。
    /// </summary>
    private (List<TranslationItem> items, int grandTotal) ExtractViaRawParser(
        string filePath,
        bool enableCleaning,
        DxfTextCleanerConfig cleanerConfig,
        Dictionary<string, (bool frozen, bool locked, bool off)> layerStates,
        IProgress<(int, int, string)>? progress,
        ref int processed)
    {
        var items = new List<TranslationItem>();

        bool LayerVisible(string ln) => layerStates.Count == 0 || IsLayerVisibleName(ln, layerStates);

        // TEXT
        var texts = DxfRawParser.ParseTextEntities(filePath);
        foreach (var t in texts)
        {
            if (!LayerVisible(t.LayerName)) continue;
            var raw = t.OriginalText;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var (cleaned, wasFiltered, filterReason) = DxfTextCleaner.Clean(raw, cleanerConfig);
            var textHandle = string.IsNullOrEmpty(t.Handle) ? "L" + t.TextLineNumber : t.Handle;
            var item = new TranslationItem
            {
                Handle = textHandle,
                EntityType = CoreEntityType.Text,
                RawOriginalText = raw,
                OriginalText = cleaned,
                LayerName = t.LayerName,
                CadHandles = new List<string> { textHandle },
            };
            if (wasFiltered) { item.Status = "skipped"; item.FilterReason = filterReason; }
            items.Add(item);
            processed++;
        }
        progress?.Report((processed, texts.Count, $"从原始 DXF 提取 TEXT {processed}/{texts.Count}…"));

        // MTEXT（整体模式；回写端按句柄整体替换）
        var mtexts = DxfRawParser.ParseMTextEntities(filePath);
        foreach (var m in mtexts)
        {
            if (!LayerVisible(m.LayerName)) continue;
            var rawValue = m.OriginalText;
            if (string.IsNullOrWhiteSpace(rawValue)) continue;
            var plainText = MTextCodec.StripFormatCodes(rawValue, out var placeholders);
            if (string.IsNullOrWhiteSpace(plainText)) continue;
            var (cleaned, wasFiltered, filterReason) = DxfTextCleaner.Clean(plainText, cleanerConfig);
            var mtextHandle = string.IsNullOrEmpty(m.Handle) ? "L" + m.LastGroup1LineNumber : m.Handle;
            var item = new TranslationItem
            {
                Handle = mtextHandle,
                EntityType = CoreEntityType.MText,
                RawOriginalText = rawValue,
                OriginalText = cleaned,
                FormatPlaceholders = placeholders,
                LayerName = m.LayerName,
                CadHandles = new List<string> { mtextHandle },
            };
            if (wasFiltered) { item.Status = "skipped"; item.FilterReason = filterReason; }
            items.Add(item);
            processed++;
        }
        progress?.Report((processed, texts.Count + mtexts.Count, $"从原始 DXF 提取 MTEXT {mtexts.Count} 项…"));

        // ATTRIB（INSERT 属性）
        int attribCount = 0;
        if (_importSettings.ImportBlockAttributes)
        {
            var attribs = DxfRawParser.ParseAttributeEntities(filePath);
            foreach (var a in attribs)
            {
                if (!LayerVisible(a.LayerName)) continue;
                var raw = a.OriginalText;
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var (cleaned, wasFiltered, filterReason) = DxfTextCleaner.Clean(raw, cleanerConfig);
            var attribHandle = string.IsNullOrEmpty(a.InsertHandle) ? "L" + a.TextLineNumber : a.CompositeKey;
            var item = new TranslationItem
            {
                Handle = attribHandle,
                EntityType = CoreEntityType.Attribute,
                RawOriginalText = raw,
                OriginalText = cleaned,
                LayerName = a.LayerName,
                AttributeTag = a.Tag,
                CadHandles = new List<string> { attribHandle },
            };
                if (wasFiltered) { item.Status = "skipped"; item.FilterReason = filterReason; }
                items.Add(item);
                processed++;
                attribCount++;
            }
            progress?.Report((processed, texts.Count + mtexts.Count + attribs.Count, $"从原始 DXF 提取 ATTRIB {attribCount} 项…"));
        }

        int grandTotal = texts.Count + mtexts.Count + attribCount;
        return (items, grandTotal);
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
