# FFmpeg GUI

当前主线版本是 `Python + PySide6`。

## 下载

当前推荐下载：
- GitHub Release：`FFmpeg_GUI_v0.1.0_win64_portable.zip`
- Release 页面：<https://github.com/KingStar-China/FFmpeg_GUI/releases/tag/v0.1.0>

说明：
- 这是当前唯一推荐下载的版本
- 内置 `ffmpeg`、`ffprobe`、`mkvextract`
- Windows 下解压后可直接运行

## 主线目录

- 当前主程序目录：`app/`

## 当前功能

- 封装 / 混流：
  - 多文件导入
  - 视频 / 音频 / 字幕按轨选择
  - 输出 `MKV` / `MP4`
  - 已选轨道顺序调整
- 提取：
  - 单轨提取
  - 原格式优先
  - 常见音频 / 字幕 / 视频转换格式
  - `MKV` 原始字幕优先走 `mkvextract`

## 运行

```powershell
cd app
.\run_windows.ps1
```

## 打包

```powershell
cd app
.\build_windows.ps1
```

## 仓库结构

- `app/`：当前维护中的 Python + PySide6 主线
- `logo/`：图标资源

## 说明

- 仓库首页和 Release 以 Python 版为准
