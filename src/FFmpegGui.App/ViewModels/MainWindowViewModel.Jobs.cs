using System.IO;
using FFmpegGui.Core;
using FFmpegGui.Infrastructure;

namespace FFmpegGui.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public async Task<string?> RunCurrentJobAsync()
    {
        if (!CanRun)
        {
            var issues = CollectIssues();
            return issues.Count > 0
                ? string.Join(Environment.NewLine, issues)
                : "请先选择输出路径。";
        }

        IReadOnlyList<MediaJob> jobs;
        string taskLabel;
        try
        {
            (taskLabel, jobs) = BuildJobs();
        }
        catch (Exception error) when (error is ToolNotFoundException
                                      or IOException
                                      or UnauthorizedAccessException
                                      or ArgumentException
                                      or NotSupportedException)
        {
            AppendLog($"[错误] {error.Message}");
            StatusText = "无法开始任务";
            return error.Message;
        }

        if (jobs.Count == 0)
        {
            return "当前选中的轨道没有可执行任务。";
        }

        ClearLog();
        AppendLog(jobs.Count > 1 ? $"[开始批量{taskLabel}]" : $"[开始{taskLabel}]");
        foreach (var job in jobs)
        {
            AppendLog(CommandLineFormatter.Format(job.Invocation.Program, job.Invocation.Arguments));
        }

        _taskCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _taskCancellation = cancellation;
        IsRunning = true;
        var allSucceeded = true;

        try
        {
            for (var index = 0; index < jobs.Count; index++)
            {
                var job = jobs[index];
                var batchSuffix = jobs.Count > 1 ? $"（{index + 1}/{jobs.Count}）" : string.Empty;
                StatusText = $"{taskLabel}中...{batchSuffix}";
                ProgressValue = 0;
                IsProgressIndeterminate = job.DurationMilliseconds <= 0;
                if (jobs.Count > 1)
                {
                    AppendLog($"[批量] 开始第 {index + 1}/{jobs.Count} 条。");
                }

                var result = await _processRunner.RunAsync(
                    job,
                    line => _dispatcher.Invoke(() => AppendLog(line)),
                    progress => _dispatcher.Invoke(() =>
                    {
                        IsProgressIndeterminate = false;
                        ProgressValue = progress;
                        StatusText = $"{taskLabel}中... {progress}%{batchSuffix}";
                    }),
                    cancellation.Token);

                if (result.ForcedCompletion && result.Success)
                {
                    AppendLog("[完成] 封面图已经写出，已结束未主动退出的处理进程。");
                }
                else if (result.Success)
                {
                    AppendLog("[完成] 任务进程已正常退出。");
                }
                else
                {
                    AppendLog($"[失败] {Path.GetFileNameWithoutExtension(job.Invocation.Program)} 退出码 {result.ExitCode}");
                    StatusText = jobs.Count > 1
                        ? $"{taskLabel}失败（第 {index + 1}/{jobs.Count} 条）"
                        : $"{taskLabel}失败";
                    allSucceeded = false;
                    break;
                }
            }

            if (allSucceeded)
            {
                ProgressValue = 100;
                StatusText = jobs.Count > 1
                    ? $"{taskLabel}完成（{jobs.Count}/{jobs.Count}）"
                    : $"{taskLabel}完成";
            }
        }
        catch (OperationCanceledException)
        {
            allSucceeded = false;
            AppendLog("[取消] 用户已取消当前任务。");
            StatusText = $"{taskLabel}已取消";
        }
        catch (Exception error) when (error is ProcessLaunchException
                                      or IOException
                                      or UnauthorizedAccessException
                                      or ArgumentException
                                      or NotSupportedException)
        {
            allSucceeded = false;
            AppendLog($"[错误] {error.Message}");
            StatusText = $"{taskLabel}失败";
            return error.Message;
        }
        finally
        {
            IsProgressIndeterminate = false;
            if (!allSucceeded && ProgressValue == 100)
            {
                ProgressValue = 0;
            }

            if (ReferenceEquals(_taskCancellation, cancellation))
            {
                _taskCancellation = null;
            }

            cancellation.Dispose();
            IsRunning = false;
            RefreshState();
        }

        return null;
    }

    private (string TaskLabel, IReadOnlyList<MediaJob> Jobs) BuildJobs()
    {
        var selectedTracks = OrderedSelectedTracks();
        if (CurrentMode == WorkMode.Mux)
        {
            EnsureOutputParent(OutputPath);
            var invocation = _invocationFactory.CreateMux(
                MediaItems.Select(item => item.Media).ToArray(),
                selectedTracks,
                _muxContainer,
                OutputPath);
            return ("封装",
            [
                new MediaJob(invocation, OutputPath, false, EstimateMuxDuration(selectedTracks)),
            ]);
        }

        if (CurrentMode == WorkMode.Extract && selectedTracks.Count > 1)
        {
            var outputDirectory = ResolveOutputDirectory(OutputPath);
            Directory.CreateDirectory(outputDirectory);
            var jobs = new List<MediaJob>();
            foreach (var track in selectedTracks)
            {
                var target = ExtractPlanner.ListExtractTargets(track).FirstOrDefault();
                if (target is null)
                {
                    continue;
                }

                var outputPath = ExtractPlanner.BuildOutputPathInDirectory(track, target, outputDirectory);
                jobs.Add(BuildExtractJob(track, target, outputPath));
            }

            return ("提取", jobs);
        }

        if (SelectedOutputOption?.Target is not { } selectedTarget)
        {
            return (CurrentMode == WorkMode.Extract ? "提取" : "转换", []);
        }

        if (CurrentMode == WorkMode.Convert && selectedTracks.Count > 1)
        {
            var outputDirectory = ResolveOutputDirectory(OutputPath);
            Directory.CreateDirectory(outputDirectory);
            var jobs = selectedTracks.Select(track =>
            {
                var outputPath = ExtractPlanner.BuildOutputPathInDirectory(track, selectedTarget, outputDirectory);
                return BuildExtractJob(track, selectedTarget, outputPath);
            }).ToArray();
            return ("转换", jobs);
        }

        EnsureOutputParent(OutputPath);
        return (
            CurrentMode == WorkMode.Extract ? "提取" : "转换",
            [BuildExtractJob(selectedTracks[0], selectedTarget, OutputPath)]);
    }

    private MediaJob BuildExtractJob(TrackInfo track, OutputTarget target, string outputPath)
    {
        var invocation = _invocationFactory.CreateExtract(track, target, outputPath);
        return new MediaJob(
            invocation,
            outputPath,
            track.IsCover,
            EstimateExtractDuration(track));
    }

    private long EstimateMuxDuration(IReadOnlyList<TrackInfo> selectedTracks)
    {
        var sourceIndices = selectedTracks
            .Where(track => !track.IsCover)
            .Select(track => track.SourceIndex)
            .Distinct();
        return sourceIndices
            .Select(index => index >= 0 && index < MediaItems.Count
                ? DurationMilliseconds(MediaItems[index].Media.DurationSeconds)
                : 0)
            .DefaultIfEmpty(0)
            .Max();
    }

    private long EstimateExtractDuration(TrackInfo track)
    {
        if (track.IsCover || track.SourceIndex < 0 || track.SourceIndex >= MediaItems.Count)
        {
            return 0;
        }

        return DurationMilliseconds(MediaItems[track.SourceIndex].Media.DurationSeconds);
    }

    private static long DurationMilliseconds(double? durationSeconds) =>
        durationSeconds is > 0
            ? Math.Max(1, (long)(durationSeconds.Value * 1_000))
            : 0;

    private static void EnsureOutputParent(string outputPath)
    {
        var parent = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }
    }
}
