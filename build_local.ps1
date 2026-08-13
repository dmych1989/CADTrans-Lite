$ErrorActionPreference = 'Stop'
$dotnet = "$env:LOCALAPPDATA\dotnet9\dotnet.exe"
$sln = "d:/GitHub/CADTrans Lite/src/CADTransLite.sln"

# Make local dotnet take precedence on PATH for the whole session
$env:PATH = "$env:LOCALAPPDATA\dotnet9;$env:PATH"

Write-Output "[$(Get-Date)] Restoring ..."
& $dotnet restore $sln
if ($LASTEXITCODE -ne 0) { throw "restore failed: $LASTEXITCODE" }

Write-Output "[$(Get-Date)] Building (Debug) ..."
& $dotnet build $sln -c Debug --no-restore
if ($LASTEXITCODE -ne 0) { throw "build failed: $LASTEXITCODE" }

Write-Output "[$(Get-Date)] BUILD_DONE"
