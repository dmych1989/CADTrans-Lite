# tools/py_patches/apply_py_patches.ps1
#
# 把本目录中「已修复」的 Python 文件覆盖到本地 Python 运行时（tools/py）。
#
# 为什么需要这个脚本：
#   tools/py 是随包分发的嵌入式 Python 运行时（1GB+，被 .gitignore 排除），其中的
#   引擎脚本/第三方库改动无法随仓库分发。这里把关键改动以「补丁源文件 + 本脚本」
#   的形式纳入版本管理，安装或重新下载运行时之后再执行一次即可复原修复。
#
# 补丁内容：
#   nllb_server.py            NLLB HTTP 服务：支持 Transfer-Encoding: chunked 请求体；
#                             请求体里没有 q 字段时返回明确的 400，而不是 200 + 空译文。
#   libretranslate_server.py  LibreTranslate 启动器。
#   argostranslate_sbd.py     覆盖 Lib/site-packages/argostranslate/sbd.py：
#                             分句全程离线 —— MiniSBD 只在本地 onnx 存在时启用（缺失回退
#                             标点分句），Stanza 分支使用 DownloadMethod.NONE，（绝不联网下载
#                             模型，避免离线环境下请求挂起几分钟）。
#
# Usage:
#   .\tools\py_patches\apply_py_patches.ps1
#   .\tools\py_patches\apply_py_patches.ps1 -PyDir "D:\CADTrans Lite\tools\py"

param(
    [string]$PyDir = ''
)

$ErrorActionPreference = 'Stop'

$patchDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Split-Path -Parent (Split-Path -Parent $patchDir)
if ([string]::IsNullOrWhiteSpace($PyDir)) { $PyDir = Join-Path $repoRoot 'tools\py' }

if (-not (Test-Path $PyDir)) {
    Write-Warning "Python 运行时目录不存在：$PyDir（请先运行 tools\setup_engines.ps1 或 tools\setup_nllb.ps1）"
    return
}

$targets = @(
    @{ Src = 'nllb_server.py';           Dst = 'nllb_server.py' },
    @{ Src = 'libretranslate_server.py'; Dst = 'libretranslate_server.py' },
    @{ Src = 'argostranslate_sbd.py';    Dst = 'Lib\site-packages\argostranslate\sbd.py' }
)

foreach ($t in $targets) {
    $src = Join-Path $patchDir $t.Src
    $dst = Join-Path $PyDir $t.Dst
    $dstDir = Split-Path -Parent $dst
    if (-not (Test-Path $dstDir)) {
        Write-Host ("跳过（目录不存在）：" + $t.Dst) -ForegroundColor Yellow
        continue
    }
    Copy-Item -LiteralPath $src -Destination $dst -Force
    Write-Host ("已应用：" + $t.Dst) -ForegroundColor Green
}

Write-Host "py_patches 应用完成。" -ForegroundColor Green
