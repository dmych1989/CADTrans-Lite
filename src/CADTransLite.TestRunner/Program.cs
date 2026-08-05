using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CADTransLite.Core.Models;
using CADTransLite.Core.Services;

namespace CADTransLite.TestRunner
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=" + new string('=', 78));
            Console.WriteLine("CADTrans Lite - 实际提取测试");
            Console.WriteLine("=" + new string('=', 78));

            string dxfPath = @"C:\Users\Administrator\Downloads\93米X13米鸡舍_t3.dxf";

            // 直接探测流式解析器是否能从 142MB 文件提取文字
            try
            {
                int t = DxfRawParser.ParseTextEntities(dxfPath).Count;
                int m = DxfRawParser.ParseMTextEntities(dxfPath).Count;
                int a = DxfRawParser.ParseAttributeEntities(dxfPath).Count;
                Console.WriteLine($"【流式解析器】TEXT={t}, MTEXT={m}, ATTRIB={a}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"【流式解析器】异常: {ex.Message}");
            }
            string outputPath = @"d:\GitHub\CADTrans Lite\test_extraction_output.xlsx";

            if (!File.Exists(dxfPath))
            {
                Console.WriteLine("❌ 文件不存在: " + dxfPath);
                return;
            }

            Console.WriteLine("\n测试文件: " + Path.GetFileName(dxfPath));

            try
            {
                // 1. 初始化Extractor
                var extractor = new DwgExtractor();
                extractor.ApplySettings(new ImportSettings
                {
                    ImportMTextWhole = true,
                    ImportMTextParagraph = false,
                    ImportBlockAttributes = true,
                    ImportDimensionText = true,
                    ImportFrozenLayers = false,
                    ImportLockedLayers = false,
                    ImportOffLayers = false
                });

                Console.WriteLine("\n1. 开始提取...");
                var (mergedItems, rawCount, loadWarning) = extractor.ExtractAndMerge(dxfPath);
                if (!string.IsNullOrEmpty(loadWarning))
                    Console.WriteLine($"⚠️ 加载警告: {loadWarning}");
                Console.WriteLine($"✅ 提取完成: {rawCount} 原始项 → {mergedItems.Count} 合并项");

                // 2. 输出前30项的对比
                Console.WriteLine("\n" + new string('-', 80));
                Console.WriteLine("前20项内容（OriginalText vs RawOriginalText）:");
                Console.WriteLine(new string('-', 80));
                for (int i = 0; i < Math.Min(20, mergedItems.Count); i++)
                {
                    var item = mergedItems[i];
                    Console.WriteLine($"\n[{i+2}] {item.EntityType}");
                    Console.WriteLine($"  OriginalText: {repr(item.OriginalText)}");
                    Console.WriteLine($"  RawOriginalText: {repr(item.RawOriginalText)}");
                    Console.WriteLine($"  CadHandles: {string.Join(", ", item.CadHandles.Take(3))}...");
                }

                // 3. 查找包含\n或\P的项
                Console.WriteLine("\n" + new string('=', 80));
                Console.WriteLine("查找包含换行符或格式码的项:");
                Console.WriteLine(new string('=', 80));
                int count_newline = 0;
                int count_backslash_p = 0;
                foreach (var item in mergedItems)
                {
                    if (item.OriginalText.Contains("\n"))
                        count_newline++;
                    if (item.RawOriginalText.Contains("\\P"))
                        count_backslash_p++;
                }
                Console.WriteLine($"包含\\n的项: {count_newline}");
                Console.WriteLine($"包含\\P的RawOriginalText: {count_backslash_p}");

                // 4. 导出Excel（测试）
                Console.WriteLine("\n" + new string('-', 80));
                Console.WriteLine("4. 导出Excel测试...");
                var excelHandler = new ExcelHandler();
                excelHandler.Export(mergedItems, outputPath);
                Console.WriteLine($"✅ Excel已导出到: {outputPath}");

                // 5. 保存原始提取结果到文本文件
                Console.WriteLine("\n" + new string('-', 80));
                Console.WriteLine("5. 保存详细提取结果到文本文件...");
                var lines = new List<string>();
                lines.Add("CADTrans Lite 提取结果");
                lines.Add(new string('=', 80));
                lines.Add("");
                lines.Add($"总项数: {mergedItems.Count}");
                lines.Add("");
                
                for (int i = 0; i < mergedItems.Count; i++)
                {
                    var item = mergedItems[i];
                    lines.Add($"--- 项 {i+2} ---");
                    lines.Add($"EntityType: {item.EntityType}");
                    lines.Add($"Handle: {item.Handle}");
                    lines.Add($"OriginalText: {repr(item.OriginalText)}");
                    lines.Add($"RawOriginalText: {repr(item.RawOriginalText)}");
                    lines.Add($"CadHandles: {string.Join(", ", item.CadHandles)}");
                    lines.Add("");
                }
                
                File.WriteAllLines(@"e:\CADTransLite_提取结果详细.txt", lines);
                Console.WriteLine("✅ 详细结果已保存到 CADTransLite_提取结果详细.txt");

                // ── 复现 App 真实调用方式：不调 ApplySettings，直接传默认 ImportSettings ──
                Console.WriteLine("\n" + new string('=', 80));
                Console.WriteLine("复现 App 调用路径（不 ApplySettings，传默认 settings）:");
                Console.WriteLine(new string('=', 80));
                var appExtractor = new DwgExtractor();
                var appImport = new ImportSettings();  // 默认
                var (appMerged, appRaw, appWarning) = appExtractor.ExtractAndMerge(dxfPath, appImport, null);
                if (!string.IsNullOrEmpty(appWarning))
                    Console.WriteLine($"⚠️ App风格加载警告: {appWarning}");
                Console.WriteLine($"App 风格: {appRaw} 原始项 → {appMerged.Count} 合并项");

                // ── 诊断用 DxfRawParser 原始计数 ──
                int rawTxt = DxfRawParser.ParseTextEntities(dxfPath).Count;
                int rawMtxt = DxfRawParser.ParseMTextEntities(dxfPath).Count;
                Console.WriteLine($"DxfRawParser 原始计数: TEXT={rawTxt}, MTEXT={rawMtxt}");

                // ── 逐行统计（绕过 grep 二进制判定），确认文件真实结构 ──
                var allLines = File.ReadAllLines(dxfPath);
                int c0 = 0, cText = 0, cMtext = 0, cInsert = 0;
                foreach (var ln in allLines)
                {
                    var t = ln.Trim();
                    if (t == "0") c0++;
                    else if (t == "TEXT") cText++;
                    else if (t == "MTEXT") cMtext++;
                    else if (t == "INSERT") cInsert++;
                }
                Console.WriteLine($"逐行统计(Trim后): 组码0={c0}, TEXT={cText}, MTEXT={cMtext}, INSERT={cInsert}, 总行数={allLines.Length}");
                var bytesAll = File.ReadAllBytes(dxfPath);
                int nul = 0; foreach (var b in bytesAll) if (b == 0) nul++;
                Console.WriteLine($"NUL字节数={nul} / 总字节={bytesAll.Length}");

                Console.WriteLine("\n" + new string('=', 80));
                Console.WriteLine("✅ 测试完成！");
                Console.WriteLine(new string('=', 80));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ 错误: {ex.Message}");
                Console.WriteLine($"{ex.StackTrace}");
            }
        }

        static string repr(string s)
        {
            if (s == null) return "null";
            return s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
