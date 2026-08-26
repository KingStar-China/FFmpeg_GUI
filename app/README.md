# FFmpeg GUI

这是历史 `Python + PySide6` `v0.1.1` 版本，仅用于兼容和回退，不参与当前 Windows 原生 release。

当前版本请使用仓库根目录的 WPF 原生版：

```powershell
.\run_windows_native.ps1
```

## 运行

```powershell
cd app
.\run_windows.ps1
```

## 打包

此脚本只用于重新生成历史 Python + Qt 版本；原生版请在仓库根目录运行 `.\build_windows_native.ps1`。

```powershell
cd app
.\build_windows.ps1
```

脚本只保留解压目录，不再生成压缩包；默认优先使用 `app/tools` 中的静态 FFmpeg/FFprobe。也可以指定其他工具目录：

```powershell
.\build_windows.ps1 -FfmpegBin "C:\Tools\ffmpeg\bin"
```

## 临时文件规则

- 当前正式功能默认不在项目根目录生成临时文件
- 如后续需要中间文件，统一放到系统临时目录 `%TEMP%\FFmpeg_GUI`
