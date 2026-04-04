# FFmpeg GUI PySide6

这是 Electron 版本之外的 Python + PySide6 重做版。

当前已完成：
- 主窗口三栏布局
- 导入主文件
- 添加媒体文件
- 使用 `ffprobe` 读取轨道
- 轨道表显示来源文件、轨道、类型、codec、语言、标题、标记
- 封装模式支持多文件混流
- 封装模式限制最多 1 条视频轨
- `MP4` 模式下会校验字幕兼容性
- 已选轨道支持顺序调整，并按顺序生成 `-map`
- 提取模式限制只能选择 1 条轨道
- 提取模式支持默认输出和可转换格式下拉
- 底部显示命令预览和执行日志

运行：

```powershell
cd pyside_app
python -m venv .venv
.venv\Scripts\Activate.ps1
pip install -r requirements.txt
python app.py
```

Windows 快速启动：

```powershell
cd pyside_app
.\run_windows.ps1
```

Windows 打包：

```powershell
cd pyside_app
.\build_windows.ps1
```
临时文件规则：

- 当前正式功能默认不在项目根目录生成临时文件
- 如后续需要中间文件，统一放到系统临时目录 `%TEMP%\FFmpeg_GUI`