# tools/build_portable.ps1
# 构建"绿色版"分发包（ZIP，解压即跑），【不包含任何模型数据】。
#
# 与 publish_app.ps1 的区别：publish_app.ps1 会把 tools/bergamot、tools/py/argos_packages、
# tools/py/models 等模型数据一并拷入发布目录；本脚本刻意排除这些模型目录，只打包：
#   - 自包含的 .NET 9 运行时 + CADTransLite.UI.exe
#   - CADTransLite.McpBridge.exe（MCP 桥接）
#   - 嵌入式 Python 运行时 tools/py（含 LibreTranslate/NLLB 引擎代码，但排除 argos_packages 与 models）
#   - deeplx_windows_amd64.exe、docs/、README.md、tools/*.ps1（安装/设置脚本）
#
# 模型请通过 tools/package_all_models.ps1 单独打包，由用户解压到"自定义模型目录"。
#
# 产物：仓库根目录下的 CADTrans-Lite-win-x64-portable.zip
#
# Usage:
#   .\tools\build_portable.ps1

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$dotnet = "$env:LOCALAPPDATA\dotnet9\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition   # tools 目录
$repoRoot  = Split-Path -Parent $scriptDir                           # 仓库根目录
$srcDir   = Join-Path $repoRoot 'src'
$uiProj   = Join-Path $srcDir 'CADTransLite.UI\CADTransLite.UI.csproj'
$mcpProj  = Join-Path $srcDir 'CADTransLite.McpServer\CADTransLite.McpServer.csproj'
$staging  = Join-Path $repoRoot 'dist_portable'
$zipPath  = Join-Path $repoRoot 'CADTrans-Lite-win-x64-portable.zip'

$env:PATH = "$env:LOCALAPPDATA\dotnet9;$env:PATH"

# 1) 清理并重建临时发布目录
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path $staging | Out-Null

# 2) 发布 UI（自包含，win-x64）
Write-Output "[$(Get-Date)] Publishing UI (self-contained, win-x64) ..."
& $dotnet publish $uiProj -c Release -r win-x64 --self-contained true -o $staging
if ($LASTEXITCODE -ne 0) { throw "UI publish failed: $LASTEXITCODE" }

# 2.1) 修复 WPF 运行时程序集（同 publish_app.ps1）
$winBase = Join-Path $staging 'WindowsBase.dll'
$needsFix = $true
if (Test-Path $winBase) { if ((Get-Item $winBase).Length -gt 1MB) { $needsFix = $false } }
if ($needsFix) {
    $desktopShared = Get-ChildItem "$env:ProgramFiles\dotnet\shared\Microsoft.WindowsDesktop.App" -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -First 1
    if ($desktopShared -and (Test-Path (Join-Path $desktopShared.FullName 'WindowsBase.dll'))) {
        Write-Output "[$(Get-Date)] 修复 WPF 运行时程序集 ..."
        Copy-Item -Path (Join-Path $desktopShared.FullName '*') -Destination $staging -Recurse -Force
    } else {
        Write-Warning "WindowsBase.dll facade 检测但无共享框架可修复，EXE 可能启动失败。"
    }
}

# 3) 发布 MCP 桥接（与 UI 同目录，便于 UI 自动拉起）
Write-Output "[$(Get-Date)] Publishing MCP bridge ..."
& $dotnet publish $mcpProj -c Release -r win-x64 --self-contained true -o $staging
if ($LASTEXITCODE -ne 0) { throw "MCP bridge publish failed: $LASTEXITCODE" }

# 4) 复制嵌入式 Python 运行时（tools/py），但【排除模型目录】
#    只排除 tools/py 顶层的 argos_packages/ 与 models/（模型数据单独打包分发）。
#    ⚠ 不能按「目录名」递归排除：Lib/site-packages 下有多处叫 models 的**代码目录**
#      （transformers\models 4550 个文件、stanza\models、onnxruntime\transformers\models 等），
#      误删会让 `import transformers` 直接抛
#      FileNotFoundError: ...\transformers\models → LibreTranslate 进程退出(1)、NLLB 翻译 500。
function Copy-PyRuntime($src, $dst, [switch]$Root) {
    if (-not (Test-Path $dst)) { New-Item -ItemType Directory -Force -Path $dst | Out-Null }
    foreach ($item in Get-ChildItem $src) {
        if ($item.PSIsContainer) {
            if ($Root -and ($item.Name -eq 'argos_packages' -or $item.Name -eq 'models')) { continue }
            Copy-PyRuntime $item.FullName (Join-Path $dst $item.Name)
        } else {
            Copy-Item $item.FullName -Destination $dst -Force
        }
    }
}
$pySrc = Join-Path $repoRoot 'tools\py'
$pyDst = Join-Path $staging 'tools\py'
if (Test-Path $pySrc) {
    Write-Output "[$(Get-Date)] 复制嵌入式 Python 运行时（仅排除顶层 argos_packages / models）..."
    Copy-PyRuntime $pySrc $pyDst -Root
} else {
    Write-Warning "tools\py 不存在，本地 LibreTranslate/NLLB 引擎将不可用。"
}

# 4.4) 补齐 VC++ 运行时（让绿色版在没有装 VC++ 2015-2022 可再发行组件的电脑上也能跑）
#      torch / onnxruntime / tokenizers 等原生扩展都静态依赖 MSVCP140.dll + VCRUNTIME140.dll。
#      python.exe 自带 vcruntime140(_1).dll，但**不带 msvcp140.dll**，干净系统上会报
#      "DLL load failed: 找不到指定的模块"。把系统里的这几个 DLL 复制到 tools\py
#      （= python.exe 所在目录，在 DLL 搜索路径里），即可免除外部依赖。
#      注：这些文件属于 MSVC 可再发行组件，允许随应用一起分发。
$vcDlls = @('msvcp140.dll','msvcp140_1.dll','msvcp140_2.dll','concrt140.dll','vcomp140.dll','vcruntime140.dll','vcruntime140_1.dll')
$vcSrc = Join-Path $env:WINDIR 'System32'
$vcCopied = 0
foreach ($n in $vcDlls) {
    $s = Join-Path $vcSrc $n
    if (Test-Path $s) {
        Copy-Item $s -Destination $pyDst -Force
        $vcCopied++
    } else {
        Write-Warning "系统缺少 $n，绿色版在未安装 VC++ 2015-2022 运行库的电脑上可能无法加载 torch。"
    }
}
Write-Output "[$(Get-Date)] 已随包附带 $vcCopied 个 VC++ 运行时 DLL。"

# 4.5) 复制集成的 ODA File Converter（免安装、可再分发；DWG/DXF 转换开箱即用）
$odaSrc = Join-Path $repoRoot 'tools\ODAFileConverter'
if (Test-Path $odaSrc) {
    Write-Output "[$(Get-Date)] 复制 ODA File Converter ..."
    Copy-Item -Path $odaSrc -Destination (Join-Path $staging 'tools') -Recurse -Force
} else {
    Write-Warning "tools\ODAFileConverter 不存在，绿色版将不含 ODA（DWG 转换需用户另装）。"
}

# 5) 复制脚本与文档（不含模型）
Write-Output "[$(Get-Date)] 复制脚本与文档 ..."
$copied = 0
foreach ($ps in Get-ChildItem (Join-Path $repoRoot 'tools') -Filter *.ps1) {
    Copy-Item $ps.FullName -Destination (Join-Path $staging 'tools') -Force
    $copied++
}
Write-Output "[$(Get-Date)] 已复制 $copied 个 tools 脚本。"

if (Test-Path (Join-Path $repoRoot 'docs')) {
    Copy-Item -Path (Join-Path $repoRoot 'docs') -Destination $staging -Recurse -Force
}
if (Test-Path (Join-Path $repoRoot 'README.md')) {
    Copy-Item -Path (Join-Path $repoRoot 'README.md') -Destination $staging -Force
}

# 5.5) 打包前清理：dotnet publish 会经 csproj 的 CopyBergamotModels 目标把 Bergamot 模型
#      一并发布到暂存目录。绿色版不含模型，这里显式删除，避免泄漏（argos/nllb 已在复制 py 时排除）。
foreach ($md in @((Join-Path $staging 'bergamot'), (Join-Path $staging 'tools\bergamot'), (Join-Path $staging 'tools\py\argos_packages'), (Join-Path $staging 'tools\py\models'))) {
    if (Test-Path $md) { Remove-Item $md -Recurse -Force; Write-Output ("[$(Get-Date)] 已从绿色版移除: " + $md) }
}

# 5.6) 完整性自检：确认 site-packages 里的「代码目录」没有被误排除
#      （历史上曾因为按目录名递归排除 models，把 transformers\models 一起删掉，
#        导致解压后 LibreTranslate 启动即崩、NLLB 翻译 500）
$critical = @(
    'tools\py\Lib\site-packages\transformers\models',
    'tools\py\Lib\site-packages\transformers\__init__.py',
    'tools\py\Lib\site-packages\stanza\models',
    'tools\py\Lib\site-packages\argostranslate\sbd.py',
    'tools\py\nllb_server.py',
    'tools\py\libretranslate_server.py'
)
$missing = @()
foreach ($rel in $critical) {
    if (-not (Test-Path (Join-Path $staging $rel))) { $missing += $rel }
}
if ($missing.Count -gt 0) {
    Write-Error ("绿色版缺少关键文件，已中止打包：`n  " + ($missing -join "`n  "))
    exit 1
}
Write-Output "[$(Get-Date)] 完整性自检通过（transformers\models 等关键目录齐全）。"

# 6) 打包为 ZIP（根目录即软件文件，解压即跑）。用 ZipFile 以支持大文件/大体积。
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Write-Output "[$(Get-Date)] 压缩为 $zipPath ..."
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Output ""
Write-Output "PORTABLE_BUILD_DONE -> $zipPath"
Write-Output "提示：模型不包含在内。请用 tools/package_all_models.ps1 单独打包，由用户解压到本地模型目录。"
