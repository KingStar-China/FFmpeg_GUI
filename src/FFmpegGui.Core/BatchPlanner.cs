namespace FFmpegGui.Core;

public static class BatchPlanner
{
    private static readonly IReadOnlyList<BatchOutputPreset> VideoOutputPresets =
    [
        new("mp4", "MP4（H.264 + AAC）"),
        new("mkv", "MKV（保留原编码）"),
        new("webm", "WebM（VP9 + Opus）"),
        new("mov", "MOV（H.264 + AAC）"),
        new("avi", "AVI（MPEG-4 + MP3）"),
    ];

    private static readonly IReadOnlyList<BatchOutputPreset> AudioOutputPresets =
    [
        new("m4a", "M4A（AAC）"),
    ];

    public static BatchMediaKind? GetMediaKind(MediaInfo media)
    {
        if (media.Tracks.Any(IsUsableVideo))
        {
            return BatchMediaKind.Video;
        }

        return media.Tracks.Any(IsUsableAudio)
            ? BatchMediaKind.Audio
            : null;
    }

    public static IReadOnlyList<BatchOutputPreset> ListOutputPresets(BatchMediaKind kind) =>
        kind == BatchMediaKind.Video ? VideoOutputPresets : AudioOutputPresets;

    public static IReadOnlyList<TrackInfo> SelectOutputTracks(
        MediaInfo media,
        BatchMediaKind kind,
        string? outputContainer = null)
    {
        var container = ResolvePreset(kind, outputContainer ?? OutputContainer(kind)).Container;
        if (kind == BatchMediaKind.Video)
        {
            var video = SelectPrimaryTrack(media.Tracks.Where(IsUsableVideo));
            if (video is null)
            {
                return [];
            }

            var audio = SelectPrimaryTrack(media.Tracks.Where(IsUsableAudio));
            var subtitle = SelectPrimaryTrack(
                media.Tracks.Where(track => IsUsableSubtitle(track, container)));
            return new[] { video, audio, subtitle }
                .Where(track => track is not null)
                .Select(track => NormalizeForSingleInput(track!))
                .ToArray();
        }

        var primaryAudio = SelectPrimaryTrack(media.Tracks.Where(IsUsableAudio));
        return primaryAudio is null
            ? []
            : [NormalizeForSingleInput(primaryAudio)];
    }

    public static string OutputContainer(BatchMediaKind kind) =>
        ListOutputPresets(kind)[0].Container;

    public static string OutputLabel(BatchMediaKind kind) =>
        ListOutputPresets(kind)[0].Label;

    public static string OutputLabel(BatchMediaKind kind, string outputContainer) =>
        ResolvePreset(kind, outputContainer).Label;

    public static string OutputDescription(BatchMediaKind kind, string outputContainer)
    {
        var container = ResolvePreset(kind, outputContainer).Container;
        if (kind == BatchMediaKind.Audio)
        {
            return "AAC 音频会直接复制；其他音频只转换为 AAC。";
        }

        return container switch
        {
            "mp4" => "已符合 H.264/AAC/MOV_TEXT 的流会直接复制；否则只转换不符合的流，并保留 1 条默认文本软字幕。",
            "mkv" => "视频和音频保留原编码；保留 1 条默认软字幕，只有容器不兼容的字幕才转换。",
            "webm" => "已符合 VP9/Opus/WebVTT 的流会直接复制；否则只转换不符合的流，并保留 1 条默认文本软字幕。",
            "mov" => "已符合 H.264/AAC/MOV_TEXT 的流会直接复制；否则只转换不符合的流，并保留 1 条默认文本软字幕。",
            "avi" => "已符合 MPEG-4/MP3 的流会直接复制；否则只转换不符合的流。AVI 不嵌入软字幕。",
            _ => string.Empty,
        };
    }

    public static IReadOnlyList<string> BuildArguments(
        MediaInfo media,
        BatchMediaKind kind,
        string outputContainer,
        string outputPath)
    {
        var container = ResolvePreset(kind, outputContainer).Container;
        var tracks = SelectOutputTracks(media, kind, container);
        var arguments = new List<string>
        {
            "-y", "-nostdin", "-progress", "pipe:1", "-nostats", "-i", media.InputPath,
        };

        if (kind == BatchMediaKind.Video)
        {
            AddVideoArguments(arguments, tracks, container);
        }
        else
        {
            AddAudioArguments(arguments, tracks);
        }

        arguments.Add(outputPath);
        return arguments;
    }

    public static string BuildOutputPath(
        MediaInfo media,
        BatchMediaKind kind,
        string outputContainer,
        string outputDirectory,
        ISet<string>? reservedPaths = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var container = ResolvePreset(kind, outputContainer).Container;
        var extension = $".{container}";
        var stem = Path.GetFileNameWithoutExtension(media.InputPath);
        var candidate = Path.Combine(outputDirectory, $"{stem}{extension}");
        var suffix = 1;
        while (IsInputPath(media, candidate)
               || File.Exists(candidate)
               || reservedPaths?.Contains(Path.GetFullPath(candidate)) == true)
        {
            var marker = suffix == 1 ? ".batch" : $".batch{suffix}";
            candidate = Path.Combine(outputDirectory, $"{stem}{marker}{extension}");
            suffix++;
        }

        reservedPaths?.Add(Path.GetFullPath(candidate));
        return candidate;
    }

    private static BatchOutputPreset ResolvePreset(BatchMediaKind kind, string outputContainer)
    {
        var preset = ListOutputPresets(kind).FirstOrDefault(item =>
            item.Container.Equals(outputContainer.Trim(), StringComparison.OrdinalIgnoreCase));
        return preset ?? throw new ArgumentException($"批量模式不支持输出格式：{outputContainer}");
    }

    private static bool IsUsableVideo(TrackInfo track) =>
        track.IsSupported && track.Kind == "video" && !track.IsCover;

    private static bool IsUsableAudio(TrackInfo track) =>
        track.IsSupported && track.Kind == "audio" && !track.IsCover;

    private static bool IsUsableSubtitle(TrackInfo track, string outputContainer)
    {
        if (!track.IsSupported || track.Kind != "subtitle")
        {
            return false;
        }

        return outputContainer switch
        {
            "mkv" => true,
            "mp4" or "mov" or "webm" => MuxPlanner.IsMp4TextSubtitle(track.Codec),
            _ => false,
        };
    }

    private static TrackInfo? SelectPrimaryTrack(IEnumerable<TrackInfo> tracks) =>
        tracks
            .OrderByDescending(track => track.Disposition.IsDefault)
            .ThenBy(track => track.StreamIndex)
            .FirstOrDefault();

    private static TrackInfo NormalizeForSingleInput(TrackInfo track) =>
        track with
        {
            TrackKey = $"0:{track.StreamIndex}",
            SourceIndex = 0,
        };

    private static bool IsInputPath(MediaInfo media, string candidate) =>
        Path.GetFullPath(media.InputPath)
            .Equals(Path.GetFullPath(candidate), StringComparison.OrdinalIgnoreCase);

    private static void AddVideoArguments(
        List<string> arguments,
        IReadOnlyList<TrackInfo> tracks,
        string outputContainer)
    {
        var video = tracks.FirstOrDefault(track => track.Kind == "video");
        if (video is null)
        {
            return;
        }

        arguments.AddRange(["-map", $"0:{video.StreamIndex}"]);
        AddVideoCodecArguments(arguments, video, outputContainer);

        var audio = tracks.FirstOrDefault(track => track.Kind == "audio");
        if (audio is not null)
        {
            arguments.AddRange(["-map", $"0:{audio.StreamIndex}"]);
            AddAudioCodecArguments(arguments, audio, outputContainer);
        }

        var subtitle = tracks.FirstOrDefault(track => track.Kind == "subtitle");
        if (subtitle is not null)
        {
            arguments.AddRange(["-map", $"0:{subtitle.StreamIndex}"]);
            AddSubtitleCodecArguments(arguments, subtitle, outputContainer);
        }

        if (outputContainer is "mp4" or "mov")
        {
            arguments.AddRange(["-movflags", "+faststart"]);
        }
    }

    private static void AddVideoCodecArguments(
        List<string> arguments,
        TrackInfo video,
        string outputContainer)
    {
        if (outputContainer == "mkv")
        {
            arguments.AddRange(["-c:v:0", "copy"]);
            return;
        }

        var targetCodec = outputContainer switch
        {
            "webm" => "vp9",
            "avi" => "mpeg4",
            _ => "h264",
        };
        if (video.Codec.Equals(targetCodec, StringComparison.OrdinalIgnoreCase))
        {
            arguments.AddRange(["-c:v:0", "copy"]);
            return;
        }

        switch (targetCodec)
        {
            case "vp9":
                arguments.AddRange([
                    "-c:v:0", "libvpx-vp9",
                    "-pix_fmt:v:0", "yuv420p",
                    "-crf:v:0", "30",
                    "-b:v:0", "0",
                ]);
                break;
            case "mpeg4":
                arguments.AddRange([
                    "-c:v:0", "mpeg4",
                    "-pix_fmt:v:0", "yuv420p",
                    "-q:v:0", "5",
                ]);
                break;
            default:
                arguments.AddRange(["-c:v:0", "libx264", "-pix_fmt:v:0", "yuv420p"]);
                break;
        }
    }

    private static void AddAudioCodecArguments(
        List<string> arguments,
        TrackInfo audio,
        string outputContainer)
    {
        if (outputContainer == "mkv")
        {
            arguments.AddRange(["-c:a:0", "copy"]);
            return;
        }

        var targetCodec = outputContainer switch
        {
            "webm" => "opus",
            "avi" => "mp3",
            _ => "aac",
        };
        if (audio.Codec.Equals(targetCodec, StringComparison.OrdinalIgnoreCase))
        {
            arguments.AddRange(["-c:a:0", "copy"]);
            return;
        }

        arguments.AddRange(targetCodec switch
        {
            "opus" => ["-c:a:0", "libopus", "-b:a:0", "160k"],
            "mp3" => ["-c:a:0", "libmp3lame", "-b:a:0", "192k"],
            _ => ["-c:a:0", "aac", "-b:a:0", "192k"],
        });
    }

    private static void AddSubtitleCodecArguments(
        List<string> arguments,
        TrackInfo subtitle,
        string outputContainer)
    {
        switch (outputContainer)
        {
            case "mkv" when IsMp4SubtitleCodec(subtitle.Codec):
                arguments.AddRange(["-c:s:0", "srt"]);
                break;
            case "mkv":
                arguments.AddRange(["-c:s:0", "copy"]);
                break;
            case "webm" when subtitle.Codec.Equals("webvtt", StringComparison.OrdinalIgnoreCase):
                arguments.AddRange(["-c:s:0", "copy"]);
                break;
            case "webm":
                arguments.AddRange(["-c:s:0", "webvtt"]);
                break;
            default:
                arguments.AddRange(IsMp4SubtitleCodec(subtitle.Codec)
                    ? ["-c:s:0", "copy"]
                    : ["-c:s:0", "mov_text"]);
                break;
        }
    }

    private static void AddAudioArguments(
        List<string> arguments,
        IReadOnlyList<TrackInfo> tracks)
    {
        var audio = tracks.FirstOrDefault(track => track.Kind == "audio");
        if (audio is null)
        {
            return;
        }

        arguments.AddRange(["-map", $"0:{audio.StreamIndex}"]);
        arguments.AddRange(audio.Codec.Equals("aac", StringComparison.OrdinalIgnoreCase)
            ? ["-c:a", "copy"]
            : ["-c:a", "aac", "-b:a", "192k"]);
    }

    private static bool IsMp4SubtitleCodec(string codec) =>
        codec.Equals("mov_text", StringComparison.OrdinalIgnoreCase)
        || codec.Equals("tx3g", StringComparison.OrdinalIgnoreCase);
}
