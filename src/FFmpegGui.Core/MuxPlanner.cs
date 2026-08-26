namespace FFmpegGui.Core;

public static class MuxPlanner
{
    private static readonly HashSet<string> AudioMixContainers = new(StringComparer.OrdinalIgnoreCase)
    {
        "m4a", "mp3", "aac", "wav", "flac", "opus",
    };

    private static readonly HashSet<string> Mp4TextSubtitleCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "ass", "mov_text", "srt", "ssa", "subrip", "text", "tx3g", "webvtt",
    };

    public static bool IsAudioOnlySelection(IReadOnlyList<TrackInfo> selectedTracks) =>
        selectedTracks.Count > 0
        && selectedTracks.All(track => track.Kind == "audio" && !track.IsCover);

    public static bool IsAudioMixContainer(string outputContainer) =>
        AudioMixContainers.Contains(NormalizeContainer(outputContainer));

    public static IReadOnlyList<string> Validate(
        IReadOnlyList<TrackInfo> selectedTracks,
        string outputContainer,
        IReadOnlyDictionary<string, OutputTarget>? targetByTrackKey = null)
    {
        var issues = new List<string>();
        if (selectedTracks.Count == 0)
        {
            issues.Add("封装模式下至少要勾选 1 条轨道。");
            return issues;
        }

        var container = NormalizeContainer(outputContainer);
        if (IsAudioMixContainer(container))
        {
            if (!IsAudioOnlySelection(selectedTracks))
            {
                issues.Add("M4A、MP3、AAC、WAV、FLAC、Opus 输出只能选择音频轨道。");
            }

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

        if (container == "mp4")
        {
            foreach (var track in selectedTracks.Where(track => track.Kind == "subtitle"))
            {
                if (targetByTrackKey?.TryGetValue(track.TrackKey, out var target) == true
                    && target.Mode == "transcode"
                    && target.CodecArguments.Contains("mov_text", StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!IsMp4TextSubtitle(track.Codec))
                {
                    issues.Add(
                        $"MP4 不支持当前字幕轨：{track.SourceFileName} / 轨道 {track.StreamIndex} / {track.Codec}。");
                }

                if (targetByTrackKey?.TryGetValue(track.TrackKey, out target) == true
                    && target.Mode == "transcode"
                    && !target.CodecArguments.Contains("mov_text", StringComparer.OrdinalIgnoreCase))
                {
                    issues.Add($"MP4 字幕目标编码必须是 MOV_TEXT：{track.SourceFileName} / 轨道 {track.StreamIndex}。");
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
        var container = NormalizeContainer(outputContainer);
        if (container == "mp4" && IsAudioOnlySelection(selectedTracks ?? []))
        {
            container = "m4a";
        }

        var extension = $".{container}";

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
        string outputPath,
        IReadOnlyDictionary<string, OutputTarget>? targetByTrackKey = null)
    {
        var container = NormalizeContainer(outputContainer);
        if (container == "mp4" && IsAudioOnlySelection(selectedTracks))
        {
            container = "m4a";
        }

        var arguments = new List<string> { "-y", "-nostdin", "-progress", "pipe:1", "-nostats" };
        foreach (var item in media)
        {
            arguments.AddRange(["-i", item.InputPath]);
        }

        if (IsAudioOnlySelection(selectedTracks) && IsAudioMixContainer(container))
        {
            AddAudioMixArguments(arguments, selectedTracks, container);
        }
        else
        {
            foreach (var track in selectedTracks)
            {
                arguments.AddRange(["-map", $"{track.SourceIndex}:{track.StreamIndex}"]);
            }

            if (container == "mkv")
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

            if (targetByTrackKey is not null)
            {
                AddTargetCodecArguments(arguments, selectedTracks, targetByTrackKey);
            }
        }

        arguments.Add(outputPath);
        return arguments;
    }

    private static void AddAudioMixArguments(
        List<string> arguments,
        IReadOnlyList<TrackInfo> selectedTracks,
        string outputContainer)
    {
        if (selectedTracks.Count == 1)
        {
            var track = selectedTracks[0];
            arguments.AddRange(["-map", $"{track.SourceIndex}:{track.StreamIndex}"]);
        }
        else
        {
            var inputs = string.Concat(
                selectedTracks.Select(track => $"[{track.SourceIndex}:{track.StreamIndex}]"));
            var filter =
                $"{inputs}amix=inputs={selectedTracks.Count}:duration=longest:dropout_transition=0:normalize=1[audio_mix]";
            arguments.AddRange(["-filter_complex", filter, "-map", "[audio_mix]"]);
        }

        arguments.AddRange(GetAudioCodecArguments(outputContainer));
    }

    private static IReadOnlyList<string> GetAudioCodecArguments(string outputContainer) =>
        outputContainer switch
        {
            "m4a" => ["-c:a", "aac", "-b:a", "192k"],
            "mp3" => ["-c:a", "libmp3lame", "-b:a", "192k"],
            "aac" => ["-c:a", "aac", "-b:a", "192k"],
            "wav" => ["-c:a", "pcm_s16le"],
            "flac" => ["-c:a", "flac"],
            "opus" => ["-c:a", "libopus", "-b:a", "160k"],
            _ => ["-c:a", "aac", "-b:a", "192k"],
        };

    private static void AddTargetCodecArguments(
        List<string> arguments,
        IReadOnlyList<TrackInfo> selectedTracks,
        IReadOnlyDictionary<string, OutputTarget> targetByTrackKey)
    {
        var videoIndex = 0;
        var audioIndex = 0;
        var subtitleIndex = 0;
        foreach (var track in selectedTracks)
        {
            var outputIndex = track.Kind switch
            {
                "video" => videoIndex++,
                "audio" => audioIndex++,
                "subtitle" => subtitleIndex++,
                _ => -1,
            };
            if (outputIndex < 0
                || !targetByTrackKey.TryGetValue(track.TrackKey, out var target)
                || target.Mode != "transcode")
            {
                continue;
            }

            foreach (var argument in target.CodecArguments)
            {
                arguments.Add(AddStreamSpecifier(argument, track.Kind, outputIndex));
            }
        }
    }

    private static string AddStreamSpecifier(string argument, string kind, int outputIndex) =>
        argument switch
        {
            "-c:v" or "-codec:v" => $"-c:v:{outputIndex}",
            "-c:a" or "-codec:a" => $"-c:a:{outputIndex}",
            "-c:s" or "-codec:s" => $"-c:s:{outputIndex}",
            "-b:v" => $"-b:v:{outputIndex}",
            "-b:a" => $"-b:a:{outputIndex}",
            "-frames:v" => $"-frames:v:{outputIndex}",
            _ => argument,
        };

    private static string NormalizeContainer(string outputContainer) =>
        outputContainer.Trim().ToLowerInvariant();
}
