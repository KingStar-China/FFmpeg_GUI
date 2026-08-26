param(
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$FfmpegBin = $env:FFMPEG_BIN_DIR
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $repoRoot 'src\FFmpegGui.App\FFmpegGui.App.csproj'
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$portableDir = [IO.Path]::GetFullPath((Join-Path $artifactRoot "FFmpeg GUI Native $Runtime"))
$portableZip = [IO.Path]::GetFullPath((Join-Path $artifactRoot "FFmpeg_GUI_v0.2.0_${Runtime}_native_portable.zip"))

function Assert-ArtifactChildPath {
    param([Parameter(Mandatory)][string]$Path)

    $rootPrefix = $artifactRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理 artifacts 目录之外的路径：$fullPath"
    }
}

if ([string]::IsNullOrWhiteSpace($FfmpegBin)) {
    $repoTools = Join-Path $repoRoot 'app\tools'
    if ((Test-Path (Join-Path $repoTools 'ffmpeg.exe')) -and
        (Test-Path (Join-Path $repoTools 'ffprobe.exe'))) {
        $FfmpegBin = $repoTools
    }
    else {
        $ffmpegCommand = Get-Command ffmpeg.exe -ErrorAction SilentlyContinue | Select-Object -First 1
        $ffprobeCommand = Get-Command ffprobe.exe -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($ffmpegCommand -and $ffprobeCommand -and
            ((Split-Path -Parent $ffmpegCommand.Source) -eq (Split-Path -Parent $ffprobeCommand.Source))) {
            $FfmpegBin = Split-Path -Parent $ffmpegCommand.Source
        }
    }
}

if ([string]::IsNullOrWhiteSpace($FfmpegBin)) {
    throw '未找到 FFmpeg。请通过 -FfmpegBin 或 FFMPEG_BIN_DIR 指定包含 ffmpeg.exe 和 ffprobe.exe 的目录。'
}

$FfmpegBin = [IO.Path]::GetFullPath($FfmpegBin)
foreach ($tool in @('ffmpeg.exe', 'ffprobe.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $FfmpegBin $tool))) {
        throw "FFmpeg 目录缺少 $tool：$FfmpegBin"
    }
}

$mkvextract = Join-Path $repoRoot 'app\tools\mkvextract.exe'
if (-not (Test-Path -LiteralPath $mkvextract)) {
    throw "未找到 mkvextract.exe：$mkvextract"
}

Assert-ArtifactChildPath -Path $portableDir
Assert-ArtifactChildPath -Path $portableZip

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
if (Test-Path -LiteralPath $portableDir) {
    Remove-Item -LiteralPath $portableDir -Recurse -Force
}
if (Test-Path -LiteralPath $portableZip) {
    Remove-Item -LiteralPath $portableZip -Force
}

& dotnet publish $project `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained false `
    --output $portableDir `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -p:DebugSymbols=false `
    -p:DebugType=None
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败，退出码：$LASTEXITCODE"
}

$toolsDir = Join-Path $portableDir 'tools'
New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
Copy-Item -LiteralPath (Join-Path $FfmpegBin 'ffmpeg.exe') -Destination $toolsDir -Force
Copy-Item -LiteralPath (Join-Path $FfmpegBin 'ffprobe.exe') -Destination $toolsDir -Force
Get-ChildItem -LiteralPath $FfmpegBin -Filter '*.dll' -File |
    Copy-Item -Destination $toolsDir -Force
Copy-Item -LiteralPath $mkvextract -Destination $toolsDir -Force

# 旧版本曾生成压缩包；本脚本现在只保留解压目录，避免本地保留两份发布产物。
if (Test-Path -LiteralPath $portableZip) {
    Remove-Item -LiteralPath $portableZip -Force
}

$sizeMb = [Math]::Round(
    ((Get-ChildItem -LiteralPath $portableDir -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB,
    1)
Write-Host "原生目录版已生成：$portableDir ($sizeMb MB)"
Write-Host "注意：此版本不内置 .NET Runtime，需要安装 .NET 10 Desktop Runtime。"
