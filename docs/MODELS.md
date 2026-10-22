# 本地翻译模型：分包、自定义目录与离线使用说明

CADTrans Lite 提供三个**本地离线**翻译引擎，都需要本地的模型/语言包文件才能工作：

| 引擎 | 模型位置（默认，在程序目录 `tools/` 下） | 说明 |
| --- | --- | --- |
| **Bergamot（本地）** | `tools/bergamot/` | 纯 .NET 原生引擎，无需 Python。每个语言对一个子目录（如 `en-zh`、`zh-en`）。 |
| **LibreTranslate（本地 / Argos）** | `tools/py/argos_packages/` | Python 运行时 + Argos 语言包（`.argosmodel`）。 |
| **NLLB（本地）** | `tools/py/models/nllb-200-distilled-600M/` | Python 运行时 + Meta NLLB-200 蒸馏模型。 |

> 其余引擎（百度 / 腾讯 / Microsoft / DeepL / DeepLX / 自定义 AI）为云端或本地代理，**不涉及模型文件**。

---

## 一、把模型分包（每个渠道单独打包）

仓库提供一键脚本 `tools/package_all_models.ps1`，把三个渠道的模型分别打成**独立的 zip**，输出到 `tools/models/`：

```
tools/models/
├─ bergamot-models.zip          # Bergamot 全部语言对
├─ libretranslate-models.zip    # Argos / LibreTranslate 语言包（argos_packages）
└─ nllb-model.zip               # NLLB-200 蒸馏模型
```

```powershell
# 在仓库根目录执行
pwsh -File tools\package_all_models.ps1
# 也可指定输出目录：
pwsh -File tools\package_all_models.ps1 -OutputDir D:\cad-models
```

每个 zip 解压后的顶层结构分别是 `bergamot/`、`argos_packages/`、`models/nllb-200-distilled-600M/` —— 与下方“自定义模型目录”约定完全一致，可直接分发给用户。

> 若只需要某一个语言对，仍可用 `tools/package_models.ps1` 单独打包 Argos 词包（按语言对粒度）。
> 目标机器上安装这些 `translate-*.zip` 用 `tools/install_argos_pairs.ps1` 一键完成（zip 内是双层压缩的
> `.argosmodel`，脚本自动解好并放进 `argos_packages\`，支持装到自定义模型目录）。

---

## 二、自定义模型目录（安装到别的电脑）

软件支持在 **“翻译 API 设置 → 本地模型目录（自定义）”** 中指定一个目录。
**留空**则使用程序自带的 `tools/` 目录；**一旦指定**，三个本地引擎的模型都从该目录下加载，无需把模型放进程序目录。

该目录内部结构（与上面的渠道包解压结构一致）：

```
<你指定的模型根目录>/
├─ bergamot/                              ← Bergamot 模型（来自 bergamot-models.zip）
├─ argos_packages/                        ← LibreTranslate / Argos 语言包（来自 libretranslate-models.zip）
└─ models/
    └─ nllb-200-distilled-600M/            ← NLLB 模型（来自 nllb-model.zip）
```

### 使用步骤（最简单：软件内一键导入）
1. 把渠道包拷到目标电脑（bergamot-models.zip / nllb-model.zip / 需要的 translate-*.zip）。
2. 打开 CADTrans Lite → 翻译 API 设置 → 本地模型目录：点“浏览”选定一个目录（或留空装入程序自带 tools），再点 **“导入模型包”** 多选这些 zip——软件自动识别类型并安装到正确位置（含双层压缩的 Argos 语言对）。
3. 选择本地引擎（Bergamot / LibreTranslate / NLLB）并“测试”。若安装前翻译服务已启动，重启软件后测试。

### 手动方式（不用导入按钮）
1. 解压 `bergamot-models.zip`、`nllb-model.zip` 到模型目录（各自自带顶层 `bergamot/`、`models/`）。
2. Argos 语言对运行 `tools\install_argos_pairs.ps1 -PackagesDir "<模型目录>\argos_packages"` 自动安装（`translate-*.zip` 内是双层压缩的 `.argosmodel`，脚本自动处理）。

> 该设置保存在用户配置（`%APPDATA%/CADTransLite/settings.json`），**独立运行的 MCP 桥接进程也会读取同一份设置**，因此在自定义目录下同样生效。

---

## 三、前提：Python 引擎运行时已就绪

Bergamot 不需要 Python；但 **LibreTranslate / NLLB 的 Python 运行时（`tools/py`，含 `argostranslate`/`libretranslate`/`transformers`/`torch` 等）会随绿色版一起分发**（运行时不是“模型”）。模型数据（语言包 / NLLB 模型）才被单独打包。

若需在本机重新安装/更新引擎与默认模型，可运行：

```powershell
.\tools\py\setup_engines.ps1     # LibreTranslate / Argos 引擎 + 基础语言包
.\tools\setup_nllb.ps1           # NLLB 模型
.\tools\setup_bergamot.ps1       # Bergamot 语言对模型
```

---

## 四、绿色版构建（不含模型）

`tools/build_portable.ps1` 生成**解压即跑**的绿色版 ZIP（`CADTrans-Lite-win-x64-portable.zip`），**刻意排除所有模型数据**：

- 自包含 .NET 9 运行时 + `CADTransLite.UI.exe`
- `CADTransLite.McpBridge.exe`（MCP 桥接）
- 嵌入式 Python 运行时 `tools/py`（排除 `argos_packages/`、`models/`）
- `deeplx_windows_amd64.exe`、`docs/`、`README.md`、`tools/*.ps1`

```powershell
pwsh -File tools\build_portable.ps1
```

模型请用 `tools/package_all_models.ps1` 单独打包分发，由用户解压到“自定义模型目录”。
