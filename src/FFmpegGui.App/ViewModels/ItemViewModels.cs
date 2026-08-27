using System.Collections.ObjectModel;
using FFmpegGui.Core;

namespace FFmpegGui.App.ViewModels;

public sealed class MediaItemViewModel(MediaInfo media, int sourceIndex)
{
    public MediaInfo Media { get; } = media;

    public int SourceIndex { get; } = sourceIndex;

    public string SourceLabel => $"素材{SourceIndex + 1}";

    public string FileName => Media.FileName;

    public string FormatName => Media.FormatName;

    public string InputPath => Media.InputPath;

    public string Details
    {
        get
        {
            var duration = Media.DurationSeconds is > 0
                ? TimeSpan.FromSeconds(Media.DurationSeconds.Value).ToString(@"hh\:mm\:ss")
                : "时长未知";
            return $"{SourceLabel}~{Media.FormatName} · {duration}";
        }
    }
}

public sealed class TrackItemViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _isSelectable;
    private string _selectableReason = string.Empty;
    private string _kindDisplay;
    private OutputTarget? _selectedTarget;

    public TrackItemViewModel(
        TrackInfo track,
        Action<TrackItemViewModel> selectionChanged,
        Action<TrackItemViewModel>? targetChanged = null)
    {
        Track = track;
        _selectionChanged = selectionChanged;
        _targetChanged = targetChanged;
        _isSelectable = track.IsSupported;
        _selectableReason = track.SupportNote ?? string.Empty;
        _kindDisplay = track.KindLabel;
        TargetOptions = new(TrackTargetPlanner.ListTargets(track));
        _selectedTarget = TargetOptions.FirstOrDefault();
    }

    private readonly Action<TrackItemViewModel> _selectionChanged;
    private readonly Action<TrackItemViewModel>? _targetChanged;

    public TrackInfo Track { get; }

    public string TrackKey => Track.TrackKey;

    public string SourceLabel => $"素材{Track.SourceIndex + 1}";

    public int StreamIndex => Track.StreamIndex;

    public string KindDisplay
    {
        get => _kindDisplay;
        private set => SetProperty(ref _kindDisplay, value);
    }

    public string Codec => Track.Codec;

    public ObservableCollection<OutputTarget> TargetOptions { get; }

    public OutputTarget? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (SetProperty(ref _selectedTarget, value))
            {
                OnPropertiesChanged(nameof(TargetCodec), nameof(Tooltip));
                _targetChanged?.Invoke(this);
            }
        }
    }

    public string TargetCodec => SelectedTarget?.Label ?? "-";

    public string Language => Track.Language ?? "-";

    public string Title => Track.Title ?? "-";

    public string Flags => Track.Disposition.Label;

    public bool IsSupported => Track.IsSupported;

    public string SourceFileName => Track.SourceFileName;

    public string Tooltip => IsSelectable
        ? $"{Track.SourceFileName}\n{DisplayText}\n目标编码：{TargetCodec}"
        : SelectableReason;

    public string DisplayText
    {
        get
        {
            var parts = new List<string> { SourceLabel, KindDisplay, Codec };
            if (!string.IsNullOrWhiteSpace(Track.Title))
            {
                parts.Add(Track.Title);
            }

            return string.Join(" / ", parts);
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !IsSelectable)
            {
                return;
            }

            if (SetProperty(ref _isSelected, value))
            {
                _selectionChanged(this);
            }
        }
    }

    public bool IsSelectable
    {
        get => _isSelectable;
        private set
        {
            if (SetProperty(ref _isSelectable, value))
            {
                OnPropertyChanged(nameof(Tooltip));
            }
        }
    }

    public string SelectableReason
    {
        get => _selectableReason;
        private set
        {
            if (SetProperty(ref _selectableReason, value))
            {
                OnPropertyChanged(nameof(Tooltip));
            }
        }
    }

    public void SetSelectedSilently(bool selected)
    {
        if (_isSelected == selected)
        {
            return;
        }

        _isSelected = selected;
        OnPropertyChanged(nameof(IsSelected));
    }

    public void SetSelectable(bool selectable, string reason = "")
    {
        IsSelectable = selectable;
        SelectableReason = reason;
        if (!selectable && IsSelected)
        {
            SetSelectedSilently(false);
        }
    }

    public void SetKindDisplay(string value)
    {
        KindDisplay = value;
        OnPropertiesChanged(nameof(DisplayText), nameof(Tooltip));
    }
}

public sealed record SelectedTrackItemViewModel(string TrackKey, string DisplayText, string Tooltip);

public sealed record OutputOptionViewModel(string Key, string Label, string? Container, OutputTarget? Target)
{
    public static OutputOptionViewModel Mux(string container, string label) =>
        new(container, label, container, null);

    public static OutputOptionViewModel ForTarget(OutputTarget target) =>
        new(target.Id, target.Label, null, target);

    public static OutputOptionViewModel BatchExtract() =>
        new("batch-extract-default", "按轨道默认格式批量提取", null, null);
}
