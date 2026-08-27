using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Threading;
using FFmpegGui.Core;
using FFmpegGui.Infrastructure;

namespace FFmpegGui.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly ToolLocator _toolLocator;
    private readonly MediaInspector _mediaInspector;
    private readonly InvocationFactory _invocationFactory;
    private readonly ProcessRunner _processRunner;
    private readonly AppSettingsStore _settingsStore;
    private readonly Dispatcher _dispatcher;
    private readonly StringBuilder _logBuilder = new();
    private readonly Dictionary<WorkMode, HashSet<string>> _modeSelectedKeys = new()
    {
        [WorkMode.SingleFile] = [],
        [WorkMode.Batch] = [],
    };
    private readonly Dictionary<WorkMode, List<string>> _modeSelectedOrders = new()
    {
        [WorkMode.SingleFile] = [],
        [WorkMode.Batch] = [],
    };

    private WorkMode _currentMode = WorkMode.SingleFile;
    private string _muxContainer = "mp4";
    private OutputOptionViewModel? _selectedOutputOption;
    private SelectedTrackItemViewModel? _selectedOrderItem;
    private string _summaryText = "还没有导入媒体文件。";
    private string _validationText = "当前还没有可校验内容。";
    private string _validationDetails = string.Empty;
    private string _outputPath = string.Empty;
    private string _commandPreview = string.Empty;
    private string _logText = string.Empty;
    private string _statusText = "空闲";
    private int _progressValue;
    private bool _isProgressIndeterminate;
    private bool _isRunning;
    private bool _isLoadingMedia;
    private bool _outputPathDirty;
    private bool _settingOutputPath;
    private bool _refreshing;
    private bool _suppressTrackChanges;
    private bool _suppressMediaChanges;
    private BatchMediaKind? _batchMediaKind;
    private CancellationTokenSource? _taskCancellation;

    public MainWindowViewModel(
        Dispatcher dispatcher,
        AppSettingsStore? settingsStore = null)
    {
        _dispatcher = dispatcher;
        _settingsStore = settingsStore ?? new AppSettingsStore();
        _currentMode = _settingsStore.LoadWorkMode();
        _toolLocator = new ToolLocator();
        _mediaInspector = new MediaInspector(_toolLocator);
        _invocationFactory = new InvocationFactory(_toolLocator);
        _processRunner = new ProcessRunner();
        ToolStatus = _toolLocator.DescribeAvailability();
        RefreshState();
    }

    public ObservableCollection<MediaItemViewModel> MediaItems { get; } = [];

    public ObservableCollection<TrackItemViewModel> Tracks { get; } = [];

    public ObservableCollection<SelectedTrackItemViewModel> SelectedTrackItems { get; } = [];

    public ObservableCollection<OutputOptionViewModel> OutputOptions { get; } = [];

    public string ToolStatus { get; }

    public bool IsSingleFileMode
    {
        get => CurrentMode == WorkMode.SingleFile;
        set
        {
            if (value)
            {
                SetMode(WorkMode.SingleFile);
            }
        }
    }

    public bool IsBatchMode
    {
        get => CurrentMode == WorkMode.Batch;
        set
        {
            if (value)
            {
                SetMode(WorkMode.Batch);
            }
        }
    }

    public OutputOptionViewModel? SelectedOutputOption
    {
        get => _selectedOutputOption;
        set
        {
            if (!SetProperty(ref _selectedOutputOption, value) || _refreshing || value is null)
            {
                return;
            }

            if (value.Container is not null)
            {
                _muxContainer = value.Container;
                if (CurrentMode == WorkMode.SingleFile
                    && string.Equals(_muxContainer, "mp4", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyMp4TargetDefaults();
                }
            }

            if (!_outputPathDirty)
            {
                SyncDefaultOutputPath(force: true);
            }

            RefreshSummaryAndCommands();
        }
    }

    public SelectedTrackItemViewModel? SelectedOrderItem
    {
        get => _selectedOrderItem;
        set
        {
            if (SetProperty(ref _selectedOrderItem, value))
            {
                RaiseActionState();
            }
        }
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public string ValidationText
    {
        get => _validationText;
        private set => SetProperty(ref _validationText, value);
    }

    public string ValidationDetails
    {
        get => _validationDetails;
        private set => SetProperty(ref _validationDetails, value);
    }

    public string OutputPath
    {
        get => _outputPath;
        set
        {
            if (!SetProperty(ref _outputPath, value))
            {
                return;
            }

            if (!_settingOutputPath)
            {
                _outputPathDirty = true;
            }

            RefreshCommandPreview();
            RaiseActionState();
        }
    }

    public string CommandPreview
    {
        get => _commandPreview;
        private set => SetProperty(ref _commandPreview, value);
    }

    public string LogText
    {
        get => _logText;
        private set => SetProperty(ref _logText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public int ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetProperty(ref _isProgressIndeterminate, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                RaiseActionState();
            }
        }
    }

    public bool HasMedia => MediaItems.Count > 0;

    public bool IsBatchOutput => CurrentMode == WorkMode.Batch;

    public bool HasBatchSelection =>
        CurrentMode == WorkMode.Batch && SelectedBatchMediaItems().Count > 0;

    public bool CanSelectAllBatch => HasBatchSelection && !IsRunning;

    public bool CanChooseOutput => HasMedia && (!IsBatchOutput || HasBatchSelection);

    public bool CanRun => HasMedia
        && !IsRunning
        && CollectIssues().Count == 0
        && !string.IsNullOrWhiteSpace(OutputPath);

    public bool CanCancel => IsRunning;

    public bool CanModifyInputs => !IsRunning && !_isLoadingMedia;

    public bool CanMoveSelectedTrack =>
        !IsRunning && CurrentMode == WorkMode.SingleFile && SelectedOrderItem is not null;

    public string RunButtonText => CurrentMode == WorkMode.Batch ? "开始批量" : "开始处理";

    public string OutputBrowseLabel => IsBatchOutput ? "选择文件夹" : "修改";

    public string ImportButtonText => IsBatchOutput ? "导入文件" : "导入主文件";

    public string SelectAllBatchLabel => _batchMediaKind switch
    {
        BatchMediaKind.Video when AreAllBatchFilesSelected() => "取消全选全部视频",
        BatchMediaKind.Audio when AreAllBatchFilesSelected() => "取消全选全部音频",
        BatchMediaKind.Video => "全选全部视频",
        BatchMediaKind.Audio => "全选全部音频",
        _ => "全选同类文件",
    };

    private WorkMode CurrentMode => _currentMode;

    public async Task<IReadOnlyList<string>> LoadFilesAsync(
        IReadOnlyList<string> paths,
        bool replace,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return ["任务执行中，不能修改输入文件。"];
        }

        if (_isLoadingMedia)
        {
            AppendLog("[忽略] 正在分析上一批文件，本次重复导入请求已忽略。");
            return [];
        }

        _isLoadingMedia = true;
        RaiseActionState();
        try
        {
            var selection = InputFilePlanner.SelectDistinctExistingFiles(
                paths,
                replace ? [] : MediaItems.Select(item => item.InputPath));
            if (selection.DuplicateCount > 0)
            {
                AppendLog($"[去重] 已忽略 {selection.DuplicateCount} 个重复文件。");
            }

            if (selection.Paths.Count == 0)
            {
                if (selection.DuplicateCount > 0)
                {
                    StatusText = $"已忽略 {selection.DuplicateCount} 个重复文件";
                }

                return [];
            }

            if (replace)
            {
                ResetMediaState();
            }

            var errors = new List<string>();
            foreach (var path in selection.Paths)
            {
                try
                {
                    StatusText = $"正在分析 {Path.GetFileName(path)}...";
                    var media = await _mediaInspector.InspectAsync(path, MediaItems.Count, cancellationToken);
                    MediaItems.Add(new MediaItemViewModel(media, MediaItems.Count, OnMediaSelectionChanged));
                    foreach (var track in media.Tracks)
                    {
                        Tracks.Add(new TrackItemViewModel(track, OnTrackSelectionChanged, OnTrackTargetChanged));
                    }

                    AppendLog($"[导入] {media.FileName}，共 {media.Tracks.Count} 条轨道。");
                }
                catch (Exception error) when (error is MediaInspectionException or ToolNotFoundException)
                {
                    var message = $"{Path.GetFileName(path)}：{error.Message}";
                    errors.Add(message);
                    AppendLog($"[错误] {path}\n{error.Message}");
                }
            }

            UpdateTrackKindLabels();
            RestoreCurrentModeSelection();
            ApplySelectionConstraints(null);
            SyncSelectedTrackOrder();
            StatusText = errors.Count == 0
                ? selection.DuplicateCount > 0
                    ? $"已导入 {selection.Paths.Count} 个文件，忽略 {selection.DuplicateCount} 个重复文件"
                    : $"已导入 {selection.Paths.Count} 个文件"
                : "部分文件导入失败";
            RefreshState();
            return errors;
        }
        finally
        {
            _isLoadingMedia = false;
            RaiseActionState();
        }
    }

    public void ClearMedia()
    {
        if (IsRunning)
        {
            return;
        }

        ResetMediaState();
        ClearLog();
        AppendLog("已清空当前媒体列表。");
        StatusText = "空闲";
        ProgressValue = 0;
        IsProgressIndeterminate = false;
        RefreshState();
    }

    public void ClearCurrentSelection()
    {
        if (IsRunning)
        {
            return;
        }

        _suppressTrackChanges = true;
        foreach (var track in Tracks)
        {
            track.SetSelectedSilently(false);
        }
        _suppressTrackChanges = false;

        _modeSelectedKeys[CurrentMode].Clear();
        _modeSelectedOrders[CurrentMode].Clear();
        if (CurrentMode == WorkMode.Batch)
        {
            _suppressMediaChanges = true;
            foreach (var item in MediaItems)
            {
                item.SetSelectedSilently(false);
            }
            _suppressMediaChanges = false;
            _batchMediaKind = null;
        }
        _outputPathDirty = false;
        SetOutputPath(string.Empty);
        AppendLog("已清空当前模式下的勾选。");
        RefreshState();
    }

    public void SetUserOutputPath(string path)
    {
        OutputPath = path.Trim();
    }

    public void CancelCurrentTask()
    {
        if (!IsRunning || _taskCancellation is null)
        {
            return;
        }

        StatusText = "正在取消任务...";
        _taskCancellation.Cancel();
    }

    public void Dispose()
    {
        _settingsStore.SaveWorkMode(CurrentMode);
        _taskCancellation?.Cancel();
        _taskCancellation?.Dispose();
        _taskCancellation = null;
    }

    private void ResetMediaState()
    {
        MediaItems.Clear();
        Tracks.Clear();
        SelectedTrackItems.Clear();
        foreach (var mode in _modeSelectedKeys.Keys)
        {
            _modeSelectedKeys[mode].Clear();
            _modeSelectedOrders[mode].Clear();
        }

        _batchMediaKind = null;

        _outputPathDirty = false;
        SetOutputPath(string.Empty);
    }

    private void SetOutputPath(string value)
    {
        _settingOutputPath = true;
        OutputPath = value;
        _settingOutputPath = false;
    }

    private void ClearLog()
    {
        _logBuilder.Clear();
        LogText = string.Empty;
    }

    private void AppendLog(string line)
    {
        if (_logBuilder.Length > 750_000)
        {
            _logBuilder.Remove(0, 250_000);
        }

        if (_logBuilder.Length > 0)
        {
            _logBuilder.AppendLine();
        }

        _logBuilder.Append(line);
        LogText = _logBuilder.ToString();
    }

    private void RaiseActionState()
    {
        OnPropertiesChanged(
            nameof(HasMedia),
            nameof(IsBatchOutput),
            nameof(HasBatchSelection),
            nameof(CanSelectAllBatch),
            nameof(CanChooseOutput),
            nameof(CanRun),
            nameof(CanCancel),
            nameof(CanModifyInputs),
            nameof(CanMoveSelectedTrack),
            nameof(RunButtonText),
            nameof(OutputBrowseLabel),
            nameof(ImportButtonText),
            nameof(SelectAllBatchLabel));
    }
}
