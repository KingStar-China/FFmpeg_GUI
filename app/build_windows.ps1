$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$version = '0.1.1'
$distRoot = Join-Path $root 'dist'
$rawDistDir = Join-Path $distRoot 'FFmpeg GUI'
$portableDistDir = Join-Path $distRoot 'FFmpeg GUI Portable'
$portableZip = Join-Path $distRoot "FFmpeg_GUI_v$version`_win64_portable.zip"

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
& .\.venv\Scripts\python.exe -m PyInstaller --clean --noconfirm --windowed --name "FFmpeg GUI" --icon "assets\app.ico" --add-data "tools;tools" --add-data "assets;assets" app.py

if (-not (Test-Path $rawDistDir)) {
    throw '未找到 PyInstaller 输出目录。'
}

@"
from pathlib import Path
import shutil
import zipfile

raw_dir = Path(r'$rawDistDir')
portable_dir = Path(r'$portableDistDir')
zip_path = Path(r'$portableZip')

if portable_dir.exists():
    shutil.rmtree(portable_dir)

shutil.copytree(raw_dir, portable_dir)
shutil.rmtree(raw_dir)

if zip_path.exists():
    zip_path.unlink()

with zipfile.ZipFile(zip_path, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for path in portable_dir.rglob('*'):
        if path.is_file():
            archive.write(path, path.relative_to(portable_dir))
"@ | & .\.venv\Scripts\python.exe -
