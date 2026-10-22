# tools/package_nllb.ps1
# 将本地 NLLB 模型打包为单一 zip（tools/models/nllb-model.zip）。
# 解压后结构：<模型根目录>/models/nllb-200-distilled-600M/…  与软件“自定义模型目录”约定一致。
#
# Usage:
#   .\tools\package_nllb.ps1                 # 默认输出到 tools/models/
#   .\tools\package_nllb.ps1 -OutputDir D:\cad-models
#   .\tools\package_nllb.ps1 -Force
#
# 前置：tools/py/models/nllb-200-distilled-600M 已由 tools/setup_nllb.ps1 下载就绪。

param(
    [string]$OutputDir = "",
    [switch]$Force = $false
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot  = Split-Path -Parent $scriptDir
$src       = Join-Path $repoRoot 'tools\py\models\nllb-200-distilled-600M'

if (-not (Test-Path $src)) {
    Write-Warning "tools\py\models\nllb-200-distilled-600M 不存在，无法打包。请先运行 tools\setup_nllb.ps1 下载模型。"
    exit 1
}

if ($OutputDir -eq '') { $OutputDir = Join-Path $scriptDir 'models' }
if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null }

$zip = Join-Path $OutputDir 'nllb-model.zip'
if ((Test-Path $zip) -and -not $Force) {
    Write-Host "已存在，跳过：$zip（用 -Force 覆盖）" -ForegroundColor Yellow
    exit 0
}
if (Test-Path $zip) { Remove-Item $zip -Force }

# 暂存为 models/nllb-200-distilled-600M 结构，使 zip 顶层为 models/，
# 解压后正好落在 <模型根目录>/models/nllb-200-distilled-600M/。
$tmp      = Join-Path $env:TEMP ('nllb_stage_' + [Guid]::NewGuid().ToString('N'))
$tmpModel = Join-Path $tmp 'models\nllb-200-distilled-600M'
New-Item -ItemType Directory -Force -Path $tmpModel | Out-Null
Copy-Item -Path (Join-Path $src '*') -Destination $tmpModel -Recurse -Force
# 压缩为 models/nllb-200-distilled-600M/ 顶层结构（与“自定义模型目录”约定一致：
# <根目录>/models/nllb-200-distilled-600M）。
# 说明：NLLB 模型含 >2GB 单体文件，Windows PowerShell 5.1 的 Compress-Archive 会报“流太长”，
# 故改用 .NET ZipFile.CreateFromDirectory（支持大文件；includeBaseDirectory=false 使顶层为 models/）。
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($tmp, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item $tmp -Recurse -Force

Write-Host "已打包 NLLB 模型 -> $zip" -ForegroundColor Green
