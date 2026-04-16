from __future__ import annotations

from pathlib import Path
import sys
import re

from PySide6.QtCore import QProcess, QTimer, Qt
from PySide6.QtGui import QAction, QDragEnterEvent, QDropEvent, QGuiApplication, QIcon
from PySide6.QtWidgets import (
    QAbstractItemView,
    QApplication,
    QComboBox,
    QFileDialog,
    QFormLayout,
    QFrame,
    QGroupBox,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QListWidget,
    QListWidgetItem,
    QMainWindow,
    QMessageBox,
    QMenu,
    QPlainTextEdit,
    QProgressBar,
    QPushButton,
    QRadioButton,
    QSplitter,
    QSizePolicy,
    QStatusBar,
    QTableWidget,
    QTableWidgetItem,
    QToolBar,
    QVBoxLayout,
    QWidget,
)

from .extract_logic import (
    build_extract_invocation,
    build_extract_output_path,
    build_extract_output_path_in_directory,
    format_process_command,
    list_convert_targets,
    list_extract_targets,
    validate_convert_selection,
    validate_extract_selection,
)
from .ffprobe_service import FFprobeError, inspect_media
from .models import ExtractTarget, MediaInfo, TrackInfo
from .mux_logic import build_default_output_path, build_mux_args, build_mux_invocation, format_ffmpeg_command, validate_mux_selection


class MainWindow(QMainWindow):
    def _source_label(self, source_index: int) -> str:
        return f"素材{source_index + 1}"

    def _track_kind_display_label(self, track: TrackInfo) -> str:
        sibling_tracks = [
            item for item in self._all_tracks()
            if item.source_index == track.source_index and item.kind == track.kind and item.disposition.attached_pic == track.disposition.attached_pic
        ]
        sibling_tracks.sort(key=lambda item: (item.stream_index, item.track_key))
        index = next((position + 1 for position, item in enumerate(sibling_tracks) if item.track_key == track.track_key), 1)

        if track.disposition.attached_pic:
            return track.kind_label
        if track.kind in {"audio", "subtitle"}:
            return f"{track.kind_label}{index}"
        if track.kind == "video" and len(sibling_tracks) > 1:
            return f"{track.kind_label}{index}"
        return track.kind_label

    def _track_display_text(self, track: TrackInfo) -> str:
        parts = [self._source_label(track.source_index), self._track_kind_display_label(track), track.codec]
        if track.title:
            parts.append(track.title)
        return " / ".join(parts)

    def _current_convert_group(self) -> str | None:
        if self.current_mode != "convert":
            return None
        selected_tracks = [track for track in self._all_tracks() if track.selected and track.supported]
        if not selected_tracks:
            return None
        return self._convert_group_key(selected_tracks[0])

    def _is_track_selectable(self, track: TrackInfo) -> bool:
        if not track.supported:
            return False
        if self.current_mode == "convert":
            current_group = self._current_convert_group()
            if current_group is None:
                return True
            return track.selected or self._convert_group_key(track) == current_group
        if track.disposition.attached_pic:
            return self.current_mode in {"mux", "extract", "convert"}
        return True
    def _apply_initial_geometry(self) -> None:
        screen = QGuiApplication.primaryScreen()
        if screen is None:
            self.resize(1180, 760)
            return

        available = screen.availableGeometry()
        width = min(1180, max(960, available.width() - 120))
        height = min(760, max(640, available.height() - 120))
        x = available.x() + (available.width() - width) // 2
        y = available.y() + (available.height() - height) // 2
        self.setGeometry(x, y, width, height)

    def __init__(self) -> None:
        super().__init__()
        self.setWindowTitle("FFmpeg GUI v0.1.1")
        self.setAcceptDrops(True)
        self.setMinimumSize(900, 620)

        self.media_list: list[MediaInfo] = []
        self.current_mode = "mux"
        self.output_container = "mkv"
        self.current_extract_target_id: str | None = None
        self.selected_track_order: list[str] = []
        self.mode_selected_track_keys: dict[str, set[str]] = {
            "mux": set(),
            "extract": set(),
            "convert": set(),
        }
        self.mode_selected_track_orders: dict[str, list[str]] = {
            "mux": [],
            "extract": [],
            "convert": [],
        }
        self.output_path_value = ""
        self.output_path_dirty = False
        self._updating_output_controls = False
        self._active_task_label: str | None = None
        self._active_task_failed = False
        self._active_output_path: str | None = None
        self._active_cover_extract = False
        self._active_cover_last_size = -1
        self._active_cover_stable_ticks = 0
        self._forced_task_success = False
        self._ignore_next_process_error = False
        self._active_process_name = "ffmpeg"
        self._active_total_duration_ms = 0
        self._active_progress_percent = -1
        self._pending_batch_jobs: list[tuple[str, list[str], str, bool, int]] = []
        self._batch_total = 0
        self._batch_completed = 0
        self._batch_task_label: str | None = None

        self.ffmpeg_process = QProcess(self)
        self.ffmpeg_process.setProcessChannelMode(QProcess.ProcessChannelMode.MergedChannels)
        self.ffmpeg_process.readyReadStandardOutput.connect(self._on_process_output)
        self.ffmpeg_process.finished.connect(self._on_process_finished)
        self.ffmpeg_process.stateChanged.connect(self._on_process_state_changed)
        self.ffmpeg_process.errorOccurred.connect(self._on_process_error)

        self.process_state_timer = QTimer(self)
        self.process_state_timer.setInterval(400)
        self.process_state_timer.timeout.connect(self._poll_process_state)
        self.process_state_timer.start()

        self._build_ui()
        self._apply_initial_geometry()
        self._refresh_all()

    def _build_ui(self) -> None:
        self._build_toolbar()

        central = QWidget(self)
        root_layout = QVBoxLayout(central)
        root_layout.setContentsMargins(12, 12, 12, 12)
        root_layout.setSpacing(10)

        root_layout.addWidget(self._build_mode_bar(), 0)
        root_layout.addWidget(self._build_main_splitter(), 1)

        self.setCentralWidget(central)

        status_bar = QStatusBar(self)
        self.setStatusBar(status_bar)
        self.task_progress_bar = QProgressBar(self)
        self.task_progress_bar.setRange(0, 100)
        self.task_progress_bar.setValue(0)
        self.task_progress_bar.setFormat("--%")
        self.task_progress_bar.setTextVisible(True)
        self.task_progress_bar.setFixedWidth(120)
        status_bar.addPermanentWidget(self.task_progress_bar, 0)
        self.task_status_label = QLabel("任务状态：空闲", self)
        status_bar.addPermanentWidget(self.task_status_label, 1)
        status_bar.showMessage("空闲")

    def _set_task_status(self, text: str) -> None:
        display_text = text
        if self._active_task_label and text.startswith(f"{self._active_task_label}中") and self._active_progress_percent >= 0:
            display_text = f"{text} {self._active_progress_percent}%"
        self.task_status_label.setText(f"任务状态：{display_text}")
        self.statusBar().showMessage(display_text)

    def _show_notice(self, text: str) -> None:
        self.statusBar().showMessage(text, 3000)

    def _set_task_progress(self, percent: int | None) -> None:
        if percent is None:
            self._active_progress_percent = -1
            self.task_progress_bar.setRange(0, 100)
            self.task_progress_bar.setValue(0)
            self.task_progress_bar.setFormat("--%")
            return

        bounded = max(0, min(100, int(percent)))
        self._active_progress_percent = bounded
        self.task_progress_bar.setRange(0, 100)
        self.task_progress_bar.setValue(bounded)
        self.task_progress_bar.setFormat(f"{bounded}%")

    def _media_duration_ms(self, source_index: int) -> int:
        if 0 <= source_index < len(self.media_list):
            duration = self.media_list[source_index].duration_seconds
            if duration and duration > 0:
                return max(1, int(duration * 1000))
        return 0

    def _estimate_mux_duration_ms(self, selected_tracks: list[TrackInfo]) -> int:
        durations: list[int] = []
        seen_sources: set[int] = set()
        for track in selected_tracks:
            if track.disposition.attached_pic or track.source_index in seen_sources:
                continue
            seen_sources.add(track.source_index)
            duration_ms = self._media_duration_ms(track.source_index)
            if duration_ms > 0:
                durations.append(duration_ms)
        return max(durations, default=0)

    def _estimate_extract_duration_ms(self, track: TrackInfo) -> int:
        if track.disposition.attached_pic:
            return 0
        return self._media_duration_ms(track.source_index)

    @staticmethod
    def _parse_timestamp_to_ms(value: str) -> int:
        hours, minutes, seconds = value.split(":")
        total_seconds = int(hours) * 3600 + int(minutes) * 60 + float(seconds)
        return int(total_seconds * 1000)

    def _extract_progress_percent(self, payload: str) -> int | None:
        if self._active_process_name == "mkvextract":
            matches = re.findall(r"(?im)\bprogress\b[^\d]*([0-9]{1,3})%", payload)
            if matches:
                return max(0, min(100, int(matches[-1])))
            return None

        if self._active_total_duration_ms <= 0:
            return None

        time_matches = re.findall(r"(?im)^out_time=(\d{2}:\d{2}:\d{2}(?:\.\d+)?)\s*$", payload)
        if time_matches:
            current_ms = self._parse_timestamp_to_ms(time_matches[-1])
            return max(0, min(99, int(current_ms * 100 / self._active_total_duration_ms)))

        numeric_matches = re.findall(r"(?im)^out_time_(us|ms)=(\d+)\s*$", payload)
        if numeric_matches:
            unit, raw_value = numeric_matches[-1]
            amount = int(raw_value)
            current_ms = amount // 1000 if unit == "us" else amount
            if unit == "ms" and current_ms > self._active_total_duration_ms * 100:
                current_ms = amount // 1000
            return max(0, min(99, int(current_ms * 100 / self._active_total_duration_ms)))

        return None

    def _update_task_progress_from_logs(self, payload: str) -> None:
        if not self._active_task_label:
            return
        percent = self._extract_progress_percent(payload)
        if percent is None or percent == self._active_progress_percent:
            return
        self._set_task_progress(percent)
        self._set_task_status(f"{self._active_task_label}中...")

    def _has_active_output_file(self) -> bool:
        if not self._active_output_path:
            return False
        output_path = Path(self._active_output_path)
        return output_path.exists() and output_path.is_file() and output_path.stat().st_size > 0

    def _start_task(self, task_label: str, output_path: str, is_cover_extract: bool = False, process_name: str = "ffmpeg", total_duration_ms: int = 0) -> None:
        self._active_task_label = task_label
        self._active_task_failed = False
        self._active_output_path = output_path
        self._active_cover_extract = is_cover_extract
        self._active_cover_last_size = -1
        self._active_cover_stable_ticks = 0
        self._forced_task_success = False
        self._ignore_next_process_error = False
        self._active_process_name = process_name
        self._active_total_duration_ms = max(0, total_duration_ms)
        self._set_task_progress(0 if self._active_total_duration_ms > 0 else None)
        self._set_task_status(f"{task_label}中...")

    def _poll_process_state(self) -> None:
        if not self._active_task_label:
            return
        if self._active_cover_extract and not self._forced_task_success and self.ffmpeg_process.state() != QProcess.ProcessState.NotRunning:
            if self._has_active_output_file():
                output_size = Path(self._active_output_path).stat().st_size if self._active_output_path else -1
                if output_size == self._active_cover_last_size:
                    self._active_cover_stable_ticks += 1
                else:
                    self._active_cover_stable_ticks = 0
                self._active_cover_last_size = output_size
                if self._active_cover_stable_ticks >= 2:
                    self._forced_task_success = True
                    self._ignore_next_process_error = True
                    self.log_output.appendPlainText("[提示] 封面图已写出，主动结束卡住的 ffmpeg 进程。")
                    self.ffmpeg_process.kill()
                    return
        if self.ffmpeg_process.state() != QProcess.ProcessState.NotRunning:
            return
        success = self._forced_task_success or (
            not self._active_task_failed
            and self.ffmpeg_process.exitStatus() == QProcess.ExitStatus.NormalExit
            and self.ffmpeg_process.exitCode() == 0
        )
        self._finish_active_task(success)

    def _build_toolbar(self) -> None:
        toolbar = QToolBar("主工具栏", self)
        toolbar.setMovable(False)
        self.addToolBar(toolbar)

        import_action = QAction("导入主文件", self)
        import_action.triggered.connect(self.import_main_file)
        toolbar.addAction(import_action)

        add_action = QAction("添加媒体文件", self)
        add_action.triggered.connect(self.add_media_files)
        toolbar.addAction(add_action)

        clear_action = QAction("清空文件", self)
        clear_action.triggered.connect(self.clear_media)
        toolbar.addAction(clear_action)

        clear_selection_action = QAction("清空勾选", self)
        clear_selection_action.triggered.connect(self.clear_current_selection)
        toolbar.addAction(clear_selection_action)

    def _build_mode_bar(self) -> QWidget:
        container = QFrame(self)
        layout = QHBoxLayout(container)
        layout.setContentsMargins(0, 0, 0, 0)

        title = QLabel("工作模式", container)
        title.setStyleSheet("font-size: 15px; font-weight: 600;")
        layout.addWidget(title)

        self.mux_radio = QRadioButton("封装", container)
        self.extract_radio = QRadioButton("提取", container)
        self.convert_radio = QRadioButton("转换", container)
        self.mux_radio.setChecked(True)
        self.mux_radio.toggled.connect(self._on_mode_changed)
        self.extract_radio.toggled.connect(self._on_mode_changed)
        self.convert_radio.toggled.connect(self._on_mode_changed)

        layout.addWidget(self.mux_radio)
        layout.addWidget(self.extract_radio)
        layout.addWidget(self.convert_radio)
        layout.addStretch(1)
        return container

    def _build_main_splitter(self) -> QSplitter:
        splitter = QSplitter(Qt.Orientation.Horizontal, self)
        splitter.addWidget(self._build_left_workspace())
        splitter.addWidget(self._build_right_sidebar())
        splitter.setSizes([1180, 380])
        return splitter

    def _build_left_workspace(self) -> QWidget:
        container = QWidget(self)
        layout = QVBoxLayout(container)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(10)

        top_row = QWidget(container)
        top_layout = QHBoxLayout(top_row)
        top_layout.setContentsMargins(0, 0, 0, 0)
        top_layout.setSpacing(10)
        top_layout.addWidget(self._build_file_panel(), 0)
        top_layout.addWidget(self._build_track_panel(), 1)

        layout.addWidget(top_row, 1)
        layout.addWidget(self._build_bottom_panel(), 0)
        return container

    def _build_right_sidebar(self) -> QWidget:
        container = QWidget(self)
        container.setMinimumWidth(360)
        container.setMaximumWidth(420)
        layout = QVBoxLayout(container)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)
        layout.addWidget(self._build_operation_panel(), 1)
        return container


    def _build_file_panel(self) -> QWidget:
        group = QGroupBox("输入文件", self)
        layout = QVBoxLayout(group)
        self.file_list_widget = QListWidget(group)
        layout.addWidget(self.file_list_widget)
        return group

    def _build_track_panel(self) -> QWidget:
        group = QGroupBox("轨道表", self)
        layout = QVBoxLayout(group)

        self.track_table = QTableWidget(0, 8, group)
        self.track_table.setHorizontalHeaderLabels(["保留", "来源", "轨道", "类型", "Codec", "语言", "标题", "标记"])
        self.track_table.verticalHeader().setVisible(False)
        self.track_table.setAlternatingRowColors(True)
        self.track_table.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        self.track_table.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)
        self.track_table.setContextMenuPolicy(Qt.ContextMenuPolicy.CustomContextMenu)
        self.track_table.customContextMenuRequested.connect(self._show_track_context_menu)
        self.track_table.horizontalHeader().setStretchLastSection(True)
        self.track_table.cellChanged.connect(self._on_track_cell_changed)
        layout.addWidget(self.track_table)
        return group

    def _build_operation_panel(self) -> QWidget:
        group = QGroupBox("操作面板", self)
        layout = QVBoxLayout(group)
        layout.setSpacing(10)

        self.summary_label = QLabel("还没有导入媒体文件。", group)
        self.summary_label.setWordWrap(True)
        self.summary_label.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Fixed)
        layout.addWidget(self.summary_label, 0)

        output_group = QGroupBox("输出设置", group)
        output_group.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Fixed)
        output_group.setFixedHeight(104)
        output_layout = QFormLayout(output_group)
        output_layout.setContentsMargins(10, 8, 10, 8)
        output_layout.setVerticalSpacing(8)
        self.output_format_combo = QComboBox(output_group)
        self.output_format_combo.currentIndexChanged.connect(lambda _index: self._on_output_format_changed())
        self.output_path_edit = QLineEdit(output_group)
        self.output_path_edit.setPlaceholderText("完整输出路径")
        self.output_path_edit.textEdited.connect(self._on_output_path_edited)
        self.output_path_button = QPushButton("修改", output_group)
        self.output_path_button.setFixedWidth(72)
        self.output_path_button.clicked.connect(self._choose_output_directory)
        output_path_row = QWidget(output_group)
        output_path_layout = QHBoxLayout(output_path_row)
        output_path_layout.setContentsMargins(0, 0, 0, 0)
        output_path_layout.setSpacing(8)
        output_path_layout.addWidget(self.output_path_edit, 1)
        output_path_layout.addWidget(self.output_path_button, 0)
        output_layout.addRow("输出格式", self.output_format_combo)
        output_layout.addRow("输出路径", output_path_row)
        layout.addWidget(output_group, 0)

        self.validation_group = QGroupBox("规则校验", group)
        self.validation_group.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Fixed)
        self.validation_group.setMaximumHeight(72)
        validation_layout = QVBoxLayout(self.validation_group)
        self.validation_label = QLabel("当前还没有可校验内容。", self.validation_group)
        self.validation_label.setWordWrap(False)
        self.validation_label.setToolTip("")
        validation_layout.addWidget(self.validation_label)
        layout.addWidget(self.validation_group, 0)

        self.selected_order_group = QGroupBox("已选轨道顺序", group)
        self.selected_order_group.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Fixed)
        self.selected_order_group.setFixedHeight(226)
        selected_layout = QVBoxLayout(self.selected_order_group)
        self.selected_order_list = QListWidget(self.selected_order_group)
        self.selected_order_list.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Expanding)
        self.selected_order_list.setMinimumHeight(156)
        selected_layout.addWidget(self.selected_order_list, 1)
        layout.addWidget(self.selected_order_group, 0)
        layout.addSpacing(20)

        order_button_widget = QWidget(group)
        order_button_widget.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Fixed)
        order_button_widget.setFixedHeight(34)
        order_button_row = QHBoxLayout(order_button_widget)
        order_button_row.setContentsMargins(0, 0, 0, 0)
        order_button_row.setSpacing(8)
        self.move_up_button = QPushButton("上移", order_button_widget)
        self.move_up_button.setFixedHeight(34)
        self.move_down_button = QPushButton("下移", order_button_widget)
        self.move_down_button.setFixedHeight(34)
        self.move_up_button.clicked.connect(lambda: self._move_selected_track(-1))
        self.move_down_button.clicked.connect(lambda: self._move_selected_track(1))
        order_button_row.addWidget(self.move_up_button)
        order_button_row.addWidget(self.move_down_button)
        layout.addWidget(order_button_widget, 0)
        layout.addSpacing(10)

        self.run_button = QPushButton("开始封装", group)
        self.run_button.setFixedHeight(38)
        self.run_button.clicked.connect(self._run_current_job)
        self.run_button.setEnabled(False)
        layout.addWidget(self.run_button, 0)
        return group

    def _build_bottom_panel(self) -> QWidget:
        container = QWidget(self)
        layout = QVBoxLayout(container)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(8)

        command_group = QGroupBox("命令预览", self)
        command_group.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Fixed)
        command_group.setMaximumHeight(92)
        command_layout = QVBoxLayout(command_group)
        self.command_preview = QPlainTextEdit(command_group)
        self.command_preview.setFixedHeight(42)
        self.command_preview.setReadOnly(True)
        self.command_preview.setPlaceholderText("导入媒体后，这里会显示生成的 ffmpeg 命令预览。")
        command_layout.addWidget(self.command_preview)

        log_group = QGroupBox("日志输出", self)
        log_group.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Fixed)
        log_group.setMaximumHeight(116)
        log_layout = QVBoxLayout(log_group)
        self.log_output = QPlainTextEdit(log_group)
        self.log_output.setFixedHeight(60)
        self.log_output.setReadOnly(True)
        self.log_output.setPlaceholderText("任务日志会显示在这里。")
        log_layout.addWidget(self.log_output)

        layout.addWidget(command_group, 0)
        layout.addWidget(log_group, 0)
        return container

    def dragEnterEvent(self, event: QDragEnterEvent) -> None:
        if self._extract_dropped_paths(event.mimeData().urls()):
            event.acceptProposedAction()
            return
        event.ignore()

    def dropEvent(self, event: QDropEvent) -> None:
        paths = self._extract_dropped_paths(event.mimeData().urls())
        if not paths:
            event.ignore()
            return

        self._load_dropped_paths(paths)
        event.acceptProposedAction()

    def _extract_dropped_paths(self, urls: list) -> list[str]:
        paths: list[str] = []
        for url in urls:
            if not url.isLocalFile():
                continue
            local_path = url.toLocalFile()
            if Path(local_path).is_file():
                paths.append(local_path)
        return paths

    def _load_dropped_paths(self, paths: list[str]) -> None:
        if not paths:
            return

        if not self.media_list:
            self.media_list = []
            self._reset_mode_selections()
            self.current_extract_target_id = None
            self.output_path_dirty = False
            first_path, *rest = paths
            self._load_media_file(first_path, source_index=0)
            start_index = len(self.media_list)
            for offset, path in enumerate(rest):
                self._load_media_file(path, source_index=start_index + offset)
        else:
            start_index = len(self.media_list)
            for offset, path in enumerate(paths):
                self._load_media_file(path, source_index=start_index + offset)

        self._show_notice(f"已拖入 {len(paths)} 个文件")

    def import_main_file(self) -> None:
        path, _ = QFileDialog.getOpenFileName(self, "选择主媒体文件")
        if not path:
            return
        self.media_list = []
        self._reset_mode_selections()
        self.current_extract_target_id = None
        self.output_path_dirty = False
        self._load_media_file(path, source_index=0)

    def add_media_files(self) -> None:
        paths, _ = QFileDialog.getOpenFileNames(self, "添加媒体文件")
        if not paths:
            return

        start_index = len(self.media_list)
        for offset, path in enumerate(paths):
            self._load_media_file(path, source_index=start_index + offset)

    def clear_media(self) -> None:
        if self.ffmpeg_process.state() != QProcess.ProcessState.NotRunning:
            self.ffmpeg_process.kill()
        self._active_task_label = None
        self._active_task_failed = False
        self._active_output_path = None
        self._active_cover_extract = False
        self._active_cover_last_size = -1
        self._active_cover_stable_ticks = 0
        self._forced_task_success = False
        self._ignore_next_process_error = False
        self._active_process_name = "ffmpeg"
        self._active_total_duration_ms = 0
        self._active_progress_percent = -1
        self._set_task_progress(None)
        self._set_task_status("空闲")
        self.media_list = []
        self._reset_mode_selections()
        self.current_extract_target_id = None
        self.output_path_value = ""
        self.output_path_dirty = False
        self.log_output.appendPlainText("已清空当前媒体列表。")
        self._refresh_all()

    def clear_current_selection(self) -> None:
        for track in self._all_tracks():
            track.selected = False
        self.selected_track_order = []
        self._persist_current_mode_selection()
        self.output_path_value = ""
        self.output_path_dirty = False
        self.log_output.appendPlainText("已清空当前模式下的勾选。")
        self._refresh_all()

    def _default_output_path(self) -> Path | None:
        if not self.media_list:
            return None
        if self.current_mode == "mux":
            return Path(build_default_output_path(self.media_list, self.output_container, self._ordered_selected_tracks()))
        selected_tracks = self._ordered_selected_tracks()
        if self.current_mode == "extract" and len(selected_tracks) > 1:
            return Path(selected_tracks[0].source_path).parent
        if self.current_mode == "convert" and len(selected_tracks) > 1:
            return Path(selected_tracks[0].source_path).parent
        if len(selected_tracks) != 1:
            return None
        target = self._current_extract_target()
        if target is None:
            return None
        return Path(build_extract_output_path(selected_tracks[0], target))

    def _sync_output_controls(self, force: bool = False) -> None:
        default_path = self._default_output_path()
        if default_path is None:
            if force:
                self._set_output_controls("")
            return
        if force or not self.output_path_dirty or not self.output_path_value:
            self.output_path_value = str(default_path)
            self._set_output_controls(self.output_path_value)

    def _set_output_controls(self, output_path: str) -> None:
        self._updating_output_controls = True
        self.output_path_edit.setText(output_path)
        self._updating_output_controls = False

    def _current_output_path(self) -> str | None:
        output_path = self.output_path_edit.text().strip()
        return output_path or None

    def _choose_output_directory(self) -> None:
        start_path = self._current_output_path()
        selected_tracks = self._ordered_selected_tracks()
        if not start_path:
            default_path = self._default_output_path()
            start_path = str(default_path) if default_path is not None else ""
        if self.current_mode in {"extract", "convert"} and len(selected_tracks) > 1:
            chosen = QFileDialog.getExistingDirectory(self, "选择输出文件夹", start_path)
            if not chosen:
                return
            self.output_path_value = chosen
            self.output_path_dirty = True
            self._set_output_controls(self.output_path_value)
            self._refresh_command_preview()
            return
        chosen, _ = QFileDialog.getSaveFileName(self, "另存为", start_path, "所有文件 (*.*)")
        if not chosen:
            return
        self.output_path_value = chosen
        self.output_path_dirty = True
        self._set_output_controls(self.output_path_value)
        self._refresh_command_preview()

    def _on_output_path_edited(self, value: str) -> None:
        if self._updating_output_controls:
            return
        self.output_path_value = value.strip()
        self.output_path_dirty = True
        self._refresh_command_preview()

    def _load_media_file(self, path: str, source_index: int) -> None:
        try:
            media = inspect_media(path, source_index=source_index)
        except FFprobeError as error:
            QMessageBox.critical(self, "导入失败", str(error))
            self._show_notice("导入失败")
            self.log_output.appendPlainText(f"[错误] {path}\n{error}")
            return

        if source_index < len(self.media_list):
            self.media_list[source_index] = media
        else:
            self.media_list.append(media)

        self._restore_current_mode_selection()
        self._apply_selection_constraints()
        self._sync_selected_track_order()
        self.log_output.appendPlainText(f"[导入] {Path(path).name}，共 {len(media.tracks)} 条轨道。")
        self._show_notice(f"已导入 {Path(path).name}")
        self._refresh_all()

    def _refresh_all(self) -> None:
        self._refresh_file_list()
        self._refresh_track_table()
        self._refresh_side_panel()
        self._refresh_command_preview()

    def _refresh_file_list(self) -> None:
        self.file_list_widget.clear()
        if not self.media_list:
            self.file_list_widget.addItem(QListWidgetItem("还没有导入任何文件。"))
            return

        for media in self.media_list:
            text = f"{media.file_name}\n{media.format_name}"
            item = QListWidgetItem(text)
            item.setToolTip(media.input_path)
            self.file_list_widget.addItem(item)

    def _refresh_track_table(self) -> None:
        self.track_table.blockSignals(True)
        all_tracks = self._all_tracks()
        self.track_table.setRowCount(len(all_tracks))

        for row, track in enumerate(all_tracks):
            check_item = QTableWidgetItem()
            if self._is_track_selectable(track):
                check_item.setFlags(Qt.ItemFlag.ItemIsEnabled | Qt.ItemFlag.ItemIsUserCheckable | Qt.ItemFlag.ItemIsSelectable)
                check_item.setCheckState(Qt.CheckState.Checked if track.selected else Qt.CheckState.Unchecked)
            else:
                check_item.setFlags(Qt.ItemFlag.ItemIsEnabled)
                check_item.setText("-")
                if self.current_mode == "convert" and track.supported:
                    check_item.setToolTip("转换模式下只能同时选择同类型轨道。")
                else:
                    check_item.setToolTip(track.support_note or "当前不支持")
            self.track_table.setItem(row, 0, check_item)
            source_item = QTableWidgetItem(self._source_label(track.source_index))
            source_item.setToolTip(track.source_file_name)
            self.track_table.setItem(row, 1, source_item)
            self.track_table.setItem(row, 2, QTableWidgetItem(str(track.stream_index)))
            self.track_table.setItem(row, 3, QTableWidgetItem(self._track_kind_display_label(track)))
            self.track_table.setItem(row, 4, QTableWidgetItem(track.codec))
            self.track_table.setItem(row, 5, QTableWidgetItem(track.language or "-"))
            self.track_table.setItem(row, 6, QTableWidgetItem(track.title or "-"))
            self.track_table.setItem(row, 7, QTableWidgetItem(track.disposition.to_label()))

        self.track_table.resizeColumnsToContents()
        self.track_table.blockSignals(False)

    def _refresh_side_panel(self) -> None:
        selected_tracks = self._ordered_selected_tracks()
        self._refresh_output_format_options(selected_tracks)

        video_count = sum(1 for track in selected_tracks if track.kind == "video")
        subtitle_count = sum(1 for track in selected_tracks if track.kind == "subtitle")
        audio_count = sum(1 for track in selected_tracks if track.kind == "audio")

        if not self.media_list:
            self.summary_label.setText("还没有导入媒体文件。")
            self.validation_label.setText("当前还没有可校验内容。")
            self._show_selected_order_placeholder("还没有已选轨道。")
            self._set_output_controls("")
            self.output_path_edit.setEnabled(False)
            self.output_path_button.setEnabled(False)
            self.run_button.setEnabled(False)
            return

        self.output_path_edit.setEnabled(True)
        self.output_path_button.setEnabled(True)

        self.summary_label.setText(
            f"共导入 {len(self.media_list)} 个文件，当前勾选 {len(selected_tracks)} 条轨道。\n"
            f"视频 {video_count} / 音频 {audio_count} / 字幕 {subtitle_count}"
        )

        issues = self._collect_issues(selected_tracks)
        if issues:
            self.validation_label.setText(issues[0])
            self.validation_label.setToolTip("\n".join(issues))
        else:
            self.validation_label.setText("当前没有阻断错误。")
            self.validation_label.setToolTip("")

        if self.current_mode == "mux":
            self.selected_order_group.setEnabled(True)
            self.run_button.setText("开始封装")
            self._refresh_selected_order_list(selected_tracks)
        elif self.current_mode == "extract":
            self.selected_order_group.setEnabled(True)
            self._refresh_selected_order_list(selected_tracks)
            self.move_up_button.setEnabled(False)
            self.move_down_button.setEnabled(False)
            self.run_button.setText("开始提取")
        else:
            self.selected_order_group.setEnabled(True)
            self._refresh_selected_order_list(selected_tracks)
            self.move_up_button.setEnabled(False)
            self.move_down_button.setEnabled(False)
            self.run_button.setText("开始转换")

        self._sync_output_controls(force=not self.output_path_dirty)
        can_run = not issues and self.ffmpeg_process.state() == QProcess.ProcessState.NotRunning
        self.run_button.setEnabled(can_run)

    def _show_selected_order_placeholder(self, message: str) -> None:
        self.selected_order_group.setEnabled(True)
        self.selected_order_list.clear()
        placeholder = QListWidgetItem(message)
        placeholder.setFlags(Qt.ItemFlag.ItemIsEnabled)
        self.selected_order_list.addItem(placeholder)
        self.move_up_button.setEnabled(False)
        self.move_down_button.setEnabled(False)

    def _refresh_selected_order_list(self, selected_tracks: list[TrackInfo]) -> None:
        current_key = None
        current_item = self.selected_order_list.currentItem()
        if current_item is not None:
            current_key = current_item.data(Qt.ItemDataRole.UserRole)

        self.selected_order_list.clear()
        for index, track in enumerate(selected_tracks):
            item = QListWidgetItem(f"{index + 1}. {self._track_display_text(track)}")
            item.setToolTip(f"{track.source_file_name}\n{self._track_display_text(track)}")
            item.setData(Qt.ItemDataRole.UserRole, track.track_key)
            self.selected_order_list.addItem(item)
            if current_key == track.track_key:
                self.selected_order_list.setCurrentItem(item)

        has_items = bool(selected_tracks)
        self.move_up_button.setEnabled(has_items)
        self.move_down_button.setEnabled(has_items)

    def _refresh_output_format_options(self, selected_tracks: list[TrackInfo]) -> None:
        self.output_format_combo.blockSignals(True)
        self.output_format_combo.clear()

        if self.current_mode == "mux":
            self.output_format_combo.addItem("MKV", userData="mkv")
            self.output_format_combo.addItem("MP4", userData="mp4")
            index = 0 if self.output_container == "mkv" else 1
            self.output_format_combo.setCurrentIndex(index)
        elif self.current_mode == "extract" and len(selected_tracks) > 1:
            self.output_format_combo.addItem("按轨道默认格式批量提取", userData="batch-extract-default")
            self.output_format_combo.setCurrentIndex(0)
        else:
            targets = self._current_single_track_targets(selected_tracks)
            current_index = 0
            for index, target in enumerate(targets):
                self.output_format_combo.addItem(target.label, userData=target)
                if target.id == self.current_extract_target_id:
                    current_index = index
            if targets:
                self.output_format_combo.setCurrentIndex(current_index)
                chosen = self.output_format_combo.currentData()
                if isinstance(chosen, ExtractTarget):
                    self.current_extract_target_id = chosen.id
            else:
                self.current_extract_target_id = None

        self.output_format_combo.blockSignals(False)
        self.output_format_combo.setEnabled(self.current_mode == "mux" or self.output_format_combo.count() > 0)

    def _collect_issues(self, selected_tracks: list[TrackInfo]) -> list[str]:
        if self.current_mode == "mux":
            return validate_mux_selection(selected_tracks, self.output_container)

        issues = validate_extract_selection(selected_tracks) if self.current_mode == "extract" else validate_convert_selection(selected_tracks)
        if self.current_mode == "convert" and not issues and not self._is_same_convert_group(selected_tracks):
            issues.append("批量转换时只能同时选择同类型轨道。")
        if self.current_mode == "extract" and len(selected_tracks) > 1:
            return issues
        if not issues and self._current_extract_target() is None:
            issues.append("当前轨道没有可用的提取输出格式。" if self.current_mode == "extract" else "当前轨道没有可用的转换输出格式。")
        return issues

    def _refresh_command_preview(self) -> None:
        selected_tracks = self._ordered_selected_tracks()
        if not self.media_list:
            self.command_preview.setPlainText("")
            return

        output_path = self._current_output_path()
        if self.current_mode == "mux":
            if not output_path:
                self.command_preview.setPlainText("请先选择输出文件夹并填写文件名。")
                return
            program, args = build_mux_invocation(self.media_list, selected_tracks, self.output_container, output_path)
            self.command_preview.setPlainText(format_ffmpeg_command(args))
            return

        if len(selected_tracks) == 1:
            target = self._current_extract_target()
            if target is not None:
                if not output_path:
                    self.command_preview.setPlainText("请先选择输出文件夹并填写文件名。")
                    return
                program, args = build_extract_invocation(selected_tracks[0], target, output_path)
                self.command_preview.setPlainText(format_process_command(program, args))
                return
        if self.current_mode == "extract" and len(selected_tracks) > 1:
            if not output_path:
                self.command_preview.setPlainText("请先选择输出文件夹。")
                return
            output_directory = Path(output_path)
            if output_directory.suffix:
                output_directory = output_directory.parent
            commands: list[str] = []
            for track in selected_tracks[:3]:
                default_targets = list_extract_targets(track)
                if not default_targets:
                    continue
                resolved_output = build_extract_output_path_in_directory(track, default_targets[0], str(output_directory))
                program, args = build_extract_invocation(track, default_targets[0], resolved_output)
                commands.append(format_process_command(program, args))
            if len(selected_tracks) > 3:
                commands.append(f"... 共 {len(selected_tracks)} 条批量提取命令")
            self.command_preview.setPlainText("\n\n".join(commands) if commands else "当前选中的轨道没有可用的提取输出格式。")
            return
        if self.current_mode == "convert" and len(selected_tracks) > 1:
            target = self._current_extract_target()
            if target is None:
                self.command_preview.setPlainText("请先选择同类型轨道并指定转换格式。")
                return
            if not output_path:
                self.command_preview.setPlainText("请先选择输出文件夹。")
                return
            output_directory = Path(output_path)
            if output_directory.suffix:
                output_directory = output_directory.parent
            commands: list[str] = []
            for track in selected_tracks[:3]:
                resolved_output = build_extract_output_path_in_directory(track, target, str(output_directory))
                program, args = build_extract_invocation(track, target, resolved_output)
                commands.append(format_process_command(program, args))
            if len(selected_tracks) > 3:
                commands.append(f"... 共 {len(selected_tracks)} 条批量转换命令")
            self.command_preview.setPlainText("\n\n".join(commands))
            return
        self.command_preview.setPlainText("请先只选择 1 条轨道并指定输出格式。")

    def _show_track_context_menu(self, position) -> None:
        item = self.track_table.itemAt(position)
        if item is None:
            return
        row = item.row()
        all_tracks = self._all_tracks()
        if row < 0 or row >= len(all_tracks):
            return
        track = all_tracks[row]
        if not track.supported:
            return

        menu = QMenu(self)
        select_all_action = menu.addAction("全选")
        triggered = menu.exec(self.track_table.viewport().mapToGlobal(position))
        if triggered == select_all_action:
            self._apply_context_select_all(track)

    def _apply_context_select_all(self, trigger_track: TrackInfo) -> None:
        selectable_tracks = [track for track in self._all_tracks() if track.supported]
        if not selectable_tracks:
            return

        for track in selectable_tracks:
            track.selected = False

        selected_keys: list[str] = []
        if self.current_mode == "mux":
            normal_videos = [track for track in selectable_tracks if track.kind == "video" and not track.disposition.attached_pic]
            non_videos = [track for track in selectable_tracks if track.kind != "video" or track.disposition.attached_pic]
            if trigger_track.kind == "video" and not trigger_track.disposition.attached_pic:
                selected_keys = [trigger_track.track_key, *[track.track_key for track in non_videos]]
            else:
                if normal_videos:
                    selected_keys.append(normal_videos[0].track_key)
                selected_keys.extend(track.track_key for track in non_videos)
        elif self.current_mode == "extract":
            selected_keys = [track.track_key for track in selectable_tracks]
        elif self.current_mode == "convert":
            group_key = self._convert_group_key(trigger_track)
            selected_keys = [track.track_key for track in selectable_tracks if self._convert_group_key(track) == group_key]

        selected_key_set = set(selected_keys)
        for track in selectable_tracks:
            track.selected = track.track_key in selected_key_set
        self.selected_track_order = selected_keys
        self._apply_selection_constraints()
        self._sync_selected_track_order()
        self._refresh_all()

    def _current_single_track_targets(self, selected_tracks: list[TrackInfo]) -> list[ExtractTarget]:
        if self.current_mode == "extract":
            selected_track = selected_tracks[0] if len(selected_tracks) == 1 else None
            return list_extract_targets(selected_track)
        if self.current_mode != "convert":
            return []
        if not selected_tracks or not self._is_same_convert_group(selected_tracks):
            return []
        target_lists = [list_convert_targets(track) for track in selected_tracks]
        if not target_lists:
            return []
        common_ids = {target.id for target in target_lists[0]}
        for target_list in target_lists[1:]:
            common_ids &= {target.id for target in target_list}
        return [target for target in target_lists[0] if target.id in common_ids]

    def _convert_group_key(self, track: TrackInfo) -> str:
        if track.disposition.attached_pic:
            return "cover"
        return track.kind

    def _is_same_convert_group(self, selected_tracks: list[TrackInfo]) -> bool:
        if not selected_tracks:
            return True
        group_key = self._convert_group_key(selected_tracks[0])
        return all(self._convert_group_key(track) == group_key for track in selected_tracks)

    def _ordered_selected_tracks(self) -> list[TrackInfo]:
        track_map = {track.track_key: track for track in self._all_tracks()}
        ordered: list[TrackInfo] = []
        for track_key in self.selected_track_order:
            track = track_map.get(track_key)
            if track and track.selected and track.supported:
                ordered.append(track)
        return ordered

    def _sync_selected_track_order(self) -> None:
        selected_keys = [track.track_key for track in self._all_tracks() if track.selected and track.supported]
        next_order = [track_key for track_key in self.selected_track_order if track_key in selected_keys]
        for track in self._all_tracks():
            if track.track_key in selected_keys and track.track_key not in next_order:
                next_order.append(track.track_key)
        self.selected_track_order = next_order
        self._persist_current_mode_selection()

    def _apply_selection_constraints(self, changed_track: TrackInfo | None = None) -> None:
        all_tracks = [track for track in self._all_tracks() if track.supported]
        if self.current_mode == "extract":
            return

        if self.current_mode == "convert":
            selected_tracks = [track for track in all_tracks if track.selected]
            keep_group = None
            if changed_track and changed_track.selected:
                keep_group = self._convert_group_key(changed_track)
            elif selected_tracks:
                for track_key in self.selected_track_order:
                    matched = next((track for track in selected_tracks if track.track_key == track_key), None)
                    if matched is not None:
                        keep_group = self._convert_group_key(matched)
                        break
                if keep_group is None:
                    keep_group = self._convert_group_key(selected_tracks[0])

            if keep_group is not None:
                for track in selected_tracks:
                    if self._convert_group_key(track) != keep_group:
                        track.selected = False
            return

        selected_videos = [track for track in all_tracks if track.kind == "video" and track.selected and not track.disposition.attached_pic]
        keep_key = None
        if changed_track and changed_track.kind == "video" and not changed_track.disposition.attached_pic and changed_track.selected:
            keep_key = changed_track.track_key
        elif selected_videos:
            for track_key in self.selected_track_order:
                if any(track.track_key == track_key for track in selected_videos):
                    keep_key = track_key
                    break
            if keep_key is None:
                keep_key = selected_videos[0].track_key

        if keep_key is not None:
            for track in selected_videos:
                track.selected = track.track_key == keep_key

        selected_covers = [track for track in all_tracks if track.selected and track.disposition.attached_pic]
        if self.current_mode != "mux":
            for track in selected_covers:
                track.selected = False
            return

        cover_keep_key = None
        if changed_track and changed_track.disposition.attached_pic and changed_track.selected:
            cover_keep_key = changed_track.track_key
        elif selected_covers:
            for track_key in self.selected_track_order:
                if any(track.track_key == track_key for track in selected_covers):
                    cover_keep_key = track_key
                    break
            if cover_keep_key is None:
                cover_keep_key = selected_covers[0].track_key

        if cover_keep_key is not None:
            for track in selected_covers:
                track.selected = track.track_key == cover_keep_key

    def _move_selected_track(self, delta: int) -> None:
        current_row = self.selected_order_list.currentRow()
        if current_row < 0 or self.current_mode != "mux":
            return
        new_row = current_row + delta
        if new_row < 0 or new_row >= len(self.selected_track_order):
            return

        self.selected_track_order[current_row], self.selected_track_order[new_row] = (
            self.selected_track_order[new_row],
            self.selected_track_order[current_row],
        )
        self._refresh_side_panel()
        self._refresh_command_preview()
        self.selected_order_list.setCurrentRow(new_row)

    def _run_current_job(self) -> None:
        selected_tracks = self._ordered_selected_tracks()
        issues = self._collect_issues(selected_tracks)
        if issues:
            QMessageBox.warning(self, "不能开始执行", "\n".join(issues))
            return

        output_path = self._current_output_path()
        if not output_path:
            QMessageBox.warning(self, "不能开始执行", "请先选择输出文件夹并填写文件名。")
            return

        self.log_output.clear()
        if self.current_mode == "mux":
            program, args = build_mux_invocation(self.media_list, selected_tracks, self.output_container, output_path)
            self.log_output.appendPlainText("[开始封装]")
            self.log_output.appendPlainText(format_ffmpeg_command(args))
            self._start_task("封装", output_path, process_name=Path(program).stem.lower(), total_duration_ms=self._estimate_mux_duration_ms(selected_tracks))
            self.ffmpeg_process.start(program, args)
            self.run_button.setEnabled(False)
            return

        if self.current_mode == "extract" and len(selected_tracks) > 1:
            output_directory = Path(output_path)
            if output_directory.suffix:
                output_directory = output_directory.parent
            output_directory.mkdir(parents=True, exist_ok=True)
            self.log_output.appendPlainText("[开始批量提取]")
            jobs: list[tuple[str, list[str], str, bool, int]] = []
            for track in selected_tracks:
                default_targets = list_extract_targets(track)
                if not default_targets:
                    continue
                resolved_output = build_extract_output_path_in_directory(track, default_targets[0], str(output_directory))
                program, args = build_extract_invocation(track, default_targets[0], resolved_output)
                self.log_output.appendPlainText(format_process_command(program, args))
                jobs.append(
                    (
                        program,
                        args,
                        resolved_output,
                        track.disposition.attached_pic,
                        self._estimate_extract_duration_ms(track),
                    )
                )
            if not jobs:
                QMessageBox.warning(self, "不能开始提取", "当前选中的轨道没有可用的默认提取格式。")
                return
            self._pending_batch_jobs = jobs
            self._batch_total = len(jobs)
            self._batch_completed = 0
            self._batch_task_label = "提取"
            self.run_button.setEnabled(False)
            self._start_next_batch_job()
            return

        track = selected_tracks[0]
        target = self._current_extract_target()
        if target is None:
            QMessageBox.warning(self, "不能开始执行", "当前没有可用的输出格式。")
            return

        if self.current_mode == "convert" and len(selected_tracks) > 1:
            output_directory = Path(output_path)
            if output_directory.suffix:
                output_directory = output_directory.parent
            output_directory.mkdir(parents=True, exist_ok=True)
            self.log_output.appendPlainText("[开始批量转换]")
            jobs: list[tuple[str, list[str], str, bool, int]] = []
            for track in selected_tracks:
                resolved_output = build_extract_output_path_in_directory(track, target, str(output_directory))
                program, args = build_extract_invocation(track, target, resolved_output)
                self.log_output.appendPlainText(format_process_command(program, args))
                jobs.append(
                    (
                        program,
                        args,
                        resolved_output,
                        track.disposition.attached_pic,
                        self._estimate_extract_duration_ms(track),
                    )
                )
            self._pending_batch_jobs = jobs
            self._batch_total = len(jobs)
            self._batch_completed = 0
            self._batch_task_label = "转换"
            self.run_button.setEnabled(False)
            self._start_next_batch_job()
            return

        program, args = build_extract_invocation(track, target, output_path)
        task_label = "提取" if self.current_mode == "extract" else "转换"
        self.log_output.appendPlainText(f"[开始{task_label}]")
        self.log_output.appendPlainText(format_process_command(program, args))
        self._start_task(task_label, output_path, track.disposition.attached_pic, Path(program).stem.lower(), self._estimate_extract_duration_ms(track))
        self.ffmpeg_process.start(program, args)
        self.run_button.setEnabled(False)

    def _on_process_output(self) -> None:
        payload = bytes(self.ffmpeg_process.readAllStandardOutput()).decode("utf-8", errors="replace")
        if payload:
            self.log_output.appendPlainText(payload.rstrip())
            self._update_task_progress_from_logs(payload)
            self._update_task_status_from_logs(payload)

    def _update_task_status_from_logs(self, payload: str) -> None:
        if not self._active_task_label:
            return
        lowered = payload.lower()
        error_markers = (
            "conversion failed!",
            "error while",
            "invalid data found when processing input",
            "could not write header",
            "error opening",
            "error initializing",
            "sequence pattern",
            "use the -update option",
        )
        if any(marker in lowered for marker in error_markers):
            self._active_task_failed = True
            self._set_task_status(f"{self._active_task_label}失败")
            return
        if "progress=end" in lowered:
            self._set_task_status(f"{self._active_task_label}完成")
            return
        for line in payload.splitlines():
            if "video:" in line and "audio:" in line:
                self._set_task_status(f"{self._active_task_label}完成")
                return

    def _on_process_finished(self, exit_code: int, _exit_status: QProcess.ExitStatus) -> None:
        if not self._active_task_label:
            return
        if exit_code == 0:
            self.log_output.appendPlainText("[完成] 任务进程已正常退出。")
        elif self._forced_task_success and self._has_active_output_file():
            self.log_output.appendPlainText("[完成] 已强制结束卡住的封面图提取进程，输出文件已生成。")
        else:
            self._active_task_failed = True
            self.log_output.appendPlainText(f"[失败] {self._active_process_name} 退出码 {exit_code}")
        self._finish_active_task((exit_code == 0 and not self._active_task_failed) or (self._forced_task_success and self._has_active_output_file()))

    def _on_process_state_changed(self, state: QProcess.ProcessState) -> None:
        if state == QProcess.ProcessState.NotRunning and self._active_task_label:
            success = (self._forced_task_success and self._has_active_output_file()) or (
                not self._active_task_failed
                and self.ffmpeg_process.exitStatus() == QProcess.ExitStatus.NormalExit
                and self.ffmpeg_process.exitCode() == 0
            )
            self._finish_active_task(success)

    def _on_process_error(self, error: QProcess.ProcessError) -> None:
        if self._ignore_next_process_error:
            self._ignore_next_process_error = False
            return
        if self._forced_task_success and self._has_active_output_file() and self._active_task_label:
            self.log_output.appendPlainText("[完成] 已强制结束卡住的封面图提取进程，输出文件已生成。")
            self._finish_active_task(True)
            return
        if error == QProcess.ProcessError.FailedToStart:
            self.log_output.appendPlainText(f"[错误] 无法启动 {self._active_process_name}，请确认系统 PATH 已配置 {self._active_process_name}。")
            self._active_task_failed = True
            if self._active_task_label:
                self._finish_active_task(False)
            else:
                self._set_task_status(f"无法启动 {self._active_process_name}")
                self._refresh_side_panel()
            return
        if not self._active_task_label:
            return
        self.log_output.appendPlainText("[错误] 任务进程异常结束。")
        self._active_task_failed = True
        self._finish_active_task(False)

    def _on_mode_changed(self, checked: bool) -> None:
        if not checked:
            return
        self._persist_current_mode_selection()
        if self.mux_radio.isChecked():
            self.current_mode = "mux"
        elif self.extract_radio.isChecked():
            self.current_mode = "extract"
        else:
            self.current_mode = "convert"
        self._restore_current_mode_selection()
        self._apply_selection_constraints()
        self._sync_selected_track_order()
        self._show_notice("已切换模式")
        self._refresh_all()

    def _on_output_format_changed(self) -> None:
        if self.current_mode == "mux":
            self.output_container = self.output_format_combo.currentData() or "mkv"
        else:
            target = self.output_format_combo.currentData()
            if isinstance(target, ExtractTarget):
                self.current_extract_target_id = target.id
        if not self.output_path_dirty:
            self._sync_output_controls(force=True)
        self._refresh_side_panel()
        self._refresh_command_preview()

    def _on_track_cell_changed(self, row: int, column: int) -> None:
        if column != 0:
            return
        track = self._all_tracks()[row]
        item = self.track_table.item(row, column)
        if not item or not track.supported:
            return
        track.selected = item.checkState() == Qt.CheckState.Checked
        self._apply_selection_constraints(track)
        self._sync_selected_track_order()
        self._refresh_all()

    def _current_extract_target(self) -> ExtractTarget | None:
        target = self.output_format_combo.currentData()
        return target if isinstance(target, ExtractTarget) else None

    def _reset_mode_selections(self) -> None:
        for mode in self.mode_selected_track_keys:
            self.mode_selected_track_keys[mode] = set()
            self.mode_selected_track_orders[mode] = []
        self.selected_track_order = []

    def _persist_current_mode_selection(self) -> None:
        if self.current_mode not in self.mode_selected_track_keys:
            return
        selected_keys = [track.track_key for track in self._all_tracks() if track.selected and track.supported]
        self.mode_selected_track_keys[self.current_mode] = set(selected_keys)
        next_order = [track_key for track_key in self.selected_track_order if track_key in self.mode_selected_track_keys[self.current_mode]]
        for track in self._all_tracks():
            if track.track_key in self.mode_selected_track_keys[self.current_mode] and track.track_key not in next_order:
                next_order.append(track.track_key)
        self.selected_track_order = next_order
        self.mode_selected_track_orders[self.current_mode] = list(next_order)

    def _restore_current_mode_selection(self) -> None:
        selected_keys = self.mode_selected_track_keys.get(self.current_mode, set())
        for track in self._all_tracks():
            track.selected = track.supported and track.track_key in selected_keys
        stored_order = self.mode_selected_track_orders.get(self.current_mode, [])
        self.selected_track_order = [track_key for track_key in stored_order if track_key in selected_keys]
        for track in self._all_tracks():
            if track.selected and track.track_key not in self.selected_track_order:
                self.selected_track_order.append(track.track_key)

    def _reset_active_task_state(self) -> None:
        self._active_task_label = None
        self._active_task_failed = False
        self._active_output_path = None
        self._active_cover_extract = False
        self._active_cover_last_size = -1
        self._active_cover_stable_ticks = 0
        self._forced_task_success = False
        self._ignore_next_process_error = False
        self._active_total_duration_ms = 0
        self._active_progress_percent = -1

    def _start_next_batch_job(self) -> None:
        if not self._pending_batch_jobs:
            return
        program, args, output_path, is_cover_extract, total_duration_ms = self._pending_batch_jobs.pop(0)
        current_index = self._batch_completed + 1
        self.log_output.appendPlainText(f"[批量] 开始第 {current_index}/{self._batch_total} 条。")
        self._start_task(self._batch_task_label or "任务", output_path, is_cover_extract, Path(program).stem.lower(), total_duration_ms)
        self.ffmpeg_process.start(program, args)

    def _finish_active_task(self, success: bool) -> None:
        if not self._active_task_label:
            return
        task_label = self._active_task_label
        if self._batch_total > 0:
            if success:
                self._batch_completed += 1
                if self._pending_batch_jobs:
                    self.log_output.appendPlainText(f"[批量] 已完成 {self._batch_completed}/{self._batch_total}，继续下一条。")
                    self._reset_active_task_state()
                    QTimer.singleShot(0, self._start_next_batch_job)
                    return
                self._set_task_progress(100)
                self._set_task_status(f"{task_label}完成（{self._batch_completed}/{self._batch_total}）")
            else:
                failed_index = self._batch_completed + 1
                self._set_task_status(f"{task_label}失败（第 {failed_index}/{self._batch_total} 条）")
                self._pending_batch_jobs.clear()
            self._reset_active_task_state()
            self._batch_total = 0
            self._batch_completed = 0
            self._batch_task_label = None
            self._refresh_side_panel()
            return

        if success:
            self._set_task_progress(100)
        self._set_task_status(f"{task_label}完成" if success else f"{task_label}失败")
        self._reset_active_task_state()
        self._batch_task_label = None
        self._refresh_side_panel()

    def _all_tracks(self) -> list[TrackInfo]:
        tracks: list[TrackInfo] = []
        for media in self.media_list:
            tracks.extend(media.tracks)
        return tracks


def _resolve_app_icon_path() -> Path | None:
    bundled_root = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parent.parent))
    candidates = [
        bundled_root / "assets" / "app.ico",
        Path(__file__).resolve().parent.parent / "assets" / "app.ico",
        Path(sys.executable).resolve().parent / "assets" / "app.ico",
    ]
    for candidate in candidates:
        if candidate.exists():
            return candidate
    return None


def run() -> int:
    app = QApplication.instance() or QApplication([])
    icon_path = _resolve_app_icon_path()
    icon = QIcon(str(icon_path)) if icon_path else QIcon()
    if not icon.isNull():
        app.setWindowIcon(icon)
    window = MainWindow()
    if not icon.isNull():
        window.setWindowIcon(icon)
    window.show()
    return app.exec()
