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

& .\.venv\Scripts\python.exe -m pip install -r requirements.txt
& .\.venv\Scripts\python.exe app.py