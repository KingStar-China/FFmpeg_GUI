namespace FFmpegGui.Core;

public static class BatchPlanner
{
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

    public static IReadOnlyList<TrackInfo> SelectOutputTracks(
        MediaInfo media,
        BatchMediaKind kind)
    {
        if (kind == BatchMediaKind.Video)
        {
            var video = SelectPrimaryTrack(media.Tracks.Where(IsUsableVideo));
            if (video is null)
            {
                return [];
            }

            var audio = SelectPrimaryTrack(media.Tracks.Where(IsUsableAudio));
            var subtitle = SelectPrimaryTrack(media.Tracks.Where(IsUsableTextSubtitle));
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
        kind == BatchMediaKind.Video ? "mp4" : "m4a";

    public static string OutputLabel(BatchMediaKind kind) =>
        kind == BatchMediaKind.Video ? "MP4（H.264 + AAC）" : "M4A（AAC）";

    public static IReadOnlyList<string> BuildArguments(
        MediaInfo media,
        BatchMediaKind kind,
        string outputPath)
    {
        var tracks = SelectOutputTracks(media, kind);
        var arguments = new List<string>
        {
            "-y", "-nostdin", "-progress", "pipe:1", "-nostats", "-i", media.InputPath,
        };

        if (kind == BatchMediaKind.Video)
        {
            AddVideoArguments(arguments, tracks);
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
        string outputDirectory,
        ISet<string>? reservedPaths = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var extension = $".{OutputContainer(kind)}";
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

    private static bool IsUsableVideo(TrackInfo track) =>
        track.IsSupported && track.Kind == "video" && !track.IsCover;

    private static bool IsUsableAudio(TrackInfo track) =>
        track.IsSupported && track.Kind == "audio" && !track.IsCover;

    private static bool IsUsableTextSubtitle(TrackInfo track) =>
        track.IsSupported
        && track.Kind == "subtitle"
        && MuxPlanner.IsMp4TextSubtitle(track.Codec);

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
        IReadOnlyList<TrackInfo> tracks)
    {
        var video = tracks.FirstOrDefault(track => track.Kind == "video");
        if (video is null)
        {
            return;
        }

        arguments.AddRange(["-map", $"0:{video.StreamIndex}"]);
        if (video.Codec.Equals("h264", StringComparison.OrdinalIgnoreCase))
        {
            arguments.AddRange(["-c:v:0", "copy"]);
        }
        else
        {
            arguments.AddRange(["-c:v:0", "libx264", "-pix_fmt:v:0", "yuv420p"]);
        }

        var audio = tracks.FirstOrDefault(track => track.Kind == "audio");
        if (audio is not null)
        {
            arguments.AddRange(["-map", $"0:{audio.StreamIndex}"]);
            if (audio.Codec.Equals("aac", StringComparison.OrdinalIgnoreCase))
            {
                arguments.AddRange(["-c:a:0", "copy"]);
            }
            else
            {
                arguments.AddRange(["-c:a:0", "aac", "-b:a:0", "192k"]);
            }
        }

        var subtitle = tracks.FirstOrDefault(track => track.Kind == "subtitle");
        if (subtitle is not null)
        {
            arguments.AddRange(["-map", $"0:{subtitle.StreamIndex}"]);
            arguments.AddRange(IsMp4SubtitleCodec(subtitle.Codec)
                ? ["-c:s:0", "copy"]
                : ["-c:s:0", "mov_text"]);
        }

        arguments.AddRange(["-movflags", "+faststart"]);
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
