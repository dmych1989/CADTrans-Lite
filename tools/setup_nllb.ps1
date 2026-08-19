# tools/setup_nllb.ps1
# Provisions the NLLB (local) translation engine for CADTrans Lite and PRE-DOWNLOADS the
# NLLB-200-Distilled-600M model into the repo so the app never fetches it from HuggingFace
# at runtime.
#
# Steps:
#   1. (if missing) Download Windows embeddable Python 3.11 into tools\py + bootstrap pip.
#   2. pip install NLLB runtime: transformers + sentencepiece + torch (CPU) + huggingface_hub.
#      Use -Ort for the lighter onnxruntime+optimum path.
#   3. PRE-DOWNLOAD the model into tools\py\models\nllb-200-distilled-600M (real files, not
#      symlinks) so it is copied into the build output and ships with the software.
#
# Usage:
#   .\tools\setup_nllb.ps1                       # default torch path + download model
#   .\tools\setup_nllb.ps1 -Ort                  # onnxruntime/optimum path + download model
#   .\tools\setup_nllb.ps1 -NoModel              # install deps only, skip the big model download
#   .\tools\setup_nllb.ps1 -ModelDir D:\models   # place the model somewhere else
#
# After this runs, CADTrans Lite auto-starts: tools\py\python.exe nllb_server.py --port 5002
# and nllb_server.py loads the bundled model from tools\py\models\nllb-200-distilled-600M
# with no network needed.

param(
    [switch]$Ort = $false,
    [switch]$NoModel = $false,
    [switch]$Mirror = $false,
    [string]$ModelDir = "",
    [string]$Cache = ""
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Locate / provision the embedded Python (same logic as setup_engines.ps1)
# ---------------------------------------------------------------------------
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$pyDir     = Join-Path $scriptDir "py"
$python    = Join-Path $pyDir "python.exe"

if (-not (Test-Path $pyDir)) { New-Item -ItemType Directory -Force -Path $pyDir | Out-Null }

if (-not (Test-Path $python)) {
    Write-Host "Embedded Python not found, downloading Windows embeddable Python 3.11 ..." -ForegroundColor Cyan
    $EmbedVersion = "3.11.9"
    $EmbedUrl     = "https://www.python.org/ftp/python/$EmbedVersion/python-$EmbedVersion-embed-amd64.zip"
    $PythonZip    = Join-Path $pyDir "python-embed.zip"
    Invoke-WebRequest -Uri $EmbedUrl -OutFile $PythonZip -UseBasicParsing
    Expand-Archive -Path $PythonZip -DestinationPath $pyDir -Force
    Remove-Item $PythonZip

    # Embeddable Python ships with 'import site' commented out; re-enable it so pip works.
    $Cfg = Join-Path $pyDir "python311._pth"
    if (Test-Path $Cfg) {
        (Get-Content $Cfg) -replace '^#import site', 'import site' | Set-Content $Cfg
    }
    Write-Host "Embedded Python ready: $python" -ForegroundColor Green
}

# Bootstrap pip if needed
Write-Host "Bootstrapping pip ..." -ForegroundColor Cyan
$GetPip = Join-Path $pyDir "get-pip.py"
if (-not (Test-Path $GetPip)) {
    Invoke-WebRequest -Uri "https://bootstrap.pypa.io/get-pip.py" -OutFile $GetPip -UseBasicParsing
}
& $python $GetPip --no-warn-script-location | Out-Null

# ---------------------------------------------------------------------------
# Install NLLB runtime dependencies
# ---------------------------------------------------------------------------
if ($Cache -ne "") {
    if (-not (Test-Path $Cache)) { New-Item -ItemType Directory -Force -Path $Cache | Out-Null }
    $env:HF_HOME = $Cache
    Write-Host "HuggingFace cache dir: $Cache" -ForegroundColor Cyan
}

# Disable telemetry / progress spam
$env:HF_HUB_DISABLE_TELEMETRY = "1"
$env:HF_HUB_DISABLE_XET = "1"
$env:HF_HUB_DOWNLOAD_TIMEOUT = "600"
$env:PYTHONWARNINGS = "ignore"

# Optional HuggingFace mirror (useful in regions with slow/blocked access to huggingface.co)
if ($Mirror) {
    $env:HF_ENDPOINT = "https://hf-mirror.com"
    Write-Host "Using HuggingFace mirror: https://hf-mirror.com" -ForegroundColor Cyan
}

Write-Host "Using Python: $python" -ForegroundColor Cyan
& $python -m pip install --upgrade pip | Out-Null

if ($Ort) {
    Write-Host "Installing ONNX Runtime path (onnxruntime + optimum + sentencepiece + transformers + huggingface_hub) ..." -ForegroundColor Cyan
    & $python -m pip install --upgrade "onnxruntime" "optimum[sentencepiece]" "transformers" "sentencepiece" "huggingface_hub" | Out-Null
    $env:NLLB_USE_ONNX = "1"
    Write-Host "ONNX Runtime path selected (INT8/FP16 quantization, closer to RTranslator runtime)." -ForegroundColor Green
} else {
    Write-Host "Installing default deps (transformers + sentencepiece + torch CPU + huggingface_hub) ..." -ForegroundColor Cyan
    & $python -m pip install --upgrade "transformers" "sentencepiece" "torch" "huggingface_hub" --extra-index-url https://download.pytorch.org/whl/cpu | Out-Null
}

if ($LASTEXITCODE -ne 0) {
    Write-Host "Dependency install failed (exit code $LASTEXITCODE)." -ForegroundColor Red
    exit $LASTEXITCODE
}

# ---------------------------------------------------------------------------
# Pre-download the 600M model into tools\py\models (ships with the app)
# ---------------------------------------------------------------------------
if ($NoModel) {
    Write-Host ""
    Write-Host "Skipped model download (-NoModel). First run will still download from HuggingFace." -ForegroundColor Yellow
    exit 0
}

if ($ModelDir -eq "") {
    $ModelDir = Join-Path $pyDir "models\nllb-200-distilled-600M"
}
if (-not (Test-Path $ModelDir)) { New-Item -ItemType Directory -Force -Path $ModelDir | Out-Null }

# Always run snapshot_download: it verifies each file against the repo and only
# (re)downloads what is missing or corrupt, so an interrupted/partial download self-heals.
$weightsOk = Test-Path (Join-Path $ModelDir "pytorch_model.bin")
if ($weightsOk) {
    Write-Host "Model weights already present at $ModelDir, verifying/refreshing ..." -ForegroundColor Cyan
} else {
    Write-Host "Pre-downloading NLLB model 'facebook/nllb-200-distilled-600M' to $ModelDir ..." -ForegroundColor Cyan
    Write-Host "(~1.2 GB; one time only, then ships with the software)" -ForegroundColor White
}
$env:NLLB_DOWNLOAD_DIR = $ModelDir
$tmpPy = Join-Path $env:TEMP "nllb_download.py"
Set-Content -Path $tmpPy -Value @'
from huggingface_hub import snapshot_download
import os
d = os.environ["NLLB_DOWNLOAD_DIR"]
snapshot_download(
    repo_id="facebook/nllb-200-distilled-600M",
    local_dir=d,
    local_dir_use_symlinks=False,
)
print("MODEL_DOWNLOAD_DONE")
'@
& $python $tmpPy
if ($LASTEXITCODE -ne 0) {
    Write-Host "Model download failed (exit code $LASTEXITCODE)." -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "Model ready at $ModelDir" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Optional: export ONNX for the optimum path (produces local onnx files too)
# ---------------------------------------------------------------------------
if ($Ort) {
    Write-Host "Exporting local model for ONNX Runtime path (slow first time) ..." -ForegroundColor Cyan
    $env:NLLB_MODEL = $ModelDir
    $tmpPy = Join-Path $env:TEMP "nllb_onnx_export.py"
    Set-Content -Path $tmpPy -Value @'
from optimum.onnxruntime import ORTModelForSeq2SeqLM
from transformers import AutoTokenizer
import os
d = os.environ["NLLB_MODEL"]
m = ORTModelForSeq2SeqLM.from_pretrained(d, export=True)
m.save_pretrained(d)
AutoTokenizer.from_pretrained(d).save_pretrained(d)
print("ONNX_EXPORT_DONE")
'@
    & $python $tmpPy
}

Write-Host ""
Write-Host "NLLB engine is ready." -ForegroundColor Green
Write-Host "Model location: $ModelDir" -ForegroundColor White
Write-Host "In CADTrans Lite, select the 'NLLB (local)' engine and click Test - no model download needed." -ForegroundColor White
