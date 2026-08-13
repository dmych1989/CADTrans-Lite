using CADTransLite.Core.Models;
using CADTransLite.Core.Services;

public static class Diag3
{
    public static void Run()
    {
        string outDxf = @"C:\Users\Administrator\Downloads\93米X13米鸡舍_t3_WRITEBACK_TEST.dxf";
        string excel = @"C:\Users\Administrator\Downloads\93米X13米鸡舍_t3_纯翻译.xlsx";

        var handler = new ExcelHandler();
        var (items, _) = handler.Import(excel, new List<TranslationItem>());

        // Re-parse the OUTPUT dxf for attribute + table cells to verify composite-key items.
        var attribs = DxfRawParser.ParseAttributeEntities(outDxf);
        var tables = DxfRawParser.ParseAcadTables(outDxf);

        // Build lookup for composite attribute keys: {InsertHandle}::{Tag}
        var attribByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in attribs)
            attribByKey[$"{a.InsertHandle}::{a.Tag}"] = a.OriginalText;

        // Build lookup for table cells by composite key {tableHandle}::R{row}::C{col}
        var tableByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tables)
            foreach (var c in t.Cells)
                tableByKey[$"{t.Handle}::R{c.Row}::C{c.Column}"] = c.Text;

        int verified = 0, failed = 0;
        var fails = new List<string>();
        foreach (var it in items)
        {
            if (string.IsNullOrWhiteSpace(it.Handle) || string.IsNullOrWhiteSpace(it.TranslatedText)) continue;
            if (it.Handle.Contains("::"))
            {
                bool ok = false;
                if (attribByKey.TryGetValue(it.Handle, out var av)) { if (av == it.TranslatedText) ok = true; }
                if (!ok && tableByKey.TryGetValue(it.Handle, out var tv)) { if (tv == it.TranslatedText) ok = true; }
                if (ok) verified++;
                else { failed++; if (fails.Count < 15) fails.Add($"composite='{it.Handle}' want='{it.TranslatedText}'"); }
            }
        }
        Console.WriteLine($"[Composite-key items] verified-in-output={verified}, still-original={failed}");
        foreach (var f in fails) Console.WriteLine("  CFAIL: " + f);

        // Also count how many of the 116 total have their translation present ANYWHERE in output.
        int anyPresent = 0;
        var presentStrings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // quick scan of output for each translated text (first 12 chars)
        var outLines = DxfRawParser.ReadDxfFile(outDxf);
        var outSet = new HashSet<string>(outLines, StringComparer.OrdinalIgnoreCase);
        foreach (var it in items)
        {
            if (string.IsNullOrWhiteSpace(it.TranslatedText)) continue;
            string probe = it.TranslatedText.Length <= 20 ? it.TranslatedText : it.TranslatedText.Substring(0, 20);
            if (outSet.Contains(probe)) anyPresent++;
        }
        Console.WriteLine($"[Probe] translated texts found as exact lines in output: {anyPresent}/{items.Count(i => !string.IsNullOrWhiteSpace(i.TranslatedText))}");
    }
}
