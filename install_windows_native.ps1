[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$SourceDir = $PSScriptRoot,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\FFmpeg GUI'),
    [switch]$SkipRuntimeInstall,
    [switch]$Launch,
    [switch]$DesktopShortcut
)

$ErrorActionPreference = 'Stop'

$appExeName = 'FFmpeg GUI.exe'
$runtimePackageId = 'Microsoft.DotNet.DesktopRuntime.10'
$runtimeInstallerUri = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe'

function Resolve-ExistingPath {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "找不到目录：$Path"
    }

    return [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Path).Path)
}

function Get-DotnetHostPaths {
    $candidates = New-Object System.Collections.Generic.List[string]
    $command = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($command) {
        $path = if ($command.Path) { $command.Path } else { $command.Source }
        if ($path) {
            [void]$candidates.Add($path)
        }
    }

    foreach ($path in @(
        (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'),
        (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe')
    )) {
        if ($path -and (Test-Path -LiteralPath $path -PathType Leaf)) {
            [void]$candidates.Add($path)
        }
    }

    return $candidates | Select-Object -Unique
}

function Test-DesktopRuntime {
    foreach ($hostPath in Get-DotnetHostPaths) {
        try {
            $runtimeList = & $hostPath --list-runtimes 2>$null
            if (($runtimeList -join [Environment]::NewLine) -match '(?m)^Microsoft\.WindowsDesktop\.App\s+10\.') {
                return $true
            }
        }
        catch {
            continue
        }
    }

    $runtimeRoots = @(
        (Join-Path $env:ProgramFiles 'dotnet\shared\Microsoft.WindowsDesktop.App'),
        (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\shared\Microsoft.WindowsDesktop.App')
    )
    foreach ($runtimeRoot in $runtimeRoots) {
        if ((Test-Path -LiteralPath $runtimeRoot -PathType Container) -and
            (Get-ChildItem -LiteralPath $runtimeRoot -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match '^10\.' })) {
            return $true
        }
    }

    return $false
}

function Install-RuntimeWithWinget {
    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if (-not $winget) {
        return $false
    }

    $wingetPath = if ($winget.Path) { $winget.Path } else { $winget.Source }
    $arguments = @(
        'install',
        '--id', $runtimePackageId,
        '--exact',
        '--source', 'winget',
        '--silent',
        '--accept-package-agreements',
        '--accept-source-agreements'
    )

    if (-not $PSCmdlet.ShouldProcess($runtimePackageId, '通过 WinGet 安装 .NET 10 Desktop Runtime')) {
        return $true
    }

    Write-Host '未检测到 .NET 10 Desktop Runtime，正在通过 WinGet 安装……'
    $process = Start-Process -FilePath $wingetPath -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -in @(0, 3010) -and (Test-DesktopRuntime)) {
        return $true
    }

    Write-Warning "WinGet 安装未完成，退出码：$($process.ExitCode)。将尝试官方安装程序。"
    return $false
}

function Install-RuntimeFromOfficialInstaller {
    $tempDir = Join-Path ([IO.Path]::GetTempPath()) "FFmpeg_GUI_Runtime_$([guid]::NewGuid().ToString('N'))"
    $installerPath = Join-Path $tempDir 'windowsdesktop-runtime-win-x64.exe'

    try {
        if (-not $PSCmdlet.ShouldProcess($runtimeInstallerUri, '下载并安装 .NET 10 Desktop Runtime')) {
            return $true
        }

        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Write-Host '正在从微软官方下载 .NET 10 Desktop Runtime……'
        Invoke-WebRequest -Uri $runtimeInstallerUri -OutFile $installerPath -UseBasicParsing
        if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf) -or
            (Get-Item -LiteralPath $installerPath).Length -lt 1MB) {
            throw '运行时安装程序下载不完整。'
        }

        Write-Host '正在运行 .NET 10 Desktop Runtime 安装程序（可能会弹出 UAC）……'
        $process = Start-Process -FilePath $installerPath -ArgumentList @('/install', '/quiet', '/norestart') -Wait -PassThru
        if ($process.ExitCode -notin @(0, 3010) -or -not (Test-DesktopRuntime)) {
            throw "官方安装程序失败，退出码：$($process.ExitCode)。"
        }

        return $true
    }
    finally {
        if (Test-Path -LiteralPath $tempDir) {
            Remove-Item -LiteralPath $tempDir -Recurse -Force
        }
    }
}

$source = Resolve-ExistingPath -Path $SourceDir
$install = [IO.Path]::GetFullPath($InstallDir)
$sourcePrefix = $source.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$sameLocation = $source.TrimEnd([IO.Path]::DirectorySeparatorChar) -ieq $install.TrimEnd([IO.Path]::DirectorySeparatorChar)
if (-not $sameLocation -and $install.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "安装目录不能位于发布目录内部：$install"
}

foreach ($relativePath in @(
    $appExeName,
    'FFmpeg GUI.runtimeconfig.json',
    'tools\ffmpeg.exe',
    'tools\ffprobe.exe',
    'tools\mkvextract.exe'
)) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $relativePath) -PathType Leaf)) {
        throw "发布目录缺少文件：$relativePath"
    }
}

$runtimeReady = Test-DesktopRuntime
if ($runtimeReady) {
    Write-Host '.NET 10 Desktop Runtime 已就绪。'
}
elseif ($SkipRuntimeInstall) {
    throw '未找到 .NET 10 Desktop Runtime。请移除 -SkipRuntimeInstall，或手动安装 Microsoft.DotNet.DesktopRuntime.10。'
}
elseif ($WhatIfPreference) {
    Write-Host 'WhatIf：将安装 .NET 10 Desktop Runtime。'
    $runtimeReady = $true
}
else {
    $runtimeReady = Install-RuntimeWithWinget
    if (-not $runtimeReady) {
        $runtimeReady = Install-RuntimeFromOfficialInstaller
    }
    if (-not $runtimeReady) {
        throw '无法安装 .NET 10 Desktop Runtime。请检查网络和管理员权限。'
    }
}

if ($sameLocation) {
    Write-Host "发布目录已是安装目录：$install"
}
elseif ($PSCmdlet.ShouldProcess($install, '复制 FFmpeg GUI 原生发布文件')) {
    New-Item -ItemType Directory -Path $install -Force | Out-Null
    Get-ChildItem -LiteralPath $source -Force |
        Copy-Item -Destination $install -Recurse -Force
}

$installedExe = Join-Path $install $appExeName
$startMenuDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\FFmpeg GUI'
$startMenuShortcut = Join-Path $startMenuDir 'FFmpeg GUI.lnk'
if (-not $sameLocation -and $PSCmdlet.ShouldProcess($startMenuShortcut, '创建开始菜单快捷方式')) {
    New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($startMenuShortcut)
    $shortcut.TargetPath = $installedExe
    $shortcut.WorkingDirectory = $install
    $shortcut.IconLocation = "$installedExe,0"
    $shortcut.Save()
}

if ($DesktopShortcut -and -not $sameLocation) {
    $desktopShortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'FFmpeg GUI.lnk'
    if ($PSCmdlet.ShouldProcess($desktopShortcutPath, '创建桌面快捷方式')) {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($desktopShortcutPath)
        $shortcut.TargetPath = $installedExe
        $shortcut.WorkingDirectory = $install
        $shortcut.IconLocation = "$installedExe,0"
        $shortcut.Save()
    }
}

if (-not $WhatIfPreference -and -not (Test-Path -LiteralPath $installedExe -PathType Leaf)) {
    throw "安装完成后找不到程序：$installedExe"
}

Write-Host "安装目录：$install"
Write-Host '开始菜单快捷方式已准备完成。'
if ($Launch -and -not $WhatIfPreference) {
    Start-Process -FilePath $installedExe -WorkingDirectory $install
}
