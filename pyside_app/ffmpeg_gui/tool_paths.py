from __future__ import annotations

import os
import shutil
import sys
from pathlib import Path


def _bundled_root() -> Path:
    return Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parent.parent))


def _tool_candidates(executable_name: str) -> list[Path]:
    return [
        _bundled_root() / "tools" / executable_name,
        Path(__file__).resolve().parent.parent / "tools" / executable_name,
        Path(sys.executable).resolve().parent / "tools" / executable_name,
    ]


def find_ffmpeg() -> str | None:
    candidates = _tool_candidates("ffmpeg.exe")
    path = shutil.which("ffmpeg")
    if path:
        candidates.append(Path(path))
    return _first_existing(candidates)


def find_ffprobe() -> str | None:
    candidates = _tool_candidates("ffprobe.exe")
    path = shutil.which("ffprobe")
    if path:
        candidates.append(Path(path))
    return _first_existing(candidates)


def find_mkvextract() -> str | None:
    candidates = _tool_candidates("mkvextract.exe")
    path = shutil.which("mkvextract")
    if path:
        candidates.append(Path(path))
    for env_name in ("ProgramFiles", "ProgramFiles(x86)"):
        base_path = os.environ.get(env_name)
        if base_path:
            candidates.append(Path(base_path) / "MKVToolNix" / "mkvextract.exe")
    return _first_existing(candidates)


def _first_existing(candidates: list[Path]) -> str | None:
    seen: set[str] = set()
    for candidate in candidates:
        candidate_str = str(candidate)
        if candidate_str in seen:
            continue
        seen.add(candidate_str)
        if candidate.exists():
            return candidate_str
    return None