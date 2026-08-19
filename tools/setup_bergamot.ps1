<#
.SYNOPSIS
    下载 Mozilla Bergamot 离线翻译模型（纯 .NET，无 Python 依赖）。

.DESCRIPTION
    CADTrans Lite 的本地离线翻译引擎 Bergamot 需要语言模型文件。本脚本从 Mozilla
    公开模型注册表下载所需方向（默认 en-* 与 *-en，可覆盖任意语言对互译）并生成
    config.txt。下载完成后，模型位于 <TargetDir>/<方向>/ 下，应用会自动加载。

.PARAMETER TargetDir
    模型输出根目录。默认：脚本所在目录下的 bergamot\（即 tools/bergamot）。
    对于已发布的程序，请指定到 publish\tools\bergamot，例如：
    .\setup_bergamot.ps1 -TargetDir "D:\GitHub\CADTrans Lite\publish\tools\bergamot"

.PARAMETER Languages
    需要覆盖的「非英文」语言列表（两字母小写）。默认 zh ru es pt fr ko ja ar de it（10 种常用语言）。
    脚本会为每个语言下载 en-<L> 与 <L>-en 两个方向。

.EXAMPLE
    .\setup_bergamot.ps1
    .\setup_bergamot.ps1 -TargetDir "..\publish\tools\bergamot"
    .\setup_bergamot.ps1 -Languages zh,ja,ko
#>
param(
    [string]$TargetDir = (Join-Path $PSScriptRoot "bergamot"),
    [string[]]$Languages = @("zh", "ru", "es", "pt", "fr", "ko", "ja", "ar", "de", "it")
)

$ErrorActionPreference = "Stop"

$RegistryUrl = "https://storage.googleapis.com/moz-fx-translations-data--303e-prod-translations-data/db/models.json"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

Write-Host "== Bergamot 模型下载工具 ==" -ForegroundColor Cyan
Write-Host "目标目录: $TargetDir"
Write-Host "语言集  : $($Languages -join ', ')"
Write-Host "正在获取模型注册表…"

$registry = Invoke-RestMethod -Uri $RegistryUrl -UseBasicParsing
$baseUrl = $registry.baseUrl
if (-not $baseUrl.EndsWith('/')) { $baseUrl = $baseUrl + '/' }

# 使用 WebClient 异步下载 + CancelAsync 实现硬性超时（.NET Framework 下能真正中止半开连接）

# 计算需要下载的方向（en-L 与 L-en）
$directions = @()
foreach ($l in $Languages) {
    $directions += "en-$l"
    $directions += "$l-en"
}
# 去重
$directions = $directions | Sort-Object -Unique

function Get-GzipDecompressedBytes {
    param([byte[]]$Data)
    $ms = New-Object System.IO.MemoryStream(,$Data)
    $gz = New-Object System.IO.Compression.GzipStream($ms, [System.IO.Compression.CompressionMode]::Decompress)
    $out = New-Object System.IO.MemoryStream
    $gz.CopyTo($out)
    $gz.Dispose(); $ms.Dispose()
    return $out.ToArray()
}

function Select-Candidate {
    param($ModelEntries)
    # 优先选择 releaseStatus == "Release" 的候选；否则取最后一个（通常质量最高）。
    $release = $ModelEntries | Where-Object { $_.releaseStatus -eq "Release" }
    if ($release -and $release.Count -gt 0) {
        if ($release -is [System.Array]) { return $release[-1] } else { return $release }
    }
    if ($ModelEntries -is [System.Array]) { return $ModelEntries[-1] } else { return $ModelEntries }
}

function Resolve-File {
    param($FilesObj, [string]$Key)
    if ($null -ne $FilesObj -and $FilesObj.PSObject.Properties[$Key]) {
        return $FilesObj.$Key
    }
    return $null
}

$total = $directions.Count
$failedDirs = @()
$idx = 0
foreach ($dir in $directions) {
    $idx++
    $dirPath = Join-Path $TargetDir $dir
    $configPath = Join-Path $dirPath "config.txt"

    if (Test-Path $configPath) {
        $valid = $false
        try { $c = [System.IO.File]::ReadAllText($configPath); if ($c.Length -gt 0 -and $c -match 'models:') { $valid = $true } } catch {}
        if ($valid) {
            Write-Host "[$idx/$total] 跳过 $dir （config.txt 已就绪）" -ForegroundColor DarkGray
            continue
        }
        Write-Host "[$idx/$total] 发现不完整的 config.txt，重新下载 $dir …" -ForegroundColor Yellow
        Remove-Item $dirPath -Recurse -Force -ErrorAction SilentlyContinue
    }

    if (-not $registry.models.PSObject.Properties[$dir]) {
        Write-Host "[$idx/$total] 跳过 $dir （注册表中无此方向模型）" -ForegroundColor Yellow
        continue
    }

    try {
    Write-Host "[$idx/$total] 下载 $dir …" -ForegroundColor Cyan
    $candidate = Select-Candidate $registry.models.$dir
    $files = $candidate.files

    $modelFile   = Resolve-File $files "model"
    $vocabFile   = Resolve-File $files "vocab"
    $srcVocab    = Resolve-File $files "srcVocab"
    $trgVocab    = Resolve-File $files "trgVocab"
    $shortlist   = Resolve-File $files "lexicalShortlist"

    if (-not $modelFile) {
        Write-Host "  ! $dir 缺少 model 文件，跳过" -ForegroundColor Red
        continue
    }

    New-Item -ItemType Directory -Force -Path $dirPath | Out-Null

    # 下载所有需要的文件（.gz 压缩，需解压）
    function Fetch-File {
        param($Entry, [string]$OutName)
        if ($null -eq $Entry) { return $null }
        $rel = $Entry.path
        $url = $baseUrl + $rel.TrimStart('/')
        # 模型文件为 .gz 压缩，解压后必须去掉 .gz 后缀（Bergamot/marian 会按 .gz 扩展名再尝试解压导致 native 崩溃）
        $saveName = $OutName -replace '\.gz$',''
        $outPath = Join-Path $dirPath $saveName
        Write-Host "    - $OutName -> $saveName" -ForegroundColor Gray
        $client = New-Object System.Net.WebClient
        $client.Headers.Add("User-Agent", "CADTrans-Bergamot-Setup")
        $global:__dlDone = $false
        $global:__dlBytes = $null
        $global:__dlErr = $null
        $ev = Register-ObjectEvent $client DownloadDataCompleted -Action {
            $global:__dlDone = $true
            $global:__dlBytes = $EventArgs.Result
            $global:__dlErr = $EventArgs.Error
        }
        try {
            $client.DownloadDataAsync($url)
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            while (-not $global:__dlDone -and $sw.Elapsed.TotalSeconds -lt 120) { Start-Sleep -Milliseconds 250 }
            $sw.Stop()
            if (-not $global:__dlDone) {
                $client.CancelAsync()
                throw ("下载 $OutName 超时（120s，半开连接已取消）")
            }
            if ($global:__dlErr) { throw ("下载 $OutName 失败：" + $global:__dlErr.Message) }
            $bytes = $global:__dlBytes
        }
        finally {
            Unregister-Event $ev.Name -ErrorAction SilentlyContinue
            $client.Dispose()
        }
        if ($rel.EndsWith('.gz')) {
            $bytes = Get-GzipDecompressedBytes -Data $bytes
        }
        [System.IO.File]::WriteAllBytes($outPath, $bytes)
        return $saveName
    }

    $mName = Fetch-File $modelFile     ([System.IO.Path]::GetFileName($modelFile.path))
    if ($srcVocab -and $trgVocab) {
        $sName = Fetch-File $srcVocab ([System.IO.Path]::GetFileName($srcVocab.path))
        $tName = Fetch-File $trgVocab ([System.IO.Path]::GetFileName($trgVocab.path))
    }
    elseif ($vocabFile) {
        $vName = Fetch-File $vocabFile ([System.IO.Path]::GetFileName($vocabFile.path))
        $sName = $vName; $tName = $vName
    }
    else {
        Write-Host "  ! $dir 缺少 vocab 文件，跳过" -ForegroundColor Red
        continue
    }
    $lName = if ($shortlist) { Fetch-File $shortlist ([System.IO.Path]::GetFileName($shortlist.path)) } else { $null }

    # gemm-precision：含 alphas 的模型使用 int8shiftAlphaAll，否则 int8shiftAll
    $gemm = if ($mName -match "alphas") { "int8shiftAlphaAll" } else { "int8shiftAll" }

    $cfg = @"
relative-paths: true
models:
- $mName
vocabs:
- $sName
- $tName
shortlist:
- $lName
- false
beam-size: 1
normalize: 1.0
word-penalty: 0
max-length-break: 128
mini-batch-words: 1024
workspace: 128
max-length-factor: 2.0
skip-cost: true
cpu-threads: 0
quiet: true
quiet-translation: true
gemm-precision: $gemm
"@
    if (-not $lName) {
        # 无短名单时移除 shortlist 段
        $cfg = ($cfg -split "`n" | Where-Object { $_ -notmatch "shortlist:" -and $_ -notmatch "^- \$lName" }) -join "`n"
    }
    [System.IO.File]::WriteAllText($configPath, $cfg, [System.Text.Encoding]::UTF8)
    Write-Host "  ✔ $dir 完成" -ForegroundColor Green
    } catch {
        Write-Host "  ! $dir 下载失败：$($_.Exception.Message)；跳过，稍后重试" -ForegroundColor Red
        $failedDirs += $dir
        if (Test-Path $dirPath) { Get-ChildItem $dirPath -ErrorAction SilentlyContinue | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue }
        continue
    }
}

# 对失败方向做有限次重试（短超时，失败即放弃，不阻塞其余语言）
if ($failedDirs.Count -gt 0) {
    Write-Host ""
    Write-Host "== 重试失败方向（最多 3 次，单次失败即跳过）==" -ForegroundColor Yellow
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $stillFailed = @()
        foreach ($dir in $failedDirs) {
            $dirPath = Join-Path $TargetDir $dir
            $configPath = Join-Path $dirPath "config.txt"
            if (Test-Path $configPath) {
                $c = [System.IO.File]::ReadAllText($configPath)
                if ($c.Length -gt 0 -and $c -match 'models:') { continue }
                Remove-Item $dirPath -Recurse -Force -ErrorAction SilentlyContinue
            }
            Write-Host "[重试 $attempt/3] $dir …" -ForegroundColor Cyan
            try {
                if (-not $registry.models.PSObject.Properties[$dir]) { Write-Host "  跳过（注册表无此方向）"; continue }
                $candidate = Select-Candidate $registry.models.$dir
                $files = $candidate.files
                $modelFile = Resolve-File $files "model"
                if (-not $modelFile) { Write-Host "  跳过（无 model）"; continue }
                New-Item -ItemType Directory -Force -Path $dirPath | Out-Null
                function Fetch-File2 {
                    param($Entry, [string]$OutName)
                    if ($null -eq $Entry) { return $null }
                    $rel = $Entry.path
                    $url = $baseUrl + $rel.TrimStart('/')
                    $saveName = $OutName -replace '\.gz$',''
                    $outPath = Join-Path $dirPath $saveName
                    Write-Host "    - $OutName -> $saveName" -ForegroundColor Gray
                    $client = New-Object System.Net.WebClient
                    $client.Headers.Add("User-Agent", "CADTrans-Bergamot-Setup")
                    $global:__dlDone = $false
                    $global:__dlBytes = $null
                    $global:__dlErr = $null
                    $ev = Register-ObjectEvent $client DownloadDataCompleted -Action {
                        $global:__dlDone = $true
                        $global:__dlBytes = $EventArgs.Result
                        $global:__dlErr = $EventArgs.Error
                    }
                    try {
                        $client.DownloadDataAsync($url)
                        $sw = [System.Diagnostics.Stopwatch]::StartNew()
                        while (-not $global:__dlDone -and $sw.Elapsed.TotalSeconds -lt 120) { Start-Sleep -Milliseconds 250 }
                        $sw.Stop()
                        if (-not $global:__dlDone) {
                            $client.CancelAsync()
                            throw ("下载 $OutName 超时（120s，半开连接已取消）")
                        }
                        if ($global:__dlErr) { throw ("下载 $OutName 失败：" + $global:__dlErr.Message) }
                        $bytes = $global:__dlBytes
                    }
                    finally {
                        Unregister-Event $ev.Name -ErrorAction SilentlyContinue
                        $client.Dispose()
                    }
                    if ($rel.EndsWith('.gz')) { $bytes = Get-GzipDecompressedBytes -Data $bytes }
                    [System.IO.File]::WriteAllBytes($outPath, $bytes)
                    return $saveName
                }
                $mName = Fetch-File2 $modelFile ([System.IO.Path]::GetFileName($modelFile.path))
                $vocabFile = Resolve-File $files "vocab"
                $srcVocab = Resolve-File $files "srcVocab"
                $trgVocab = Resolve-File $files "trgVocab"
                $shortlist = Resolve-File $files "lexicalShortlist"
                if ($srcVocab -and $trgVocab) {
                    $sName = Fetch-File2 $srcVocab ([System.IO.Path]::GetFileName($srcVocab.path))
                    $tName = Fetch-File2 $trgVocab ([System.IO.Path]::GetFileName($trgVocab.path))
                } elseif ($vocabFile) {
                    $vName = Fetch-File2 $vocabFile ([System.IO.Path]::GetFileName($vocabFile.path))
                    $sName = $vName; $tName = $vName
                } else { Write-Host "  跳过（无 vocab）"; continue }
                $lName = if ($shortlist) { Fetch-File2 $shortlist ([System.IO.Path]::GetFileName($shortlist.path)) } else { $null }
                $gemm = if ($mName -match "alphas") { "int8shiftAlphaAll" } else { "int8shiftAll" }
                $cfg = @"
relative-paths: true
models:
- $mName
vocabs:
- $sName
- $tName
shortlist:
- $lName
- false
beam-size: 1
normalize: 1.0
word-penalty: 0
max-length-break: 128
mini-batch-words: 1024
workspace: 128
max-length-factor: 2.0
skip-cost: true
cpu-threads: 0
quiet: true
quiet-translation: true
gemm-precision: $gemm
"@
                if (-not $lName) {
                    $cfg = ($cfg -split "`n" | Where-Object { $_ -notmatch "shortlist:" -and $_ -notmatch "^- \$lName" }) -join "`n"
                }
                [System.IO.File]::WriteAllText($configPath, $cfg, [System.Text.Encoding]::UTF8)
                Write-Host "  ✔ $dir 重试成功" -ForegroundColor Green
            } catch {
                Write-Host "  ! $dir 重试失败：$($_.Exception.Message)" -ForegroundColor Red
                $stillFailed += $dir
            }
        }
        $failedDirs = $stillFailed
        if ($failedDirs.Count -eq 0) { break }
    }
    if ($failedDirs.Count -gt 0) {
        Write-Host "以下方向仍未能下载（可稍后单独重试，不影响其余语言）：" -ForegroundColor Yellow
        $failedDirs | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
    }
}

Write-Host "" 
Write-Host "== 下载完成 ==" -ForegroundColor Cyan
Write-Host "模型目录: $TargetDir"
Write-Host "在 CADTrans Lite 设置中选择「Bergamot (本地)」引擎即可离线翻译。" -ForegroundColor White
