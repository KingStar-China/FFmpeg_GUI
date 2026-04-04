from __future__ import annotations

import os
import shutil
import sys
from pathlib import Path

from .models import ExtractTarget, TrackInfo
from .tool_paths import find_ffmpeg

AUDIO_COPY_EXTENSIONS = {
    "aac": "aac",
    "mp3": "mp3",
    "flac": "flac",
    "ac3": "ac3",
    "eac3": "eac3",
    "opus": "opus",
    "vorbis": "ogg",
}

TEXT_SUBTITLE_CODECS = {
    "ass": "ass",
    "ssa": "ass",
    "subrip": "srt",
    "srt": "srt",
    "webvtt": "vtt",
    "mov_text": "srt",
    "text": "srt",
    "tx3g": "srt",
}

IMAGE_SUBTITLE_CODECS = {
    "hdmv_pgs_subtitle",
    "dvd_subtitle",
    "xsub",
    "dvb_subtitle",
}

RAW_SUBTITLE_EXTENSIONS = {
    "ass": ("ass", "原始格式（ASS） (*.ass)"),
    "ssa": ("ass", "原始格式（ASS/SSA） (*.ass)"),
    "subrip": ("srt", "原始格式（SRT） (*.srt)"),
    "srt": ("srt", "原始格式（SRT） (*.srt)"),
    "webvtt": ("vtt", "原始格式（WebVTT） (*.vtt)"),
    "hdmv_pgs_subtitle": ("sup", "原始格式（SUP） (*.sup)"),
}

IMAGE_COVER_EXTENSIONS = {
    "png": "png",
    "mjpeg": "jpg",
    "jpeg": "jpg",
    "jpg": "jpg",
    "webp": "webp",
}


MATROSKA_EXTENSIONS = {".mkv", ".mka", ".mks", ".mk3d"}
RAW_SUBTITLE_OUTPUT_EXTENSIONS = {"ass", "srt", "vtt", "sup"}


def validate_extract_selection(selected_tracks: list[TrackInfo]) -> list[str]:
    if len(selected_tracks) == 1:
        return []
    return ["提取模式下必须且只能勾选 1 条轨道。"]


def list_extract_targets(track: TrackInfo | None) -> list[ExtractTarget]:
    if track is None:
        return []

    codec = track.codec.strip().lower()
    if track.disposition.attached_pic:
        targets = [_default_cover_target(codec)]
        targets.extend(
            [
                ExtractTarget("cover-png", "转换为 PNG (*.png)", "png", "transcode", ("-c:v", "png", "-frames:v", "1")),
                ExtractTarget("cover-jpg", "转换为 JPG (*.jpg)", "jpg", "transcode", ("-c:v", "mjpeg", "-frames:v", "1")),
                ExtractTarget("cover-webp", "转换为 WebP (*.webp)", "webp", "transcode", ("-c:v", "libwebp", "-frames:v", "1")),
            ]
        )
        return _dedupe_targets(targets)

    if track.kind == "audio":
        targets = [_default_audio_target(codec)]
        targets.extend(
            [
                ExtractTarget("audio-mp3", "转换为 MP3 (*.mp3)", "mp3", "transcode", ("-c:a", "libmp3lame")),
                ExtractTarget("audio-aac", "转换为 AAC (*.aac)", "aac", "transcode", ("-c:a", "aac")),
                ExtractTarget("audio-flac", "转换为 FLAC (*.flac)", "flac", "transcode", ("-c:a", "flac")),
                ExtractTarget("audio-wav", "转换为 WAV (*.wav)", "wav", "transcode", ("-c:a", "pcm_s16le")),
                ExtractTarget("audio-opus", "转换为 Opus (*.opus)", "opus", "transcode", ("-c:a", "libopus")),
            ]
        )
        return _dedupe_targets(targets)

    if track.kind == "subtitle":
        raw_target = _raw_subtitle_target(codec)
        targets = [raw_target] if raw_target is not None else [_default_subtitle_target(codec)]
        if codec not in IMAGE_SUBTITLE_CODECS:
            targets.extend(
                [
                    ExtractTarget("sub-srt", "转换为 SRT (*.srt)", "srt", "transcode", ("-c:s", "srt")),
                    ExtractTarget("sub-ass", "转换为 ASS (*.ass)", "ass", "transcode", ("-c:s", "ass")),
                    ExtractTarget("sub-vtt", "转换为 WebVTT (*.vtt)", "vtt", "transcode", ("-c:s", "webvtt")),
                ]
            )
        return _dedupe_targets(targets)

    if track.kind == "video":
        targets = [_default_video_target(codec)]
        targets.extend(
            [
                ExtractTarget(
                    "video-mp4-h264",
                    "转换为 MP4 (H.264) (*.mp4)",
                    "mp4",
                    "transcode",
                    ("-c:v", "libx264", "-pix_fmt", "yuv420p", "-movflags", "+faststart"),
                ),
                ExtractTarget(
                    "video-webm-vp9",
                    "转换为 WebM (VP9) (*.webm)",
                    "webm",
                    "transcode",
                    ("-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0"),
                ),
                ExtractTarget(
                    "video-avi-mpeg4",
                    "转换为 AVI (MPEG-4) (*.avi)",
                    "avi",
                    "transcode",
                    ("-c:v", "mpeg4", "-q:v", "5"),
                ),
            ]
        )
        return _dedupe_targets(targets)

    return []


def build_extract_output_path(track: TrackInfo, target: ExtractTarget) -> str:
    source = Path(track.source_path)
    kind = _sanitize_name(track.kind_label)
    codec = _sanitize_name(track.codec or "unknown")
    return str(
        source.with_name(
            f"{source.stem}.src{track.source_index + 1}.{kind}.{codec}.track{track.stream_index}.{target.extension}"
        )
    )


def build_extract_args(track: TrackInfo, target: ExtractTarget, output_path: str) -> list[str]:
    args: list[str] = ["-y", "-nostdin", "-progress", "pipe:1", "-nostats", "-i", track.source_path, "-map", f"0:{track.stream_index}", "-map_metadata", "-1", "-map_chapters", "-1"]
    if target.mode == "copy":
        args.extend(["-c", "copy"])
    else:
        args.extend(target.codec_args)

    if track.disposition.attached_pic and target.extension in {"png", "jpg", "webp"}:
        if "-frames:v" not in args:
            args.extend(["-frames:v", "1"])
        args.extend(["-f", "image2", "-update", "1"])

    args.append(output_path)
    return args


def build_extract_invocation(track: TrackInfo, target: ExtractTarget, output_path: str) -> tuple[str, list[str]]:
    mkvextract_path = preferred_mkvextract_path(track, target)
    if mkvextract_path is not None:
        return mkvextract_path, ["tracks", track.source_path, f"{track.stream_index}:{output_path}"]
    return find_ffmpeg() or "ffmpeg", build_extract_args(track, target, output_path)


def format_process_command(program: str, args: list[str]) -> str:
    parts = [program, *args]
    return " ".join(_quote(arg) for arg in parts)


def format_extract_command(track: TrackInfo, target: ExtractTarget) -> str:
    output_path = build_extract_output_path(track, target)
    program, args = build_extract_invocation(track, target, output_path)
    return format_process_command(program, args)


def preferred_mkvextract_path(track: TrackInfo, target: ExtractTarget) -> str | None:
    if track.kind != "subtitle" or target.mode != "copy":
        return None
    if target.extension not in RAW_SUBTITLE_OUTPUT_EXTENSIONS:
        return None
    if Path(track.source_path).suffix.lower() not in MATROSKA_EXTENSIONS:
        return None
    return find_mkvextract()


def find_mkvextract() -> str | None:
    bundled_root = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parent.parent))
    candidates: list[Path] = [
        bundled_root / "tools" / "mkvextract.exe",
        Path(__file__).resolve().parent.parent / "tools" / "mkvextract.exe",
        Path(sys.executable).resolve().parent / "tools" / "mkvextract.exe",
    ]

    path = shutil.which("mkvextract")
    if path:
        candidates.append(Path(path))

    for env_name in ("ProgramFiles", "ProgramFiles(x86)"):
        base_path = os.environ.get(env_name)
        if base_path:
            candidates.append(Path(base_path) / "MKVToolNix" / "mkvextract.exe")

    for candidate in candidates:
        if candidate.exists():
            return str(candidate)
    return None

def _default_cover_target(codec: str) -> ExtractTarget:
    extension = IMAGE_COVER_EXTENSIONS.get(codec)
    if extension == "png":
        return ExtractTarget("cover-default-png", "默认输出（PNG） (*.png)", "png", "transcode", ("-c:v", "png", "-frames:v", "1"))
    if extension == "jpg":
        return ExtractTarget("cover-default-jpg", "默认输出（JPG） (*.jpg)", "jpg", "transcode", ("-c:v", "mjpeg", "-frames:v", "1"))
    if extension == "webp":
        return ExtractTarget("cover-default-webp", "默认输出（WebP） (*.webp)", "webp", "transcode", ("-c:v", "libwebp", "-frames:v", "1"))
    return ExtractTarget("cover-default-png", "默认输出（PNG） (*.png)", "png", "transcode", ("-c:v", "png", "-frames:v", "1"))


def _default_audio_target(codec: str) -> ExtractTarget:
    if codec.startswith("pcm"):
        return ExtractTarget("audio-copy-wav", "默认输出（WAV） (*.wav)", "wav", "copy")
    extension = AUDIO_COPY_EXTENSIONS.get(codec)
    if extension:
        return ExtractTarget(f"audio-copy-{extension}", f"默认输出（原格式） (*.{extension})", extension, "copy")
    return ExtractTarget("audio-copy-mka", "默认输出（安全回退 MKA） (*.mka)", "mka", "copy")


def _raw_subtitle_target(codec: str) -> ExtractTarget | None:
    raw_target = RAW_SUBTITLE_EXTENSIONS.get(codec)
    if raw_target is None:
        return None
    extension, label = raw_target
    return ExtractTarget(f"sub-raw-{extension}", label, extension, "copy")


def _default_subtitle_target(codec: str) -> ExtractTarget:
    raw_target = _raw_subtitle_target(codec)
    if raw_target is not None:
        return raw_target

    extension = TEXT_SUBTITLE_CODECS.get(codec)
    if extension == "ass":
        mode = "copy" if codec == "ass" else "transcode"
        codec_args = () if mode == "copy" else ("-c:s", "ass")
        return ExtractTarget("sub-default-ass", "默认输出（ASS） (*.ass)", "ass", mode, codec_args)
    if extension == "srt":
        mode = "copy" if codec in {"subrip", "srt"} else "transcode"
        codec_args = () if mode == "copy" else ("-c:s", "srt")
        return ExtractTarget("sub-default-srt", "默认输出（SRT） (*.srt)", "srt", mode, codec_args)
    if extension == "vtt":
        mode = "copy" if codec == "webvtt" else "transcode"
        codec_args = () if mode == "copy" else ("-c:s", "webvtt")
        return ExtractTarget("sub-default-vtt", "默认输出（WebVTT） (*.vtt)", "vtt", mode, codec_args)
    return ExtractTarget("sub-default-mks", "默认输出（安全回退 MKS） (*.mks)", "mks", "copy")


def _dedupe_targets(targets: list[ExtractTarget]) -> list[ExtractTarget]:
    seen: set[str] = set()
    result: list[ExtractTarget] = []
    for target in targets:
        if target.id in seen:
            continue
        seen.add(target.id)
        result.append(target)
    return result

def _default_video_target(codec: str) -> ExtractTarget:
    if codec in {"h264", "avc1", "hevc", "h265"}:
        return ExtractTarget("video-default-mp4-copy", "默认输出（MP4） (*.mp4)", "mp4", "copy")
    if codec in {"vp8", "vp9"}:
        return ExtractTarget("video-default-webm-copy", "默认输出（WebM） (*.webm)", "webm", "copy")
    if codec in {"mpeg4", "msmpeg4", "msmpeg4v2", "msmpeg4v3", "xvid", "divx", "mjpeg"}:
        return ExtractTarget("video-default-avi-copy", "默认输出（AVI） (*.avi)", "avi", "copy")
    if codec in {"av1"}:
        return ExtractTarget("video-default-mkv-av1", "默认输出（MKV） (*.mkv)", "mkv", "copy")
    return ExtractTarget("video-copy-mkv", "默认输出（单轨 MKV） (*.mkv)", "mkv", "copy")

def _sanitize_name(value: str) -> str:
    cleaned = ''.join(char if char.isalnum() or char in {'-', '_'} else '_' for char in value.strip())
    return cleaned.strip('_') or 'unknown'




def _quote(value: str) -> str:
    if not value:
        return '""'
    if any(char.isspace() for char in value) or '"' in value:
        return '"' + value.replace('"', '\\"') + '"'
    return value
