# FFmpeg GUI

当前稳定版是 Windows 原生 `v0.2.0`，技术栈为 `C# + .NET 10 + WPF Fluent`。原有 `Python + PySide6` 的 `v0.1.1` 继续保留，作为旧版兼容与回退实现。

## 当前能力

- 封装 / 混流：多文件导入、按轨选择、视频默认 MP4；仅选择音频时自动混音为 1 条音频流，默认 M4A，并支持 MP3/AAC/WAV/FLAC/Opus。
- 提取：原始轨道提取、常见视频/音频/字幕转换、MKV 字幕调用 `mkvextract`。
- 格式转换：调用 FFmpeg 执行单文件转换。
- 原生体验：系统 Fluent 主题、文件拖放、异步任务、实时进度、日志和任务取消。
- 安全检查：禁止覆盖输入文件，并在执行前显示规则校验与完整命令预览。

## 运行原生版

需要 Windows 和 .NET 10 SDK：

```powershell
.\run_windows_native.ps1
```

也可以把媒体文件路径作为参数传入，程序启动后会自动分析：

```powershell
.\run_windows_native.ps1 "D:\Media\sample.mkv"
```

## 测试与构建

```powershell
dotnet test .\FFmpegGui.slnx --configuration Release
dotnet build .\src\FFmpegGui.App\FFmpegGui.App.csproj --configuration Release
```

生成包含 .NET Runtime、FFmpeg、FFprobe 和 mkvextract 的 x64 自包含便携包：

```powershell
.\build_windows_native.ps1
```

如果仓库的 `app\tools` 中没有 FFmpeg，可显式指定：

```powershell
.\build_windows_native.ps1 -FfmpegBin "C:\Tools\ffmpeg\bin"
```

输出位于 `artifacts\`。

## 仓库结构

- `src/FFmpegGui.App/`：WPF 界面与交互。
- `src/FFmpegGui.Core/`：封装、提取、转换规则和参数生成。
- `src/FFmpegGui.Infrastructure/`：FFprobe 分析、工具定位和进程执行。
- `tests/FFmpegGui.Core.Tests/`：核心规则自动化测试。
- `app/`：保留的 Python `v0.1.1` 旧版。
- `logo/`：图标资源。

## 稳定版下载

- 当前版本：`FFmpeg_GUI_v0.2.0_win-x64_native_portable.zip`
- Release 页面：<https://github.com/KingStar-China/FFmpeg_GUI/releases/tag/v0.2.0>
- 便携包内置 .NET Runtime、FFmpeg、FFprobe 和 mkvextract，解压后直接运行 `FFmpeg GUI.exe`。
- 旧版 `v0.1.1`：<https://github.com/KingStar-China/FFmpeg_GUI/releases/tag/v0.1.1>
