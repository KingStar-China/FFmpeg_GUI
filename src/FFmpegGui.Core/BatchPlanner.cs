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
            return audio is null
                ? [NormalizeForSingleInput(video)]
                : [NormalizeForSingleInput(video), NormalizeForSingleInput(audio)];
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
}
