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
        OnPropertiesChanged(nameof(IsMuxMode), nameof(IsExtractMode), nameof(IsConvertMode));
        RestoreCurrentModeSelection();
        ApplySelectionConstraints(null);
        SyncSelectedTrackOrder();
        StatusText = "已切换模式";
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
        RefreshState();
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
        if (CurrentMode == WorkMode.Extract)
        {
            UpdateTrackSelectability();
            return;
        }

        _suppressTrackChanges = true;
        if (CurrentMode == WorkMode.Convert)
        {
            var selectedTracks = supportedTracks.Where(track => track.IsSelected).ToArray();
            string? keepGroup = null;
            if (changedTrack is { IsSelected: true })
            {
                keepGroup = changedTrack.Track.ConvertGroup;
            }
            else if (selectedTracks.Length > 0)
            {
                foreach (var key in _modeSelectedOrders[CurrentMode])
                {
                    var match = selectedTracks.FirstOrDefault(track => track.TrackKey == key);
                    if (match is not null)
                    {
                        keepGroup = match.Track.ConvertGroup;
                        break;
                    }
                }

                keepGroup ??= selectedTracks[0].Track.ConvertGroup;
            }

            if (keepGroup is not null)
            {
                foreach (var track in selectedTracks.Where(track => track.Track.ConvertGroup != keepGroup))
                {
                    track.SetSelectedSilently(false);
                }
            }

            _suppressTrackChanges = false;
            UpdateTrackSelectability();
            return;
        }

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
        var convertGroup = CurrentMode == WorkMode.Convert
            ? Tracks.FirstOrDefault(track => track.IsSelected)?.Track.ConvertGroup
            : null;

        foreach (var track in Tracks)
        {
            if (!track.IsSupported)
            {
                track.SetSelectable(false, track.Track.SupportNote ?? "当前不支持");
                continue;
            }

            if (CurrentMode == WorkMode.Convert
                && convertGroup is not null
                && !track.IsSelected
                && track.Track.ConvertGroup != convertGroup)
            {
                track.SetSelectable(false, "转换模式下只能同时选择同类型轨道。");
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
        if (CurrentMode == WorkMode.Mux)
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
        else if (CurrentMode == WorkMode.Extract)
        {
            selectedKeys.AddRange(selectableTracks.Select(track => track.TrackKey));
        }
        else
        {
            selectedKeys.AddRange(selectableTracks
                .Where(track => track.Track.ConvertGroup == triggerTrack.Track.ConvertGroup)
                .Select(track => track.TrackKey));
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
        var selectedTracks = OrderedSelectedTracks();
        _refreshing = true;
        OutputOptions.Clear();

        if (CurrentMode == WorkMode.Mux)
        {
            OutputOptions.Add(OutputOptionViewModel.Mux("mkv", "MKV"));
            OutputOptions.Add(OutputOptionViewModel.Mux("mp4", "MP4"));
        }
        else if (CurrentMode == WorkMode.Extract && selectedTracks.Count > 1)
        {
            OutputOptions.Add(OutputOptionViewModel.BatchExtract());
        }
        else
        {
            IReadOnlyList<OutputTarget> targets = CurrentMode == WorkMode.Extract
                ? ExtractPlanner.ListExtractTargets(selectedTracks.Count == 1 ? selectedTracks[0] : null)
                : ExtractPlanner.CommonConvertTargets(selectedTracks);
            foreach (var target in targets)
            {
                OutputOptions.Add(OutputOptionViewModel.ForTarget(target));
            }
        }

        var selectedOption = CurrentMode == WorkMode.Mux
            ? OutputOptions.FirstOrDefault(option => option.Container == _muxContainer)
            : OutputOptions.FirstOrDefault(option => option.Key == _currentTargetId);
        selectedOption ??= OutputOptions.FirstOrDefault();
        _selectedOutputOption = selectedOption;
        OnPropertyChanged(nameof(SelectedOutputOption));
        if (selectedOption?.Target is not null)
        {
            _currentTargetId = selectedOption.Target.Id;
        }

        _refreshing = false;
    }

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

        SummaryText = $"共导入 {MediaItems.Count} 个文件，当前勾选 {selectedTracks.Count} 条轨道。\n"
            + $"视频 {selectedTracks.Count(track => track.Kind == "video")} / "
            + $"音频 {selectedTracks.Count(track => track.Kind == "audio")} / "
            + $"字幕 {selectedTracks.Count(track => track.Kind == "subtitle")}";

        var issues = CollectIssues();
        ValidationText = issues.Count == 0 ? "当前没有阻断错误。" : issues[0];
        ValidationDetails = string.Join(Environment.NewLine, issues);
        RefreshCommandPreview();
        RaiseActionState();
    }

    private IReadOnlyList<string> CollectIssues()
    {
        var selectedTracks = OrderedSelectedTracks();
        if (CurrentMode == WorkMode.Mux)
        {
            var muxIssues = MuxPlanner.Validate(selectedTracks, _muxContainer).ToList();
            if (!string.IsNullOrWhiteSpace(OutputPath)
                && !MuxPlanner.IsOutputPathDistinct(MediaItems.Select(item => item.Media).ToArray(), OutputPath))
            {
                muxIssues.Add("输出文件不能与任一输入文件相同，请修改输出文件名。");
            }

            return muxIssues;
        }

        var issues = ExtractPlanner.Validate(selectedTracks, CurrentMode).ToList();
        if (CurrentMode == WorkMode.Convert
            && issues.Count == 0
            && selectedTracks.Any(track => track.ConvertGroup != selectedTracks[0].ConvertGroup))
        {
            issues.Add("批量转换时只能同时选择同类型轨道。");
        }

        if (CurrentMode == WorkMode.Extract && selectedTracks.Count > 1)
        {
            return issues;
        }

        if (selectedTracks.Count == 1
            && !string.IsNullOrWhiteSpace(OutputPath)
            && !MuxPlanner.IsOutputPathDistinct(MediaItems.Select(item => item.Media).ToArray(), OutputPath))
        {
            issues.Add("输出文件不能与任一输入文件相同，请修改输出文件名。");
        }

        if (issues.Count == 0 && SelectedOutputOption?.Target is null)
        {
            issues.Add(CurrentMode == WorkMode.Extract
                ? "当前轨道没有可用的提取输出格式。"
                : "当前轨道没有可用的转换输出格式。");
        }

        return issues;
    }

    private void SyncDefaultOutputPath(bool force)
    {
        string? defaultPath = null;
        var selectedTracks = OrderedSelectedTracks();
        if (HasMedia && CurrentMode == WorkMode.Mux)
        {
            defaultPath = MuxPlanner.BuildDefaultOutputPath(
                MediaItems.Select(item => item.Media).ToArray(),
                _muxContainer,
                selectedTracks);
        }
        else if (HasMedia
                 && CurrentMode is WorkMode.Extract or WorkMode.Convert
                 && selectedTracks.Count > 1)
        {
            defaultPath = Path.GetDirectoryName(selectedTracks[0].SourcePath);
        }
        else if (selectedTracks.Count == 1 && SelectedOutputOption?.Target is { } target)
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

        var selectedTracks = OrderedSelectedTracks();
        if (CurrentMode == WorkMode.Mux)
        {
            if (string.IsNullOrWhiteSpace(OutputPath))
            {
                CommandPreview = "请先选择输出文件夹并填写文件名。";
                return;
            }

            var invocation = new ProcessInvocation(
                _toolLocator.FindFfmpeg() ?? "ffmpeg",
                MuxPlanner.BuildArguments(
                    MediaItems.Select(item => item.Media).ToArray(),
                    selectedTracks,
                    _muxContainer,
                    OutputPath));
            CommandPreview = CommandLineFormatter.Format(invocation.Program, invocation.Arguments);
            return;
        }

        if (selectedTracks.Count == 1 && SelectedOutputOption?.Target is { } singleTarget)
        {
            if (string.IsNullOrWhiteSpace(OutputPath))
            {
                CommandPreview = "请先选择输出文件夹并填写文件名。";
                return;
            }

            var invocation = BuildPreviewExtractInvocation(selectedTracks[0], singleTarget, OutputPath);
            CommandPreview = CommandLineFormatter.Format(invocation.Program, invocation.Arguments);
            return;
        }

        if (CurrentMode == WorkMode.Extract && selectedTracks.Count > 1)
        {
            if (string.IsNullOrWhiteSpace(OutputPath))
            {
                CommandPreview = "请先选择输出文件夹。";
                return;
            }

            var directory = ResolveOutputDirectory(OutputPath);
            var commands = new List<string>();
            foreach (var track in selectedTracks.Take(3))
            {
                var target = ExtractPlanner.ListExtractTargets(track).FirstOrDefault();
                if (target is null)
                {
                    continue;
                }

                var output = ExtractPlanner.BuildOutputPathInDirectory(track, target, directory);
                var invocation = BuildPreviewExtractInvocation(track, target, output);
                commands.Add(CommandLineFormatter.Format(invocation.Program, invocation.Arguments));
            }

            if (selectedTracks.Count > 3)
            {
                commands.Add($"... 共 {selectedTracks.Count} 条批量提取命令");
            }

            CommandPreview = commands.Count == 0
                ? "当前选中的轨道没有可用的提取输出格式。"
                : string.Join(Environment.NewLine + Environment.NewLine, commands);
            return;
        }

        if (CurrentMode == WorkMode.Convert
            && selectedTracks.Count > 1
            && SelectedOutputOption?.Target is { } batchTarget)
        {
            if (string.IsNullOrWhiteSpace(OutputPath))
            {
                CommandPreview = "请先选择输出文件夹。";
                return;
            }

            var directory = ResolveOutputDirectory(OutputPath);
            var commands = selectedTracks.Take(3).Select(track =>
            {
                var output = ExtractPlanner.BuildOutputPathInDirectory(track, batchTarget, directory);
                var invocation = BuildPreviewExtractInvocation(track, batchTarget, output);
                return CommandLineFormatter.Format(invocation.Program, invocation.Arguments);
            }).ToList();
            if (selectedTracks.Count > 3)
            {
                commands.Add($"... 共 {selectedTracks.Count} 条批量转换命令");
            }

            CommandPreview = string.Join(Environment.NewLine + Environment.NewLine, commands);
            return;
        }

        CommandPreview = "请先选择轨道并指定输出格式。";
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
