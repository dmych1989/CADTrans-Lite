using CADTransLite.Core.Models;
using CADTransLite.Core.Services;

public static class Diag4
{
    public static void Run()
    {
        string dxf = @"C:\Users\Administrator\Downloads\93米X13米鸡舍_t3.dxf";
        string excel = @"C:\Users\Administrator\Downloads\93米X13米鸡舍_t3_纯翻译.xlsx";

        var attribs = DxfRawParser.ParseAttributeEntities(dxf);
        var handler = new ExcelHandler();
        var (items, _) = handler.Import(excel, new List<TranslationItem>());

        // For the 15 composite items, show their handle and whether any parsed attribute's
        // CompositeKey / InsertHandle / Tag matches.
        var keys = new HashSet<string>(attribs.Select(a => a.CompositeKey), StringComparer.OrdinalIgnoreCase);
        Console.WriteLine($"parsed attribute CompositeKeys sample (first 20):");
        foreach (var a in attribs.Take(20)) Console.WriteLine($"  '{a.CompositeKey}'  (insert={a.InsertHandle}, tag={a.Tag})");

        foreach (var it in items.Where(i => i.Handle != null && i.Handle.Contains("::")))
        {
            bool hit = keys.Contains(it.Handle);
            Console.WriteLine($"ITEM handle='{it.Handle}'  orig='{it.OriginalText}'  -> matched parsed attr: {hit}");
        }
    }
}
