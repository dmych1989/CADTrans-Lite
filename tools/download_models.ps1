# tools/download_models.ps1
# Download selected Argos Translate offline translation models (.argosmodel) for CADTrans Lite.
#
# Unlike tools/py/setup_engines.ps1 (which hard-codes 9 language pairs, ~2GB), this script lists
# ALL available language pairs from the official Argos package index (argospm-index) and lets you
# pick exactly which ones to download, so you only fetch the models you actually need.
#
# Models are saved into the bundled argos_packages directory that the .NET host points to via
# ARGOS_PACKAGES_DIR, so the app loads them fully offline at runtime.
#
# Usage:
#   .\tools\download_models.ps1                 # interactive: list pairs, then enter ids/codes to download
#   .\tools\download_models.ps1 -ListOnly       # just list available pairs, download nothing
#   .\tools\download_models.ps1 -All            # download every available pair
#   .\tools\download_models.ps1 -Pairs en_zh,zh_en,en_es,es_en   # specific pairs (from_to, comma-separated)
#   .\tools\download_models.ps1 -Pairs en_zh -OutputDir D:\models # custom output directory
#   .\tools\download_models.ps1 -IndexUrl <url> # use a custom / mirror index
#
# NOTE: you still need to run tools\py\setup_engines.ps1 once to install the Python engines
# (argostranslate / libretranslate) before the downloaded models can be used by the app.

param(
    [string]$Pairs = "",
    [switch]$All = $false,
    [switch]$ListOnly = $false,
    [switch]$Force = $false,
    [string]$OutputDir = "",
    [string]$IndexUrl = "https://raw.githubusercontent.com/argosopentech/argospm-index/main/index.json"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
# The model CDN (argos-net.com) requires TLS 1.2; force it so downloads don't fail with
# "The underlying connection was closed: An error occurred while sending.".
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

# ---- fetch + parse the official index --------------------------------------
function Get-ArgosIndex {
    param([string]$Url)
    Write-Host "Fetching Argos model index: $Url" -ForegroundColor Cyan
    $tmp = Join-Path $env:TEMP "argos_index.json"
    Invoke-WebRequest -Uri $Url -OutFile $tmp -UseBasicParsing -TimeoutSec 90
    $json = Get-Content -Encoding utf8 $tmp -Raw | ConvertFrom-Json
    # Keep only translate packages (both from_code and to_code present), de-dup per pair, take newest.
    $translate = $json | Where-Object {
        ($_.from_code -ne $null -and $_.from_code -ne '') -and
        ($_.to_code -ne $null -and $_.to_code -ne '')
    }
    $groups = $translate | Group-Object { "$($_.from_code)_$($_.to_code)" }
    $result = foreach ($g in $groups) {
        $ranked = $g.Group | ForEach-Object {
            $v = [version]'0.0'
            try { $v = [version]($_.package_version -replace '_', '.') } catch { }
            [PSCustomObject]@{ pkg = $_; ver = $v }
        } | Sort-Object ver -Descending | Select-Object -First 1
        $ranked.pkg
    }
    return $result
}

$packages = @(Get-ArgosIndex $IndexUrl)
Write-Host "Index loaded: $($packages.Count) language pairs available." -ForegroundColor Green

function Format-Pair {
    param($p)
    return ("{0} -> {1}  ({2} -> {3}, v{4})" -f $p.from_code, $p.to_code, $p.from_name, $p.to_name, $p.package_version)
}

if ($ListOnly) {
    $i = 1
    foreach ($p in $packages) { Write-Host ("{0,3}. {1}" -f $i, (Format-Pair $p)); $i++ }
    exit 0
}

# ---- decide which packages to download -------------------------------------
$selected = @()
if ($All) {
    $selected = $packages
} elseif ($Pairs -ne "") {
    $want = $Pairs -split ',' | ForEach-Object { $_.Trim().ToLower() } | Where-Object { $_ -ne '' }
    foreach ($w in $want) {
        $m = $packages | Where-Object { ("$($_.from_code)_$($_.to_code)") -eq $w }
        if ($m) { $selected += $m } else { Write-Host "  ! pair not found: $w (use -ListOnly to see available)" -ForegroundColor Yellow }
    }
} else {
    $i = 1
    foreach ($p in $packages) { Write-Host ("{0,3}. {1}" -f $i, (Format-Pair $p)); $i++ }
    Write-Host ""
    $ans = Read-Host "Enter ids/codes to download (e.g. '1,3,5' or 'en_zh,zh_en', or 'all')"
    if ($ans.Trim().ToLower() -eq 'all') {
        $selected = $packages
    } else {
        $tokens = $ans -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }
        foreach ($t in $tokens) {
            if ($t -match '^\d+$') {
                $idx = [int]$t
                if ($idx -ge 1 -and $idx -le $packages.Count) { $selected += $packages[$idx - 1] }
                else { Write-Host "  ! index out of range: $t" -ForegroundColor Yellow }
            } else {
                $m = $packages | Where-Object { ("$($_.from_code)_$($_.to_code)") -eq $t.ToLower() }
                if ($m) { $selected += $m } else { Write-Host "  ! pair not found: $t" -ForegroundColor Yellow }
            }
        }
    }
}

if ($selected.Count -eq 0) { Write-Host "No models selected, exiting." -ForegroundColor Yellow; exit 0 }

# ---- resolve output directory (the bundled argos_packages) -----------------
if ($OutputDir -eq "") {
    $pyDir = $null
    $candidates = @(
        (Join-Path $scriptDir "py"),
        (Join-Path (Split-Path $scriptDir) "py"),
        $scriptDir
    )
    foreach ($c in $candidates) {
        if (Test-Path (Join-Path $c "python.exe")) { $pyDir = $c; break }
    }
    if ($pyDir) { $OutputDir = Join-Path $pyDir "argos_packages" }
    else { $OutputDir = Join-Path $scriptDir "argos_packages" }
}
if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null }

Write-Host "Downloading $($selected.Count) model(s) into: $OutputDir" -ForegroundColor Cyan

$ok = 0; $skip = 0; $fail = 0
foreach ($p in $selected) {
    $fname = "translate-$($p.from_code)_$($p.to_code)-$($p.package_version).argosmodel"
    $dest = Join-Path $OutputDir $fname
    $link = if ($p.links -is [array]) { $p.links[0] } else { $p.links }
    if ((Test-Path $dest) -and -not $Force) {
        Write-Host "  ~ $fname  (already present, skipped)" -ForegroundColor DarkGray
        $skip++
        continue
    }
    Write-Host "  + $fname  <- $link" -ForegroundColor White
    try {
        # Prefer curl.exe (ships with Windows 10/11): shows a live progress bar, handles TLS
        # 1.2 / redirects automatically, and avoids long silent stretches that look like a hang.
        # Fall back to Invoke-WebRequest when curl is unavailable.
        $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
        if ($curl) {
            & curl.exe -L --retry 3 --retry-delay 2 -o $dest $link
            if ($LASTEXITCODE -ne 0) { throw "curl exited with code $LASTEXITCODE" }
        } else {
            Invoke-WebRequest -Uri $link -OutFile $dest -UseBasicParsing -TimeoutSec 900
        }
        if (-not (Test-Path $dest) -or (Get-Item $dest).Length -eq 0) { throw "download produced an empty file" }
        Write-Host ("    done ({0} MB)" -f [math]::Round((Get-Item $dest).Length / 1MB, 1)) -ForegroundColor Green
        $ok++
    } catch {
        Write-Host ("    FAILED: {0}" -f $_.Exception.Message) -ForegroundColor Red
        if (Test-Path $dest) { Remove-Item $dest -Force }
        $fail++
    }
}

Write-Host ""
Write-Host ("Finished. downloaded={0} skipped={1} failed={2}" -f $ok, $skip, $fail) -ForegroundColor Green
Write-Host "Model directory: $OutputDir" -ForegroundColor White
Write-Host "Tip: in CADTrans Lite, select the 'Argos Translate (local)' engine to use them offline." -ForegroundColor White
