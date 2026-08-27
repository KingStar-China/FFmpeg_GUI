using System.Globalization;
using System.Text.RegularExpressions;

namespace FFmpegGui.Core;

public static partial class ProgressParser
{
    private static readonly HashSet<string> FfmpegProgressKeys = new(StringComparer.Ordinal)
    {
        "bitrate",
        "drop_frames",
        "dup_frames",
        "fps",
        "frame",
        "out_time",
        "out_time_ms",
        "out_time_us",
        "progress",
        "speed",
        "total_size",
    };

    public static int? Parse(string payload, string processName, long totalDurationMilliseconds)
    {
        if (processName.Equals("mkvextract", StringComparison.OrdinalIgnoreCase))
        {
            var matches = MkvProgressRegex().Matches(payload);
            return matches.Count == 0
                ? null
                : Math.Clamp(int.Parse(matches[^1].Groups[1].Value, CultureInfo.InvariantCulture), 0, 100);
        }

        if (totalDurationMilliseconds <= 0)
        {
            return null;
        }

        var timeMatches = OutTimeRegex().Matches(payload);
        if (timeMatches.Count > 0
            && TimeSpan.TryParse(timeMatches[^1].Groups[1].Value, CultureInfo.InvariantCulture, out var timestamp))
        {
            return Percentage((long)timestamp.TotalMilliseconds, totalDurationMilliseconds);
        }

        var numericMatches = NumericOutTimeRegex().Matches(payload);
        if (numericMatches.Count == 0
            || !long.TryParse(numericMatches[^1].Groups[2].Value, CultureInfo.InvariantCulture, out var amount))
        {
            return null;
        }

        // FFmpeg 的 progress 协议中 out_time_ms 历史上同样输出微秒值。
        // 若把它按毫秒处理，短任务会在刚开始时错误跳到 99%。
        var milliseconds = amount / 1_000;

        return Percentage(milliseconds, totalDurationMilliseconds);
    }

    public static bool IsFfmpegProgressProtocolLine(string payload, string processName)
    {
        if (!processName.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var separator = payload.IndexOf('=');
        if (separator <= 0)
        {
            return false;
        }

        var key = payload[..separator].Trim();
        return FfmpegProgressKeys.Contains(key)
            || key.StartsWith("stream_", StringComparison.Ordinal)
            && key.EndsWith("_q", StringComparison.Ordinal);
    }

    private static int Percentage(long currentMilliseconds, long totalDurationMilliseconds) =>
        Math.Clamp((int)(currentMilliseconds * 100 / totalDurationMilliseconds), 0, 99);

    [GeneratedRegex(@"(?im)\bprogress\b[^\d]*([0-9]{1,3})%")]
    private static partial Regex MkvProgressRegex();

    [GeneratedRegex(@"(?im)^out_time=(\d{2}:\d{2}:\d{2}(?:\.\d+)?)\s*$")]
    private static partial Regex OutTimeRegex();

    [GeneratedRegex(@"(?im)^out_time_(us|ms)=(\d+)\s*$")]
    private static partial Regex NumericOutTimeRegex();
}
