# FFmpeg GUI

当前主线版本是 `Python + PySide6`。

## 当前主线

- 主程序目录：`pyside_app/`
- 当前发布版：Python 绿色版
- GitHub Release 下载：
  - `FFmpeg_GUI_v0.1.0_win64_portable.zip`

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
cd pyside_app
.\run_windows.ps1
```

## 打包

```powershell
cd pyside_app
.\build_windows.ps1
```

## 仓库结构

- `pyside_app/`：当前维护中的 Python + PySide6 主线
- `legacy/electron/`：旧的 Electron + React 实现，仅作历史归档
- `logo/`：图标资源

## 说明

- 当前仓库首页和 Release 以 Python 版为准
- Electron 版不再作为主线继续整理发布