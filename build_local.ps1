$ErrorActionPreference = 'Stop'

# CADTrans-Lite 构建脚本（可移植：路径从本文件位置推导）
$localDotnet = Join-Path $env:LOCALAPPDATA 'dotnet9\dotnet.exe'
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { 'dotnet' }
$sln = Join-Path $PSScriptRoot 'src\CADTransLite.sln'

# 让本地 dotnet 在本次会话 PATH 中优先
$env:PATH = "$env:LOCALAPPDATA\dotnet9;$env:PATH"

Write-Output "[$(Get-Date)] Restoring ..."
& $dotnet restore $sln
if ($LASTEXITCODE -ne 0) { throw "restore failed: $LASTEXITCODE" }

Write-Output "[$(Get-Date)] Building (Debug) ..."
& $dotnet build $sln -c Debug --no-restore
if ($LASTEXITCODE -ne 0) { throw "build failed: $LASTEXITCODE" }

Write-Output "[$(Get-Date)] BUILD_DONE"
