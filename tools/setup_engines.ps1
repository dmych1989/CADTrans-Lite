# tools/py/setup_engines.ps1
# Provisions the Argos Translate (local) and LibreTranslate (local) engines for CADTrans Lite and
# BUNDLES their language packs + MiniSBD sentence-splitter models into the repo so the app never
# downloads anything at runtime.
#
# Bundled artifacts (all under tools\py, so they are copied into the build output and ship):
#   tools\py\argos_packages\            -> Argos translate packages (en<->zh/ja/ko/fr/de/es/ru/pt/it)
#   tools\py\argos-translate\minisbd\   -> MiniSBD ONNX sentence-splitter models
#
# The .NET host (LocalServerHelper) sets ARGOS_PACKAGES_DIR / ARGOS_CHUNK_TYPE=MINISBD /
# XDG_DATA_HOME at launch, so the servers load everything from these bundled dirs, fully offline.
#
# Usage:
#   .\tools\py\setup_engines.ps1              # install engines + all language packs + minisbd
#   .\tools\py\setup_engines.ps1 -NoModel     # install the python engines only (skip packs)
#   .\tools\py\setup_engines.ps1 -Index https://pypi.tuna.tsinghua.edu.cn/simple   # use a PyPI mirror

param(
    [switch]$NoModel = $false,
    [string]$Index = ""
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$pyDir     = $scriptDir
$python    = Join-Path $pyDir "python.exe"

# ---------------------------------------------------------------------------
# Provision the embedded Python (reuse the same approach as setup_nllb.ps1)
# ---------------------------------------------------------------------------
if (-not (Test-Path $python)) {
    Write-Host "Embedded Python not found, downloading Windows embeddable Python 3.11 ..." -ForegroundColor Cyan
    $EmbedVersion = "3.11.9"
    $EmbedUrl     = "https://www.python.org/ftp/python/$EmbedVersion/python-$EmbedVersion-embed-amd64.zip"
    $PythonZip    = Join-Path $pyDir "python-embed.zip"
    Invoke-WebRequest -Uri $EmbedUrl -OutFile $PythonZip -UseBasicParsing
    Expand-Archive -Path $PythonZip -DestinationPath $pyDir -Force
    Remove-Item $PythonZip
    $Cfg = Join-Path $pyDir "python311._pth"
    if (Test-Path $Cfg) {
        (Get-Content $Cfg) -replace '^#import site', 'import site' | Set-Content $Cfg
    }
    Write-Host "Embedded Python ready: $python" -ForegroundColor Green
}

$GetPip = Join-Path $pyDir "get-pip.py"
if (-not (Test-Path $GetPip)) {
    Invoke-WebRequest -Uri "https://bootstrap.pypa.io/get-pip.py" -OutFile $GetPip -UseBasicParsing
}
& $python $GetPip --no-warn-script-location | Out-Null

# ---------------------------------------------------------------------------
# Install the engines
# ---------------------------------------------------------------------------
$pipArgs = @("-m", "pip", "install", "--upgrade", "argostranslate", "libretranslate")
if ($Index -ne "") { $pipArgs += @("--index-url", $Index) }
Write-Host "Installing argostranslate + libretranslate ..." -ForegroundColor Cyan
& $python @pipArgs | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Engine install failed (exit code $LASTEXITCODE)." -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "Engines installed." -ForegroundColor Green

if ($NoModel) {
    Write-Host "Skipped language packs (-NoModel)." -ForegroundColor Yellow
    exit 0
}

# ---------------------------------------------------------------------------
# Bundle language packs + MiniSBD models into tools\py
# ---------------------------------------------------------------------------
$env:ARGOS_PACKAGES_DIR = Join-Path $pyDir "argos_packages"
$env:XDG_DATA_HOME      = $pyDir
New-Item -ItemType Directory -Force -Path $env:ARGOS_PACKAGES_DIR | Out-Null

Write-Host "Installing Argos language packs (en<->zh/ja/ko/fr/de/es/ru/pt/it) ..." -ForegroundColor Cyan
Write-Host "(this downloads ~2 GB of models once; they then ship with the software)" -ForegroundColor White

$tmpPy = Join-Path $env:TEMP "engines_bundle.py"
Set-Content -Path $tmpPy -Value @'
import os
from argostranslate.package import (
    update_package_index, get_available_packages, get_installed_packages,
)

update_package_index()
avail = {(p.from_code, p.to_code): p for p in get_available_packages()}
installed = {(p.from_code, p.to_code) for p in get_installed_packages()}

pairs = [
    ("en", "zh"), ("zh", "en"),
    ("en", "ja"), ("ja", "en"),
    ("en", "ko"), ("ko", "en"),
    ("en", "fr"), ("fr", "en"),
    ("en", "de"), ("de", "en"),
    ("en", "es"), ("es", "en"),
    ("en", "ru"), ("ru", "en"),
    ("en", "pt"), ("pt", "en"),
    ("en", "it"), ("it", "en"),
]

ok = 0
for f, t in pairs:
    if (f, t) in installed:
        print("SKIP (installed)", f, "->", t, flush=True)
        ok += 1
        continue
    pkg = avail.get((f, t))
    if pkg is None:
        print("MISSING", f, "->", t, flush=True)
        continue
    try:
        print("installing", f, "->", t, flush=True)
        pkg.install()
        print("OK", f, "->", t, flush=True)
        ok += 1
    except Exception as e:
        print("FAIL", f, "->", t, ":", e, flush=True)

# Bundle MiniSBD sentence-splitter ONNX models (no HuggingFace/Stanza download at runtime).
from minisbd import models as minisbd_models
minisbd_models.cache_dir = os.path.join(os.environ["XDG_DATA_HOME"], "argos-translate", "minisbd")
os.makedirs(minisbd_models.cache_dir, exist_ok=True)
langs = ["en", "zh-hans", "ja", "ko", "fr", "de", "es", "ru", "pt", "it"]
print("downloading MiniSBD models:", langs, flush=True)
minisbd_models.download_models(load_only=langs)

print("ENGINES_BUNDLE_DONE ok=%d/%d" % (ok, len(pairs)))
'@

& $python $tmpPy
if ($LASTEXITCODE -ne 0) {
    Write-Host "Language pack / MiniSBD bundling failed (exit code $LASTEXITCODE)." -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "Argos Translate + LibreTranslate are ready and fully offline." -ForegroundColor Green
Write-Host "Packs: $env:ARGOS_PACKAGES_DIR" -ForegroundColor White
Write-Host "MiniSBD: $(Join-Path $pyDir 'argos-translate\minisbd')" -ForegroundColor White
Write-Host "In CADTrans Lite, select 'Argos Translate (local)' or 'LibreTranslate (local)' and click Test." -ForegroundColor White
