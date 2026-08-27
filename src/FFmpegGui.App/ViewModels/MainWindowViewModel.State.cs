using System.IO;
using FFmpegGui.Core;

namespace FFmpegGui.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void SetMode(WorkMode mode)
    {
        if (_currentMode == mode || IsRunning)
        {
            return;
        }

        PersistCurrentModeSelection();
        _currentMode = mode;
        OnPropertiesChanged(nameof(IsSingleFileMode), nameof(IsBatchMode));
        RestoreCurrentModeSelection();
        ApplySelectionConstraints(null);
        SyncSelectedTrackOrder();
        StatusText = mode == WorkMode.Batch
            ? "已切换为批量模式，请先在左侧选择一个视频或音频文件"
            : "已切换为单文件模式";
        RefreshState();
    }

    private void OnMediaSelectionChanged(MediaItemViewModel changedItem)
    {
        if (_suppressMediaChanges || IsRunning || CurrentMode != WorkMode.Batch)
        {
            return;
        }

        if (changedItem.IsSelected)
        {
            if (changedItem.BatchKind is null
                || (_batchMediaKind is not null && changedItem.BatchKind != _batchMediaKind))
            {
                changedItem.SetSelectedSilently(false);
                return;
            }

            _batchMediaKind ??= changedItem.BatchKind;
        }

        RefreshBatchMediaKind();
        if (_batchMediaKind is null)
        {
            _outputPathDirty = false;
            SetOutputPath(string.Empty);
        }

        UpdateBatchMediaSelectability();
        RefreshState();
    }

    private void OnTrackSelectionChanged(TrackItemViewModel changedTrack)
    {
        if (_suppressTrackChanges || IsRunning)
        {
            return;
        }

        ApplySelectionConstraints(changedTrack);
        SyncSelectedTrackOrder();
        if (changedTrack.IsSelected && IsMp4OutputSelected())
        {
            ApplyMp4TargetDefault(changedTrack);
        }
        RefreshState();
    }

    private void OnTrackTargetChanged(TrackItemViewModel changedTrack)
    {
        if (!IsRunning)
        {
            RefreshState();
        }
    }

    private void UpdateTrackKindLabels()
    {
        foreach (var track in Tracks)
        {
            var siblings = Tracks
                .Where(item => item.Track.SourceIndex == track.Track.SourceIndex
                    && item.Track.Kind == track.Track.Kind
                    && item.Track.IsCover == track.Track.IsCover)
                .OrderBy(item => item.Track.StreamIndex)
                .ThenBy(item => item.TrackKey, StringComparer.Ordinal)
                .ToArray();
            var index = Array.FindIndex(siblings, item => item.TrackKey == track.TrackKey) + 1;

            var label = track.Track.KindLabel;
            if (!track.Track.IsCover && track.Track.Kind is "audio" or "subtitle")
            {
                label = $"{label}{Math.Max(index, 1)}";
            }
            else if (!track.Track.IsCover && track.Track.Kind == "video" && siblings.Length > 1)
            {
                label = $"{label}{Math.Max(index, 1)}";
            }

            track.SetKindDisplay(label);
        }
    }

    private void PersistCurrentModeSelection()
    {
        var selectedKeys = Tracks
            .Where(track => track.IsSelected && track.IsSupported)
            .Select(track => track.TrackKey)
            .ToHashSet(StringComparer.Ordinal);
        _modeSelectedKeys[CurrentMode] = selectedKeys;

        var nextOrder = _modeSelectedOrders[CurrentMode]
            .Where(selectedKeys.Contains)
            .ToList();
        foreach (var track in Tracks)
        {
            if (selectedKeys.Contains(track.TrackKey) && !nextOrder.Contains(track.TrackKey, StringComparer.Ordinal))
            {
                nextOrder.Add(track.TrackKey);
            }
        }

        _modeSelectedOrders[CurrentMode] = nextOrder;
    }

    private void RestoreCurrentModeSelection()
    {
        var selectedKeys = _modeSelectedKeys[CurrentMode];
        _suppressTrackChanges = true;
        foreach (var track in Tracks)
        {
            track.SetSelectedSilently(track.IsSupported && selectedKeys.Contains(track.TrackKey));
        }
        _suppressTrackChanges = false;

        var storedOrder = _modeSelectedOrders[CurrentMode];
        storedOrder.RemoveAll(key => !selectedKeys.Contains(key));
        foreach (var track in Tracks)
        {
            if (track.IsSelected && !storedOrder.Contains(track.TrackKey, StringComparer.Ordinal))
            {
                storedOrder.Add(track.TrackKey);
            }
        }
    }

    private void ApplySelectionConstraints(TrackItemViewModel? changedTrack)
    {
        var supportedTracks = Tracks.Where(track => track.IsSupported).ToArray();
        if (CurrentMode == WorkMode.Batch)
        {
            UpdateTrackSelectability();
            return;
        }

        _suppressTrackChanges = true;
        var selectedVideos = supportedTracks
            .Where(track => track.IsSelected && track.Track.Kind == "video" && !track.Track.IsCover)
            .ToArray();
        var videoKey = ChooseTrackToKeep(
            selectedVideos,
            changedTrack is { IsSelected: true }
                && changedTrack.Track.Kind == "video"
                && !changedTrack.Track.IsCover
                    ? changedTrack.TrackKey
                    : null);
        if (videoKey is not null)
        {
            foreach (var track in selectedVideos)
            {
                track.SetSelectedSilently(track.TrackKey == videoKey);
            }
        }

        var selectedCovers = supportedTracks
            .Where(track => track.IsSelected && track.Track.IsCover)
            .ToArray();
        var coverKey = ChooseTrackToKeep(
            selectedCovers,
            changedTrack is { IsSelected: true } && changedTrack.Track.IsCover
                ? changedTrack.TrackKey
                : null);
        if (coverKey is not null)
        {
            foreach (var track in selectedCovers)
            {
                track.SetSelectedSilently(track.TrackKey == coverKey);
            }
        }

        _suppressTrackChanges = false;
        UpdateTrackSelectability();
    }

    private string? ChooseTrackToKeep(
        IReadOnlyList<TrackItemViewModel> selectedTracks,
        string? changedTrackKey)
    {
        if (changedTrackKey is not null)
        {
            return changedTrackKey;
        }

        foreach (var key in _modeSelectedOrders[CurrentMode])
        {
            if (selectedTracks.Any(track => track.TrackKey == key))
            {
                return key;
            }
        }

        return selectedTracks.FirstOrDefault()?.TrackKey;
    }

    private void UpdateTrackSelectability()
    {
        foreach (var track in Tracks)
        {
            if (!track.IsSupported)
            {
                track.SetSelectable(false, track.Track.SupportNote ?? "当前不支持");
                continue;
            }

            if (CurrentMode == WorkMode.Batch)
            {
                track.SetSelectable(false, "批量模式按左侧文件选择，不在轨道表中分轨。");
                continue;
            }

            track.SetSelectable(true);
        }
    }

    private void SyncSelectedTrackOrder()
    {
        var selectedKeys = Tracks
            .Where(track => track.IsSelected && track.IsSupported)
            .Select(track => track.TrackKey)
            .ToHashSet(StringComparer.Ordinal);
        var nextOrder = _modeSelectedOrders[CurrentMode]
            .Where(selectedKeys.Contains)
            .ToList();
        foreach (var track in Tracks)
        {
            if (selectedKeys.Contains(track.TrackKey) && !nextOrder.Contains(track.TrackKey, StringComparer.Ordinal))
            {
                nextOrder.Add(track.TrackKey);
            }
        }

        _modeSelectedKeys[CurrentMode] = selectedKeys;
        _modeSelectedOrders[CurrentMode] = nextOrder;
    }

    public void SelectAllLike(TrackItemViewModel triggerTrack)
    {
        if (IsRunning || CurrentMode != WorkMode.SingleFile || !triggerTrack.IsSupported)
        {
            return;
        }

        var selectableTracks = Tracks.Where(track => track.IsSupported).ToArray();
        var selectedKeys = new List<string>();
        var normalVideos = selectableTracks
            .Where(track => track.Track.Kind == "video" && !track.Track.IsCover)
            .ToArray();
        var nonVideos = selectableTracks
            .Where(track => track.Track.Kind != "video" || track.Track.IsCover)
            .ToArray();
        if (triggerTrack.Track.Kind == "video" && !triggerTrack.Track.IsCover)
        {
            selectedKeys.Add(triggerTrack.TrackKey);
            selectedKeys.AddRange(nonVideos.Select(track => track.TrackKey));
        }
        else
        {
            if (normalVideos.Length > 0)
            {
                selectedKeys.Add(normalVideos[0].TrackKey);
            }

            selectedKeys.AddRange(nonVideos.Select(track => track.TrackKey));
        }

        var selectedSet = selectedKeys.ToHashSet(StringComparer.Ordinal);
        _suppressTrackChanges = true;
        foreach (var track in selectableTracks)
        {
            track.SetSelectedSilently(selectedSet.Contains(track.TrackKey));
        }
        _suppressTrackChanges = false;

        _modeSelectedOrders[CurrentMode] = selectedKeys;
        ApplySelectionConstraints(null);
        SyncSelectedTrackOrder();
        RefreshState();
    }

    public void SelectAllBatch()
    {
        if (!CanSelectAllBatch || _batchMediaKind is null)
        {
            return;
        }

        var shouldClear = AreAllBatchFilesSelected();
        _suppressMediaChanges = true;
        foreach (var item in MediaItems)
        {
            if (item.BatchKind == _batchMediaKind)
            {
                item.SetSelectedSilently(!shouldClear);
            }
        }
        _suppressMediaChanges = false;

        if (shouldClear)
        {
            _batchMediaKind = null;
            _outputPathDirty = false;
            SetOutputPath(string.Empty);
        }

        UpdateBatchMediaSelectability();
        RefreshState();
    }

    private bool AreAllBatchFilesSelected()
    {
        if (_batchMediaKind is null)
        {
            return false;
        }

        var matchingItems = MediaItems
            .Where(item => item.BatchKind == _batchMediaKind)
            .ToArray();
        return matchingItems.Length > 0 && matchingItems.All(item => item.IsSelected);
    }

    private IReadOnlyList<MediaItemViewModel> SelectedBatchMediaItems() =>
        MediaItems
            .Where(item => item.IsSelected && item.BatchKind == _batchMediaKind)
            .ToArray();

    private void RefreshBatchMediaKind()
    {
        _batchMediaKind = MediaItems
            .Where(item => item.IsSelected)
            .Select(item => item.BatchKind)
            .FirstOrDefault(kind => kind is not null);
    }

    private void UpdateBatchMediaSelectability()
    {
        foreach (var item in MediaItems)
        {
            if (item.BatchKind is null)
            {
                item.SetSelectable(false, "批量模式只支持包含视频或音频的文件。");
                continue;
            }

            var isSameKind = _batchMediaKind is null || item.BatchKind == _batchMediaKind;
            var reason = isSameKind
                ? string.Empty
                : _batchMediaKind == BatchMediaKind.Video
                    ? "当前批次已经选择了视频，只能继续选择视频文件。"
                    : "当前批次已经选择了音频，只能继续选择音频文件。";
            item.SetSelectable(isSameKind, reason);
        }
    }

    public void MoveSelectedTrack(int delta)
    {
        if (!CanMoveSelectedTrack || SelectedOrderItem is null)
        {
            return;
        }

        var order = _modeSelectedOrders[CurrentMode];
        var currentIndex = order.FindIndex(key => key == SelectedOrderItem.TrackKey);
        var targetIndex = currentIndex + delta;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= order.Count)
        {
            return;
        }

        (order[currentIndex], order[targetIndex]) = (order[targetIndex], order[currentIndex]);
        var selectedKey = SelectedOrderItem.TrackKey;
        RefreshSelectedTrackItems();
        SelectedOrderItem = SelectedTrackItems.FirstOrDefault(item => item.TrackKey == selectedKey);
        RefreshSummaryAndCommands();
    }

    private IReadOnlyList<TrackItemViewModel> OrderedSelectedTrackItems()
    {
        var trackMap = Tracks.ToDictionary(track => track.TrackKey, StringComparer.Ordinal);
        return _modeSelectedOrders[CurrentMode]
            .Select(key => trackMap.GetValueOrDefault(key))
            .Where(track => track is { IsSelected: true, IsSupported: true })
            .Cast<TrackItemViewModel>()
            .ToArray();
    }

    private IReadOnlyList<TrackInfo> OrderedSelectedTracks() =>
        OrderedSelectedTrackItems().Select(item => item.Track).ToArray();

    private void RefreshState()
    {
        RefreshBatchMediaKind();
        UpdateBatchMediaSelectability();
        UpdateTrackSelectability();
        RefreshOutputOptions();
        RefreshSelectedTrackItems();
        if (!_outputPathDirty)
        {
            SyncDefaultOutputPath(force: true);
        }

        RefreshSummaryAndCommands();
    }

    private void RefreshOutputOptions()
    {
        var previousOutputContainer = _selectedOutputOption?.Container;
        var selectedTracks = OrderedSelectedTracks();
        var targetByTrackKey = SelectedTargetMap();
        var operation = CurrentMode == WorkMode.SingleFile
            ? TrackTargetPlanner.Classify(selectedTracks, targetByTrackKey)
            : SingleFileOperation.None;
        _refreshing = true;
        OutputOptions.Clear();

        if (CurrentMode == WorkMode.SingleFile)
        {
            if (operation == SingleFileOperation.Mux)
            {
                var audioCount = selectedTracks.Count(track => track.Kind == "audio" && !track.IsCover);
                var mp4Label = audioCount > 1
                    ? "MP4（H.264 + AAC 混音）"
                    : "MP4（H.264 + AAC）";
                OutputOptions.Add(OutputOptionViewModel.Mux("mp4", mp4Label));
                OutputOptions.Add(OutputOptionViewModel.Mux("mkv", "MKV"));
            }
            else if (operation == SingleFileOperation.AudioMix)
            {
                OutputOptions.Add(OutputOptionViewModel.Mux("m4a", "M4A（AAC 混音，默认）"));
                OutputOptions.Add(OutputOptionViewModel.Mux("mp3", "MP3（混音）"));
                OutputOptions.Add(OutputOptionViewModel.Mux("aac", "AAC（混音）"));
                OutputOptions.Add(OutputOptionViewModel.Mux("wav", "WAV（PCM 混音）"));
                OutputOptions.Add(OutputOptionViewModel.Mux("flac", "FLAC（无损混音）"));
                OutputOptions.Add(OutputOptionViewModel.Mux("opus", "Opus（混音）"));
            }
            else if ((operation is SingleFileOperation.Extract or SingleFileOperation.Convert)
                     && selectedTracks.Count == 1
                     && targetByTrackKey.TryGetValue(selectedTracks[0].TrackKey, out var target))
            {
                var outputTarget = target with
                {
                    Label = $"{target.Extension.ToUpperInvariant()}（{TrackTargetPlanner.OperationLabel(operation)}） (*.{target.Extension})",
                };
                OutputOptions.Add(OutputOptionViewModel.ForTarget(outputTarget));
            }
        }
        else if (_batchMediaKind is not null)
        {
            var container = BatchPlanner.OutputContainer(_batchMediaKind.Value);
            OutputOptions.Add(OutputOptionViewModel.Mux(
                container,
                BatchPlanner.OutputLabel(_batchMediaKind.Value)));
        }

        var selectedOption = operation is SingleFileOperation.Mux or SingleFileOperation.AudioMix
            ? OutputOptions.FirstOrDefault(option => option.Container == _muxContainer)
            : null;
        selectedOption ??= OutputOptions.FirstOrDefault();
        _selectedOutputOption = selectedOption;
        OnPropertyChanged(nameof(SelectedOutputOption));
        if (selectedOption?.Container is not null)
        {
            _muxContainer = selectedOption.Container;
        }

        if (CurrentMode == WorkMode.SingleFile
            && string.Equals(selectedOption?.Container, "mp4", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(previousOutputContainer, "mp4", StringComparison.OrdinalIgnoreCase))
        {
            ApplyMp4TargetDefaults();
        }

        _refreshing = false;
    }

    private bool IsMp4OutputSelected() =>
        string.Equals(_selectedOutputOption?.Container, "mp4", StringComparison.OrdinalIgnoreCase);

    private void ApplyMp4TargetDefaults()
    {
        foreach (var track in OrderedSelectedTrackItems())
        {
            ApplyMp4TargetDefault(track);
        }
    }

    private static void ApplyMp4TargetDefault(TrackItemViewModel track)
    {
        var targetId = track.Track.Kind switch
        {
            "video" when !track.Track.IsCover => "video-mp4-h264",
            "audio" when !track.Track.IsCover => "audio-m4a",
            "subtitle" when MuxPlanner.IsMp4TextSubtitle(track.Track.Codec) => "sub-mp4-mov-text",
            _ => null,
        };
        if (targetId is null)
        {
            return;
        }

        var target = track.TargetOptions.FirstOrDefault(item => item.Id == targetId);
        if (target is not null)
        {
            track.SetTargetSilently(target);
        }
    }

    private IReadOnlyDictionary<string, OutputTarget> SelectedTargetMap() =>
        OrderedSelectedTrackItems()
            .Where(item => item.SelectedTarget is not null)
            .ToDictionary(item => item.TrackKey, item => item.SelectedTarget!, StringComparer.Ordinal);

    private void RefreshSelectedTrackItems()
    {
        var selectedKey = SelectedOrderItem?.TrackKey;
        SelectedTrackItems.Clear();
        var index = 1;
        foreach (var track in OrderedSelectedTrackItems())
        {
            SelectedTrackItems.Add(new SelectedTrackItemViewModel(
                track.TrackKey,
                $"{index}. {track.DisplayText}",
                $"{track.SourceFileName}\n{track.DisplayText}"));
            index++;
        }

        SelectedOrderItem = selectedKey is null
            ? null
            : SelectedTrackItems.FirstOrDefault(item => item.TrackKey == selectedKey);
    }

    private void RefreshSummaryAndCommands()
    {
        var selectedTracks = OrderedSelectedTracks();
        if (!HasMedia)
        {
            SummaryText = "还没有导入媒体文件。";
            ValidationText = "当前还没有可校验内容。";
            ValidationDetails = string.Empty;
            CommandPreview = string.Empty;
            RaiseActionState();
            return;
        }

        if (CurrentMode == WorkMode.Batch)
        {
            var selectedFiles = SelectedBatchMediaItems();
            if (_batchMediaKind is null || selectedFiles.Count == 0)
            {
                SummaryText = $"共导入 {MediaItems.Count} 个文件。\n请先在左侧选择 1 个视频或音频文件。";
            }
            else
            {
                var kindLabel = _batchMediaKind == BatchMediaKind.Video ? "视频" : "音频";
                var outputLabel = BatchPlanner.OutputLabel(_batchMediaKind.Value);
                SummaryText = $"共导入 {MediaItems.Count} 个文件，当前选择 {selectedFiles.Count} 个{kindLabel}文件。\n"
                    + $"批量输出：每个文件单独生成 1 个 {outputLabel} 文件。\n"
                    + (_batchMediaKind == BatchMediaKind.Video
                        ? "已符合 H.264/AAC/MOV_TEXT 的流会直接复制；否则只转换不符合的流，并保留 1 条默认文本软字幕。"
                        : "AAC 音频会直接复制；其他音频只转换为 AAC。");
            }

            var batchIssues = CollectIssues();
            ValidationText = batchIssues.Count == 0 ? "当前没有阻断错误。" : batchIssues[0];
            ValidationDetails = string.Join(Environment.NewLine, batchIssues);
            RefreshCommandPreview();
            RaiseActionState();
            return;
        }

        var targetByTrackKey = SelectedTargetMap();
        var operation = TrackTargetPlanner.Classify(selectedTracks, targetByTrackKey);
        var summary = $"共导入 {MediaItems.Count} 个文件，当前勾选 {selectedTracks.Count} 条轨道。\n"
            + $"视频 {selectedTracks.Count(track => track.Kind == "video")} / "
            + $"音频 {selectedTracks.Count(track => track.Kind == "audio")} / "
            + $"字幕 {selectedTracks.Count(track => track.Kind == "subtitle")}";
        if (selectedTracks.Count > 0)
        {
            summary += $"\n当前操作：{TrackTargetPlanner.OperationLabel(operation)}";
        }

        if (operation == SingleFileOperation.AudioMix)
        {
            summary += "\n多条音频将混音为 1 条音频流。\n右侧输出格式决定最终混音编码。";
        }
        else if (operation == SingleFileOperation.Mux
                 && string.Equals(_muxContainer, "mp4", StringComparison.OrdinalIgnoreCase)
                 && selectedTracks.Any(track => track.Kind == "video" && !track.IsCover)
                 && selectedTracks.Any(track => track.Kind == "audio" && !track.IsCover))
        {
            var audioCount = selectedTracks.Count(track => track.Kind == "audio" && !track.IsCover);
            summary += audioCount > 1
                ? "\nMP4 输出：H.264 + AAC 混音（多条音频合成为 1 条音频流）。"
                : "\nMP4 输出：H.264 视频 + AAC 音频。";
        }

        SummaryText = summary;

        var issues = CollectIssues();
        ValidationText = issues.Count == 0 ? "当前没有阻断错误。" : issues[0];
        ValidationDetails = string.Join(Environment.NewLine, issues);
        RefreshCommandPreview();
        RaiseActionState();
    }

    private IReadOnlyList<string> CollectIssues()
    {
        var selectedTracks = OrderedSelectedTracks();
        if (CurrentMode == WorkMode.Batch)
        {
            var selectedFiles = SelectedBatchMediaItems();
            if (_batchMediaKind is null || selectedFiles.Count == 0)
            {
                return ["批量模式下请先在左侧选择 1 个视频或音频文件。"];
            }

            var batchIssues = new List<string>();
            foreach (var item in selectedFiles)
            {
                if (BatchPlanner.SelectOutputTracks(item.Media, _batchMediaKind.Value).Count == 0)
                {
                    batchIssues.Add($"{item.FileName} 没有可用于批量处理的主轨道。");
                }
            }

            if (!string.IsNullOrWhiteSpace(OutputPath))
            {
                try
                {
                    _ = Path.GetFullPath(OutputPath);
                    if (File.Exists(OutputPath))
                    {
                        batchIssues.Add("批量输出路径必须是文件夹，不能是文件。");
                    }
                }
                catch (Exception error) when (error is ArgumentException
                                                or NotSupportedException
                                                or PathTooLongException)
                {
                    batchIssues.Add("批量输出文件夹路径无效。");
                }
            }

            return batchIssues;
        }

        if (selectedTracks.Count == 0)
        {
            return ["单文件模式下至少要勾选 1 条轨道。"];
        }

        var targetByTrackKey = SelectedTargetMap();
        var operation = TrackTargetPlanner.Classify(selectedTracks, targetByTrackKey);
        var issues = selectedTracks
            .Where(track => !targetByTrackKey.ContainsKey(track.TrackKey))
            .Select(track => $"{track.SourceFileName} / 轨道 {track.StreamIndex} 没有可用的目标编码。")
            .ToList();
        if (operation is SingleFileOperation.Mux or SingleFileOperation.AudioMix)
        {
            issues.AddRange(MuxPlanner.Validate(selectedTracks, _muxContainer, targetByTrackKey));
        }

        if (!string.IsNullOrWhiteSpace(OutputPath)
            && !MuxPlanner.IsOutputPathDistinct(MediaItems.Select(item => item.Media).ToArray(), OutputPath))
        {
            issues.Add("输出文件不能与任一输入文件相同，请修改输出文件名。");
        }

        return issues;
    }

    private void SyncDefaultOutputPath(bool force)
    {
        string? defaultPath = null;
        var selectedTracks = OrderedSelectedTracks();
        var targetByTrackKey = SelectedTargetMap();
        var operation = TrackTargetPlanner.Classify(selectedTracks, targetByTrackKey);
        if (HasMedia
            && CurrentMode == WorkMode.SingleFile
            && (operation is SingleFileOperation.Mux or SingleFileOperation.AudioMix))
        {
            defaultPath = MuxPlanner.BuildDefaultOutputPath(
                MediaItems.Select(item => item.Media).ToArray(),
                _muxContainer,
                selectedTracks);
        }
        else if (HasMedia
                 && CurrentMode == WorkMode.SingleFile
                 && selectedTracks.Count == 1
                 && targetByTrackKey.TryGetValue(selectedTracks[0].TrackKey, out var target))
        {
            defaultPath = ExtractPlanner.BuildOutputPath(selectedTracks[0], target);
        }
        else if (CurrentMode == WorkMode.Batch && HasBatchSelection)
        {
            defaultPath = Path.GetDirectoryName(SelectedBatchMediaItems()[0].InputPath);
        }

        if (force || string.IsNullOrWhiteSpace(OutputPath))
        {
            SetOutputPath(defaultPath ?? string.Empty);
        }
    }

    private void RefreshCommandPreview()
    {
        if (!HasMedia)
        {
            CommandPreview = string.Empty;
            return;
        }

        if (CurrentMode == WorkMode.Batch)
        {
            if (!HasBatchSelection)
            {
                CommandPreview = "请先在左侧选择一个视频或音频文件。";
                return;
            }

            if (string.IsNullOrWhiteSpace(OutputPath))
            {
                CommandPreview = "请先选择输出文件夹。";
                return;
            }

            try
            {
                var jobs = BuildBatchJobSpecs();
                if (jobs.Count == 0)
                {
                    CommandPreview = "当前选择没有可执行的批量任务。";
                    return;
                }

                var first = jobs[0];
                var invocation = new ProcessInvocation(
                    _toolLocator.FindFfmpeg() ?? "ffmpeg",
                    BatchPlanner.BuildArguments(
                        first.Media,
                        _batchMediaKind!.Value,
                        first.OutputPath));
                var prefix = jobs.Count > 1 ? $"共 {jobs.Count} 个独立任务，以下为第 1 条：\n" : string.Empty;
                CommandPreview = prefix + CommandLineFormatter.Format(invocation.Program, invocation.Arguments);
            }
            catch (Exception error) when (error is ArgumentException
                                          or NotSupportedException
                                          or PathTooLongException)
            {
                CommandPreview = $"批量输出文件夹路径无效：{error.Message}";
            }
            return;
        }

        var selectedTracks = OrderedSelectedTracks();
        if (selectedTracks.Count == 0)
        {
            CommandPreview = "请先选择轨道。";
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            CommandPreview = "请先选择输出路径。";
            return;
        }

        var targetByTrackKey = SelectedTargetMap();
        var operation = TrackTargetPlanner.Classify(selectedTracks, targetByTrackKey);
        if (operation is SingleFileOperation.Mux or SingleFileOperation.AudioMix)
        {
            var invocation = new ProcessInvocation(
                _toolLocator.FindFfmpeg() ?? "ffmpeg",
                MuxPlanner.BuildArguments(
                    MediaItems.Select(item => item.Media).ToArray(),
                    selectedTracks,
                    _muxContainer,
                    OutputPath,
                    targetByTrackKey));
            CommandPreview = CommandLineFormatter.Format(invocation.Program, invocation.Arguments);
            return;
        }

        if (selectedTracks.Count == 1
            && targetByTrackKey.TryGetValue(selectedTracks[0].TrackKey, out var singleTarget))
        {
            var invocation = BuildPreviewExtractInvocation(selectedTracks[0], singleTarget, OutputPath);
            CommandPreview = CommandLineFormatter.Format(invocation.Program, invocation.Arguments);
            return;
        }

        CommandPreview = "当前选中的轨道没有可用的目标编码。";
    }

    private ProcessInvocation BuildPreviewExtractInvocation(
        TrackInfo track,
        OutputTarget target,
        string outputPath)
    {
        var mkvExtract = _toolLocator.FindMkvExtract();
        return mkvExtract is not null && ExtractPlanner.ShouldUseMkvExtract(track, target)
            ? new ProcessInvocation(mkvExtract, ExtractPlanner.BuildMkvExtractArguments(track, outputPath))
            : new ProcessInvocation(
                _toolLocator.FindFfmpeg() ?? "ffmpeg",
                ExtractPlanner.BuildFfmpegArguments(track, target, outputPath));
    }

    private static string ResolveOutputDirectory(string outputPath)
    {
        if (!Path.HasExtension(outputPath))
        {
            return outputPath;
        }

        return Path.GetDirectoryName(outputPath) ?? outputPath;
    }
}
