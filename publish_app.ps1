$ErrorActionPreference = 'Stop'

# Self-contained publish for CADTrans Lite (no .NET runtime needed on the user's PC).
# Bundles the .NET 9 runtime into publish/ via the Release self-contained config in
# CADTransLite.UI.csproj (RuntimeIdentifier=win-x64, SelfContained=true).

$dotnet = "$env:LOCALAPPDATA\dotnet9\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$srcDir   = Join-Path $repoRoot 'src'
$uiProj   = Join-Path $srcDir 'CADTransLite.UI\CADTransLite.UI.csproj'
$publishDir = Join-Path $repoRoot 'publish'

$env:PATH = "$env:LOCALAPPDATA\dotnet9;$env:PATH"

Write-Output "[$(Get-Date)] Restoring ..."
& $dotnet restore $uiProj
if ($LASTEXITCODE -ne 0) { throw "restore failed: $LASTEXITCODE" }

Write-Output "[$(Get-Date)] Publishing (Release, self-contained, win-x64) ..."
& $dotnet publish $uiProj -c Release -r win-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "publish failed: $LASTEXITCODE" }

# Copy Bergamot offline models if provisioned at the repo root.
$bergSrc = Join-Path $repoRoot 'tools\bergamot'
$bergDst = Join-Path $publishDir 'tools\bergamot'
if (Test-Path $bergSrc) {
    Write-Output "[$(Get-Date)] Copying Bergamot models ..."
    if (-not (Test-Path $bergDst)) { New-Item -ItemType Directory -Path $bergDst | Out-Null }
    Copy-Item -Path (Join-Path $bergSrc '*') -Destination $bergDst -Recurse -Force
} else {
    Write-Output "[$(Get-Date)] (跳过) tools\bergamot 不存在，本地翻译模型未下载。可运行 tools\setup_bergamot.ps1 获取。"
}

Write-Output "[$(Get-Date)] PUBLISH_DONE -> $publishDir"
