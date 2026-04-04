from __future__ import annotations

import json
import subprocess
from pathlib import Path

from .models import MediaInfo, TrackDisposition, TrackInfo
from .tool_paths import find_ffprobe


IMAGE_FILE_EXTENSIONS = {".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif"}


class FFprobeError(RuntimeError):
    pass


def ensure_ffprobe() -> str:
    ffprobe_path = find_ffprobe()
    if ffprobe_path:
        return ffprobe_path
    raise FFprobeError("未找到 ffprobe，可将 ffprobe.exe 放到程序 tools 目录或配置到系统 PATH。")


def inspect_media(input_path: str, source_index: int) -> MediaInfo:
    ffprobe_path = ensure_ffprobe()
    args = [
        ffprobe_path,
        "-v",
        "error",
        "-print_format",
        "json",
        "-show_streams",
        "-show_format",
        input_path,
    ]
    result = subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode != 0:
        raise FFprobeError(result.stderr.strip() or "ffprobe 解析失败。")

    parsed = json.loads(result.stdout or "{}")
    streams = parsed.get("streams", [])
    tracks = [map_stream_to_track(stream, input_path, source_index) for stream in streams]
    mark_embedded_cover_art(tracks, input_path)

    media_format = parsed.get("format", {})
    return MediaInfo(
        input_path=input_path,
        file_name=Path(input_path).name,
        format_name=str(media_format.get("format_name", "unknown")),
        duration_seconds=_to_float(media_format.get("duration")),
        size_bytes=_to_int(media_format.get("size")),
        tracks=tracks,
    )


def mark_embedded_cover_art(tracks: list[TrackInfo], input_path: str) -> None:
    image_path = Path(input_path)
    if image_path.suffix.lower() not in IMAGE_FILE_EXTENSIONS:
        return

    video_tracks = [track for track in tracks if track.kind == "video"]
    if len(video_tracks) != 1 or len(tracks) != 1:
        return

    cover_track = video_tracks[0]
    cover_track.disposition.attached_pic = True
    cover_track.selected = True
    cover_track.supported = True
    cover_track.support_note = None


def map_stream_to_track(stream: dict, source_path: str, source_index: int) -> TrackInfo:
    codec_type = str(stream.get("codec_type", "unknown"))
    kind = normalize_track_kind(codec_type)
    supported = kind in {"video", "audio", "subtitle"}
    tags = stream.get("tags") or {}
    disposition = map_disposition(stream.get("disposition") or {})
    stream_index = int(stream.get("index", -1))
    source_file_name = Path(source_path).name
    return TrackInfo(
        track_key=f"{source_index}:{stream_index}",
        source_index=source_index,
        source_path=source_path,
        source_file_name=source_file_name,
        stream_index=stream_index,
        kind=kind,
        codec=str(stream.get("codec_name", "unknown")),
        codec_long_name=_to_optional_string(stream.get("codec_long_name")),
        language=_to_optional_string(tags.get("language")),
        title=_to_optional_string(tags.get("title")),
        supported=supported,
        support_note=None if supported else "v1 不支持此类轨道",
        disposition=disposition,
        selected=supported,
    )


def map_chapter_to_track(chapter: dict, chapter_index: int, source_path: str, source_index: int) -> TrackInfo:
    tags = chapter.get("tags") or {}
    return TrackInfo(
        track_key=f"{source_index}:chapter:{chapter_index}",
        source_index=source_index,
        source_path=source_path,
        source_file_name=Path(source_path).name,
        stream_index=-1000 - chapter_index,
        kind="chapter",
        codec="chapter",
        codec_long_name="Chapter metadata",
        title=_to_optional_string(tags.get("title")) or f"Chapter {chapter_index + 1}",
        supported=False,
        support_note="v1 不支持章节导出或编辑",
        synthetic=True,
    )


def map_disposition(input_data: dict) -> TrackDisposition:
    return TrackDisposition(
        default=input_data.get("default") == 1,
        forced=input_data.get("forced") == 1,
        hearing_impaired=input_data.get("hearing_impaired") == 1,
        visual_impaired=input_data.get("visual_impaired") == 1,
        attached_pic=input_data.get("attached_pic") == 1,
    )


def normalize_track_kind(codec_type: str) -> str:
    if codec_type in {"video", "audio", "subtitle", "data", "attachment"}:
        return codec_type
    return "unknown"


def _to_optional_string(value: object) -> str | None:
    if value in (None, ""):
        return None
    return str(value)


def _to_float(value: object) -> float | None:
    if value in (None, ""):
        return None
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def _to_int(value: object) -> int | None:
    if value in (None, ""):
        return None
    try:
        return int(float(value))
    except (TypeError, ValueError):
        return None