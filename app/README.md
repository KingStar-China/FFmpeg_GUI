# FFmpeg GUI

这是当前维护中的 `Python + PySide6` 主线版本。

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

## 临时文件规则

- 当前正式功能默认不在项目根目录生成临时文件
- 如后续需要中间文件，统一放到系统临时目录 `%TEMP%\FFmpeg_GUI`