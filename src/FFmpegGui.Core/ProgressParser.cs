using System.Globalization;
using System.Text.RegularExpressions;

namespace FFmpegGui.Core;

public static partial class ProgressParser
{
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

        var unit = numericMatches[^1].Groups[1].Value;
        var milliseconds = unit == "us" ? amount / 1_000 : amount;
        if (unit == "ms" && milliseconds > totalDurationMilliseconds * 100)
        {
            milliseconds = amount / 1_000;
        }

        return Percentage(milliseconds, totalDurationMilliseconds);
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
