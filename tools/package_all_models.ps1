# tools/package_all_models.ps1
# 一键把三个"本地离线引擎"的模型分别打成独立 zip（按渠道）：
#   bergamot-models.zip          (Bergamot)
#   libretranslate-models.zip    (LibreTranslate / Argos 语言包)
#   nllb-model.zip               (NLLB-200 蒸馏模型)
# 输出默认到 tools/models/，可用 -OutputDir 改到自定义目录。
#
# 这些 zip 与软件"翻译 API 设置 → 本地模型目录"配合使用：
#   把三个 zip 解压到同一个"模型根目录"下，软件即从此处加载模型，无需把模型放进程序目录。
#
# Usage:
#   .\tools\package_all_models.ps1
#   .\tools\package_all_models.ps1 -OutputDir D:\cad-models -Force

param(
    [string]$OutputDir = "",
    [switch]$Force = $false
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$here = $PSScriptRoot
$argsList = @()
if ($OutputDir -ne '') { $argsList += @{ OutputDir = $OutputDir } }
if ($Force) { $argsList += @{ Force = $true } }

& "$here\package_bergamot.ps1" @argsList
& "$here\package_libretranslate.ps1" @argsList
& "$here\package_nllb.ps1" @argsList

Write-Host ""
Write-Host "全部渠道模型打包完成。请把 tools/models/ 下的三个 zip 分发给用户，" -ForegroundColor Green
Write-Host "用户把它们解压到同一个目录，并在软件"本地模型目录"中指定该目录即可。" -ForegroundColor Green
