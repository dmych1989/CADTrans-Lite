# Argos 翻译模型：分包与使用说明

CADTrans Lite 的「Argos Translate（本地）」/「LibreTranslate（本地）」离线翻译引擎，需要本地的语言模型文件（`.argosmodel`）才能工作。

本目录（`tools/models/`）下为你提供了**每个语言对一个独立 zip** 的分发包：

```
tools/models/
├─ translate-en_zh-1_9.zip     # 英 → 中
├─ translate-zh_en-1_9.zip     # 中 → 英
├─ translate-en_ja-1_9.zip     # 英 → 日
├─ ...（共 100 个语言对）
```

每个 zip 内只有一个文件，例如 `translate-en_zh-1_9.zip` 解压后就是 `translate-en_zh-1_9.argosmodel`。

---

## 模型放在哪个目录？

> **应用运行时，从「CADTransLite.UI.exe 所在目录」下的 `argos_packages` 子目录加载模型。**

把 `.argosmodel` 文件放进 `<应用目录>\argos_packages\` 即可，无需其他配置。

### 绿色版（解压即用）
解压 `CADTrans-Lite-win-x64-portable.zip` 后，目录结构类似：

```
D:\CADTransLite\
├─ CADTransLite.UI.exe
├─ deeplx_windows_amd64.exe
├─ argos_packages\          ← 把模型放这里
└─ ...
```

把需要的 `.argosmodel` 复制进 `argos_packages`：

```
D:\CADTransLite\argos_packages\translate-en_zh-1_9.argosmodel
D:\CADTransLite\argos_packages\translate-zh_en-1_9.argosmodel
```

### 安装版（Inno/NSIS 安装到 Program Files）
安装目录即应用目录，例如：

```
C:\Program Files\CADTrans Lite\argos_packages\translate-en_zh-1_9.argosmodel
```

> 若 `argos_packages` 目录不存在，新建一个同名文件夹即可。

---

## 操作步骤（以英⇄中为例）

1. 从 `tools/models/` 找到 `translate-en_zh-1_9.zip` 和 `translate-zh_en-1_9.zip`。
2. 分别解压，得到两个 `.argosmodel` 文件。
3. 把它们复制到 `<应用目录>\argos_packages\`。
4. 打开 CADTrans Lite，翻译引擎选择 **Argos Translate（本地）** 或 **LibreTranslate（本地）**。
5. 点「测试」验证离线翻译可用。

需要哪些语言对，就放哪些；放错或多余的模型不会报错，只是用不到。

---

## 前提：Python 引擎已就绪

模型文件本身不含运行时。使用前请先运行一次：

```powershell
.\tools\py\setup_engines.ps1
```

它会安装内嵌 Python + `argostranslate` / `libretranslate` 引擎（约 2 GB，含一组基础语言包）。之后再往 `argos_packages` 里追加本分发包的模型即可扩展语言支持。

---

## 自己生成 / 重新打包

仓库提供两个脚本：

- `tools/download_models.ps1` —— 仅下载模型到 `tools/py/argos_packages/`（不打包）。
- `tools/package_models.ps1` —— 下载并把**每个模型单独打成 zip** 输出到 `tools/models/`。

```powershell
# 列出全部 100 个语言对
pwsh -File tools\package_models.ps1 -ListOnly

# 打包全部
pwsh -File tools\package_models.ps1 -All

# 只打包指定语言对
pwsh -File tools\package_models.ps1 -Pairs en_zh,zh_en,en_es,es_en

# 自定义输出目录
pwsh -File tools\package_models.ps1 -All -OutputDir D:\cad-models
```

> 模型来自 Argos 官方包索引（argospm-index），下载地址 `argos-net.com`，需系统支持 TLS 1.2（Windows 10/11 默认满足）。
