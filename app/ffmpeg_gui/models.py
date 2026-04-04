from __future__ import annotations

from dataclasses import dataclass, field


TRACK_KIND_LABELS = {
    "video": "视频",
    "audio": "音频",
    "subtitle": "字幕",
    "data": "数据",
    "attachment": "附件",
    "chapter": "章节",
    "unknown": "未知",
}


@dataclass(slots=True)
class TrackDisposition:
    default: bool = False
    forced: bool = False
    hearing_impaired: bool = False
    visual_impaired: bool = False
    attached_pic: bool = False

    def to_label(self) -> str:
        labels: list[str] = []
        if self.default:
            labels.append("default")
        if self.forced:
            labels.append("forced")
        if self.hearing_impaired:
            labels.append("hearing")
        if self.visual_impaired:
            labels.append("visual")
        if self.attached_pic:
            labels.append("attached_pic")
        return " / ".join(labels) if labels else "-"


@dataclass(slots=True)
class TrackInfo:
    track_key: str
    source_index: int
    source_path: str
    source_file_name: str
    stream_index: int
    kind: str
    codec: str
    codec_long_name: str | None = None
    language: str | None = None
    title: str | None = None
    supported: bool = False
    support_note: str | None = None
    disposition: TrackDisposition = field(default_factory=TrackDisposition)
    synthetic: bool = False
    selected: bool = False

    @property
    def kind_label(self) -> str:
        if self.disposition.attached_pic:
            return "封面图"
        return TRACK_KIND_LABELS.get(self.kind, "未知")


@dataclass(slots=True)
class MediaInfo:
    input_path: str
    file_name: str
    format_name: str
    duration_seconds: float | None
    size_bytes: int | None
    tracks: list[TrackInfo]


@dataclass(slots=True, frozen=True)
class ExtractTarget:
    id: str
    label: str
    extension: str
    mode: str
    codec_args: tuple[str, ...] = ()
