# CADTrans Lite — 开发文档

> **版本**: v3.0-dev  
> **更新**: 
> - 🚧 v3.0：基于 DocuTranslate-for-engineer 对比分析，新增文本清洗/过滤、Unicode 字体管理、MTEXT 回写增强
> - ✅ 新增「保存所有设置」按钮（翻译状态区域下方）
> - ✅ 保留各翻译引擎的「测试XXX连接」按钮
> - ✅ 多行文本处理默认改为「作为整体提取」(ImportMTextWhole=true)
> - ✅ 「一键翻译」和「导入回填」按钮默认可用（随时可点击）
> - ✅ 支持独立运行：无任务时点击按钮可自定义选择文件
> - ✅ 翻译后自动导出翻译好的 Excel 文件
> - ✅ 修复 DeepLX 测试接口错误提示
> - ✅ 修复回填后 CAD 文件为空的问题
> **核心定位**: 一键提取 CAD (.dwg/.dxf) 文件中的文字 → 导出为两列 Excel（原文 / 译文）→ 翻译后回填替换  
> **参考产品**: 轻语 CAD Translator  
> **测试文件**: `E:\CADTrans Lite\英文版汉韬尼日利亚104x13.4x4蛋鸡舍方案图及土建图.dwg/.dxf`

---

## 1. 产品概述

### 1.1 功能目标

| 功能 | 说明 |
|------|------|
| 文字提取 | 扫描 DWG/DXF 文件中所有文字对象，提取文本内容 |
| 导出 Excel | 生成两列 Excel：原文 | 译文（无 ID 列，无单元格保护） |
| 翻译回填 | 读取翻译后的 Excel，将译文写回 DWG/DXF 文件并另存 |
| 语言支持 | 中文、英文、俄语、西班牙语、葡萄牙语、阿拉伯语（主）+ 法语、越南语、韩语、日语、意大利语、德语、泰语 |

### 1.2 核心设计原则

- **合并相同文本**：相同文本只占一行，id 列记录所有对应的 Handle（逗号分隔）
- **两列结构**：原文 | 译文（ID 列不在 Excel 中显示，Handle 信息内部保留用于回填匹配）
- **无单元格保护**：所有单元格均可自由编辑，方便翻译工作
- **格式码保留**：MText 格式码（如 `\P`）在导出时保留，回填时还原
- **非破坏性**：回填时生成新文件（`_translated` 后缀），不覆盖原始文件
- **灵活工作流**：支持独立操作，无需按顺序执行（v2.7 新增）

### 1.3 用户工作流（v2.7 更新）

```
┌──────────────┐     ┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│  拖入 CAD 文件 │ ──▶ │  提取并导出   │ ──▶ │  翻译 Excel   │ ──▶ │  导入并回填   │
│  (.dwg/.dxf)  │     │  Excel(2列)  │     │  (B列填译文)  │     │  → 新文件    │
└──────────────┘     └──────────────┘     └──────────────┘     └──────────────┘

【v2.7 新增】独立操作模式：
- 「一键翻译」按钮随时可用：点击后选择任意 Excel 文件进行翻译
- 「导入回填」按钮随时可用：点击后选择 Excel 文件和 CAD 文件进行回填
- 翻译完成后自动导出翻译好的 Excel 文件到源文件目录
```

### 1.4 Excel 格式规范（参考）

基于 `20260526090802_en-zh_all.xlsx` 分析：

| 列 | 表头 | 说明 | 示例 |
|----|------|------|------|
| A | 原文 | 原始文本（保留 `\P` 等格式码） | `Side wall insulation panels\PDouble-sided...` |
| B | 译文 | 翻译文本（用户填写） | `侧墙保温板\P双面...` |

**合并策略**：相同文本合并为一行，Handle 信息内部保留（不在 Excel 中显示），回填时通过行号匹配原文。

---

## 2. 技术架构

### 2.1 技术选型

| 模块 | 推荐方案 | 备选方案 |
|------|---------|---------|
| DWG/DXF 读写 | **ODA File Converter + ezdxf/netDxf** | AutoCAD .NET API / Teigha |
| DWG 版本转换 | **ODA File Converter**（免费） | AutoCAD / Teigha |
| Excel 读写 | **EPPlus** (NuGet) | NPOI / ClosedXML |
| UI 框架 | **WPF** (.NET 8) | WinForms / Avalonia |
| 翻译 API | **DeepL / 百度翻译 / 腾讯翻译君** | — |
| 打包分发 | 单文件发布 + 内嵌运行时 | — |

### 2.2 ODA File Converter 集成方案

#### 2.2.1 什么是 ODA File Converter？

**ODA File Converter** 是由 Open Design Alliance (ODA) 开发的**免费 CAD 文件版本转换工具**，主要用于解决不同版本 CAD 文件之间的兼容性问题。

**核心功能**：
- 支持 DWG/DXF 格式互转
- 支持批量转换
- 自动审计修复文件错误
- 数据无损保留
- **无需安装 AutoCAD** 即可独立运行

**下载地址**：https://www.opendesign.com/guestfiles/oda_file_converter

#### 2.2.2 为什么 CAD 翻译需要 ODA File Converter？

| 问题 | 解决方案 |
|------|----------|
| **DWG 版本过高** | 用户发送的是 AutoCAD 2024 版 DWG，但软件只能处理 2018 版 |
| **DWG 版本过低** | 用户发送的是 R14 版 DWG，现代库无法正确解析 |
| **格式兼容性** | 不同 CAD 软件生成的 DWG 文件可能存在格式差异 |
| **文件损坏** | 部分 DWG 文件存在轻微损坏，需要修复 |

**ODA 的作用**：作为"版本桥梁"，将任意版本的 DWG 文件转换为标准格式，确保后续处理流程顺畅。

#### 2.2.3 集成方案（参考轻语CAD）

**轻语CAD Translator 的工作流程**（已验证）：

```
┌──────────────┐     ┌──────────────────────┐     ┌──────────────┐
│  输入 DWG 文件 │ ──▶ │ ODA File Converter    │ ──▶ │  转换为 DXF   │
└──────────────┘     │  (版本转换/修复)       │     └──────────────┘
                     └──────────────────────┘              │
                                                           ▼
┌──────────────┐     ┌──────────────────┐     ┌──────────────────┐
│  回填翻译结果 │ ◀── │ ezdxf 读写 DXF    │ ◀── │ 提取文字内容      │
└──────────────┘     └──────────────────┘     └──────────────────┘
                            │
                            ▼
                     ┌──────────────────┐
                     │ 导出 Excel 翻译   │
                     │ 或调用翻译API    │
                     └──────────────────┘
```

**轻语CAD 的技术栈**（从目录结构分析）：
- Python 3.9 + PySide6 (Qt6)
- **ezdxf** - DXF 文件读写
- **ODA File Converter** - DWG → DXF 转换
- pandas - Excel 处理
- requests - 翻译 API 调用

#### 2.2.4 ODA File Converter 命令行调用

```csharp
public class OdaConverter
{
    private readonly string _odaPath;

    public OdaConverter(string odaPath)
    {
        _odaPath = odaPath; // 例如: "C:\Program Files\ODA\ODAFileConverter.exe"
    }

    /// <summary>
    /// 将 DWG 转换为 DXF
    /// </summary>
    /// <param name="inputDwg">输入 DWG 文件路径</param>
    /// <param name="outputDir">输出目录</param>
    /// <param name="targetVersion">目标版本（默认 ACAD2018）</param>
    public string ConvertDwgToDxf(string inputDwg, string outputDir, string targetVersion = "ACAD2018")
    {
        // ODA File Converter 命令行参数
        // 用法: ODAFileConverter.exe <input_path> <output_path> <version> <audit> <recurse> <input_filter>
        // 参数说明:
        //   input_path   - 输入文件或目录
        //   output_path  - 输出目录
        //   version      - 目标版本: ACAD2000, ACAD2004, ACAD2007, ACAD2010, ACAD2013, ACAD2018
        //   audit        - 是否审计修复: 0=否, 1=是
        //   recurse      - 是否递归处理子目录: 0=否, 1=是
        //   input_filter - 输入文件过滤器: dwg, dxf, 或 *

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _odaPath,
                Arguments = $"\"{Path.GetDirectoryName(inputDwg)}\" \"{outputDir}\" {targetVersion} 1 0 dwg",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new Exception($"ODA File Converter 失败: {error}");
        }

        // 返回生成的 DXF 文件路径
        string dxfFileName = Path.GetFileNameWithoutExtension(inputDwg) + ".dxf";
        return Path.Combine(outputDir, dxfFileName);
    }

    /// <summary>
    /// 将 DXF 转换回 DWG
    /// </summary>
    public string ConvertDxfToDwg(string inputDxf, string outputDir, string targetVersion = "ACAD2018")
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _odaPath,
                Arguments = $"\"{Path.GetDirectoryName(inputDxf)}\" \"{outputDir}\" {targetVersion} 1 0 dxf",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        process.WaitForExit();

        string dwgFileName = Path.GetFileNameWithoutExtension(inputDxf) + ".dwg";
        return Path.Combine(outputDir, dwgFileName);
    }
}
```

#### 2.2.5 完整的 DWG 处理流程

```csharp
public class CadProcessor
{
    private readonly OdaConverter _odaConverter;
    private readonly CadExtractor _extractor;
    private readonly CadWriter _writer;

    public CadProcessor(string odaPath)
    {
        _odaConverter = new OdaConverter(odaPath);
        _extractor = new CadExtractor();
        _writer = new CadWriter();
    }

    /// <summary>
    /// 从 DWG 文件提取文字（自动处理版本兼容）
    /// </summary>
    public TranslationTask ExtractFromDwg(string dwgPath, LanguageInfo sourceLang, LanguageInfo targetLang)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // 1. 使用 ODA 将 DWG 转换为 DXF
            string dxfPath = _odaConverter.ConvertDwgToDxf(dwgPath, tempDir);

            // 2. 使用 ezdxf/netDxf 提取文字
            var task = _extractor.ExtractFromDxf(dxfPath, sourceLang, targetLang);
            task.SourceFilePath = dwgPath; // 记录原始 DWG 路径

            return task;
        }
        finally
        {
            // 清理临时文件（可选）
            // Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// 将翻译结果回填到 DWG 文件
    /// </summary>
    public void WriteBackToDwg(TranslationTask task, string outputDwgPath)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // 1. 先将原始 DWG 转换为 DXF
            string dxfPath = _odaConverter.ConvertDwgToDxf(task.SourceFilePath, tempDir);

            // 2. 在 DXF 中回填翻译
            _writer.WriteBackToDxf(task, dxfPath);

            // 3. 将 DXF 转换回 DWG
            _odaConverter.ConvertDxfToDwg(dxfPath, Path.GetDirectoryName(outputDwgPath));
        }
        finally
        {
            // 清理临时文件
            Directory.Delete(tempDir, true);
        }
    }
}
```

### 2.3 项目结构

```
CADTransLite/
├── src/
│   ├── CADTransLite.sln
│   ├── CADTransLite.Core/              # 核心逻辑层
│   │   ├── Models/
│   │   │   ├── TranslationItem.cs      # 数据模型
│   │   │   └── LanguageInfo.cs         # 语言配置
│   │   ├── Services/
│   │   │   ├── OdaConverter.cs         # ODA File Converter 调用（DWG↔DXF）
│   │   │   ├── CadProcessor.cs         # CAD 文件处理入口
│   │   │   ├── CadExtractor.cs         # CAD 文字提取（DXF）
│   │   │   ├── CadWriter.cs            # CAD 回填写入（DXF）
│   │   │   ├── ExcelHandler.cs         # Excel 导入导出
│   │   │   ├── MTextCodec.cs           # MText 格式码处理
│   │   │   └── TranslationService.cs   # 翻译 API 服务
│   │   └── CADTransLite.Core.csproj
│   ├── CADTransLite.UI/                # 界面层
│   │   ├── MainWindow.xaml
│   │   ├── MainWindowViewModel.cs
│   │   ├── Controls/
│   │   │   └── LanguageSelector.xaml   # 语言选择控件
│   │   └── CADTransLite.UI.csproj
│   └── CADTransLite.Tests/            # 单元测试
│       ├── CadExtractorTests.cs
│       ├── ExcelHandlerTests.cs
│       └── TestFiles/
├── tools/
│   └── ODAFileConverter/              # ODA File Converter（需用户安装）
└── docs/
    └── README.md
```
│           ├── sample.dwg
│           └── sample.dxf
└── docs/
    └── README.md
```

---

## 3. 核心逻辑设计

### 3.1 数据模型

#### 3.1.1 TranslationItem（单条翻译任务）

```csharp
/// <summary>
/// 单条翻译任务项，对应 Excel 中的一行
/// </summary>
public class TranslationItem
{
    /// <summary>CAD 实体的 Handle 列表（相同文本合并时包含多个）</summary>
    public List<string> CadHandles { get; set; } = new();

    /// <summary>Handle 格式化字符串（格式：@_Handle_&f0）</summary>
    public string IdString => string.Join(",", CadHandles.Select(h => $"@_{h}_&f0"));

    /// <summary>CAD 实体类型: DBText / MText / Attribute</summary>
    public string EntityType { get; set; }

    /// <summary>原始文本 (对应 Excel B 列)</summary>
    public string OriginalText { get; set; }

    /// <summary>翻译文本 (对应 Excel C 列)</summary>
    public string TranslatedText { get; set; }

    /// <summary>是否包含 MText 格式码</summary>
    public bool HasFormatCodes { get; set; }

    /// <summary>MText 格式码占位符还原表 (仅 MText 使用)</summary>
    public Dictionary<string, string> FormatPlaceholders { get; set; }
}

/// <summary>
/// 翻译任务集合（支持合并相同文本）
/// </summary>
public class TranslationTask
{
    /// <summary>源文件路径</summary>
    public string SourceFilePath { get; set; }

    /// <summary>源文件类型: DWG / DXF</summary>
    public CadFileType FileType { get; set; }

    /// <summary>源语言</summary>
    public LanguageInfo SourceLanguage { get; set; }

    /// <summary>目标语言</summary>
    public LanguageInfo TargetLanguage { get; set; }

    /// <summary>翻译项列表（已合并相同文本）</summary>
    public List<TranslationItem> Items { get; set; } = new();

    /// <summary>提取时间</summary>
    public DateTime ExtractedAt { get; set; }
}

public enum CadFileType { DWG, DXF }
```

#### 3.1.2 LanguageInfo（语言配置）

```csharp
/// <summary>
/// 支持的语言配置
/// </summary>
public class LanguageInfo
{
    public string Code { get; set; }        // ISO 639-1 代码
    public string Name { get; set; }        // 显示名称
    public string NativeName { get; set; }  // 本地名称
    public bool IsPrimary { get; set; }     // 是否主要支持语言
}

/// <summary>
/// 预定义语言列表
/// </summary>
public static class SupportedLanguages
{
    // 主要支持语言
    public static readonly LanguageInfo Chinese = new() { Code = "zh", Name = "Chinese", NativeName = "中文", IsPrimary = true };
    public static readonly LanguageInfo English = new() { Code = "en", Name = "English", NativeName = "English", IsPrimary = true };
    public static readonly LanguageInfo Russian = new() { Code = "ru", Name = "Russian", NativeName = "Русский", IsPrimary = true };
    public static readonly LanguageInfo Spanish = new() { Code = "es", Name = "Spanish", NativeName = "Español", IsPrimary = true };
    public static readonly LanguageInfo Portuguese = new() { Code = "pt", Name = "Portuguese", NativeName = "Português", IsPrimary = true };
    public static readonly LanguageInfo Arabic = new() { Code = "ar", Name = "Arabic", NativeName = "العربية", IsPrimary = true };

    // 扩展支持语言
    public static readonly LanguageInfo French = new() { Code = "fr", Name = "French", NativeName = "Français", IsPrimary = false };
    public static readonly LanguageInfo Vietnamese = new() { Code = "vi", Name = "Vietnamese", NativeName = "Tiếng Việt", IsPrimary = false };
    public static readonly LanguageInfo Korean = new() { Code = "ko", Name = "Korean", NativeName = "한국어", IsPrimary = false };
    public static readonly LanguageInfo Japanese = new() { Code = "ja", Name = "Japanese", NativeName = "日本語", IsPrimary = false };
    public static readonly LanguageInfo Italian = new() { Code = "it", Name = "Italian", NativeName = "Italiano", IsPrimary = false };
    public static readonly LanguageInfo German = new() { Code = "de", Name = "German", NativeName = "Deutsch", IsPrimary = false };
    public static readonly LanguageInfo Thai = new() { Code = "th", Name = "Thai", NativeName = "ไทย", IsPrimary = false };

    public static List<LanguageInfo> GetAll() => new()
    {
        Chinese, English, Russian, Spanish, Portuguese, Arabic,
        French, Vietnamese, Korean, Japanese, Italian, German, Thai
    };

    public static List<LanguageInfo> GetPrimary() => GetAll().Where(l => l.IsPrimary).ToList();
}
```

### 3.2 文字提取模块 (CadExtractor)

#### 3.2.1 支持的输入格式

| 格式 | 扩展名 | 读写方式 | 说明 |
|------|--------|---------|------|
| DWG | .dwg | AutoCAD .NET API / Teigha | 二进制格式，需 AutoCAD 或 Teigha |
| DXF | .dxf | netDxf / AutoCAD .NET API | 文本格式，可直接解析 |

#### 3.2.2 扫描对象类型

| CAD 对象 | 说明 | 处理要点 |
|----------|------|---------|
| `DBText` | 单行文字 | 直接读取 `TextString` |
| `MText` | 多行文字 | 保留格式码（`\P`, `\L`, `{\F ...}` 等） |
| `Attribute` | 块属性文字 | 需进入 BlockReference 遍历 |
| `Dimension` | 标注文字 | 读取 `TextOverride`（如有） |

#### 3.2.3 MText 格式码保留策略

与之前的"占位符替换"策略不同，参考 Excel 显示，格式码（如 `\P`）应**保留在原文中**，便于用户理解上下文。

**示例**：
```
原始 MText:  "Side wall insulation panels\PDouble-sided 0.35mm"
导出原文:    "Side wall insulation panels\PDouble-sided 0.35mm"（保留 \P）
翻译后:      "侧墙保温板\P双面 0.35mm"（保留 \P）
回填:        "侧墙保温板\P双面 0.35mm"（\P 会被 CAD 解释为换行）
```

**格式码清单**（需保留）：

| 格式码 | 含义 | 示例 |
|--------|------|------|
| `\P` | 换行 | `Line1\PLine2` |
| `\L...\l` | 下划线 | `\Lunderlined text\l` |
| `\O...\o` | 上划线 | `\Ooverlined text\o` |
| `\K...\k` | 删除线 | `\Kstrikethrough\k` |
| `{\F...;...}` | 字体切换 | `{\FArial;text}` |
| `\H...;` | 高度 | `\H2.0x;big text` |
| `\W...;` | 宽度因子 | `\W0.8;narrow` |
| `\C...;` | 颜色 | `\C1;red text` |
| `\S...;` | 堆叠分数 | `\S1/2;` |

#### 3.2.4 提取与合并流程

```csharp
public class CadExtractor
{
    /// <summary>
    /// 从 DWG/DXF 文件提取文字并合并相同文本
    /// </summary>
    public TranslationTask Extract(string filePath, LanguageInfo sourceLang, LanguageInfo targetLang)
    {
        var fileType = Path.GetExtension(filePath).ToLower() == ".dxf" 
            ? CadFileType.DXF 
            : CadFileType.DWG;

        // 1. 提取所有文字对象
        var rawItems = fileType == CadFileType.DXF
            ? ExtractFromDxf(filePath)
            : ExtractFromDwg(filePath);

        // 2. 合并相同文本
        var mergedItems = MergeSameText(rawItems);

        return new TranslationTask
        {
            SourceFilePath = filePath,
            FileType = fileType,
            SourceLanguage = sourceLang,
            TargetLanguage = targetLang,
            Items = mergedItems,
            ExtractedAt = DateTime.Now
        };
    }

    /// <summary>
    /// 合并相同文本的项
    /// </summary>
    private List<TranslationItem> MergeSameText(List<TranslationItem> rawItems)
    {
        return rawItems
            .GroupBy(item => item.OriginalText)
            .Select(group =>
            {
                var first = group.First();
                return new TranslationItem
                {
                    CadHandles = group.SelectMany(g => g.CadHandles).ToList(),
                    EntityType = first.EntityType,
                    OriginalText = first.OriginalText,
                    TranslatedText = first.OriginalText, // 初始 = 原文
                    HasFormatCodes = first.HasFormatCodes,
                    FormatPlaceholders = first.FormatPlaceholders
                };
            })
            .OrderBy(item => item.OriginalText) // 按原文排序，便于查找
            .ToList();
    }

    /// <summary>
    /// 从 DWG 文件提取（使用 AutoCAD .NET API）
    /// </summary>
    private List<TranslationItem> ExtractFromDwg(string dwgPath)
    {
        var items = new List<TranslationItem>();

        using var db = new Database(false, true);
        db.ReadDwgFile(dwgPath, FileOpenMode.OpenForReadAndAllShare, true, null);

        using var tr = db.TransactionManager.StartTransaction();

        // 遍历模型空间
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

        foreach (ObjectId id in modelSpace)
        {
            var entity = tr.GetObject(id, OpenMode.ForRead);

            switch (entity)
            {
                case DBText text when !string.IsNullOrWhiteSpace(text.TextString):
                    items.Add(new TranslationItem
                    {
                        CadHandles = new List<string> { id.Handle.Value.ToString() },
                        EntityType = "DBText",
                        OriginalText = text.TextString,
                        TranslatedText = text.TextString
                    });
                    break;

                case MText mtext when !string.IsNullOrWhiteSpace(mtext.TextString):
                    items.Add(new TranslationItem
                    {
                        CadHandles = new List<string> { id.Handle.Value.ToString() },
                        EntityType = "MText",
                        OriginalText = mtext.TextString, // 保留格式码
                        TranslatedText = mtext.TextString,
                        HasFormatCodes = ContainsFormatCodes(mtext.TextString)
                    });
                    break;

                case BlockReference blk:
                    foreach (ObjectId attrId in blk.AttributeCollection)
                    {
                        var attr = (AttributeReference)tr.GetObject(attrId, OpenMode.ForRead);
                        if (!string.IsNullOrWhiteSpace(attr.TextString))
                        {
                            items.Add(new TranslationItem
                            {
                                CadHandles = new List<string> { attrId.Handle.Value.ToString() },
                                EntityType = "Attribute",
                                OriginalText = attr.TextString,
                                TranslatedText = attr.TextString
                            });
                        }
                    }
                    break;
            }
        }

        return items;
    }

    /// <summary>
    /// 从 DXF 文件提取（使用 netDxf）
    /// </summary>
    private List<TranslationItem> ExtractFromDxf(string dxfPath)
    {
        var items = new List<TranslationItem>();
        var dxf = DxfDocument.Load(dxfPath);

        // 提取 DBText
        foreach (var text in dxf.Entities.Texts)
        {
            if (!string.IsNullOrWhiteSpace(text.Text))
            {
                items.Add(new TranslationItem
                {
                    CadHandles = new List<string> { text.Handle },
                    EntityType = "DBText",
                    OriginalText = text.Text,
                    TranslatedText = text.Text
                });
            }
        }

        // 提取 MText
        foreach (var mtext in dxf.Entities.MTexts)
        {
            if (!string.IsNullOrWhiteSpace(mtext.Text))
            {
                items.Add(new TranslationItem
                {
                    CadHandles = new List<string> { mtext.Handle },
                    EntityType = "MText",
                    OriginalText = mtext.Text,
                    TranslatedText = mtext.Text,
                    HasFormatCodes = ContainsFormatCodes(mtext.Text)
                });
            }
        }

        // 提取块属性
        foreach (var insert in dxf.Entities.Inserts)
        {
            foreach (var attr in insert.Attributes)
            {
                if (!string.IsNullOrWhiteSpace(attr.Text))
                {
                    items.Add(new TranslationItem
                    {
                        CadHandles = new List<string> { attr.Handle },
                        EntityType = "Attribute",
                        OriginalText = attr.Text,
                        TranslatedText = attr.Text
                    });
                }
            }
        }

        return items;
    }

    private bool ContainsFormatCodes(string text)
    {
        return text.Contains("\\P") || text.Contains("\\L") || text.Contains("\\l") ||
               text.Contains("\\O") || text.Contains("\\o") || text.Contains("\\F") ||
               text.Contains("\\H") || text.Contains("\\W") || text.Contains("\\C") ||
               text.Contains("\\S") || text.Contains("{\\");
    }
}
```

### 3.3 Excel 交互模块 (ExcelHandler)

#### 3.3.1 导出 Excel（三列格式）

```csharp
public class ExcelHandler
{
    /// <summary>
    /// 导出翻译任务到 Excel（参考格式）
    /// </summary>
    public void ExportToExcel(TranslationTask task, string outputPath)
    {
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("原文");

        // 表头（两列：原文、译文，无 ID 列）
        sheet.Cells[1, 1].Value = "原文";
        sheet.Cells[1, 2].Value = "译文";

        // 表头样式
        using (var range = sheet.Cells[1, 1, 1, 2])
        {
            range.Style.Font.Bold = true;
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(Color.LightGray);
        }

        // 数据行
        for (int i = 0; i < task.Items.Count; i++)
        {
            int row = i + 2;
            var item = task.Items[i];

            // A列: 原文
            sheet.Cells[row, 1].Value = item.OriginalText;

            // B列: 译文（初始为空，用户填写）
            sheet.Cells[row, 2].Value = string.IsNullOrEmpty(item.TranslatedText) 
                ? null 
                : item.TranslatedText;
        }

        // 列宽设置（A=75, B=75）
        sheet.Column(1).Width = 75;
        sheet.Column(2).Width = 75;

        // B列浅黄色底纹，提示用户编辑此列
        using (var range = sheet.Cells[2, 2, task.Items.Count + 1, 2])
        {
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(Color.LightYellow);
        }

        // 文本自动换行
        sheet.Cells[2, 1, task.Items.Count + 1, 2].Style.WrapText = true;

        // 取消单元格保护（所有单元格可编辑）
        sheet.Cells[1, 1, task.Items.Count + 1, 2].Style.Locked = false;

        package.SaveAs(new FileInfo(outputPath));
    }

    /// <summary>
    /// 从 Excel 导入翻译结果
    /// </summary>
    public (TranslationTask task, string error) ImportFromExcel(
        string excelPath, TranslationTask originalTask)
    {
        using var package = new ExcelPackage(new FileInfo(excelPath));
        var sheet = package.Workbook.Worksheets[0];

        int dataRows = sheet.Dimension?.Rows ?? 0;
        if (dataRows <= 1)
            return (null, "Excel 文件为空或格式错误");

        dataRows -= 1; // 减去表头

        // 校验：行数必须一致
        if (dataRows != originalTask.Items.Count)
        {
            return (null, $"行数不匹配！期望 {originalTask.Items.Count} 行，Excel 中有 {dataRows} 行。\n" +
                          "请勿增删行，只修改「译文」列（B列）。");
        }

        var result = new TranslationTask
        {
            SourceFilePath = originalTask.SourceFilePath,
            FileType = originalTask.FileType,
            SourceLanguage = originalTask.SourceLanguage,
            TargetLanguage = originalTask.TargetLanguage,
            ExtractedAt = originalTask.ExtractedAt,
            Items = new List<TranslationItem>()
        };

        for (int i = 0; i < originalTask.Items.Count; i++)
        {
            int row = i + 2;
            var originalItem = originalTask.Items[i];

            // 读取 B 列翻译文本
            string translated = sheet.Cells[row, 2].Text?.Trim();

            // 校验 A 列是否被篡改
            string original = sheet.Cells[row, 1].Text?.Trim();
            if (original != originalItem.OriginalText)
            {
                return (null, $"第 {row} 行「原文」列被修改，请勿编辑 A 列。\n" +
                              $"期望: \"{originalItem.OriginalText?.Substring(0, Math.Min(50, originalItem.OriginalText?.Length ?? 0))}...\"\n" +
                              $"实际: \"{original?.Substring(0, Math.Min(50, original?.Length ?? 0))}...\"");
            }

            // 跳过空值（保留原文）
            var item = new TranslationItem
            {
                CadHandles = originalItem.CadHandles,
                EntityType = originalItem.EntityType,
                OriginalText = originalItem.OriginalText,
                TranslatedText = string.IsNullOrEmpty(translated) ? originalItem.OriginalText : translated,
                HasFormatCodes = originalItem.HasFormatCodes,
                FormatPlaceholders = originalItem.FormatPlaceholders
            };

            result.Items.Add(item);
        }

        return (result, null);
    }
}
```

### 3.4 翻译回填模块 (CadWriter)

```csharp
public class CadWriter
{
    /// <summary>
    /// 将翻译结果回填到 CAD 文件
    /// </summary>
    public void WriteBack(TranslationTask task, string outputPath)
    {
        // 根据文件类型选择写入方式
        if (task.FileType == CadFileType.DXF)
            WriteBackToDxf(task, outputPath);
        else
            WriteBackToDwg(task, outputPath);
    }

    private void WriteBackToDwg(TranslationTask task, string outputPath)
    {
        // 复制原始文件到新路径
        File.Copy(task.SourceFilePath, outputPath, true);

        using var db = new Database(false, true);
        db.ReadDwgFile(outputPath, FileOpenMode.OpenForReadAndWrite, true, null);

        using var tr = db.TransactionManager.StartTransaction();

        int successCount = 0;
        int skipCount = 0;

        foreach (var item in task.Items)
        {
            // 跳过未翻译的行（原文 = 译文）
            if (item.TranslatedText == item.OriginalText)
            {
                skipCount += item.CadHandles.Count;
                continue;
            }

            // 遍历所有 Handle（相同文本可能对应多个实体）
            foreach (var handleStr in item.CadHandles)
            {
                if (!long.TryParse(handleStr, out long handleValue))
                    continue;

                var handle = new Handle(handleValue);
                var objId = db.GetObjectId(false, handle, 0);

                if (objId.IsNull)
                    continue;

                var entity = tr.GetObject(objId, OpenMode.ForWrite);

                switch (entity)
                {
                    case DBText text:
                        text.TextString = item.TranslatedText;
                        successCount++;
                        break;

                    case MText mtext:
                        mtext.TextString = item.TranslatedText; // 格式码已保留
                        successCount++;
                        break;

                    case AttributeReference attr:
                        attr.TextString = item.TranslatedText;
                        successCount++;
                        break;
                }
            }
        }

        tr.Commit();
        db.SaveAs(outputPath, DwgVersion.Current);

        Log.Info($"回填完成: {successCount} 个实体成功，{skipCount} 个跳过（未翻译）");
    }

    private void WriteBackToDxf(TranslationTask task, string outputPath)
    {
        var dxf = DxfDocument.Load(task.SourceFilePath);

        foreach (var item in task.Items)
        {
            if (item.TranslatedText == item.OriginalText)
                continue;

            foreach (var handleStr in item.CadHandles)
            {
                // 根据 Handle 查找实体并更新
                UpdateEntityByHandle(dxf, handleStr, item.TranslatedText, item.EntityType);
            }
        }

        dxf.Save(outputPath);
    }

    private void UpdateEntityByHandle(DxfDocument dxf, string handle, string newText, string entityType)
    {
        switch (entityType)
        {
            case "DBText":
                var text = dxf.Entities.Texts.FirstOrDefault(t => t.Handle == handle);
                if (text != null) text.Text = newText;
                break;

            case "MText":
                var mtext = dxf.Entities.MTexts.FirstOrDefault(m => m.Handle == handle);
                if (mtext != null) mtext.Text = newText;
                break;

            case "Attribute":
                // 需要遍历所有 Insert 查找属性
                foreach (var insert in dxf.Entities.Inserts)
                {
                    var attr = insert.Attributes.FirstOrDefault(a => a.Handle == handle);
                    if (attr != null)
                    {
                        attr.Text = newText;
                        break;
                    }
                }
                break;
        }
    }
}
```

### 3.5 翻译服务模块 (TranslationService)

```csharp
public interface ITranslationApi
{
    Task<string> TranslateAsync(string text, string sourceLang, string targetLang);
    Task<List<string>> TranslateBatchAsync(List<string> texts, string sourceLang, string targetLang);
}

public class TranslationService
{
    private readonly ITranslationApi _api;

    public TranslationService(ITranslationApi api)
    {
        _api = api;
    }

    /// <summary>
    /// 批量翻译所有项（一键翻译）
    /// </summary>
    public async Task TranslateAllAsync(
        TranslationTask task, 
        IProgress<int> progress = null,
        CancellationToken cancellationToken = default)
    {
        // 批量翻译，每批 50 条（避免 API 限制）
        const int batchSize = 50;
        var batches = task.Items
            .Select((item, index) => (item, index))
            .GroupBy(x => x.index / batchSize)
            .Select(g => g.Select(x => x.item).ToList())
            .ToList();

        int processedCount = 0;

        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var texts = batch.Select(item => item.OriginalText).ToList();
            var translations = await _api.TranslateBatchAsync(
                texts, 
                task.SourceLanguage.Code, 
                task.TargetLanguage.Code);

            for (int i = 0; i < batch.Count; i++)
            {
                batch[i].TranslatedText = translations[i];
            }

            processedCount += batch.Count;
            progress?.Report(processedCount * 100 / task.Items.Count);

            // 避免 API 限流
            await Task.Delay(100, cancellationToken);
        }
    }
}
```

---

## 4. 界面设计

### 4.1 布局方案

```
┌───────────────────────────────────────────────────────────────────────────┐
│  CADTrans Lite                                                      [_][□][×] │
├───────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │                                                                     │    │
│  │   📂 将 .dwg / .dxf 文件拖拽到此处                                 │    │
│  │      或点击选择文件                                                │    │
│  │                                                                     │    │
│  │   E:\CADTrans Lite\英文版汉韬尼日利亚...dwg                       │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                             │
│  ┌─ 语言设置 ───────────────────────────────────────────────────────┐      │
│  │ 源语言: [English ▼]      目标语言: [中文 ▼]                      │      │
│  └────────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
│  ┌─ 设置 ▼ ────────────────────────────────────────────────────────────┐   │
│  │ ODA路径: [D:/Program Files/ODA/...]          [浏览] [🔴 未检测到]  │   │
│  │ 导入设置: [☑代理] [☑块属性] [☑标注] [☑段落] [ ]整体                 │   │
│  │          [ ]冻结图层 [ ]锁定图层 [ ]关闭图层                        │   │
│  │ 导出后缀: [_纯译文_______________] 示例: building_纯译文.dxf      │   │
│  └────────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
│  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐        │
│  │ 📤 提取并导出Excel │  │ 🌐 一键翻译       │  │ 📥 导入并回填     │        │
│  └──────────────────┘  └──────────────────┘  └──────────────────┘        │
│                                                                             │
│  ┌─ 进度 ───────────────────────────────────────────────────────────────┐   │
│  │ ████████████████████████░░░░  80%  翻译中...                      │   │
│  │                                                                      │   │
│  │ 📊 统计: 已提取 96 条文本 (DBText: 32, MText: 50, Attr: 14)       │   │
│  │ 📄 Excel: E:\CADTrans Lite\20260526_trans.xlsx                     │   │
│  └─────────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
├───────────────────────────────────────────────────────────────────────────┤
│  就绪                                                                        │
└───────────────────────────────────────────────────────────────────────────┘
```

### 4.2 语言选择器控件

```xml
<!-- LanguageSelector.xaml -->
<UserControl x:Class="CADTransLite.UI.Controls.LanguageSelector"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>

        <TextBlock Grid.Column="0" Text="源语言:" VerticalAlignment="Center" Margin="0,0,8,0"/>
        <ComboBox Grid.Column="1" ItemsSource="{Binding Languages}" 
                  SelectedItem="{Binding SourceLanguage}"
                  DisplayMemberPath="NativeName" MinWidth="120"/>

        <TextBlock Grid.Column="2" Text="目标语言:" VerticalAlignment="Center" Margin="16,0,8,0"/>
        <ComboBox Grid.Column="3" ItemsSource="{Binding Languages}" 
                  SelectedItem="{Binding TargetLanguage}"
                  DisplayMemberPath="NativeName" MinWidth="120"/>
    </Grid>
</UserControl>
```

### 4.3 MainWindowViewModel

```csharp
public class MainWindowViewModel : ObservableObject
{
    // ── 属性 ──
    [ObservableProperty] private string _cadFilePath;
    [ObservableProperty] private bool _isExtractEnabled;
    [ObservableProperty] private bool _isTranslateEnabled;
    [ObservableProperty] private bool _isImportEnabled;
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private string _statisticsText;

    [ObservableProperty] private LanguageInfo _sourceLanguage = SupportedLanguages.English;
    [ObservableProperty] private LanguageInfo _targetLanguage = SupportedLanguages.Chinese;
    public List<LanguageInfo> Languages => SupportedLanguages.GetAll();

    // ── 自定义设置属性 ──
    [ObservableProperty] private string _odaPath = @"D:\Program Files\ODA\ODAFileConverter 24.11.0\ODAFileConverter.exe";
    [ObservableProperty] private bool _isOdaAvailable;
    [ObservableProperty] private string _odaStatusText = "检测中...";
    [ObservableProperty] private Brush _odaStatusColor = Brushes.Gray;

    // ── 导入设置 ──
    [ObservableProperty] private bool _importProxyObjects = true;       // 代理对象(自动炸开)
    [ObservableProperty] private bool _importBlockAttributes = true;   // 块属性标记
    [ObservableProperty] private bool _importDimensionText = true;    // 标注文字
    [ObservableProperty] private bool _importMTextParagraph = true;    // 多行文本-按段落提取
    [ObservableProperty] private bool _importMTextWhole = false;       // 多行文本-作为整体提取
    [ObservableProperty] private bool _importFrozenLayers = false;     // 冻结图层
    [ObservableProperty] private bool _importLockedLayers = false;     // 锁定图层
    [ObservableProperty] private bool _importOffLayers = false;       // 关闭图层

    // ── 导出设置 ──
    [ObservableProperty] private string _exportSuffix = "_纯译文";    // 导出文件后缀

    // ── 内部状态 ──
    private TranslationTask _currentTask;
    private string _currentExcelPath;

    // ── 服务 ──
    private readonly CadExtractor _extractor = new();
    private readonly CadWriter _writer = new();
    private readonly ExcelHandler _excelHandler = new();
    private readonly TranslationService _translationService;

    // ── 命令 ──
    [RelayCommand] private async Task SelectCadFile() { /* ... */ }
    [RelayCommand] private async Task ExtractAndExport() { /* ... */ }
    [RelayCommand] private async Task OneClickTranslate() { /* ... */ }
    [RelayCommand] private async Task ImportAndWriteBack() { /* ... */ }
    [RelayCommand] private void BrowseOdaPath() { /* ... */ }
    [RelayCommand] private void OpenOdaDownload() { /* ... */ }
}
```

---

## 5. 测试计划

### 5.1 测试文件

| 文件 | 路径 | 说明 |
|------|------|------|
| DWG 测试文件 | `E:\CADTrans Lite\英文版汉韬尼日利亚104x13.4x4蛋鸡舍方案图及土建图.dwg` | 英文图纸，包含 DBText / MText / Attribute |
| DXF 测试文件 | `E:\CADTrans Lite\英文版汉韬尼日利亚104x13.4x4蛋鸡舍方案图及土建图.dxf` | 同一图纸的 DXF 版本 |
| 参考 Excel | `E:\CADTrans Lite\20260526090802_en-zh_all.xlsx` | 参考格式（96行，合并相同文本） |

### 5.2 功能测试用例

| # | 测试场景 | 输入 | 预期结果 |
|---|---------|------|---------|
| T1 | DWG 文件提取 | 英文版...dwg | 生成 Excel，包含 id/原文/译文 三列 |
| T2 | DXF 文件提取 | 英文版...dxf | 生成 Excel，结构与 DWG 一致 |
| T3 | 合并相同文本 | 重复文本的 DWG | 相同文本合并为一行，id 列包含多个 Handle |
| T4 | MText 格式码保留 | 含 `\P` 的 MText | 原文保留 `\P`，翻译后回填仍然正确显示换行 |
| T5 | 空译文行跳过 | Excel C 列部分为空 | 回填时跳过空行，保留原文 |
| T6 | 一键翻译 | 提取后点击翻译 | 调用翻译 API，填充 C 列 |
| T7 | 语言切换 | 源=英文，目标=俄语 | 翻译结果为俄语 |
| T8 | Excel 校验 - 行数不匹配 | 增删行后导入 | 弹窗报错，拒绝回填 |
| T9 | Excel 校验 - B 列被篡改 | 修改 B 列后导入 | 弹窗报错，拒绝回填 |
| T10 | 大文件处理 | 10000+ 文字对象 | 显示进度条，可取消 |

### 5.3 边界测试

| # | 测试场景 | 预期结果 |
|---|---------|---------|
| E1 | 空 DWG（无文字对象） | 提示"未找到可提取的文字" |
| E2 | 纯数字/符号文本 | 仍被提取，但不建议翻译 |
| E3 | 超长文本（>1000字符） | 正常提取和翻译 |
| E4 | 特殊字符（®、™、©） | 正确保留 |
| E5 | RTL 语言（阿拉伯语） | 翻译和回填正确处理 |

---

## 6. 开发难点与解决方案

### 6.1 难点清单

| # | 难点 | 风险等级 | 解决方案 |
|---|------|---------|---------|
| 1 | 相同文本合并后 id 列格式 | 🟡 中 | 使用 `@_Handle_&f0` 格式，逗号分隔多个 Handle |
| 2 | MText 格式码在翻译中丢失 | 🔴 高 | 不替换占位符，保留原格式码，用户翻译时保持 |
| 3 | DXF 文件 Handle 格式不同 | 🟡 中 | 使用 netDxf 库，Handle 为字符串类型 |
| 4 | 翻译 API 限流 | 🟡 中 | 批量请求 + 延迟 + 进度显示 |
| 5 | 阿拉伯语 RTL 显示 | 🟡 中 | UI 使用 FlowDirection="RightToLeft" |
| 6 | Handle 查找失败 | 🟢 低 | 记录日志，跳过该条，继续回填 |

### 6.2 id 格式说明

参考 Excel 中的 id 格式：`@_Handle_&f0`

```csharp
// 生成 id 字符串
public string GenerateIdString(List<string> handles)
{
    return string.Join(",", handles.Select(h => $"@_{h}_&f0"));
}

// 解析 id 字符串
public List<string> ParseIdString(string idString)
{
    if (string.IsNullOrWhiteSpace(idString))
        return new List<string>();

    return idString.Split(',')
        .Select(id => id.Trim())
        .Where(id => id.StartsWith("@_") && id.EndsWith("_&f0"))
        .Select(id => id.Substring(2, id.Length - 6)) // 去掉 @_ 和 _&f0
        .ToList();
}
```

---

## 7. 发布与打包

### 7.1 发布配置

```xml
<PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <ApplicationIcon>Assets\app.ico</ApplicationIcon>
    <AssemblyName>CADTransLite</AssemblyName>
</PropertyGroup>
```

### 7.2 依赖项

| NuGet 包 | 用途 | 版本 |
|----------|------|------|
| EPPlus | Excel 读写 | ≥ 7.0 |
| netDxf | DXF 文件读写 | ≥ 2.5 |
| AutoCAD.NET | DWG 文件读写（可选） | — |
| CommunityToolkit.Mvvm | MVVM 框架 | ≥ 8.0 |

### 7.3 无 AutoCAD 运行方案

如果目标机器未安装 AutoCAD，可使用以下替代方案：

| 方案 | 说明 | 许可证 |
|------|------|--------|
| **Teigha / OpenDesign** | 商业库，支持 DWG/DXF 读写 | 商业许可 |
| **netDxf** | 开源库，仅支持 DXF | MIT |
| **DXF 解析** | 直接解析 DXF 文本格式 | 自行实现 |

---

## 8. 版本规划

| 版本 | 功能 |
|------|------|
| **v1.0** | 核心：DWG/DXF 提取 + 三列 Excel 导出/导入 + 回填 |
| **v1.1** | 优化：MText 格式码保留 + 合并相同文本 |
| **v2.0** | 扩展：集成翻译 API 一键翻译 + 多语言支持 |
| **v2.1** | 扩展：批量文件处理 + 进度显示 |
| **v2.2** | 新增：自定义设置（ODA路径/导入设置/导出后缀） |
| **v2.3** | 新增：AI模型自定义设置（API Key/Base URL/模型名称）+ 百度翻译API |
| **v3.0** | 扩展：布局空间文字提取 + 标注文字支持 |

---

## 9. 附录

### 9.1 ODA File Converter 详细说明

#### 9.1.1 软件简介

**ODA File Converter** 是 Open Design Alliance (ODA) 提供的**免费命令行工具**，用于 CAD 文件格式转换和版本兼容处理。

**官方网站**: https://www.opendesign.com/guestfiles/oda_file_converter

**主要功能**:
- DWG ↔ DXF 格式互转
- 支持的 CAD 版本: R12 ~ AutoCAD 2024
- 批量文件转换
- 自动审计修复 (AUDIT)
- **无需安装 AutoCAD** 即可独立运行

#### 9.1.2 安装与配置

**安装步骤**:
1. 从官网下载 ODA File Converter（免费，需注册 ODA 账号）
2. 运行安装程序，选择安装目录（如 `C:\Program Files\ODA\ODAFileConverter`）
3. 验证安装: 运行 `ODAFileConverter.exe --help`

**轻语CAD 的配置**（参考）:
- 安装目录: 用户自定义（建议默认路径）
- 教程视频: `轻语CAD Translator\tutorials\ODA File Converter安装与配置.mp4`

#### 9.1.3 命令行参数详解

```bash
ODAFileConverter.exe <input_path> <output_path> <version> <audit> <recurse> <input_filter>
```

| 参数 | 说明 | 示例值 |
|------|------|--------|
| `input_path` | 输入文件或目录 | `E:\CADTrans Lite\` |
| `output_path` | 输出目录 | `E:\CADTrans Lite\converted\` |
| `version` | 目标 CAD 版本 | `ACAD2018` |
| `audit` | 是否审计修复 | `1` (是) / `0` (否) |
| `recurse` | 是否递归子目录 | `1` (是) / `0` (否) |
| `input_filter` | 输入文件类型 | `dwg` / `dxf` / `*` |

**支持的 CAD 版本**:
| 版本参数 | AutoCAD 版本 |
|----------|-------------|
| `ACAD2000` | AutoCAD 2000 |
| `ACAD2004` | AutoCAD 2004 |
| `ACAD2007` | AutoCAD 2007 |
| `ACAD2010` | AutoCAD 2010 |
| `ACAD2013` | AutoCAD 2013 |
| `ACAD2018` | AutoCAD 2018 |
| `ACAD2021` | AutoCAD 2021 |
| `ACAD2024` | AutoCAD 2024 |

#### 9.1.4 使用示例

**DWG → DXF 转换**:
```bash
# 转换单个文件
ODAFileConverter.exe "E:\drawings\" "E:\converted\" ACAD2018 1 0 dwg

# 批量转换目录下所有 DWG
ODAFileConverter.exe "E:\drawings\" "E:\converted\" ACAD2018 1 1 dwg
```

**DXF → DWG 转换**:
```bash
ODAFileConverter.exe "E:\converted\" "E:\final\" ACAD2018 1 0 dxf
```

#### 9.1.5 在 CADTrans Lite 中的集成方案

#### 9.1.5.1 自定义 ODA File Converter 路径设置

```csharp
/// <summary>
/// ODA File Converter 路径配置
/// </summary>
public class OdaSettings
{
    /// <summary>
    /// ODA File Converter 可执行文件路径（默认安装路径）
    /// </summary>
    public string ExecutablePath { get; set; } = 
        @"D:\Program Files\ODA\ODAFileConverter 24.11.0\ODAFileConverter.exe";

    /// <summary>
    /// 默认输出 CAD 版本
    /// </summary>
    public string DefaultVersion { get; set; } = "ACAD2018";

    /// <summary>
    /// 是否启用审计修复
    /// </summary>
    public bool EnableAudit { get; set; } = true;
}
```

**UI 设计**:
```xml
<!-- 设置面板 - ODA路径设置 -->
<StackPanel Margin="0,8">
    <TextBlock Text="ODA File Converter 路径:" FontWeight="Bold" Margin="0,0,0,4"/>
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>
        <TextBox Grid.Column="0" Text="{Binding OdaPath}" 
                 ToolTip="ODA File Converter 可执行文件路径"/>
        <Button Grid.Column="1" Content="浏览..." Command="{Binding BrowseOdaPathCommand}" 
                Margin="8,0,0,0" Padding="12,4"/>
    </Grid>
    <TextBlock Text="提示: 默认路径 D:/Program Files/ODA/ODAFileConverter 24.11.0/ODAFileConverter.exe"
               Foreground="Gray" FontSize="11" Margin="0,4,0,0"/>
    
    <!-- ODA状态显示 -->
    <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
        <Ellipse Width="12" Height="12" Fill="{Binding OdaStatusColor}"/>
        <TextBlock Text="{Binding OdaStatusText}" Margin="8,0,0,0" VerticalAlignment="Center"/>
        <Button Content="下载 ODA" Command="{Binding OpenOdaDownloadCommand}" 
                Visibility="{Binding IsOdaAvailable, Converter={StaticResource InverseBoolToVisConverter}}"
                Margin="16,0,0,0" Padding="8,2"/>
    </StackPanel>
</StackPanel>
```

#### 9.1.5.2 ViewModel 实现

```csharp
public class MainWindowViewModel : ObservableObject
{
    // ODA 设置
    [ObservableProperty] private string _odaPath = @"D:\Program Files\ODA\ODAFileConverter 24.11.0\ODAFileConverter.exe";
    [ObservableProperty] private bool _isOdaAvailable;
    [ObservableProperty] private string _odaStatusText = "检测 ODA 状态...";
    [ObservableProperty] private Brush _odaStatusColor = Brushes.Gray;

    public ICommand BrowseOdaPathCommand { get; }
    public ICommand OpenOdaDownloadCommand { get; }

    public MainWindowViewModel()
    {
        BrowseOdaPathCommand = new RelayCommand(BrowseOdaPath);
        OpenOdaDownloadCommand = new RelayCommand(OpenOdaDownload);
        
        // 窗口加载时检测 ODA 状态
        Loaded += async (s, e) => await CheckOdaStatusAsync();
    }

    private void BrowseOdaPath()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "ODA File Converter (ODAFileConverter.exe)|ODAFileConverter.exe",
            Title = "选择 ODA File Converter 可执行文件",
            FileName = "ODAFileConverter.exe"
        };

        if (dialog.ShowDialog() == true)
        {
            OdaPath = dialog.FileName;
            _ = CheckOdaStatusAsync();
        }
    }

    private async Task CheckOdaStatusAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(OdaPath) || !File.Exists(OdaPath))
            {
                IsOdaAvailable = false;
                OdaStatusText = "未检测到 ODA File Converter";
                OdaStatusColor = Brushes.Red;
                return;
            }

            // 验证 ODA 可执行性（简单测试）
            var testResult = await OdaConverter.TestConnectionAsync(OdaPath);
            if (testResult)
            {
                IsOdaAvailable = true;
                OdaStatusText = $"ODA 已就绪 ({Path.GetDirectoryName(OdaPath)})";
                OdaStatusColor = Brushes.Green;
            }
            else
            {
                IsOdaAvailable = false;
                OdaStatusText = "ODA File Converter 无法正常运行";
                OdaStatusColor = Brushes.Orange;
            }
        }
        catch (Exception ex)
        {
            IsOdaAvailable = false;
            OdaStatusText = $"ODA 检测失败: {ex.Message}";
            OdaStatusColor = Brushes.Red;
        }
    }

    private void OpenOdaDownload()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://www.opendesign.com/guestfiles/oda_file_converter",
            UseShellExecute = true
        });
    }
}
```

**错误处理**:
```csharp
try
{
    string dxfPath = odaConverter.ConvertDwgToDxf(dwgPath, tempDir);
}
catch (Exception ex)
{
    // 可能的错误:
    // 1. ODA 未安装或路径错误
    // 2. DWG 文件损坏严重，无法转换
    // 3. 磁盘空间不足
    // 4. 权限不足
    
    Logger.Error($"ODA 转换失败: {ex.Message}");
    
    // 提示用户安装 ODA
    if (ex.Message.Contains("找不到") || ex.Message.Contains("not found"))
    {
        ShowMessage("请先安装 ODA File Converter\n下载地址: https://www.opendesign.com/guestfiles/oda_file_converter");
    }
}
```

#### 9.1.6 为什么选择 ODA File Converter？

| 对比项 | ODA File Converter | AutoCAD API |
|--------|-------------------|-------------|
| **许可证** | 免费（需注册） | 需购买 AutoCAD |
| **部署要求** | 无需 AutoCAD | 需安装 AutoCAD |
| **版本兼容** | 支持 R12~2024 | 仅支持安装版本 |
| **批量处理** | 原生支持 | 需自行实现 |
| **审计修复** | 内置 AUDIT 功能 | 需调用 AUDIT 命令 |
| **跨平台** | Windows / Linux / macOS | Windows only |

### 9.2 自定义导入设置功能

#### 9.2.1 导入设置概述

用户可自定义导入 CAD 文件时的提取选项，确保只提取需要翻译的内容。

#### 9.2.2 设置项说明

| 设置项 | 默认值 | 说明 |
|--------|--------|------|
| **代理对象** | ✅ 启用 | 自动炸开代理对象，提取其中文字 |
| **块属性标记** | ✅ 启用 | 提取块属性中的属性值 |
| **提示和值** | ✅ 启用 | 提取标注中的提示文字和测量值 |
| **标注** | ✅ 启用 | 提取尺寸标注中的文字内容 |
| **多行文本-按段落提取** | ✅ 启用 | 多行文本按段落分割，每段单独提取 |
| **多行文本-作为整体** | ❌ 禁用 | 多行文本作为整体提取 |
| **冻结图层** | ❌ 禁用 | 是否提取冻结图层中的文字 |
| **锁定图层** | ❌ 禁用 | 是否提取锁定图层中的文字 |
| **关闭图层** | ❌ 禁用 | 是否提取关闭图层中的文字 |

#### 9.2.3 UI 设计

```xml
<!-- 设置面板 - 导入设置 -->
<Expander Header="导入设置" IsExpanded="False" Margin="0,8,0,0">
    <StackPanel Margin="8">
        <!-- 元素类型 -->
        <TextBlock Text="提取元素类型:" FontWeight="Bold" Margin="0,0,0,8"/>
        <WrapPanel>
            <CheckBox Content="代理对象(自动炸开)" IsChecked="{Binding ImportProxyObjects}" Margin="0,4,16,4"/>
            <CheckBox Content="块属性标记" IsChecked="{Binding ImportBlockAttributes}" Margin="0,4,16,4"/>
            <CheckBox Content="提示和值" IsChecked="{Binding ImportDimensionText}" Margin="0,4,16,4"/>
            <CheckBox Content="标注" IsChecked="{Binding ImportDimension}" Margin="0,4,16,4"/>
        </WrapPanel>

        <!-- 多行文本处理 -->
        <TextBlock Text="多行文本处理:" FontWeight="Bold" Margin="0,12,0,8"/>
        <StackPanel Orientation="Horizontal">
            <RadioButton Content="按段落提取" IsChecked="{Binding ImportMTextParagraph}" 
                        GroupName="MTextMode" Margin="0,4,16,4"/>
            <RadioButton Content="作为整体提取" IsChecked="{Binding ImportMTextWhole}" 
                        GroupName="MTextMode" Margin="0,4,16,4"/>
        </StackPanel>

        <!-- 特殊图层 -->
        <TextBlock Text="特殊图层处理:" FontWeight="Bold" Margin="0,12,0,8"/>
        <WrapPanel>
            <CheckBox Content="冻结图层" IsChecked="{Binding ImportFrozenLayers}" Margin="0,4,16,4">
                <CheckBox.ToolTip>启用后将提取冻结图层中的文字</CheckBox.ToolTip>
            </CheckBox>
            <CheckBox Content="锁定图层" IsChecked="{Binding ImportLockedLayers}" Margin="0,4,16,4">
                <CheckBox.ToolTip>启用后将提取锁定图层中的文字</CheckBox.ToolTip>
            </CheckBox>
            <CheckBox Content="关闭图层" IsChecked="{Binding ImportOffLayers}" Margin="0,4,16,4">
                <CheckBox.ToolTip>启用后将提取关闭图层中的文字</CheckBox.ToolTip>
            </CheckBox>
        </WrapPanel>
    </StackPanel>
</Expander>
```

#### 9.2.4 CadExtractor 集成

```csharp
/// <summary>
/// 导入设置配置
/// </summary>
public class ImportSettings
{
    public bool ImportProxyObjects { get; set; } = true;
    public bool ImportBlockAttributes { get; set; } = true;
    public bool ImportDimensionText { get; set; } = true;
    public bool ImportMTextParagraph { get; set; } = true;
    public bool ImportMTextWhole { get; set; } = false;
    public bool ImportFrozenLayers { get; set; } = false;
    public bool ImportLockedLayers { get; set; } = false;
    public bool ImportOffLayers { get; set; } = false;
}

public class CadExtractor
{
    private ImportSettings _importSettings = new();

    public void SetImportSettings(ImportSettings settings)
    {
        _importSettings = settings;
    }

    /// <summary>
    /// 提取文字（根据导入设置过滤）
    /// </summary>
    private List<TranslationItem> ExtractWithSettings(string filePath)
    {
        var items = new List<TranslationItem>();

        foreach (Entity entity in modelSpace)
        {
            // 检查图层是否可见
            if (!IsLayerVisible(entity.Layer)) continue;

            switch (entity)
            {
                case DBText text when !string.IsNullOrWhiteSpace(text.TextString):
                    items.Add(CreateItem(text));
                    break;

                case MText mtext when !string.IsNullOrWhiteSpace(mtext.TextString):
                    items.AddRange(ProcessMText(mtext));
                    break;

                case BlockReference blk when _importSettings.ImportBlockAttributes:
                    items.AddRange(ExtractBlockAttributes(blk));
                    break;

                case Dimension dim when _importSettings.ImportDimensionText:
                    items.AddRange(ExtractDimensionText(dim));
                    break;

                case ProxyEntity proxy when _importSettings.ImportProxyObjects:
                    items.AddRange(ExplodeAndExtract(proxy));
                    break;
            }
        }

        return items;
    }

    /// <summary>
    /// 检查图层是否可见
    /// </summary>
    private bool IsLayerVisible(string layerName)
    {
        var layer = GetLayer(layerName);
        if (layer == null) return true;

        // 检查冻结状态
        if (!layer.IsFrozen && !_importSettings.ImportFrozenLayers) return false;
        
        // 检查锁定状态
        if (layer.IsLocked && !_importSettings.ImportLockedLayers) return false;

        // 检查关闭状态
        if (!layer.IsVisible && !_importSettings.ImportOffLayers) return false;

        return true;
    }

    /// <summary>
    /// 处理多行文本
    /// </summary>
    private List<TranslationItem> ProcessMText(MText mtext)
    {
        if (_importSettings.ImportMTextWhole)
        {
            // 作为整体提取
            return new List<TranslationItem> { CreateItem(mtext) };
        }
        else
        {
            // 按段落提取（按 \P 分割）
            var paragraphs = mtext.Text.Split(new[] { "\\P" }, StringSplitOptions.None);
            return paragraphs
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => new TranslationItem
                {
                    CadHandles = new List<string> { mtext.Handle },
                    EntityType = "MText.Paragraph",
                    OriginalText = p,
                    TranslatedText = p
                })
                .ToList();
        }
    }
}
```

### 9.3 导出文件后缀设置功能

#### 9.3.1 功能说明

用户可自定义导出文件的名称后缀，默认为 `_纯译文`。

#### 9.3.2 UI 设计

```xml
<!-- 设置面板 - 导出后缀 -->
<StackPanel Margin="0,8,0,0">
    <TextBlock Text="导出文件后缀:" FontWeight="Bold" Margin="0,0,0,4"/>
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>
        <TextBox Grid.Column="0" Text="{Binding ExportSuffix, UpdateSourceTrigger=PropertyChanged}"
                 MaxLength="50" Width="200" HorizontalAlignment="Left"/>
        <TextBlock Grid.Column="1" Margin="8,0,0,0" VerticalAlignment="Center">
            <Run Text="示例: "/>
            <Run Text="{Binding CadFileName}" FontWeight="Bold"/>
            <Run Text="{Binding ExportSuffix}"/>
            <Run Text=".dxf" Foreground="Gray"/>
        </TextBlock>
    </Grid>
    <TextBlock Text="提示: 后缀将添加到文件名中，用于区分原文件和翻译文件"
               Foreground="Gray" FontSize="11" Margin="0,4,0,0"/>
</StackPanel>
```

#### 9.3.3 后缀应用逻辑

```csharp
/// <summary>
/// 生成导出文件路径
/// </summary>
public string GenerateOutputPath(string sourcePath, string suffix)
{
    var directory = Path.GetDirectoryName(sourcePath);
    var fileName = Path.GetFileNameWithoutExtension(sourcePath);
    var extension = Path.GetExtension(sourcePath);

    // 添加后缀
    var outputFileName = $"{fileName}{suffix}{extension}";
    
    return Path.Combine(directory, outputFileName);
}

/// <summary>
/// 示例：
/// 源文件: E:\CAD\building.dxf
/// 后缀:   _纯译文
/// 输出:   E:\CAD\building_纯译文.dxf
/// 
/// 源文件: E:\CAD\building.dwg
/// 后缀:   _翻译后
/// 输出:   E:\CAD\building_翻译后.dwg
/// </summary>

// 在 MainWindowViewModel 中使用
private void ApplySuffixToOutput()
{
    if (string.IsNullOrWhiteSpace(ExportSuffix))
    {
        ExportSuffix = "_纯译文"; // 默认值
    }
    var outputPath = GenerateOutputPath(CurrentTask.SourceFilePath, ExportSuffix);
    // 使用 outputPath 进行导出
}
```

### 9.4 AI翻译API设置功能

#### 9.4.1 功能说明

用户可选择使用自定义AI模型或百度翻译API进行文档翻译。

#### 9.4.2 自定义AI模型设置

| 设置项 | 默认值 | 说明 |
|--------|--------|------|
| **启用自定义模型** | ❌ 禁用 | 是否使用自定义AI模型 |
| **API Key** | 空 | AI模型的API密钥 |
| **Base URL** | `https://api.openai.com/v1` | API服务地址 |
| **模型名称** | `gpt-4o-mini` | 调用的模型名称 |

#### 9.4.3 百度翻译API设置

| 设置项 | 默认值 | 说明 |
|--------|--------|------|
| **启用百度翻译** | ❌ 禁用 | 是否使用百度翻译API |
| **App ID** | 空 | 百度翻译开放平台的App ID |
| **App Key** | 空 | 百度翻译开放平台的App Key |

**申请地址**: https://fanyi-api.baidu.com/

#### 9.4.4 UI 设计

```xml
<!-- 设置面板 - 翻译API设置 -->
<Expander Header="翻译API设置" IsExpanded="True" Margin="0,8,0,0">
    <StackPanel Margin="8">
        <!-- 自定义AI模型 -->
        <GroupBox Header="自定义AI模型" Margin="0,0,0,8">
            <StackPanel Margin="8">
                <CheckBox Content="启用自定义AI模型" 
                          IsChecked="{Binding EnableCustomAI}" 
                          Margin="0,0,0,8"/>
                
                <TextBlock Text="API Key:" Margin="0,0,0,4"/>
                <PasswordBox x:Name="ApiKeyBox" 
                            Password="{Binding ApiKey, Mode=TwoWay}"
                            IsEnabled="{Binding EnableCustomAI}"
                            Margin="0,0,0,8"/>
                
                <TextBlock Text="Base URL:" Margin="0,0,0,4"/>
                <TextBox Text="{Binding BaseUrl}"
                         IsEnabled="{Binding EnableCustomAI}"
                         ToolTip="例如: https://api.openai.com/v1 或自定义兼容API"
                         Margin="0,0,0,8"/>
                
                <TextBlock Text="模型名称:" Margin="0,0,0,4"/>
                <ComboBox IsEditable="True" 
                         Text="{Binding ModelName}"
                         IsEnabled="{Binding EnableCustomAI}"
                         Margin="0,0,0,4">
                    <ComboBoxItem Content="gpt-4o-mini"/>
                    <ComboBoxItem Content="gpt-4o"/>
                    <ComboBoxItem Content="claude-3-haiku"/>
                    <ComboBoxItem Content="deepseek-chat"/>
                </ComboBox>
                <TextBlock Text="提示: 支持OpenAI兼容API格式"
                           Foreground="Gray" FontSize="11"/>
            </StackPanel>
        </GroupBox>

        <!-- 百度翻译API -->
        <GroupBox Header="百度翻译API">
            <StackPanel Margin="8">
                <CheckBox Content="启用百度翻译API" 
                          IsChecked="{Binding EnableBaiduTranslate}"
                          Margin="0,0,0,8"/>
                
                <TextBlock Text="App ID:" Margin="0,0,0,4"/>
                <TextBox Text="{Binding BaiduAppId}"
                         IsEnabled="{Binding EnableBaiduTranslate}"
                         Margin="0,0,0,8"/>
                
                <TextBlock Text="App Key:" Margin="0,0,0,4"/>
                <PasswordBox Password="{Binding BaiduAppKey, Mode=TwoWay}"
                            IsEnabled="{Binding EnableBaiduTranslate}"
                            Margin="0,0,0,8"/>
                
                <HyperlinkButton Content="申请百度翻译API →" 
                                 NavigateUri="https://fanyi-api.baidu.com/"
                                 HorizontalAlignment="Left"/>
            </StackPanel>
        </GroupBox>

        <!-- 当前使用的翻译方式 -->
        <Border Background="#FFF3E0" CornerRadius="4" Padding="8" Margin="0,8,0,0">
            <StackPanel Orientation="Horizontal">
                <TextBlock Text="当前翻译方式: " FontWeight="Bold"/>
                <TextBlock Text="{Binding CurrentTranslationMethod}">
                    <TextBlock.Style>
                        <Style TargetType="TextBlock">
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding EnableCustomAI}" Value="True">
                                    <Setter Property="Text" Value="{Binding ModelName}"/>
                                </DataTrigger>
                                <DataTrigger Binding="{Binding EnableBaiduTranslate}" Value="True">
                                    <Setter Property="Text" Value="百度翻译API"/>
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </TextBlock.Style>
                </TextBlock>
            </StackPanel>
        </Border>
    </StackPanel>
</Expander>
```

#### 9.4.5 后端实现

```csharp
/// <summary>
/// 翻译API配置
/// </summary>
public class TranslationApiSettings
{
    // 自定义AI模型设置
    public bool EnableCustomAI { get; set; } = false;
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ModelName { get; set; } = "gpt-4o-mini";

    // 百度翻译设置
    public bool EnableBaiduTranslate { get; set; } = false;
    public string BaiduAppId { get; set; } = string.Empty;
    public string BaiduAppKey { get; set; } = string.Empty;
}

/// <summary>
/// 自定义AI翻译服务（OpenAI兼容格式）
/// </summary>
public class CustomAiTranslationService : ITranslationApi
{
    private readonly TranslationApiSettings _settings;
    private readonly HttpClient _httpClient;

    public CustomAiTranslationService(TranslationApiSettings settings)
    {
        _settings = settings;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", 
            $"Bearer {_settings.ApiKey}");
    }

    public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang)
    {
        var prompt = $"Translate the following text from {sourceLang} to {targetLang}. " +
                     "Only return the translated text, no explanations:\n\n{text}";

        var requestBody = new
        {
            model = _settings.ModelName,
            messages = new[] { new { role = "user", content = prompt } },
            temperature = 0.3
        };

        var content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync(
            $"{_settings.BaseUrl}/chat/completions",
            content);

        var responseBody = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OpenAiResponse>(responseBody);

        return result?.Choices?[0]?.Message?.Content ?? text;
    }

    public async Task<List<string>> TranslateBatchAsync(
        List<string> texts, string sourceLang, string targetLang)
    {
        var results = new List<string>();
        foreach (var text in texts)
        {
            var translated = await TranslateAsync(text, sourceLang, targetLang);
            results.Add(translated);
            await Task.Delay(100); // 避免限流
        }
        return results;
    }
}

/// <summary>
/// 百度翻译服务
/// </summary>
public class BaiduTranslateService : ITranslationApi
{
    private readonly TranslationApiSettings _settings;
    private static readonly HttpClient _httpClient = new();

    public BaiduTranslateService(TranslationApiSettings settings)
    {
        _settings = settings;
    }

    public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang)
    {
        var salt = DateTime.Now.Ticks.ToString();
        var signStr = $"{_settings.BaiduAppId}{text}{salt}{_settings.BaiduAppKey}";
        var sign = GetMd5Hash(signStr);

        var url = $"https://fanyi-api.baidu.com/api/trans/vip/translate?" +
                  $"q={Uri.EscapeDataString(text)}&" +
                  $"from={sourceLang}&to={targetLang}&" +
                  $"appid={_settings.BaiduAppId}&salt={salt}&sign={sign}";

        var response = await _httpClient.GetAsync(url);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<BaiduResponse>(json);

        return result?.TransResult?[0]?.Dst ?? text;
    }

    public async Task<List<string>> TranslateBatchAsync(
        List<string> texts, string sourceLang, string targetLang)
    {
        var results = new List<string>();
        foreach (var text in texts)
        {
            var translated = await TranslateAsync(text, sourceLang, targetLang);
            results.Add(translated);
            await Task.Delay(50); // 百度限制每分钟100次
        }
        return results;
    }

    private static string GetMd5Hash(string input)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }
}

/// <summary>
/// 翻译服务工厂
/// </summary>
public class TranslationServiceFactory
{
    public static ITranslationApi Create(TranslationApiSettings settings)
    {
        if (settings.EnableCustomAI && !string.IsNullOrEmpty(settings.ApiKey))
        {
            return new CustomAiTranslationService(settings);
        }
        else if (settings.EnableBaiduTranslate && 
                 !string.IsNullOrEmpty(settings.BaiduAppId) &&
                 !string.IsNullOrEmpty(settings.BaiduAppKey))
        {
            return new BaiduTranslateService(settings);
        }
        else
        {
            throw new InvalidOperationException(
                "请至少配置一种翻译API（自定义AI模型或百度翻译）");
        }
    }
}
```

### 9.5 设置持久化

所有自定义设置应保存到用户配置文件中：

```csharp
/// <summary>
/// 用户设置持久化
/// </summary>
public class UserSettings
{
    // ODA 设置
    public string OdaPath { get; set; } = @"D:\Program Files\ODA\ODAFileConverter 24.11.0\ODAFileConverter.exe";

    // 导入设置
    public ImportSettings Import { get; set; } = new();

    // 导出设置
    public string ExportSuffix { get; set; } = "_纯译文";

    // 翻译API设置
    public TranslationApiSettings TranslationApi { get; set; } = new();

    // 语言设置
    public string SourceLanguageCode { get; set; } = "en";
    public string TargetLanguageCode { get; set; } = "zh";
}

public class SettingsManager
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CADTransLite",
        "settings.json");

    public static UserSettings Load()
    {
        if (File.Exists(SettingsPath))
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<UserSettings>(json);
        }
        return new UserSettings();
    }

    public static void Save(UserSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions 
        { 
            WriteIndented = true 
        });
        File.WriteAllText(SettingsPath, json);
    }
}
```

### 9.6 参考 Excel 数据分析

```
文件: 20260526090802_en-zh_all.xlsx
总行数: 96 (不含表头)
列数: 3 (id, 原文, 译文)

文本长度统计:
  最短: 2 字符
  最长: 1318 字符
  平均: 54.6 字符

ID 数量分布 (每行对应的 CAD 实体数):
  最少: 1 个
  最多: 48 个
  平均: 3.2 个

示例数据:
  Row 2: id=@_2D07D5_&f0, 原文=Simple open single tile poop room
  Row 3: id=@_2E9C72_&f0,@_2E9D68_&f0,... (15个), 原文=DATE
```

### 9.2 测试文件信息

```
DWG 文件: 英文版汉韬尼日利亚104x13.4x4蛋鸡舍方案图及土建图.dwg
DXF 文件: 英文版汉韬尼日利亚104x13.4x4蛋鸡舍方案图及土建图.dxf
内容: 蛋鸡舍建筑图纸，包含英文标注
```

---

### 9.7 调试日志路径设置

为便于排查 ODA File Converter 转换错误，CADTrans Lite 会将详细调试日志写入固定路径。

| 项目 | 说明 |
|------|------|
| 日志路径 | `<应用程序目录>\log\CADTransLite_OdaDebug.log`（相对路径，自动创建） |
| 日志内容 | ODA 可执行文件路径、源文件状态、临时目录内容、命令行参数、进程输出（stdout/stderr）、输出目录内容 |
| 自动创建 | 程序会自动创建 `log` 子目录（如果不存在） |
| 日志格式 | `[HH:mm:ss.fff] 消息内容`（带毫秒时间戳） |

**日志启用代码位置**：`OdaConverter.cs` 中的 `LogPath` 字段和 `Log()` 方法。

**代码示例**：
```csharp
// v1.4 - 日志路径改为相对路径（应用程序目录下）
private static readonly string LogPath =
    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log", "CADTransLite_OdaDebug.log");
```

**日志清理**：建议定期清理该日志文件，避免占用过多磁盘空间。

---

*文档更新时间: 2026-05-26*  
*版本: v2.5 - Excel 导出改为两列格式，日志路径改为相对路径*
---

## 附录 A. v2.8 更新详情

### A.1 新增功能：「保存所有设置」按钮

#### 功能概述
在**翻译状态区域**下方新增「保存所有设置」按钮，用于将所有配置持久化到配置文件，避免用户重复输入。

#### 按钮位置对比

| 按钮类型 | 位置 | 功能 | 命令绑定 |
|---------|------|------|---------|
| **保存所有设置** | 翻译状态指示器下方（`TranslationStatusIndicator.xaml`） | 保存所有 API 配置、导入设置、语言选择到 `settings.json` | `SaveSettingsCommand` |
| **测试XXX连接** | 各个翻译引擎设置面板中（`MainWindow.xaml`） | 测试对应翻译 API 的连接状态 | `TestTranslationApiCommand` |

#### 技术实现

**1. TranslationStatusIndicator.xaml**（第 52 行）
```xml
<Button Content="保存所有设置"
        Command="{Binding SaveSettingsCommand}"
        Style="{DynamicResource BrowseButtonStyle}"
        FontSize="10" Padding="6,3" Margin="0,4,0,0"
        ToolTip="保存所有API设置到配置文件" />
```

**2. MainWindowViewModel.cs**
```csharp
// 属性声明
public RelayCommand SaveSettingsCommand { get; }

// 构造函数中初始化
SaveSettingsCommand = new RelayCommand(SaveSettings, () => true);
```

**3. SaveSettings() 方法功能**
- 保存翻译 API 配置（引擎选择、API Key、Base URL、模型名称等）
- 保存导入设置（代理对象炸开、块属性、标注、多行文本、特殊图层等）
- 保存导出后缀设置
- 保存语言选择（源语言、目标语言）
- 保存到路径：`%APPDATA%\CADTransLite\settings.json`

#### 用户工作流

```
┌────────────────┐
│  修改配置（如：选择引擎、填写 API Key）                    │
└────────────────┘
                        ▼
┌────────────────┐
│  点击「保存所有设置」按钮                                    │
└────────────────┘
                        ▼
┌────────────────┐
│  所有配置保存到 settings.json                                │
└────────────────┘
                        ▼
┌────────────────┐
│  关闭程序，重新打开                                          │
└────────────────┘
                        ▼
┌────────────────┐
│  所有设置自动恢复（从 settings.json 加载）                  │
└────────────────┘
```

#### 修改的文件

| 文件 | 修改内容 |
|------|---------|
| `TranslationStatusIndicator.xaml` | 将「测试所有已配置的API」按钮改为「保存所有设置」 |
| `MainWindowViewModel.cs` | 添加 `SaveSettingsCommand` 属性并初始化 |

---

### A.2 保留功能：「测试XXX连接」按钮

#### 功能说明
各个翻译引擎设置面板中的**「测试XXX连接」**按钮保持不变，用于测试对应 API 的连接状态。

#### 按钮列表

| 翻译引擎 | 按钮文本 | 命令绑定 | 所在文件 |
|---------|---------|---------|---------|
| 自定义 AI | 「测试自定义AI连接」 | `TestTranslationApiCommand` | `MainWindow.xaml` |
| 百度翻译 | 「测试百度翻译连接」 | `TestTranslationApiCommand` | `MainWindow.xaml` |
| 腾讯翻译 | 「测试腾讯翻译连接」 | `TestTranslationApiCommand` | `MainWindow.xaml` |
| 微软翻译 | 「测试微软翻译连接」 | `TestTranslationApiCommand` | `MainWindow.xaml` |
| DeepLX | 「测试 DeepLX 连接」 | `TestTranslationApiCommand` | `MainWindow.xaml` |

#### 使用场景

- 用户填写 API 配置后，点击「测试XXX连接」验证配置是否正确
- 如果测试成功，状态指示器显示绿色勾；如果失败，显示红色叉和错误信息
- 测试通过后，用户可以点击「保存所有设置」持久化配置

---

### A.3 设置文件格式

**路径**：`%APPDATA%\CADTransLite\settings.json`

**示例内容**：
```json
{
  "OdaPath": "D:\\Program Files\\ODA\\ODAFileConverter 24.11.0\\ODAFileConverter.exe",
  "ExportSuffix": "_translated",
  "SourceLanguageCode": "EN",
  "TargetLanguageCode": "ZH",
  "SelectedProvider": "Baidu",
  "TranslationApi": {
    "Provider": "Baidu",
    "BaiduAppId": "your_app_id",
    "BaiduSecret": "your_secret",
    "CustomBaseUrl": "",
    "CustomApiKey": "",
    "CustomModel": ""
  },
  "Import": {
    "ExplodeProxyObjects": true,
    "ProcessBlockAttributes": true,
    "ProcessDimensions": true,
    "ImportMTextWhole": true,
    "SpecialLayerProcessing": true
  }
}
```

---

### A.4 编译信息

- **目标框架**：`net9.0-windows`
- **编译结果**：✅ 0 错误，27 个 NU1603 警告（不影响运行）
- **exe 路径**：`E:\CADTrans Lite\src\CADTransLite.UI\bin\Debug\net9.0-windows\CADTransLite.UI.exe`

---

### A.5 已知问题（v2.8）

1. **DeepLX 测试接口 503 错误**
   - 原因：IP 被 DeepL 免费 API 临时封禁
   - 解决方案：等待 10-30 分钟自动解封，或切换到百度翻译 API

2. **回填后 CAD 文件为空**
   - 状态：待排查 `DwgWriter.cs` 的保存逻辑
   - 优先级：高（影响核心功能）

3. **「导入回填」不能单独使用**
   - 状态：待修复 `ImportAndWriteBack()` 方法的独立运行逻辑
   - 优先级：中（影响用户体验）

---

### A.6 更新时间

- **版本**：v2.8
- **更新日期**：2026-05-27
- **修改者**：QClaw (AI Assistant)
- **审核者**：待用户测试反馈

---

## 附录 B. v3.0 架构设计（基于 DocuTranslate 对比分析）

> 详细对比分析见 `docs/comparison-and-update-plan.md`

### B.1 新增服务类

#### B.1.1 DxfTextCleaner — 文本清洗过滤器

**文件**: `Services/DxfTextCleaner.cs`

```
职责：在提取阶段过滤不需要翻译的文本（工程编码、数字、符号、目标语言内容等）

DxfTextCleanerConfig:
  - SourceLang: string (zh/en/ja/ko)
  - TargetLang: string
  - FilterEmpty: bool = true
  - FilterNumber: bool = true
  - FilterSymbol: bool = true
  - FilterCode: bool = true
  - FilterTargetLang: bool = true
  - FilterNonSourceLang: bool = false
  - CustomSkipPatterns: List<string>

DxfTextCleaner:
  + Clean(text, config) → (cleanedText, wasFiltered, filterReason)
  + IsNumber(text): 识别数字+单位、范围、比值
  + IsCode(text): 8种工程编码正则
  + IsEngineeringTag(text): 设备标签/型号
  + MatchesLanguage(text, lang): Unicode 范围检测
```

**正则模式**（从 DocuTranslate 移植）：

| 类型 | 模式 | 匹配示例 |
|------|------|---------|
| 数字+单位 | `^\d+[.,]?\d*\s*(mm\|cm\|m\|kg\|kpa\|mpa\|pa\|v\|kv\|a\|hz\|kw\|w\|%\|deg\|°)$` | `25mm`, `380V`, `1.5kW` |
| 数字范围 | `^\d+[.,]?\d*\s*[~\-–]\s*\d+[.,]?\d*$` | `1-10`, `3.5~7.2` |
| 比值 | `^\d+[.,]?\d*\s*[:/]\s*\d+[.,]?\d*$` | `1:100`, `3/4` |
| 工程编码-1 | `^[A-Z]{1,8}-\d{1,6}[A-Z]?(?:-\d+)?$` | `A-001`, `ELEC-123B` |
| 工程编码-2 | `^[A-Z]{2,5}\d{2,6}[A-Z]?$` | `EL01234`, `HVAC567` |
| 设备标签 | 必须包含大写字母+数字 | `AHU-01`, `P-100` |

**集成方式**：在 `DwgExtractor.ExtractAndMerge()` 中，提取文字后立即调用 `DxfTextCleaner.Clean()`，标记被过滤的条目（Status=skipped, FilterReason=具体原因）。

#### B.1.2 DxfStyleManager — Unicode 字体样式管理

**文件**: `Services/DxfStyleManager.cs`

```
职责：翻译写回 DXF 时，自动创建/管理文字样式，确保非 ASCII 字符正确显示

DxfStyleManager:
  + EnsureUnicodeStyle(doc, text, existingStyleName) → styleName
      检测字符脚本 → 查找/创建对应文字样式 → 返回样式名
  + DetectScript(text) → Script enum (CJK/Arabic/Devanagari/Thai/Greek/Latin/Korean/Japanese)
  + EncodeDxfUnicode(text) → string (非 ASCII 编码为 \U+XXXX)

ScriptFontMap (静态映射):
  CJK → "simsun.ttc"       (宋体)
  Arabic → "arial.ttf"     (Arial)
  Devanagari → "mangal.ttf" (Mangal)
  Thai → "tahoma.ttf"      (Tahoma)
  Greek → "arial.ttf"      (Arial)
  Korean → "batang.ttc"    (Batang)
  Japanese → "msmincho.ttc" (MS Mincho)
```

**集成方式**：在 `DwgWriter.WriteBack()` 中，写入译文前调用 `EnsureUnicodeStyle()`，为含非 ASCII 字符的译文创建合适的文字样式。

#### B.1.3 MTextRebuilder — MTEXT 回写增强

**文件**: `Services/MTextRebuilder.cs`

```
职责：替代 MTextCodec.RestoreFormatCodes() 的简单替换，实现命令骨架保留+视觉换行

MTextRebuilder:
  + RebuildMtextContent(originalRaw, translatedText, entityWidth) → rebuiltContent
      1. 解析原始 MTEXT 命令骨架（{\H1.5x;...}, {\C6;...}等）
      2. 替换可见文字槽位为译文
      3. 按视觉宽度自动换行
  + WrapPlainText(text, entityWidth, charWidths) → wrappedText
      空格=0.35, ASCII=0.55, CJK=1.0
      在 entityWidth 内插入 \P 换行符
  + ReplaceVisibleText(originalSkeleton, translatedText) → result
      仅替换文字字符，保留所有 MTEXT 命令
```

**集成方式**：修改 `DwgWriter` 的 MTEXT 回写分支，用 `MTextRebuilder.RebuildMtextContent()` 替代 `MTextCodec.RestoreFormatCodes()`。保留 MTextCodec 作为 fallback。

### B.2 现有文件变更

#### B.2.1 TranslationItem.cs — 新增字段

```csharp
// Phase 2: 实体类型扩展
public string? BlockName { get; set; }       // 所属块名
public string? AttributeTag { get; set; }     // ATTRIB 的 tag
public int TableRow { get; set; } = -1;      // ACAD_TABLE 行索引 (-1 = 无)
public int TableColumn { get; set; } = -1;   // ACAD_TABLE 列索引 (-1 = 无)

// Phase 1: 清洗/过滤
public string? FilterReason { get; set; }    // 过滤原因
public string? CleanedText { get; set; }     // 清洗后的文本
public string? Status { get; set; }          // translated/skipped/error
public string? Remark { get; set; }          // 备注
```

#### B.2.2 EntityType 枚举 — 扩展

```csharp
public enum EntityType
{
    Text,
    MText,
    Attribute,
    TableCell,   // v3.0: ACAD_TABLE 单元格
    MLeader,     // v3.0: 多重引线标注
}
```

#### B.2.3 DwgExtractor.cs — 集成清洗器

```
ExtractAndMerge() 流程变更:
  1. 提取文字（现有逻辑）
  2. [新增] 对每条文字调用 DxfTextCleaner.Clean()
  3. 标记 filtered 的条目: Status="skipped", FilterReason=原因
  4. 合并相同文本（现有逻辑，但使用 CleanedText 作为二次去重键）
  5. [新增] 遍历 ACAD_TABLE 和 MLEADER 实体（Phase 2）
```

#### B.2.4 DwgWriter.cs — 集成字体管理 + MTEXT 重建

```
WriteBack() 流程变更:
  1. 加载 DXF 文档
  2. 对每条翻译项:
     a. [新增] DxfStyleManager.EnsureUnicodeStyle() — 确保字体样式
     b. 对 MTEXT: [新增] MTextRebuilder.RebuildMtextContent() — 重建内容
     c. 对 TEXT/ATTRIB: 直接写回（现有逻辑）
     d. [新增] 对 TABLE/MLEADER: 按 Handle 定位并写回（Phase 2）
  3. 保存 DXF
```

### B.3 项目文件结构变更

```
CADTransLite.Core/Services/
├── DxfTextCleaner.cs          # [新增] Phase 1.1 文本清洗过滤器
├── DxfStyleManager.cs         # [新增] Phase 1.2 Unicode 字体样式管理
├── MTextRebuilder.cs          # [新增] Phase 1.3 MTEXT 回写增强
├── DwgExtractor.cs            # [修改] 集成清洗器 + 扩展实体类型
├── DwgWriter.cs               # [修改] 集成字体管理 + MTEXT 重建 + 新实体回写
├── MTextCodec.cs              # [保留] 作为 MTextRebuilder 的 fallback
├── TranslationMerger.cs      # [修改] 增加 CleanedText 二次去重
├── ExcelHandler.cs            # [修改] 支持富元数据列导出/导入
├── OdaConverter.cs            # [保留]
├── TranslationService.cs      # [保留]
└── ...

CADTransLite.Core/Models/
├── TranslationItem.cs         # [修改] 新增 Phase 2 字段 + 过滤状态字段
├── ImportSettings.cs         # [修改] 新增文本清洗配置
└── ...
```

### B.4 实施优先级

| 优先级 | 任务 | 预估工作量 | 依赖 |
|--------|------|-----------|------|
| ★★★★★ | V3-1.2 DxfStyleManager | 3-5 天 | 无 |
| ★★★★★ | V3-1.1 DxfTextCleaner | 3-5 天 | 无 |
| ★★★★☆ | V3-1.3 MTextRebuilder | 5-7 天 | V3-1.2 |
| ★★★★☆ | V3-2.1 ACAD_TABLE | 3-5 天 | V3-1.1 |
| ★★★★☆ | V3-2.2 MLEADER | 3-5 天 | V3-1.1 |
| ★★★☆☆ | V3-3.1 富元数据导出 | 3-5 天 | V3-1.1, V3-2.1 |
| ★★★☆☆ | V3-3.2 清洗后去重 | 2-3 天 | V3-1.1 |

### B.5 技术风险

| 风险 | 缓解措施 |
|------|---------|
| netDxf 对 ACAD_TABLE/MLEADER 支持不完整 | 先验证 API 可用性；不支持则直接操作 DXF CODE |
| MTEXT 格式码边界情况多 | 保留 MTextCodec 作为 fallback，渐进替换 |
| Unicode 字体在用户机器上可能不存在 | 使用系统通用字体 + 降级策略 + 配置化映射 |
| 富元数据 Excel 与现有 2 列格式不兼容 | 版本化导出，保留 2 列兼容模式 |

---

*文档更新时间: 2026-05-27*  
*版本: v3.0-dev - 基于 DocuTranslate 对比分析的新架构设计*
