from __future__ import annotations

from pathlib import Path

from .models import MediaInfo, TrackInfo


MP4_TEXT_SUBTITLE_CODECS = {
    "ass",
    "mov_text",
    "srt",
    "ssa",
    "subrip",
    "text",
    "tx3g",
    "webvtt",
}


def validate_mux_selection(selected_tracks: list[TrackInfo], output_container: str) -> list[str]:
    issues: list[str] = []
    if not selected_tracks:
        issues.append("封装模式下至少要勾选 1 条轨道。")
        return issues

    video_tracks = [track for track in selected_tracks if track.kind == "video" and not track.disposition.attached_pic]
    if len(video_tracks) > 1:
        issues.append("封装模式下最多只能勾选 1 条视频轨。")

    cover_tracks = [track for track in selected_tracks if track.disposition.attached_pic]
    if len(cover_tracks) > 1:
        issues.append("封装模式下最多只能勾选 1 张封面图。")

    if output_container == "mp4":
        for track in selected_tracks:
            if track.kind != "subtitle":
                continue
            if not is_mp4_text_subtitle(track.codec):
                issues.append(
                    f"MP4 不支持当前字幕轨：{track.source_file_name} / 轨道 {track.stream_index} / {track.codec}。"
                )
    return issues


def is_mp4_text_subtitle(codec: str) -> bool:
    return codec.strip().lower() in MP4_TEXT_SUBTITLE_CODECS


def build_default_output_path(media_list: list[MediaInfo], output_container: str) -> str:
    if not media_list:
        return f"output.{output_container}"
    first_input = Path(media_list[0].input_path)
    return str(first_input.with_suffix(f".{output_container}"))


def build_mux_args(
    media_list: list[MediaInfo],
    selected_tracks: list[TrackInfo],
    output_container: str,
    output_path: str,
) -> list[str]:
    args: list[str] = ["-y", "-nostdin", "-progress", "pipe:1", "-nostats"]
    for media in media_list:
        args.extend(["-i", media.input_path])

    for track in selected_tracks:
        args.extend(["-map", f"{track.source_index}:{track.stream_index}"])

    normal_video_tracks = [track for track in selected_tracks if track.kind == "video" and not track.disposition.attached_pic]
    video_tracks = [track for track in selected_tracks if track.kind == "video"]
    cover_tracks = [track for track in selected_tracks if track.disposition.attached_pic]
    audio_tracks = [track for track in selected_tracks if track.kind == "audio"]
    subtitle_tracks = [track for track in selected_tracks if track.kind == "subtitle"]

    if output_container == "mkv":
        args.extend(["-c", "copy"])
    else:
        if video_tracks:
            args.extend(["-c:v", "copy"])
        if audio_tracks:
            args.extend(["-c:a", "copy"])
        if subtitle_tracks:
            args.extend(["-c:s", "mov_text"])
        for output_video_index, track in enumerate([item for item in selected_tracks if item.kind == "video"]):
            if track.disposition.attached_pic:
                args.extend([f"-disposition:v:{output_video_index}", "attached_pic"])

    args.append(output_path)
    return args


def format_ffmpeg_command(args: list[str]) -> str:
    parts = ["ffmpeg", *args]
    return " ".join(_quote(arg) for arg in parts)


def _quote(value: str) -> str:
    if not value:
        return '""'
    if any(char.isspace() for char in value) or '"' in value:
        return '"' + value.replace('"', '\\"') + '"'
    return value


