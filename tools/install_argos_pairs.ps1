# tools/install_argos_pairs.ps1
# 把 translate-*.zip（LibreTranslate/Argos 语言对，双层压缩：zip -> .argosmodel）自动
# 安装到 argos_packages 目录。可安装到程序默认位置，也可安装到自定义模型根目录下。
#
# Usage:
#   .\tools\install_argos_pairs.ps1
#       （安装到仓库默认 tools\py\argos_packages\）
#   .\tools\install_argos_pairs.ps1 -PackagesDir "D:\CADModels\argos_packages"
#       （安装到自定义模型根目录下的 argos_packages\，配合软件“本地模型目录”设置）
#   .\tools\install_argos_pairs.ps1 -ZipsDir "D:\下载" -PackagesDir "D:\CADModels\argos_packages"

param(
    [string]$ZipsDir = "",
    [string]$PackagesDir = ""
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot  = Split-Path -Parent $scriptDir

if ($ZipsDir -eq '')     { $ZipsDir     = Join-Path $repoRoot 'tools\models' }
if ($PackagesDir -eq '') { $PackagesDir = Join-Path $repoRoot 'tools\py\argos_packages' }

Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Force -Path $PackagesDir | Out-Null

$zips = Get-ChildItem $ZipsDir -Filter 'translate-*.zip' -ErrorAction SilentlyContinue
if (-not $zips) {
    Write-Warning ("Not found translate-*.zip in: " + $ZipsDir)
    exit 1
}

$installed = 0
foreach ($z in $zips) {
    $tmp = Join-Path $env:TEMP ('argos_inst_' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    try {
        # 第一层：zip -> 取出里面的 .argosmodel
        [System.IO.Compression.ZipFile]::ExtractToDirectory($z.FullName, $tmp)
        $am = Get-ChildItem $tmp -Recurse -Filter '*.argosmodel' | Select-Object -First 1
        if (-not $am) { Write-Warning ("no .argosmodel inside, skip: " + $z.Name); continue }

        # 第二层：.argosmodel（也是 zip）-> 解到暂存区
        $stage = Join-Path $tmp 'stage'
        New-Item -ItemType Directory -Force -Path $stage | Out-Null
        [System.IO.Compression.ZipFile]::ExtractToDirectory($am.FullName, $stage)

        # .argosmodel 内部常带一层与包同名的顶层目录，若如此则取该目录为包根
        $top = Get-ChildItem $stage -Force
        $pkgRoot = $stage
        if (($top | Measure-Object).Count -eq 1 -and $top[0].PSIsContainer) { $pkgRoot = $top[0].FullName }

        # 目录名：优先用包内自带名（如 translate-en_zh-1_9），否则由文件名规范化
        $dirName = if ($pkgRoot -ne $stage) { Split-Path -Leaf $pkgRoot } else { $am.BaseName -replace '\.', '_' }
        $dest = Join-Path $PackagesDir $dirName
        if (Test-Path $dest) { Write-Host ("exists, skip: " + $dirName); continue }

        # 直接复制包根目录到目标名（目标不存在时即整体重命名复制，避免 src\* 的容器/叶项冲突）
        Copy-Item -Path $pkgRoot -Destination $dest -Recurse -Force
        if (-not (Test-Path (Join-Path $dest 'metadata.json'))) {
            Write-Warning ("metadata.json missing, may be invalid: " + $dirName)
        }
        Write-Host ("installed: " + $dirName)
        $installed++
    }
    finally {
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ("done, installed " + $installed + " pair(s). packages dir: " + $PackagesDir)
