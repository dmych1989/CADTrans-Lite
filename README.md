# CADTrans Lite

**轻量级 CAD 图纸翻译工具** — 提取 DWG/DXF 文件中的文字，导出为 Excel 翻译表，支持多种翻译 API 一键翻译，回填生成翻译后的新 CAD 文件。

![.NET 9](https://img.shields.io/badge/.NET-9.0-blue)
![WPF](https://img.shields.io/badge/UI-WPF-purple)
![License](https://img.shields.io/badge/License-MIT-green)
![Version](https://img.shields.io/badge/Version-3.4-orange)

---

## 功能特性

![](https://github.com/dmych1989/CADTrans-Lite/blob/main/images/%E8%BD%AF%E4%BB%B6%E6%88%AA%E5%9B%BE3.0.png)

### 核心工作流：三步翻译

```
① 提取导出 → ② 一键翻译 → ③ 导入回填
CAD 文件      API 翻译      新 CAD 文件
```

1. **① 提取导出**：拖入 DWG/DXF 文件，自动提取所有可翻译文字（TEXT、MTEXT、ATTRIB、ACAD_TABLE、MULTILEADER），导出为 Excel 翻译表
2. **② 一键翻译**：选择翻译引擎和语言对，自动翻译 Excel 中的译文列
3. **③ 导入回填**：导入翻译后的 Excel，自动生成翻译完成的新 CAD 文件

### 支持的 CAD 实体类型

| 实体类型        | 说明         | 回写方式                               |
| ----------- | ---------- | ---------------------------------- |
| TEXT        | 单行文字       | 原始 DXF 行级替换                        |
| MTEXT       | 多行文字       | 原始 DXF 行级替换（支持 MText 格式码）          |
| ATTRIB      | 块属性        | 原始 DXF 行级替换（InsertHandle::Tag 组合键） |
| ACAD_TABLE  | AutoCAD 表格 | DxfTextReplacer 原始标签替换             |
| MULTILEADER | 多重引线       | DxfTextReplacer 原始标签替换             |

### 支持的翻译引擎

| 引擎      | 需要的配置                       |
| ------- | --------------------------- |
| 百度翻译    | App ID + Secret Key         |
| 腾讯翻译    | Secret ID + Secret Key      |
| DeepL   | Auth Key                    |
| DeepLX  | Base URL（自建 DeepLX 服务）      |
| 微软翻译    | Subscription Key + Region   |
| 自定义 API | URL + API Key（兼容 OpenAI 格式） |

### Excel 翻译表格式

- **2 列格式（默认）**：原文 | 译文 — 简洁轻量，适合手工编辑
- **11 列富格式**：Handle | 类型 | 图层 | 块名 | 属性标签 | 表格位置 | 原文 | 清洗文本 | 译文 | 状态 | 备注 — 适合专业审校
- **导出纯翻译**：仅导出已翻译且去重的原文/译文对照表（2 列）
- **自动格式检测**：导入时自动识别 2 列或 11 列格式

### DWG 支持

通过 ODA File Converter 实现 DWG ↔ DXF 互转：

- 输入 .dwg 文件自动转 DXF 进行处理
- 输出可选择 DXF 或 DWG 格式
- 支持 DWG 输出版本选择（ACAD 2000/2004/2007/2010/2013/2018）

### Phase 4 新功能 (v3.4)

| 功能 | 说明 |
| --- | --- |
| 布局自适应 | 翻译后文字过长时自动缩放字高 + 刷新 MTEXT 边界宽度（最小 0.65x） |
| 术语表系统 | JSON 持久化术语表，翻译后自动应用术语替换，支持 DataGrid 原生编辑 |
| AI 智能过滤 | 翻译前用 AI 判断文本是否需要翻译（KEEP/SKIP），过滤尺寸/公差/符号等 |
| DWG 版本选择 | 回填输出时可选择 DWG 版本（ACAD 2000~2018） |

---

## 系统要求

- **操作系统**：Windows 10/11（64-bit）
- **运行时**：.NET 9.0 Desktop Runtime
- **可选**：ODA File Converter（用于 DWG 格式支持）

---

## 快速开始

### 1. 克隆仓库

```bash
git clone https://github.com/<your-username>/CADTrans-Lite.git
cd CADTrans-Lite
```

### 2. 还原依赖

```bash
cd src
dotnet restore
```

> **注意**：如果 `dotnet restore` 因 NuGet 源问题失败，项目自带 `NuGet.Config`，确保网络可访问 nuget.org。

### 3. 编译

```bash
dotnet build CADTransLite.sln --configuration Release
```

### 4. 运行

```bash
cd CADTransLite.UI/bin/Release/net9.0-windows
./CADTransLite.UI.exe
```

---

## 操作步骤

### 基本翻译流程

1. **拖入文件**：将 .dwg 或 .dxf 文件拖入窗口左侧文件框
2. **配置翻译引擎**：在右侧面板选择翻译引擎，填入 API 密钥，选择源语言和目标语言
3. **① 提取导出**：点击"① 提取导出"按钮，自动提取文字并导出 Excel
4. **② 一键翻译**：点击"② 一键翻译"按钮，API 自动翻译所有条目
   - 也可以手动在 Excel 中编辑译文列
5. **③ 导入回填**：点击"③ 导入回填"按钮，导入翻译后的 Excel 并生成新 CAD 文件

### 导出纯翻译对照表

1. 完成翻译后，点击"① 提取导出"下方的"导出纯翻译"按钮
2. 导出的 Excel 仅包含 2 列（原文/译文），且只含已翻译且去重的条目
3. 适用于术语库积累和翻译记忆复用

### 手动编辑 Excel 翻译

1. 提取导出后，打开生成的 Excel 文件
2. 在"译文"列中手动输入翻译
3. 保存 Excel 文件
4. 回到软件，点击"③ 导入回填"导入编辑后的 Excel

---

## 项目结构

```
CADTrans Lite/
├── src/
│   ├── CADTransLite.sln                 # 解决方案文件
│   ├── global.json                      # .NET SDK 版本锁定
│   ├── NuGet.Config                     # NuGet 源配置
│   │
│   ├── CADTransLite.Core/               # 核心业务逻辑库
│   │   ├── Interfaces/
│   │   │   └── ITranslationApi.cs       # 翻译 API 接口
│   │   ├── Models/
│   │   │   ├── TranslationItem.cs       # 翻译条目数据模型
│   │   │   ├── DxfRawEntity.cs          # DXF 原始实体模型（TEXT/MTEXT/ATTRIB/ACAD_TABLE/MLEADER）
│   │   │   ├── UserSettings.cs          # 用户设置
│   │   │   ├── LanguageInfo.cs          # 语言代码映射
│   │   │   └── ...                      # 其他模型
│   │   └── Services/
│   │       ├── DwgExtractor.cs          # DXF 文字提取（netDxf）
│   │       ├── DwgWriter.cs             # DXF 回写（纯原始行级替换）
│   │       ├── DxfRawParser.cs          # DXF 原始文本解析器
│   │       ├── DxfTextReplacer.cs       # ACAD_TABLE/MLEADER 原始替换
│   │       ├── ExcelHandler.cs          # Excel 导入导出（EPPlus）
│   │       ├── MTextCodec.cs            # MText 格式码编解码
│   │       ├── MTextRebuilder.cs        # MText 翻译后重构
│   │       ├── DxfTextCleaner.cs        # DXF 文本清洗
│   │       ├── CleanedTextDeduplicator.cs # 清洗文本去重
│   │       ├── TranslationService.cs    # 翻译服务调度
│   │       ├── BaiduTranslator.cs       # 百度翻译
│   │       ├── TencentTranslator.cs     # 腾讯翻译
│   │       ├── DeepLTranslator.cs       # DeepL 翻译
│   │       ├── DeepLXTranslator.cs      # DeepLX 翻译
│   │       ├── MicrosoftTranslator.cs   # 微软翻译
│   │       ├── CustomAiTranslator.cs    # 自定义 API 翻译
│   │       ├── OdaConverter.cs          # ODA DWG↔DXF 转换
│   │       ├── DxfLayoutAdjuster.cs     # 布局自适应（Phase 4）
│   │       ├── GlossaryManager.cs       # 术语表管理（Phase 4）
│   │       ├── AiTextFilter.cs          # AI 智能过滤（Phase 4）
│   │       └── ...                      # 其他服务
│   │
│   ├── CADTransLite.UI/                 # WPF 用户界面
│   │   ├── MainWindow.xaml              # 主窗口布局
│   │   ├── MainWindowViewModel.cs       # 主窗口视图模型
│   │   ├── Controls/                    # 自定义控件
│   │   └── Converters.cs               # 值转换器
│   │
│   └── CADTransLite.Tests/              # 单元测试
│       ├── DxfRawParserTests.cs
│       ├── DxfTextReplacerTests.cs
│       ├── ExcelHandlerTests.cs
│       ├── MTextCodecTests.cs
│       └── ...
│
├── docs/                                # 设计文档
│   ├── CADTrans Lite 开发文档 v2.1.md
│   ├── PRD-v2.1.md
│   ├── phase2-architecture.md           # Phase 2 架构设计
│   ├── phase3-architecture.md           # Phase 3 架构设计
│   ├── phase4-architecture.md           # Phase 4 架构设计
│   ├── class-diagram.mermaid            # 类图
│   └── sequence-diagram.mermaid         # 时序图
│
├── LICENSE                              # MIT 开源协议
└── README.md                            # 本文件
```

---

## 核心技术要点

### 回写机制（v3.1+）

DwgWriter 采用**纯原始 DXF 行级文本替换**，而非 netDxf 的 Load/Save 方式：

```
原始 DXF 文件 → File.Copy(复制原文件，保留全部内容)
              → DxfRawParser 解析 TEXT/MTEXT/ATTRIB 实体的 Handle 和行号
              → 在行级别精准替换文本值
              → DxfTextReplacer 处理 ACAD_TABLE/MLEADER
              → DxfLayoutAdjuster 布局自适应（字高缩放 + MTEXT 边界刷新）
              → GlossaryManager 应用术语替换（可选）
              → AiTextFilter AI 过滤 SKIP 条目（可选）
              → File.WriteAllLines 保存
```

**为什么不使用 netDxf Save？** netDxf 无法序列化它不支持的实体类型（ACAD_TABLE、MULTILEADER、自定义实体等），导致输出文件丢失内容。纯行级替换保证所有 DXF 内容完整保留。

### MText 格式码处理

MText 使用 DXF 格式码（如 `\P` 换行、`{\fSimSun...}` 字体定义等）。翻译时：

1. `MTextCodec.StripForTranslation` — 剥离格式码，提取纯文本
2. 翻译纯文本
3. `MTextRebuilder.Rebuild` — 将翻译后文本重新嵌入 MText 格式码框架

### Excel 导入匹配

- **11 列格式**：通过 Handle 精确匹配，行号降级兜底
- **2 列格式**：通过原文文本匹配 + 行号顺序匹配
- 自动检测格式，无需手动指定

---

## 打包与发布

### 前置条件

- .NET 9.0 SDK
- Windows 10/11

### 编译 Release 版本

```bash
cd src
dotnet restore
dotnet build CADTransLite.sln --configuration Release
```

输出路径：`src/CADTransLite.UI/bin/Release/net9.0-windows/`

### 发布自包含版本（无需安装 .NET Runtime）

```bash
cd src/CADTransLite.UI
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
```

输出路径：`src/CADTransLite.UI/bin/Release/net9.0-windows/win-x64/publish/`

### 发布单文件版本

```bash
cd src/CADTransLite.UI
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

输出：单个 `.exe` 文件 + 依赖 DLL

### 注意事项

- ODA File Converter 需单独安装（可选，仅 DWG 格式需要）
- 首次运行需要配置翻译 API 密钥
- 设置文件存储在 `%APPDATA%/CADTransLite/` 目录

---

## 二次开发指南

### 添加新的翻译引擎

1. 在 `CADTransLite.Core/Services/` 下创建新类，实现 `ITranslationApi` 接口：

```csharp
public class MyTranslator : ITranslationApi
{
    public string ProviderName => "MyTranslator";

    public async Task<string> TranslateAsync(
        string text, string sourceLang, string targetLang,
        CancellationToken ct = default)
    {
        // 实现翻译逻辑
    }
}
```

2. 在 `MainWindowViewModel.cs` 的 `BuildTranslationApi()` 中注册新引擎
3. 在 `MainWindow.xaml` 中添加对应配置面板

### 添加新的 CAD 实体类型支持

1. 在 `DxfRawEntity.cs` 中添加实体模型类
2. 在 `DxfRawParser.cs` 中添加解析方法
3. 在 `DwgExtractor.cs`（提取）和 `DwgWriter.cs`（回写）中添加处理逻辑
4. 对于 netDxf 不支持的实体类型，在 `DxfTextReplacer.cs` 中添加原始替换逻辑

### 运行测试

```bash
cd src
dotnet test CADTransLite.Tests
```

---

## 主要依赖

| 包                     | 版本         | 用途             |
| --------------------- | ---------- | -------------- |
| netDxf                | 2024.9.2.1 | DXF 文件解析（提取阶段） |
| EPPlus                | 7.5.2      | Excel 文件读写     |
| CommunityToolkit.Mvvm | 8.2.1      | MVVM 框架（源码生成器） |
| .NET                  | 9.0        | 运行时            |

---

## 常见问题

**Q：导入回填后 CAD 文件内容不完整？**
A：v3.1+ 已修复此问题。DwgWriter 现在使用纯原始 DXF 行级替换，保证所有内容完整保留。

**Q：DWG 文件无法打开？**
A：需要安装 ODA File Converter（在设置中配置路径），用于 DWG ↔ DXF 互转。

**Q：翻译 API 返回 429 错误？**
A：翻译请求频率过高，被 API 限流。降低并发或更换 API 套餐。

**Q：Excel 导入后译文没有回填？**
A：确保 Excel 的行数和提取时一致（2 列格式），或使用 11 列格式通过 Handle 精确匹配。

---

## 开源协议

本项目基于 [MIT License](LICENSE) 开源。

---

## 致谢

- [netDxf](https://github.com/haplokuon/netDxf) — DXF 文件读写库
- [EPPlus](https://github.com/EPPlusSoftware/EPPlus) — Excel 操作库
- [DocuTranslate](https://github.com/DocuTranslate-for-engineer) — 参考了其 DXF 原始文本替换思路
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) — MVVM 框架

## 打赏
![](https://github.com/dmych1989/CADTrans-Lite/blob/main/images/%E6%89%93%E8%B5%8F%E4%BA%8C%E7%BB%B4%E7%A0%81.jpg)

