# CADTrans-Lite

CAD 图纸翻译工具（Lite 版）：从 AutoCAD `DXF/DWG` 图纸中提取可翻译文本（含 `TEXT`、`MTEXT`、`ATTRIB` 等），交给翻译引擎翻译后，再把译文**无损回填**到图纸，生成结构合法、可直接用 AutoCAD / 兼容软件打开的翻译后图纸。

> 适用于建筑、机械、电气等工程图纸的中英（及多语言）翻译场景。

## 功能特性

- **Raw DXF 解析**：不依赖 AutoCAD，直接按组码（group code）逐行解析 DXF，保留原始编码、行结构与格式占位符（如 `%%`、字段代码），避免回填后结构损坏。
- **多实体提取**：`TEXT` / `MTEXT` / `ATTRIB` 等文本实体提取为结构化条目，导出到 Excel 便于批量翻译。
- **安全回填（WriteBack）**：译文以「组码 + 值」严格交替的方式写回，长 `MTEXT` 自动切分到多个 `3`/`1` 组，确保输出文件总行数为偶数、可被 CAD 软件正常打开。
- **多种翻译引擎**：
  - **DeepLX**：内置 `deeplx_windows_amd64.exe`（及 `src/CADTransLite.UI/deeplx_backup.bin` 资源备份，构建时自动恢复）。
  - **LibreTranslate + Argos Translate**：本地离线翻译，运行时通过 `tools/py` 提供（见下方「翻译引擎配置」）。
  - **NLLB**：通过 `tools/setup_nllb.ps1` 配置。
- **格式占位符保护**：原文中的格式代码在翻译前后保持一一对应，防止译文错位。

## 环境要求

- **Windows**（WPF / `net9.0-windows`）
- **.NET 9 SDK**（`global.json` 锁定 `9.0.314`，`rollForward: latestMinor`）

> 提示：若使用本机用户目录安装的 SDK（如 `%LOCALAPPDATA%\dotnet9\dotnet.exe`），`build.bat` 会自动优先选用它。

## 构建

仓库根目录提供两个等价构建脚本：

```bat
build.bat          :: 双击或命令行运行（Debug 配置）
```

或 PowerShell：

```powershell
pwsh -File build_local.ps1
```

构建产物位于 `src\CADTransLite.UI\bin\Debug\net9.0-windows\`。

## 翻译引擎配置

本地离线翻译（LibreTranslate + Argos）所需的 Python 运行时**不纳入仓库**（体积数 GB），请在本机按需制备：

```powershell
pwsh -File tools\setup_engines.ps1   # 制备 tools/py（LibreTranslate + Argos 及语言包）
pwsh -File tools\setup_nllb.ps1      # 可选：配置 NLLB 引擎
```

`deeplx_windows_amd64.exe` 已随仓库提供，构建时由 `deeplx_backup.bin` 校验/恢复，无需额外下载。

### 自定义下载 Argos 翻译模型

`tools/setup_engines.ps1` 会一次性下载一组固定的语言包（约 2 GB）。如果只想按需下载特定语言对，可用 `download_models.ps1`：它基于 Argos 官方包索引动态列出**全部**可用语言对，由你自由选择。

```powershell
pwsh -File tools\download_models.ps1 -ListOnly                              # 列出全部 100 个语言对
pwsh -File tools\download_models.ps1                                        # 交互式：列出后输入编号/代码
pwsh -File tools\download_models.ps1 -Pairs en_zh,zh_en,en_es,es_en         # 指定语言对（from_to，逗号分隔）
pwsh -File tools\download_models.ps1 -All                                   # 下载全部
pwsh -File tools\download_models.ps1 -Pairs en_zh -OutputDir D:\models      # 指定输出目录
```

模型默认下载到 `tools/py/argos_packages`（与 app 的 `ARGOS_PACKAGES_DIR` 一致，离线加载）。已存在的模型会自动跳过。需要先运行 `setup_engines.ps1` 安装 Python 引擎，模型才会被使用。

## 使用流程

1. **① 提取导出**：加载源 DXF，提取可翻译文本并导出为 Excel（`*_纯翻译.xlsx`）。
2. **② 翻译**：用本地/在线引擎翻译 Excel 中的条目（保持「原文—译文—Handle」对应）。
3. **③ 导入回填**：将翻译后的 Excel 导入，工具生成结构合法的 `*_translated.dxf`。

## 项目结构

```
CADTrans-Lite/
├─ src/
│  ├─ CADTransLite.sln        # 解决方案
│  ├─ CADTransLite.Core/      # 核心：DXF 解析、文本提取、Excel 导入导出、WriteBack、MTEXT 重建
│  ├─ CADTransLite.UI/        # WPF 界面（含 deeplx_backup.bin 资源）
│  ├─ CADTransLite.Tests/     # 单元测试
│  ├─ CADTransLite.TestRunner/
│  └─ DxfGen/                 # DXF 生成辅助工具
├─ docs/                      # 设计文档、架构图（Mermaid）、PRD
├─ tools/
│  ├─ setup_engines.ps1       # 制备本地翻译 Python 运行时
│  ├─ setup_nllb.ps1          # 配置 NLLB 引擎
│  ├─ download_models.ps1     # 自定义选择下载 Argos 翻译模型
│  └─ ai-cad.py               # AI 辅助脚本
├─ build.bat / build_local.ps1
├─ LICENSE
└─ README.md
```

## 已知问题 / 修复记录

- **MTEXT 回填结构损坏（已修复）**：早期版本在回写 `MTEXT` 时，前缀拷贝把原组码行也一并写入，与新生成的组码行重复，导致前一个组码缺失对应值行，输出 DXF 行数为奇数、被 CAD 软件判定为损坏。`DwgWriter.cs` 已修正：前缀拷贝止于值行之前、由 chunks 重新生成组码，并复用原组码前导空格；译文为空时兜底保留 `(组码+空值)`，裸换行转 `\P`。

## 许可证

[MIT](LICENSE)
