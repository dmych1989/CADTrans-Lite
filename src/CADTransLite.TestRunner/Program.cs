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

            string dxfPath = @"e:\CADTrans Lite\英文版汉韬尼日利亚104x13.4x4蛋鸡舍方案图及土建图.dxf";
            string outputPath = @"e:\CADTrans Lite\test_extraction_output.xlsx";

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
                var (mergedItems, rawCount) = extractor.ExtractAndMerge(dxfPath);
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
