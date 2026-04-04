from __future__ import annotations

import tempfile
from pathlib import Path


APP_TEMP_DIR_NAME = "FFmpeg_GUI"


def get_app_temp_dir() -> Path:
    temp_dir = Path(tempfile.gettempdir()) / APP_TEMP_DIR_NAME
    temp_dir.mkdir(parents=True, exist_ok=True)
    return temp_dir