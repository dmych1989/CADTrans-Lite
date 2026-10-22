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
#    argos_packages/ 与 models/ 属于模型数据，单独打包分发，不进绿色版。
function Copy-PyRuntime($src, $dst) {
    if (-not (Test-Path $dst)) { New-Item -ItemType Directory -Force -Path $dst | Out-Null }
    foreach ($item in Get-ChildItem $src) {
        if ($item.PSIsContainer) {
            if ($item.Name -eq 'argos_packages' -or $item.Name -eq 'models') { continue }
            Copy-PyRuntime $item.FullName (Join-Path $dst $item.Name)
        } else {
            Copy-Item $item.FullName -Destination $dst -Force
        }
    }
}
$pySrc = Join-Path $repoRoot 'tools\py'
$pyDst = Join-Path $staging 'tools\py'
if (Test-Path $pySrc) {
    Write-Output "[$(Get-Date)] 复制嵌入式 Python 运行时（排除 argos_packages / models）..."
    Copy-PyRuntime $pySrc $pyDst
} else {
    Write-Warning "tools\py 不存在，本地 LibreTranslate/NLLB 引擎将不可用。"
}

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

# 6) 打包为 ZIP（根目录即软件文件，解压即跑）。用 ZipFile 以支持大文件/大体积。
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Write-Output "[$(Get-Date)] 压缩为 $zipPath ..."
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Output ""
Write-Output "PORTABLE_BUILD_DONE -> $zipPath"
Write-Output "提示：模型不包含在内。请用 tools/package_all_models.ps1 单独打包，由用户解压到本地模型目录。"
