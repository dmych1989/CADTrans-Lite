// Throwaway diagnostic: runs the SAME import + write-back matching logic as the app
// against the user's real files, and reports how many Excel rows actually match a
// handle in the raw-parsed DXF (and how many fall through as "not found").
using System.Diagnostics;
using CADTransLite.Core.Models;
using CADTransLite.Core.Services;

string dxf = @"C:\Users\Administrator\Downloads\93米X13米鸡舍_t3.dxf";
string excel = @"C:\Users\Administrator\Downloads\93米X13米鸡舍_t3_纯翻译.xlsx";

var handler = new ExcelHandler();
var (items, err) = handler.Import(excel, new List<TranslationItem>());
Console.WriteLine($"Imported rows: {items.Count}, error: {err ?? "none"}");

int withTrans = items.Count(i => !string.IsNullOrWhiteSpace(i.TranslatedText));
int withHandle = items.Count(i => !string.IsNullOrWhiteSpace(i.Handle));
Console.WriteLine($"Rows with translation: {withTrans}");
Console.WriteLine($"Rows with Handle (col A): {withHandle}");

// Raw-parse the DXF exactly like the app does during write-back.
var sw = Stopwatch.StartNew();
var txt = DxfRawParser.ParseTextEntities(dxf);
var mtext = DxfRawParser.ParseMTextEntities(dxf);
var attrib = DxfRawParser.ParseAttributeEntities(dxf);
var tables = DxfRawParser.ParseAcadTables(dxf);
var mleaders = DxfRawParser.ParseMultiLeaders(dxf);
sw.Stop();
Console.WriteLine($"Raw parse took {sw.ElapsedMilliseconds} ms");
Console.WriteLine($"TEXT={txt.Count} MTEXT={mtext.Count} ATTRIB={attrib.Count} TABLES={tables.Count} MLEADERS={mleaders.Count}");

// Build handle lookup exactly like DwgWriter.WriteBack does.
var lookup = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
foreach (var e in txt) if (!string.IsNullOrEmpty(e.Handle)) lookup[e.Handle] = e;
foreach (var e in mtext) if (!string.IsNullOrEmpty(e.Handle)) lookup[e.Handle] = e;
foreach (var e in attrib) if (!string.IsNullOrEmpty(e.Handle)) lookup[e.Handle] = e;
foreach (var t in tables) if (!string.IsNullOrEmpty(t.Handle)) lookup[t.Handle] = t;
foreach (var ml in mleaders) if (!string.IsNullOrEmpty(ml.Handle)) lookup[ml.Handle] = ml;
Console.WriteLine($"Total unique handles in DXF raw parse: {lookup.Count}");

int matched = 0, unmatched = 0;
var samples = new List<string>();
foreach (var it in items)
{
    if (string.IsNullOrWhiteSpace(it.Handle)) { unmatched++; if (samples.Count < 10) samples.Add($"EMPTY handle | orig='{it.OriginalText}' | trans='{it.TranslatedText}'"); continue; }
    // split composite handle (merged rows use '&')
    var hs = it.Handle.Split('&');
    bool any = false;
    foreach (var h in hs) { if (lookup.ContainsKey(h)) { any = true; break; } }
    if (any) matched++; else { unmatched++; if (samples.Count < 10) samples.Add($"handle='{it.Handle}' | orig='{it.OriginalText}' | trans='{it.TranslatedText}'"); }
}
Console.WriteLine($"--- MATCH RESULT ---");
Console.WriteLine($"Matched (handle present in DXF): {matched}");
Console.WriteLine($"Unmatched (handle NOT in DXF): {unmatched}");
Console.WriteLine("Sample unmatched rows:");
foreach (var s in samples) Console.WriteLine("  " + s);

        Diag4.Run();
        Diag3.Run();
        Diag2.Run();
