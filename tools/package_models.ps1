# tools/package_models.ps1
# Download Argos Translate models and package EACH one into its own .zip, so users can grab only
# the language pairs they need and drop them into the app's argos_packages folder.
#
# Each output zip contains a single file, e.g.:
#   tools\models\translate-en_zh-1_9.zip  ->  translate-en_zh-1_9.argosmodel
#
# Where the app looks for them at runtime:
#   <directory of CADTransLite.UI.exe>\argos_packages\<file>.argosmodel
# (The .NET host sets ARGOS_PACKAGES_DIR = <exe dir>\argos_packages via LocalServerHelper.)
#
# Usage:
#   .\tools\package_models.ps1                 # interactive pick
#   .\tools\package_models.ps1 -All            # download + package every available pair
#   .\tools\package_models.ps1 -Pairs en_zh,zh_en,en_es   # specific pairs only
#   .\tools\package_models.ps1 -ListOnly       # just list pairs, package nothing
#   .\tools\package_models.ps1 -All -OutputDir D:\cad-models   # custom output dir
#   .\tools\package_models.ps1 -All -NoPackage             # download only, skip zipping
#
# Requires: tools\py\setup_engines.ps1 must have run once so the Python engine + argostranslate
# are installed (this script only fetches the .argosmodel files). The packaged zips are usable
# on any machine that already has the Python engine provisioned.

param(
    [string]$Pairs = "",
    [switch]$All = $false,
    [switch]$ListOnly = $false,
    [switch]$NoPackage = $false,
    [switch]$Force = $false,
    [string]$OutputDir = "",
    [string]$IndexUrl = "https://raw.githubusercontent.com/argosopentech/argospm-index/main/index.json"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
# The model CDN (argos-net.com) requires TLS 1.2.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

# ---- fetch + parse the official index --------------------------------------
function Get-ArgosIndex {
    param([string]$Url)
    Write-Host "Fetching Argos model index: $Url" -ForegroundColor Cyan
    $tmp = Join-Path $env:TEMP "argos_index.json"
    Invoke-WebRequest -Uri $Url -OutFile $tmp -UseBasicParsing -TimeoutSec 90
    $json = Get-Content -Encoding utf8 $tmp -Raw | ConvertFrom-Json
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

function Format-Pair { param($p); ("{0} -> {1}  ({2} -> {3}, v{4})" -f $p.from_code, $p.to_code, $p.from_name, $p.to_name, $p.package_version) }

if ($ListOnly) {
    $i = 1
    foreach ($p in $packages) { Write-Host ("{0,3}. {1}" -f $i, (Format-Pair $p)); $i++ }
    exit 0
}

# ---- decide which packages --------------------------------------------------
$selected = @()
if ($All) {
    $selected = $packages
} elseif ($Pairs -ne "") {
    $want = $Pairs -split ',' | ForEach-Object { $_.Trim().ToLower() } | Where-Object { $_ -ne '' }
    foreach ($w in $want) {
        $m = $packages | Where-Object { ("$($_.from_code)_$($_.to_code)") -eq $w }
        if ($m) { $selected += $m } else { Write-Host "  ! pair not found: $w" -ForegroundColor Yellow }
    }
} else {
    $i = 1
    foreach ($p in $packages) { Write-Host ("{0,3}. {1}" -f $i, (Format-Pair $p)); $i++ }
    Write-Host ""
    $ans = Read-Host "Enter ids/codes to package (e.g. '1,3,5' or 'en_zh,zh_en', or 'all')"
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

# ---- resolve staging dir (the bundled argos_packages) ----------------------
$pyDir = $null
$candidates = @(
    (Join-Path $scriptDir "py"),
    (Join-Path (Split-Path $scriptDir) "py"),
    $scriptDir
)
foreach ($c in $candidates) {
    if (Test-Path (Join-Path $c "python.exe")) { $pyDir = $c; break }
}
if ($pyDir) { $staging = Join-Path $pyDir "argos_packages" }
else { $staging = Join-Path $scriptDir "argos_packages" }
if (-not (Test-Path $staging)) { New-Item -ItemType Directory -Force -Path $staging | Out-Null }

# ---- resolve output dir for the per-model zips -----------------------------
if ($OutputDir -eq "") { $OutputDir = Join-Path $scriptDir "models" }
if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null }

Write-Host "Staging (download) dir : $staging" -ForegroundColor Cyan
Write-Host "Output (zip) dir       : $OutputDir" -ForegroundColor Cyan
Write-Host "Packaging $($selected.Count) model(s)..." -ForegroundColor Cyan

$ok = 0; $skip = 0; $fail = 0
foreach ($p in $selected) {
    $fname = "translate-$($p.from_code)_$($p.to_code)-$($p.package_version).argosmodel"
    $dest  = Join-Path $staging $fname
    $link  = if ($p.links -is [array]) { $p.links[0] } else { $p.links }

    # 1) download (skip if already staged, unless -Force)
    if ((Test-Path $dest) -and -not $Force) {
        Write-Host "  ~ $fname  (already staged, skipped download)" -ForegroundColor DarkGray
    } else {
        Write-Host "  + $fname  <- $link" -ForegroundColor White
        try {
            $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
            if ($curl) {
                & curl.exe -L --retry 3 --retry-delay 2 -o $dest $link
                if ($LASTEXITCODE -ne 0) { throw "curl exited with code $LASTEXITCODE" }
            } else {
                Invoke-WebRequest -Uri $link -OutFile $dest -UseBasicParsing -TimeoutSec 900
            }
            if (-not (Test-Path $dest) -or (Get-Item $dest).Length -eq 0) { throw "empty file" }
            Write-Host ("    downloaded ({0} MB)" -f [math]::Round((Get-Item $dest).Length / 1MB, 1)) -ForegroundColor Green
        } catch {
            Write-Host ("    DOWNLOAD FAILED: {0}" -f $_.Exception.Message) -ForegroundColor Red
            if (Test-Path $dest) { Remove-Item $dest -Force }
            $fail++
            continue
        }
    }

    # 2) package into its own zip (skip if zip exists, unless -Force)
    if ($NoPackage) { $ok++; continue }
    $zipName = [IO.Path]::GetFileNameWithoutExtension($fname) + ".zip"
    $zipPath = Join-Path $OutputDir $zipName
    if ((Test-Path $zipPath) -and -not $Force) {
        Write-Host "  ~ $zipName  (zip already exists, skipped)" -ForegroundColor DarkGray
        $skip++
        continue
    }
    try {
        # One .argosmodel per zip, stored flat (no subfolder).
        Compress-Archive -Path $dest -DestinationPath $zipPath -Force
        Write-Host ("    packaged -> $zipName ({0} MB)" -f [math]::Round((Get-Item $zipPath).Length / 1MB, 1)) -ForegroundColor Green
        $ok++
    } catch {
        Write-Host ("    ZIP FAILED: {0}" -f $_.Exception.Message) -ForegroundColor Red
        if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
        $fail++
    }
}

Write-Host ""
Write-Host ("Finished. packaged={0} skipped={1} failed={2}" -f $ok, $skip, $fail) -ForegroundColor Green
Write-Host "Per-model zips in: $OutputDir" -ForegroundColor White
Write-Host "To use: extract a zip and put the .argosmodel into <app dir>\argos_packages\ ." -ForegroundColor White
