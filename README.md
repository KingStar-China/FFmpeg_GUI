# FFmpeg GUI

一个给 FFmpeg 用的桌面图形界面，面向不想手敲命令的用户。

当前版本重点做两件事：
- 封装：把多个输入文件里的视频、音频、字幕轨道按需混流到 `mkv` 或 `mp4`
- 提取：把单条轨道提取出来，并提供原格式或可转换格式

## 当前功能

### 1. 封装 / 混流
- 支持导入主文件，再追加多个媒体文件
- 轨道表会显示所有输入文件的轨道
- 每条轨道都能单独勾选或取消
- 默认最多只允许选择 1 条视频轨
- 音频和字幕可多选
- 已选轨道支持上移 / 下移，最终输出顺序按界面顺序执行
- 输出容器支持：`MKV`、`MP4`
- `MP4` 支持文本软字幕，输出时自动转成 `mov_text`
- 如果选中了 `PGS`、`DVD subtitle` 这类不兼容字幕，程序会阻止导出到 `MP4`

### 2. 提取
- 轨道表里只保留 1 条轨道时，进入提取模式
- 默认优先原格式导出
- 同时提供可转换格式下拉选择
- 音频支持常见转换格式：`MP3`、`AAC`、`FLAC`、`WAV`、`Opus`
- 字幕支持常见转换格式：`SRT`、`ASS`、`WebVTT`
- 视频支持常见输出格式：`单轨 MKV`、`MP4 (H.264)`、`WebM (VP9)`、`AVI (MPEG-4)`

## 使用前提
- 系统：`Windows 10` 或更高
- 已安装 `ffmpeg` 和 `ffprobe`
- 并且这两个命令已经加入系统 `PATH`

如果没有配置好 `ffmpeg` / `ffprobe`，程序会直接提示无法运行。

## 开发运行

```powershell
npm install
npm run dev
```

## 构建

```powershell
npm run build
```

## 绿色版

绿色版目录：
- `release/win-unpacked`

双击下面这个文件即可启动：
- `release/win-unpacked/FFmpeg GUI.exe`

## 项目结构

- `src/main`：Electron 主进程和 IPC
- `src/renderer`：React 图形界面
- `src/shared`：轨道规则、命令生成、类型定义、测试

## 已知限制
- 目前只做单任务工作流，不支持批处理队列
- 不包含内置 FFmpeg，需要使用系统里已有的 `ffmpeg` / `ffprobe`
- `Windows 7 / 8 / 8.1` 不在当前 Electron 版本支持范围内
- 当前仓库主要验证了 Windows 环境

## 仓库

- GitHub: https://github.com/KingStar-China/FFmpeg_GUI