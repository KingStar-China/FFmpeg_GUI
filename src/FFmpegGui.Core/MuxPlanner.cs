namespace FFmpegGui.Core;

public static class MuxPlanner
{
    private static readonly HashSet<string> Mp4TextSubtitleCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "ass", "mov_text", "srt", "ssa", "subrip", "text", "tx3g", "webvtt",
    };

    public static IReadOnlyList<string> Validate(
        IReadOnlyList<TrackInfo> selectedTracks,
        string outputContainer)
    {
        var issues = new List<string>();
        if (selectedTracks.Count == 0)
        {
            issues.Add("封装模式下至少要勾选 1 条轨道。");
            return issues;
        }

        if (selectedTracks.Count(track => track.Kind == "video" && !track.IsCover) > 1)
        {
            issues.Add("封装模式下最多只能勾选 1 条视频轨。");
        }

        if (selectedTracks.Count(track => track.IsCover) > 1)
        {
            issues.Add("封装模式下最多只能勾选 1 张封面图。");
        }

        if (outputContainer.Equals("mp4", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var track in selectedTracks.Where(track => track.Kind == "subtitle"))
            {
                if (!IsMp4TextSubtitle(track.Codec))
                {
                    issues.Add(
                        $"MP4 不支持当前字幕轨：{track.SourceFileName} / 轨道 {track.StreamIndex} / {track.Codec}。");
                }
            }
        }

        return issues;
    }

    public static bool IsMp4TextSubtitle(string codec) =>
        Mp4TextSubtitleCodecs.Contains(codec.Trim());

    public static string BuildDefaultOutputPath(
        IReadOnlyList<MediaInfo> media,
        string outputContainer,
        IReadOnlyList<TrackInfo>? selectedTracks = null)
    {
        var extension = outputContainer.Equals("mp4", StringComparison.OrdinalIgnoreCase)
            && IsAacAudioOnlySelection(selectedTracks ?? [])
                ? ".m4a"
                : $".{outputContainer}";

        if (media.Count == 0)
        {
            return $"output{extension}";
        }

        var candidate = Path.ChangeExtension(media[0].InputPath, extension);
        if (IsOutputPathDistinct(media, candidate))
        {
            return candidate;
        }

        var directory = Path.GetDirectoryName(media[0].InputPath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(media[0].InputPath);
        return Path.Combine(directory, $"{stem}.muxed{extension}");
    }

    public static bool IsOutputPathDistinct(IReadOnlyList<MediaInfo> media, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return true;
        }

        string fullOutputPath;
        try
        {
            fullOutputPath = Path.GetFullPath(outputPath);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return true;
        }

        return media.All(item =>
            !Path.GetFullPath(item.InputPath).Equals(fullOutputPath, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<string> BuildArguments(
        IReadOnlyList<MediaInfo> media,
        IReadOnlyList<TrackInfo> selectedTracks,
        string outputContainer,
        string outputPath)
    {
        var arguments = new List<string> { "-y", "-nostdin", "-progress", "pipe:1", "-nostats" };
        foreach (var item in media)
        {
            arguments.AddRange(["-i", item.InputPath]);
        }

        foreach (var track in selectedTracks)
        {
            arguments.AddRange(["-map", $"{track.SourceIndex}:{track.StreamIndex}"]);
        }

        if (outputContainer.Equals("mkv", StringComparison.OrdinalIgnoreCase))
        {
            arguments.AddRange(["-c", "copy"]);
        }
        else
        {
            if (selectedTracks.Any(track => track.Kind == "video"))
            {
                arguments.AddRange(["-c:v", "copy"]);
            }

            if (selectedTracks.Any(track => track.Kind == "audio"))
            {
                arguments.AddRange(["-c:a", "copy"]);
            }

            if (selectedTracks.Any(track => track.Kind == "subtitle"))
            {
                arguments.AddRange(["-c:s", "mov_text"]);
            }

            var outputVideoIndex = 0;
            foreach (var track in selectedTracks.Where(track => track.Kind == "video"))
            {
                if (track.IsCover)
                {
                    arguments.AddRange([$"-disposition:v:{outputVideoIndex}", "attached_pic"]);
                }

                outputVideoIndex++;
            }
        }

        arguments.Add(outputPath);
        return arguments;
    }

    private static bool IsAacAudioOnlySelection(IReadOnlyList<TrackInfo> selectedTracks) =>
        selectedTracks.Count == 1
        && selectedTracks[0].Kind == "audio"
        && !selectedTracks[0].IsCover
        && selectedTracks[0].Codec.Equals("aac", StringComparison.OrdinalIgnoreCase);
}
