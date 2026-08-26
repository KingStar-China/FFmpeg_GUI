param(
    [string]$FfmpegBin = $env:FFMPEG_BIN_DIR
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$version = '0.1.1'
$distRoot = Join-Path $root 'dist'
$rawDistDir = Join-Path $distRoot 'FFmpeg GUI'
$portableDistDir = Join-Path $distRoot 'FFmpeg GUI Portable'
$portableZip = Join-Path $distRoot "FFmpeg_GUI_v$version`_win64_portable.zip"
$stagingTools = Join-Path ([IO.Path]::GetTempPath()) "FFmpeg_GUI_pyinstaller_tools_$([guid]::NewGuid().ToString('N'))"

$python = 'C:\Users\administor\AppData\Local\Programs\Python\Python310\python.exe'
if (-not (Test-Path $python)) {
    throw '未找到 Python 3.10，请先安装 Python。'
}

if (-not (Test-Path '.venv\Scripts\python.exe')) {
    & $python -m venv .venv
}

if ([string]::IsNullOrWhiteSpace($FfmpegBin)) {
    $localTools = Join-Path $root 'tools'
    if ((Test-Path -LiteralPath (Join-Path $localTools 'ffmpeg.exe')) -and
        (Test-Path -LiteralPath (Join-Path $localTools 'ffprobe.exe'))) {
        $FfmpegBin = $localTools
    }
    else {
        $FfmpegBin = 'C:\Jinxin\ffmpeg-master-latest-win64-gpl-shared\bin'
    }
}

$FfmpegBin = [IO.Path]::GetFullPath($FfmpegBin)
foreach ($tool in @('ffmpeg.exe', 'ffprobe.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $FfmpegBin $tool))) {
        throw "FFmpeg 目录缺少 $tool：$FfmpegBin"
    }
}

New-Item -ItemType Directory -Force .\tools | Out-Null
Copy-Item -LiteralPath (Join-Path $FfmpegBin 'ffmpeg.exe') -Destination .\tools\ffmpeg.exe -Force
Copy-Item -LiteralPath (Join-Path $FfmpegBin 'ffprobe.exe') -Destination .\tools\ffprobe.exe -Force
Get-ChildItem -LiteralPath $FfmpegBin -Filter '*.dll' -File |
    Copy-Item -Destination .\tools -Force

if (-not (Test-Path '.\tools\mkvextract.exe')) {
    throw '未找到 tools\mkvextract.exe。'
}

& .\.venv\Scripts\python.exe -m pip install -r requirements.txt
& .\.venv\Scripts\python.exe -m pip install pyinstaller

try {
    if (Test-Path -LiteralPath $rawDistDir) {
        Remove-Item -LiteralPath $rawDistDir -Recurse -Force
    }
    if (Test-Path -LiteralPath $portableDistDir) {
        Remove-Item -LiteralPath $portableDistDir -Recurse -Force
    }
    if (Test-Path -LiteralPath $portableZip) {
        Remove-Item -LiteralPath $portableZip -Force
    }

    New-Item -ItemType Directory -Force -Path $stagingTools | Out-Null
    Copy-Item -LiteralPath (Join-Path $FfmpegBin 'ffmpeg.exe') -Destination $stagingTools -Force
    Copy-Item -LiteralPath (Join-Path $FfmpegBin 'ffprobe.exe') -Destination $stagingTools -Force
    Copy-Item -LiteralPath (Join-Path $root 'tools\mkvextract.exe') -Destination $stagingTools -Force

    & .\.venv\Scripts\python.exe -m PyInstaller --clean --noconfirm --windowed --name "FFmpeg GUI" --icon "assets\app.ico" --add-data "$stagingTools;tools" --add-data "assets;assets" app.py
    if ($LASTEXITCODE -ne 0) {
        throw "PyInstaller 打包失败，退出码：$LASTEXITCODE"
    }

    if (-not (Test-Path -LiteralPath $rawDistDir)) {
        throw '未找到 PyInstaller 输出目录。'
    }

    $rawToolsDir = Join-Path $rawDistDir '_internal\tools'
    if (-not (Test-Path -LiteralPath $rawToolsDir)) {
        throw '未找到 PyInstaller 工具目录。'
    }

    # 只在打包完成后复制 DLL，避免 PyInstaller 再把同一批 DLL 收集到 _internal 根目录。
    Get-ChildItem -LiteralPath $FfmpegBin -Filter '*.dll' -File |
        Copy-Item -Destination $rawToolsDir -Force

    Move-Item -LiteralPath $rawDistDir -Destination $portableDistDir
    $sizeMb = [Math]::Round(
        ((Get-ChildItem -LiteralPath $portableDistDir -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB,
        1)
    Write-Host "旧版目录版已生成：$portableDistDir ($sizeMb MB)"
    Write-Host '本脚本不再生成压缩包。'
}
finally {
    if (Test-Path -LiteralPath $stagingTools) {
        Remove-Item -LiteralPath $stagingTools -Recurse -Force
    }
}
