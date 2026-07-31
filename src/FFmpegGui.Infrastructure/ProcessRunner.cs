using System.Diagnostics;
using System.Text;
using FFmpegGui.Core;

namespace FFmpegGui.Infrastructure;

public sealed record ProcessRunResult(
    bool Success,
    int ExitCode,
    bool ForcedCompletion,
    bool ErrorMarkerSeen);

public sealed class ProcessRunner
{
    private static readonly string[] ErrorMarkers =
    [
        "conversion failed!",
        "error while",
        "invalid data found when processing input",
        "could not write header",
        "error opening",
        "error initializing",
        "sequence pattern",
        "use the -update option",
    ];

    public async Task<ProcessRunResult> RunAsync(
        MediaJob job,
        Action<string> onOutput,
        Action<int>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var processName = Path.GetFileNameWithoutExtension(job.Invocation.Program).ToLowerInvariant();
        var startInfo = new ProcessStartInfo
        {
            FileName = job.Invocation.Program,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in job.Invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new ProcessLaunchException($"无法启动 {processName}。");
            }
        }
        catch (Exception error) when (error is not ProcessLaunchException)
        {
            throw new ProcessLaunchException($"无法启动 {processName}：{error.Message}", error);
        }

        var errorMarkerSeen = false;
        void HandleLine(string line)
        {
            onOutput(line);
            if (ErrorMarkers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                errorMarkerSeen = true;
            }

            var progress = ProgressParser.Parse(line, processName, job.DurationMilliseconds);
            if (progress.HasValue)
            {
                onProgress?.Invoke(progress.Value);
            }
        }

        var standardOutputTask = PumpAsync(process.StandardOutput, HandleLine);
        var standardErrorTask = PumpAsync(process.StandardError, HandleLine);
        using var monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var coverMonitorTask = job.IsCoverExtraction
            ? MonitorCoverOutputAsync(process, job.OutputPath, monitorCancellation.Token)
            : Task.FromResult(false);

        using var cancellationRegistration = cancellationToken.Register(() => TryKill(process));
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await Task.WhenAll(standardOutputTask, standardErrorTask);
            throw;
        }
        finally
        {
            await monitorCancellation.CancelAsync();
        }

        await Task.WhenAll(standardOutputTask, standardErrorTask);
        var forcedCompletion = await coverMonitorTask;
        var outputExists = File.Exists(job.OutputPath) && new FileInfo(job.OutputPath).Length > 0;
        var success = (process.ExitCode == 0 && !errorMarkerSeen) || (forcedCompletion && outputExists);
        return new ProcessRunResult(success, process.ExitCode, forcedCompletion, errorMarkerSeen);
    }

    private static async Task PumpAsync(StreamReader reader, Action<string> onLine)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            onLine(line);
        }
    }

    private static async Task<bool> MonitorCoverOutputAsync(
        Process process,
        string outputPath,
        CancellationToken cancellationToken)
    {
        long lastSize = -1;
        var stableTicks = 0;
        try
        {
            while (!process.HasExited)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
                if (!File.Exists(outputPath))
                {
                    continue;
                }

                var size = new FileInfo(outputPath).Length;
                if (size <= 0)
                {
                    continue;
                }

                if (size == lastSize)
                {
                    stableTicks++;
                }
                else
                {
                    stableTicks = 0;
                    lastSize = size;
                }

                if (stableTicks < 2)
                {
                    continue;
                }

                TryKill(process);
                return true;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        return false;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}

public sealed class ProcessLaunchException : InvalidOperationException
{
    public ProcessLaunchException(string message) : base(message)
    {
    }

    public ProcessLaunchException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
