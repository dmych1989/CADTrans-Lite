// BridgeHandler.cs
// Dispatches bridge commands to the CADTrans Lite Core pipeline (extract -> translate -> writeback).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Text.Json;
using System.Threading.Tasks;
using CADTransLite.Core.Models;
using CADTransLite.Core.Services;

namespace CADTransLite.McpServer;

internal static class BridgeHandler
{
    public static async Task<BridgeResponse> Dispatch(BridgeRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Command))
            return BridgeResponse.Fail("缺少 command 字段");

        var p = req.Params ?? new JsonElement();
        try
        {
            return req.Command switch
            {
                "translate_text" => await TranslateText(p, ct),
                "list_engines" => ListEngines(),
                "list_language_pairs" => await ListLanguagePairs(p),
                "read_entities" => await ReadEntities(p, ct),
                "write_translation" => await WriteTranslation(p, ct),
                "translate_drawing" => await TranslateDrawing(p, ct),
                "get_status" => await GetStatus(p),
                _ => BridgeResponse.Fail($"未知命令：{req.Command}")
            };
        }
        catch (Exception ex)
        {
            return BridgeResponse.Fail($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    // --- commands ---------------------------------------------------------

    static async Task<BridgeResponse> TranslateText(JsonElement p, CancellationToken ct)
    {
        var text = p.Str("text");
        if (string.IsNullOrEmpty(text))
            return BridgeResponse.Fail("缺少 text");

        var src = p.Str("source", "en");
        var tgt = p.Str("target", "zh");
        var engine = p.Str("engine", "Argos Translate (本地)");
        var cfg = ParseConfig(p);

        var api = EngineFactory.Build(engine, cfg);
        var svc = new TranslationService(api);
        var items = new List<TranslationItem> { new() { OriginalText = text, RawOriginalText = text } };
        await svc.TranslateItemsAsync(items, src, tgt, null, ct);

        var translated = items[0].TranslatedText ?? text;
        return BridgeResponse.Ok(new
        {
            translated_text = translated,
            engine,
            source = src,
            target = tgt
        });
    }

    static BridgeResponse ListEngines() => BridgeResponse.Ok(new
    {
        engines = EngineFactory.EngineNames,
        local_defaults = EngineFactory.LocalDefaults,
        note = "本地引擎(Argos/LibreTranslate/NLLB/DeepLX)需对应 HTTP 服务已启动；远程引擎(百度/腾讯/Microsoft/DeepL/自定义AI)需提供 API 密钥。"
    });

    static async Task<BridgeResponse> ListLanguagePairs(JsonElement p)
    {
        var url = p.Str("argos_url", EngineFactory.LocalDefaults["argos_url"]);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var raw = await http.GetStringAsync(url.TrimEnd('/') + "/ready");
            int numModels = -1;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("num_models", out var nm)) numModels = nm.GetInt32();
            }
            catch { /* ignore parse issues */ }

            return BridgeResponse.Ok(new { argos_url = url, ready = true, num_models = numModels, raw });
        }
        catch (Exception ex)
        {
            return BridgeResponse.Ok(new { argos_url = url, ready = false, error = ex.Message });
        }
    }

    static async Task<BridgeResponse> ReadEntities(JsonElement p, CancellationToken ct)
    {
        var filePath = p.Str("file_path");
        if (string.IsNullOrEmpty(filePath))
            return BridgeResponse.Fail("缺少 file_path");
        filePath = Path.GetFullPath(filePath);
        if (!File.Exists(filePath))
            return BridgeResponse.Fail($"文件不存在：{filePath}");

        var import = ParseImportSettings(p);
        var extractor = new DwgExtractor();
        string dxfPath;
        bool wasDwg;
        List<TranslationItem> items;
        string? warning;

        if (IsDwg(filePath))
        {
            var oda = new OdaConverter(ParseOda(p));
            if (!oda.IsAvailable)
                return BridgeResponse.Fail("DWG 文件需要 ODA File Converter，未检测到（可用 oda_path 指定）。");
            dxfPath = await oda.DwgToDxfAsync(filePath, Path.GetDirectoryName(filePath)!, ct);
            wasDwg = true;
            (items, _, warning) = extractor.ExtractAndMerge(dxfPath, import, null);
        }
        else
        {
            dxfPath = filePath;
            wasDwg = false;
            (items, _, warning) = extractor.ExtractAndMerge(filePath, import, null);
        }

        SessionCache.Set(filePath, new DrawingSession
        {
            OriginalPath = filePath,
            DxfPath = dxfPath,
            Items = items,
            WasDwg = wasDwg
        });

        var entities = items.Select(i => new
        {
            id = i.IdString,
            handle = i.Handle,
            entity_type = i.EntityType.ToString(),
            original_text = i.OriginalText,
            layer = i.LayerName,
            block = i.BlockName
        }).ToList();

        return BridgeResponse.Ok(new
        {
            file_path = filePath,
            dxf_path = dxfPath,
            entity_count = entities.Count,
            warning,
            entities
        });
    }

    static async Task<BridgeResponse> WriteTranslation(JsonElement p, CancellationToken ct)
    {
        var filePath = p.Str("file_path");
        if (string.IsNullOrEmpty(filePath))
            return BridgeResponse.Fail("缺少 file_path");
        filePath = Path.GetFullPath(filePath);

        var session = SessionCache.Get(filePath);
        if (session == null)
        {
            // Auto-read so a write can be issued without an explicit prior read_entities.
            var read = await ReadEntities(p, ct);
            if (!read.Success) return read;
            session = SessionCache.Get(filePath) ?? throw new InvalidOperationException("无法获取会话");
        }

        if (!p.Has("translations"))
            return BridgeResponse.Fail("缺少 translations 列表");
        var transArr = p.GetProperty("translations");
        if (transArr.ValueKind != JsonValueKind.Array)
            return BridgeResponse.Fail("translations 必须为数组");

        var byId = new Dictionary<string, TranslationItem>(StringComparer.OrdinalIgnoreCase);
        var byHandle = new Dictionary<string, TranslationItem>(StringComparer.OrdinalIgnoreCase);
        var byText = new Dictionary<string, TranslationItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in session.Items)
        {
            if (!string.IsNullOrEmpty(it.IdString)) byId[it.IdString] = it;
            if (!string.IsNullOrEmpty(it.Handle)) byHandle[it.Handle] = it;
            if (!string.IsNullOrEmpty(it.OriginalText)) byText[it.OriginalText.Trim()] = it;
        }

        int updated = 0, notFound = 0;
        foreach (var el in transArr.EnumerateArray())
        {
            var translated = el.Str("translated");
            if (string.IsNullOrEmpty(translated)) continue;

            TranslationItem? target = null;
            var id = el.Str("id");
            var handle = el.Str("handle");
            var original = el.Str("original");
            if (!string.IsNullOrEmpty(id) && byId.TryGetValue(id, out var a)) target = a;
            else if (!string.IsNullOrEmpty(handle) && byHandle.TryGetValue(handle, out var b)) target = b;
            else if (!string.IsNullOrEmpty(original) && byText.TryGetValue(original.Trim(), out var c)) target = c;

            if (target != null) { target.TranslatedText = translated; updated++; }
            else notFound++;
        }

        var enableLayout = p.Bool("enable_layout_adjust", true);
        var writer = new DwgWriter();
        var (outPath, log) = writer.WriteBack(session.DxfPath, session.Items, null, "_translated", enableLayout);

        if (session.WasDwg)
        {
            var oda = new OdaConverter(ParseOda(p));
            if (oda.IsAvailable)
                outPath = await oda.DxfToDwgAsync(outPath, Path.GetDirectoryName(outPath)!, null, ct);
        }

        return BridgeResponse.Ok(new
        {
            output_path = outPath,
            updated,
            not_found = notFound,
            total = session.Items.Count,
            log
        });
    }

    static async Task<BridgeResponse> TranslateDrawing(JsonElement p, CancellationToken ct)
    {
        var filePath = p.Str("file_path");
        if (string.IsNullOrEmpty(filePath))
            return BridgeResponse.Fail("缺少 file_path");
        filePath = Path.GetFullPath(filePath);
        if (!File.Exists(filePath))
            return BridgeResponse.Fail($"文件不存在：{filePath}");

        var src = p.Str("source", "en");
        var tgt = p.Str("target", "zh");
        var engine = p.Str("engine", "Argos Translate (本地)");
        var cfg = ParseConfig(p);
        var import = ParseImportSettings(p);

        string dxfPath;
        bool wasDwg;
        if (IsDwg(filePath))
        {
            var oda = new OdaConverter(ParseOda(p));
            if (!oda.IsAvailable)
                return BridgeResponse.Fail("DWG 文件需要 ODA File Converter，未检测到（可用 oda_path 指定）。");
            dxfPath = await oda.DwgToDxfAsync(filePath, Path.GetDirectoryName(filePath)!, ct);
            wasDwg = true;
        }
        else
        {
            dxfPath = filePath;
            wasDwg = false;
        }

        var extractor = new DwgExtractor();
        var (items, rawCount, warning) = extractor.ExtractAndMerge(dxfPath, import, null);

        var api = EngineFactory.Build(engine, cfg);
        var svc = new TranslationService(api);
        await svc.TranslateItemsAsync(items, src, tgt, null, ct);

        var enableLayout = p.Bool("enable_layout_adjust", true);
        var writer = new DwgWriter();
        var (outPath, log) = writer.WriteBack(dxfPath, items, null, "_translated", enableLayout);

        var translatedCount = items.Count(i =>
            !string.IsNullOrEmpty(i.TranslatedText) && i.TranslatedText != i.OriginalText);

        if (wasDwg)
        {
            var oda = new OdaConverter(ParseOda(p));
            if (oda.IsAvailable)
                outPath = await oda.DxfToDwgAsync(outPath, Path.GetDirectoryName(filePath)!, null, ct);
        }

        SessionCache.Set(filePath, new DrawingSession
        {
            OriginalPath = filePath,
            DxfPath = dxfPath,
            Items = items,
            WasDwg = wasDwg
        });

        return BridgeResponse.Ok(new
        {
            output_path = outPath,
            entities_total = items.Count,
            raw_count = rawCount,
            translated_count = translatedCount,
            source = src,
            target = tgt,
            engine,
            warning,
            log
        });
    }

    static async Task<BridgeResponse> GetStatus(JsonElement p)
    {
        var argosUrl = p.Str("argos_url", EngineFactory.LocalDefaults["argos_url"]);
        var argosReady = await ProbeArgos(argosUrl);
        return BridgeResponse.Ok(new
        {
            ready = true,
            argos_url = argosUrl,
            argos_ready = argosReady,
            cwd = Directory.GetCurrentDirectory()
        });
    }

    // --- helpers ----------------------------------------------------------

    static async Task<bool> ProbeArgos(string url)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            await http.GetStringAsync(url.TrimEnd('/') + "/ready");
            return true;
        }
        catch
        {
            return false;
        }
    }

    static Dictionary<string, string> ParseConfig(JsonElement p)
    {
        var cfg = new Dictionary<string, string>();
        foreach (var key in new[]
        {
            "argos_url", "libre_url", "nllb_url", "deeplx_url",
            "base_url", "api_key", "model",
            "app_id", "app_key", "secret_id", "secret_key", "region"
        })
        {
            var v = p.Str(key);
            if (!string.IsNullOrEmpty(v)) cfg[key] = v;
        }
        return cfg;
    }

    static ImportSettings ParseImportSettings(JsonElement p) => new()
    {
        ImportBlockAttributes = p.Bool("import_block_attributes", true),
        ImportMTextParagraph = p.Bool("import_mtext_paragraph", true),
        ImportMTextWhole = p.Bool("import_mtext_whole", true),
        ImportFrozenLayers = p.Bool("import_frozen_layers", true),
        ImportLockedLayers = p.Bool("import_locked_layers", true),
        ImportOffLayers = p.Bool("import_off_layers", false),
        UseRichExcelFormat = p.Bool("use_rich_excel_format", true),
        EnableCleanedDedup = p.Bool("enable_cleaned_dedup", false),
        EnableLayoutAdjust = p.Bool("enable_layout_adjust", true),
        EnableAiFilter = p.Bool("enable_ai_filter", false),
        EnableGlossary = p.Bool("enable_glossary", false)
    };

    static OdaSettings ParseOda(JsonElement p)
    {
        var odaPath = p.Str("oda_path");
        return string.IsNullOrEmpty(odaPath)
            ? new OdaSettings()
            : new OdaSettings { ExecutablePath = odaPath };
    }

    static bool IsDwg(string path) =>
        string.Equals(Path.GetExtension(path), ".dwg", StringComparison.OrdinalIgnoreCase);
}
