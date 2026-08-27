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
        StatusText = mode == WorkMode.Batch ? "批量模式暂未开放" : "已切换为单文件模式";
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
                track.SetSelectable(false, "批量模式暂未开放。");
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
        if (IsRunning || !triggerTrack.IsSupported)
        {
            return;
        }

        var selectableTracks = Tracks.Where(track => track.IsSupported).ToArray();
        var selectedKeys = new List<string>();
        if (CurrentMode == WorkMode.SingleFile)
        {
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
        }
        else
        {
            selectedKeys.AddRange(selectableTracks.Select(track => track.TrackKey));
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
                OutputOptions.Add(OutputOptionViewModel.Mux("mp4", "MP4（H.264 + AAC）"));
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

        if (string.Equals(selectedOption?.Container, "mp4", StringComparison.OrdinalIgnoreCase)
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
            SummaryText = "批量模式暂未开放。";
            ValidationText = "批量模式暂未开放。";
            ValidationDetails = string.Empty;
            CommandPreview = string.Empty;
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
            summary += "\nMP4 输出：H.264 视频 + AAC 音频；多条音频将混音为 1 条音频流。";
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
            return ["批量模式暂未开放。"];
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
            CommandPreview = "批量模式暂未开放。";
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
