using System.Diagnostics;
using System.Text;
using FFmpegGui.Core;

namespace FFmpegGui.Infrastructure;

public sealed class MediaInspector(ToolLocator toolLocator)
{
    public async Task<MediaInfo> InspectAsync(
        string inputPath,
        int sourceIndex,
        CancellationToken cancellationToken = default)
    {
        var ffprobePath = toolLocator.RequireFfprobe();
        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in new[]
                 {
                     "-v", "error", "-print_format", "json", "-show_streams", "-show_format", inputPath,
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new MediaInspectionException("ffprobe 启动失败。");
            }
        }
        catch (Exception error) when (error is not MediaInspectionException)
        {
            throw new MediaInspectionException($"无法启动 ffprobe：{error.Message}", error);
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var output = await outputTask;
        var errorOutput = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new MediaInspectionException(
                string.IsNullOrWhiteSpace(errorOutput) ? "ffprobe 解析失败。" : errorOutput.Trim());
        }

        try
        {
            return FfprobeJsonParser.Parse(output, inputPath, sourceIndex);
        }
        catch (Exception error)
        {
            throw new MediaInspectionException($"ffprobe 返回了无法解析的数据：{error.Message}", error);
        }
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

public sealed class MediaInspectionException : InvalidOperationException
{
    public MediaInspectionException(string message) : base(message)
    {
    }

    public MediaInspectionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
