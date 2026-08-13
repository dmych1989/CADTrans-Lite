using System.Text;
using System.Text.RegularExpressions;
using CADTransLite.Core.Models;
using CADTransLite.Core.Services;
using OfficeOpenXml;

ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

string src = @"C:\Users\Administrator\Downloads\93米X13米鸡舍_t3.dxf";
string excel = args.Length > 0 ? args[0] : @"C:\Users\Administrator\Downloads\93米X13米鸡舍_t3_纯翻译.xlsx";
string outSuffix = args.Length > 1 ? args[1] : "_WRITEBACK_TEST";

var handler = new ExcelHandler();
var (items, err) = handler.Import(excel, new List<TranslationItem>());
if (err != null) { Console.WriteLine("IMPORT ERR: " + err); return; }
Console.WriteLine($"Excel: {excel}");
Console.WriteLine($"Imported {(items?.Count ?? 0)} items");

var byType = items!.GroupBy(x => x.EntityType).ToDictionary(g => g.Key, g => g.Count());
Console.WriteLine("By type: " + string.Join(", ", byType.Select(kv => $"{kv.Key}={kv.Value}")));

var writer = new DwgWriter();
var (outPath, log) = writer.WriteBack(src, items!, suffix: outSuffix);
int warns = log.Count(x => x.StartsWith("[WARN]") || x.StartsWith("[SKIP]"));
Console.WriteLine("Output: " + outPath);
Console.WriteLine($"Log lines: {log.Count}, warns/skips: {warns}");
foreach (var l in log.Where(x => x.StartsWith("[WARN]") || x.StartsWith("[SKIP]")).Take(30))
    Console.WriteLine("  " + l);

string[] lines = File.ReadAllLines(outPath);
int n = lines.Length;
Console.WriteLine($"Total lines: {n} ({(n % 2 == 0 ? "EVEN OK" : "ODD BAD")})");
int bad = 0;
for (int i = 0; i < n; i += 2)
    if (!Regex.IsMatch(lines[i].Trim(), @"^[+-]?\d+$")) { bad++; if (bad <= 5) Console.WriteLine($"  non-code line {i + 1}: {lines[i]}"); }
Console.WriteLine($"Non-code-line errors: {bad} ({(bad == 0 ? "OK" : "BAD")})");
