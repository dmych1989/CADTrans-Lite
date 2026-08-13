using System.Diagnostics;
using CADTransLite.Core.Models;
using CADTransLite.Core.Services;

public static class Diag2
{
    public static void Run()
    {
        string dxf = @"C:\Users\Administrator\Downloads\93米X13米鸡舍_t3.dxf";
        string excel = @"C:\Users\Administrator\Downloads\93米X13米鸡舍_t3_纯翻译.xlsx";

        var handler = new ExcelHandler();
        var (items, err) = handler.Import(excel, new List<TranslationItem>());
        Console.WriteLine($"Imported rows: {items.Count}, error: {err ?? "none"}");

        var sw = Stopwatch.StartNew();
        var writer = new DwgWriter();
        var result = writer.WriteBack(dxf, items, suffix: "_WRITEBACK_TEST");
        sw.Stop();
        string outDxfActual = result.outputFilePath;
        Console.WriteLine($"WriteBack produced: {outDxfActual} in {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"WriteBack log lines: {result.log.Count}");
        var attrOk = result.log.Count(x => x.StartsWith("[OK] ATTR"));
        var attrWarn = result.log.Count(x => x.StartsWith("[WARN]") && x.Contains("::"));
        Console.WriteLine($"[OK] ATTR lines: {attrOk}  | [WARN] with '::': {attrWarn}");
        foreach (var l in result.log.Where(x => x.Contains("::")).Take(30))
            Console.WriteLine("  LOG: " + l);

        // Re-parse the output DXF and compare the translated handles' text vs the Excel translation.
        var txt = DxfRawParser.ParseTextEntities(outDxfActual);
        var mtext = DxfRawParser.ParseMTextEntities(outDxfActual);
        var attrib = DxfRawParser.ParseAttributeEntities(outDxfActual);
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in txt) if (!string.IsNullOrEmpty(e.Handle)) lookup[e.Handle] = e.OriginalText;
        foreach (var e in mtext) if (!string.IsNullOrEmpty(e.Handle)) lookup[e.Handle] = e.OriginalText;
        foreach (var e in attrib) if (!string.IsNullOrEmpty(e.Handle)) lookup[e.Handle] = e.OriginalText;

        int verified = 0, stillOriginal = 0;
        var misses = new List<string>();
        foreach (var it in items)
        {
            if (string.IsNullOrWhiteSpace(it.Handle) || string.IsNullOrWhiteSpace(it.TranslatedText)) continue;
            var hs = it.Handle.Split('&');
            bool ok = false;
            foreach (var h in hs)
            {
                if (lookup.TryGetValue(h, out var cur))
                {
                    if (cur == it.TranslatedText) { ok = true; break; }
                }
            }
            if (ok) verified++;
            else
            {
                bool anyChanged = false;
                foreach (var h in hs)
                    if (lookup.TryGetValue(h, out var cur) && cur != it.OriginalText) anyChanged = true;
                if (anyChanged) verified++;
                else { stillOriginal++; if (misses.Count < 15) misses.Add($"handle='{it.Handle}' orig='{it.OriginalText}' want='{it.TranslatedText}'"); }
            }
        }
        Console.WriteLine($"--- VERIFY IN OUTPUT ---");
        Console.WriteLine($"Translated-and-verified in output: {verified}");
        Console.WriteLine($"Still showing ORIGINAL text (FAILED): {stillOriginal}");
        foreach (var m in misses) Console.WriteLine("  MISS: " + m);
    }
}
