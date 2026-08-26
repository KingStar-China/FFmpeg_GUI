# FFmpeg GUI

当前稳定版是 Windows 原生 `v0.2.0`，技术栈为 `C# + .NET 10 + WPF Fluent`。发布物只包含原生版；`app/` 中的 `Python + PySide6` 仅作为历史源码保留，不参与 release。

## 当前能力

- 单文件：统一处理提取、转换、封装和混音；轨道表中的“目标编码”可逐轨选择，单轨输出格式跟随目标编码，多轨自动提供对应的容器或混音格式。
- 音频混音：选择多条音频会通过 FFmpeg 混音为 1 条音频流，默认 M4A，并支持 MP3/AAC/WAV/FLAC/Opus。
- 批量：入口已保留，暂未开放。
- 原生体验：系统 Fluent 主题、文件拖放、异步任务、实时进度、日志和任务取消。
- 安全检查：禁止覆盖输入文件，并在执行前显示规则校验与完整命令预览。

## 运行原生版

开发运行需要 Windows 和 .NET 10 SDK；运行发布目录需要 .NET 10 Desktop Runtime：

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

生成不包含 .NET Runtime 的 x64 原生目录版（需要目标机安装 .NET 10 Desktop Runtime）：

```powershell
.\build_windows_native.ps1
```

如果仓库的 `app\tools` 中没有 FFmpeg，可显式指定：

```powershell
.\build_windows_native.ps1 -FfmpegBin "C:\Tools\ffmpeg\bin"
```

输出位于 `artifacts\FFmpeg GUI Native win-x64\`，脚本不再额外生成压缩包。

## 仓库结构

- `src/FFmpegGui.App/`：WPF 界面与交互。
- `src/FFmpegGui.Core/`：封装、提取、转换规则和参数生成。
- `src/FFmpegGui.Infrastructure/`：FFprobe 分析、工具定位和进程执行。
- `tests/FFmpegGui.Core.Tests/`：核心规则自动化测试。
- `app/`：历史 Python `v0.1.1` 源码、图标和 FFmpeg 工具，不参与原生 release。
- `logo/`：图标资源。

## 稳定版下载

- 当前版本：`artifacts\FFmpeg GUI Native win-x64\`
- Release 页面：<https://github.com/KingStar-China/FFmpeg_GUI/releases/tag/v0.2.0>
- 原生目录版包含 FFmpeg、FFprobe 和 mkvextract；目标机需先安装 .NET 10 Desktop Runtime，然后运行 `FFmpeg GUI.exe`。
- 旧版 `v0.1.1`：<https://github.com/KingStar-China/FFmpeg_GUI/releases/tag/v0.1.1>
