# tools/package_libretranslate.ps1
# 将本地 Argos / LibreTranslate 语言包目录打包为单一 zip（tools/models/libretranslate-models.zip）。
# 解压后结构：<模型根目录>/argos_packages/…  与软件“自定义模型目录”约定一致。
#
# 说明：每个语言对的独立 .argosmodel 也可用 tools/package_models.ps1 单独打包；
#       本脚本把已就绪的整个 argos_packages 目录打包成一个“渠道包”，便于一次性分发。
#
# Usage:
#   .\tools\package_libretranslate.ps1                 # 默认输出到 tools/models/
#   .\tools\package_libretranslate.ps1 -OutputDir D:\cad-models
#   .\tools\package_libretranslate.ps1 -Force
#
# 前置：tools/py/argos_packages 已由 tools/setup_engines.ps1 安装就绪。

param(
    [string]$OutputDir = "",
    [switch]$Force = $false
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot  = Split-Path -Parent $scriptDir
$src       = Join-Path $repoRoot 'tools\py\argos_packages'

if (-not (Test-Path $src)) {
    Write-Warning "tools\py\argos_packages 不存在，无法打包。请先运行 tools\setup_engines.ps1 安装语言包。"
    exit 1
}

if ($OutputDir -eq '') { $OutputDir = Join-Path $scriptDir 'models' }
if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null }

$zip = Join-Path $OutputDir 'libretranslate-models.zip'
if ((Test-Path $zip) -and -not $Force) {
    Write-Host "已存在，跳过：$zip（用 -Force 覆盖）" -ForegroundColor Yellow
    exit 0
}
if (Test-Path $zip) { Remove-Item $zip -Force }

# 传入目录时，目录本身作为 zip 根条目，解压即得到 argos_packages/…
Compress-Archive -Path $src -DestinationPath $zip -Force
Write-Host "已打包 LibreTranslate/Argos 语言包 -> $zip" -ForegroundColor Green
