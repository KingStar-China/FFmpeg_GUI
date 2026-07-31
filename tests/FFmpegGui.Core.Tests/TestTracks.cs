using FFmpegGui.Core;

namespace FFmpegGui.Core.Tests;

internal static class TestTracks
{
    public static TrackInfo Create(
        string kind,
        string codec,
        int streamIndex = 0,
        int sourceIndex = 0,
        bool isCover = false,
        string sourcePath = @"C:\Media\source.mkv") =>
        new(
            $"{sourceIndex}:{streamIndex}",
            sourceIndex,
            sourcePath,
            Path.GetFileName(sourcePath),
            streamIndex,
            kind,
            codec,
            null,
            null,
            null,
            true,
            null,
            new TrackDisposition(IsAttachedPicture: isCover));

    public static MediaInfo Media(params TrackInfo[] tracks) =>
        new(@"C:\Media\source.mkv", "source.mkv", "matroska", 10, 1_000, tracks);
}
