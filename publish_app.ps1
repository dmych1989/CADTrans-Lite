$ErrorActionPreference = 'Stop'

# Self-contained publish for CADTrans Lite (no .NET runtime needed on the user's PC).
# Bundles the .NET 9 runtime into publish/ via the Release self-contained config in
# CADTransLite.UI.csproj (RuntimeIdentifier=win-x64, SelfContained=true).

$dotnet = "$env:LOCALAPPDATA\dotnet9\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$srcDir   = Join-Path $repoRoot 'src'
$uiProj   = Join-Path $srcDir 'CADTransLite.UI\CADTransLite.UI.csproj'
$mcpProj  = Join-Path $srcDir 'CADTransLite.McpServer\CADTransLite.McpServer.csproj'
$publishDir = Join-Path $repoRoot 'publish'

$env:PATH = "$env:LOCALAPPDATA\dotnet9;$env:PATH"

Write-Output "[$(Get-Date)] Restoring ..."
& $dotnet restore $uiProj
if ($LASTEXITCODE -ne 0) { throw "restore failed: $LASTEXITCODE" }

Write-Output "[$(Get-Date)] Publishing (Release, self-contained, win-x64) ..."
& $dotnet publish $uiProj -c Release -r win-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "publish failed: $LASTEXITCODE" }

# HARDENING: ensure the real WPF runtime assemblies are present.
# On machines where the Microsoft.WindowsDesktop.App.Runtime pack is missing, `dotnet publish`
# can accidentally drop the 15.8 KB reference-assembly facade of WindowsBase.dll (instead of the
# ~2.1 MB real implementation), which makes the self-contained EXE fail at startup with
# "Could not load file or assembly 'WindowsBase'". Copy the genuine implementation assemblies
# from the shared desktop framework (same 9.0.x version) over the output to guarantee correctness.
$winBase = Join-Path $publishDir 'WindowsBase.dll'
$needsFix = $true
if (Test-Path $winBase) {
    $wbSize = (Get-Item $winBase).Length
    # real implementation is > 1 MB; the facade is ~16 KB
    if ($wbSize -gt 1MB) { $needsFix = $false }
}
if ($needsFix) {
    $desktopShared = Get-ChildItem "$env:ProgramFiles\dotnet\shared\Microsoft.WindowsDesktop.App" -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -First 1
    if ($desktopShared -and (Test-Path (Join-Path $desktopShared.FullName 'WindowsBase.dll'))) {
        Write-Output "[$(Get-Date)] Fixing WPF runtime assemblies from shared framework: $($desktopShared.FullName)"
        Copy-Item -Path (Join-Path $desktopShared.FullName '*') -Destination $publishDir -Recurse -Force
    } else {
        Write-Warning "WindowsBase.dll facade detected but no shared WindowsDesktop framework found to fix it. The EXE may fail to start."
    }
}

# Publish the MCP bridge (CADTransLite.McpBridge.exe) into the SAME publish/ folder so the
# UI can auto-launch it from its own directory (TryStartMcpBridge checks AppContext.BaseDirectory first).
Write-Output "[$(Get-Date)] Publishing MCP bridge (CADTransLite.McpBridge.exe) ..."
& $dotnet publish $mcpProj -c Release -r win-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "mcp bridge publish failed: $LASTEXITCODE" }

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
