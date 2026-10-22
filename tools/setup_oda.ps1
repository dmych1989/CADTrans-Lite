# tools/setup_oda.ps1
# Copy the locally installed ODA File Converter (freely redistributable, runs portably)
# into the repo at tools/ODAFileConverter/ so build_portable.ps1 can bundle it with the
# green (portable) package. The app auto-detects tools/ODAFileConverter at runtime.
#
# Usage:
#   .\tools\setup_oda.ps1                                   # default source below
#   .\tools\setup_oda.ps1 -Source "C:\Program Files\ODA\ODAFileConverter 24.11.0"

param(
    [string]$Source = "D:\Program Files\ODA\ODAFileConverter 24.11.0"
)

$ErrorActionPreference = 'Stop'

$dest = Join-Path $PSScriptRoot 'ODAFileConverter'
if (-not (Test-Path (Join-Path $Source 'ODAFileConverter.exe'))) {
    Write-Warning ("Source not found or missing ODAFileConverter.exe: " + $Source)
    exit 1
}

New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item -Path (Join-Path $Source '*') -Destination $dest -Recurse -Force
Write-Host ("ODA File Converter copied -> " + $dest)
