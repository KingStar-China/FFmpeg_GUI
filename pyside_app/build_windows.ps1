$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$python = 'C:\Users\administor\AppData\Local\Programs\Python\Python310\python.exe'
if (-not (Test-Path $python)) {
    throw '未找到 Python 3.10，请先安装 Python。'
}

if (-not (Test-Path '.venv\Scripts\python.exe')) {
    & $python -m venv .venv
}

$ffmpegSource = 'C:\Jinxin\ffmpeg-master-latest-win64-gpl-shared\bin'
if (-not (Test-Path $ffmpegSource)) {
    throw '未找到 FFmpeg bin 目录，请先确认本机 FFmpeg 安装路径。'
}

New-Item -ItemType Directory -Force .\tools | Out-Null
Copy-Item "$ffmpegSource\ffmpeg.exe" .\tools\ffmpeg.exe -Force
Copy-Item "$ffmpegSource\ffprobe.exe" .\tools\ffprobe.exe -Force
Get-ChildItem $ffmpegSource -Filter '*.dll' | Copy-Item -Destination .\tools -Force

if (-not (Test-Path '.\tools\mkvextract.exe')) {
    throw '未找到 tools\mkvextract.exe。'
}

& .\.venv\Scripts\python.exe -m pip install -r requirements.txt
& .\.venv\Scripts\python.exe -m pip install pyinstaller
& .\.venv\Scripts\pyinstaller.exe --noconfirm --windowed --name "FFmpeg GUI" --icon "assets\app.ico" --add-data "tools;tools" --add-data "assets;assets" app.py