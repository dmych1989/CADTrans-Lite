# tools/package_bergamot.ps1
# 将本地 Bergamot 模型目录打包为单一 zip（tools/models/bergamot-models.zip）。
# 解压后结构：<模型根目录>/bergamot/…  与软件“自定义模型目录”约定一致。
#
# Usage:
#   .\tools\package_bergamot.ps1                 # 默认输出到 tools/models/
#   .\tools\package_bergamot.ps1 -OutputDir D:\cad-models
#   .\tools\package_bergamot.ps1 -Force          # 覆盖已存在的 zip
#
# 前置：tools/bergamot 已由 tools/setup_bergamot.ps1 下载就绪。

param(
    [string]$OutputDir = "",
    [switch]$Force = $false
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot  = Split-Path -Parent $scriptDir
$src       = Join-Path $repoRoot 'tools\bergamot'

if (-not (Test-Path $src)) {
    Write-Warning "tools\bergamot 不存在，无法打包。请先运行 tools\setup_bergamot.ps1 下载模型。"
    exit 1
}

if ($OutputDir -eq '') { $OutputDir = Join-Path $scriptDir 'models' }
if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null }

$zip = Join-Path $OutputDir 'bergamot-models.zip'
if ((Test-Path $zip) -and -not $Force) {
    Write-Host "已存在，跳过：$zip（用 -Force 覆盖）" -ForegroundColor Yellow
    exit 0
}
if (Test-Path $zip) { Remove-Item $zip -Force }

# Compress-Archive 传入目录时，目录本身作为 zip 根条目，解压即得到 bergamot/…
Compress-Archive -Path $src -DestinationPath $zip -Force
Write-Host "已打包 Bergamot 模型 -> $zip" -ForegroundColor Green
