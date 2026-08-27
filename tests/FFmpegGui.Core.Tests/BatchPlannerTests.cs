using FFmpegGui.Core;

namespace FFmpegGui.Core.Tests;

[TestClass]
public sealed class BatchPlannerTests
{
    [TestMethod]
    public void ListOutputPresets_VideoUsesMp4AsDefaultAndLabelsEveryCodecPair()
    {
        var presets = BatchPlanner.ListOutputPresets(BatchMediaKind.Video);

        CollectionAssert.AreEqual(
            new[]
            {
                "MP4（H.264 + AAC）",
                "MKV（保留原编码）",
                "WebM（VP9 + Opus）",
                "MOV（H.264 + AAC）",
                "AVI（MPEG-4 + MP3）",
            },
            presets.Select(item => item.Label).ToArray());
        Assert.AreEqual("mp4", BatchPlanner.OutputContainer(BatchMediaKind.Video));
    }

    [TestMethod]
    public void GetMediaKind_ClassifiesByFileInsteadOfExposingTracks()
    {
        var video = TestTracks.Media(
            TestTracks.Create("audio", "aac", 1),
            TestTracks.Create("video", "h264", 0));
        var audio = TestTracks.Media(TestTracks.Create("audio", "flac", 0));
        var subtitle = TestTracks.Media(TestTracks.Create("subtitle", "subrip", 0));

        Assert.AreEqual(BatchMediaKind.Video, BatchPlanner.GetMediaKind(video));
        Assert.AreEqual(BatchMediaKind.Audio, BatchPlanner.GetMediaKind(audio));
        Assert.IsNull(BatchPlanner.GetMediaKind(subtitle));
    }

    [TestMethod]
    public void SelectOutputTracks_VideoUsesDefaultVideoAudioAndTextSubtitle()
    {
        var media = new MediaInfo(
            @"C:\Media\movie.mkv",
            "movie.mkv",
            "matroska",
            10,
            1_000,
            [
                TestTracks.Create("video", "hevc", 0, 4, sourcePath: @"C:\Media\movie.mkv"),
                TestTracks.Create("audio", "mp3", 1, 4, isDefault: true, sourcePath: @"C:\Media\movie.mkv"),
                TestTracks.Create("video", "h264", 2, 4, isDefault: true, sourcePath: @"C:\Media\movie.mkv"),
                TestTracks.Create("audio", "aac", 3, 4, sourcePath: @"C:\Media\movie.mkv"),
                TestTracks.Create("subtitle", "subrip", 4, 4, isDefault: true, sourcePath: @"C:\Media\movie.mkv"),
                TestTracks.Create("subtitle", "ass", 5, 4, sourcePath: @"C:\Media\movie.mkv"),
            ]);

        var tracks = BatchPlanner.SelectOutputTracks(media, BatchMediaKind.Video);

        Assert.HasCount(3, tracks);
        Assert.AreEqual(2, tracks[0].StreamIndex);
        Assert.AreEqual(1, tracks[1].StreamIndex);
        Assert.AreEqual(4, tracks[2].StreamIndex);
        Assert.IsTrue(tracks.All(track => track.SourceIndex == 0));
    }

    [TestMethod]
    public void BuildArguments_CopiesCompatibleMp4Streams()
    {
        var media = TestTracks.Media(
            TestTracks.Create("video", "h264", 0),
            TestTracks.Create("audio", "aac", 1),
            TestTracks.Create("subtitle", "mov_text", 2));

        var arguments = BatchPlanner.BuildArguments(
            media,
            BatchMediaKind.Video,
            "mp4",
            @"C:\Out\movie.mp4");

        AssertArgumentValue(arguments, "-c:v:0", "copy");
        AssertArgumentValue(arguments, "-c:a:0", "copy");
        AssertArgumentValue(arguments, "-c:s:0", "copy");
        CollectionAssert.DoesNotContain(arguments.ToList(), "libx264");
    }

    [TestMethod]
    public void BuildArguments_TranscodesOnlyIncompatibleMp4Streams()
    {
        var media = TestTracks.Media(
            TestTracks.Create("video", "hevc", 0),
            TestTracks.Create("audio", "flac", 1),
            TestTracks.Create("subtitle", "ass", 2));

        var arguments = BatchPlanner.BuildArguments(
            media,
            BatchMediaKind.Video,
            "mp4",
            @"C:\Out\movie.mp4");

        AssertArgumentValue(arguments, "-c:v:0", "libx264");
        AssertArgumentValue(arguments, "-c:a:0", "aac");
        AssertArgumentValue(arguments, "-c:s:0", "mov_text");
    }

    [TestMethod]
    public void SelectOutputTracks_SkipsBitmapSubtitle()
    {
        var media = TestTracks.Media(
            TestTracks.Create("video", "h264", 0),
            TestTracks.Create("audio", "aac", 1),
            TestTracks.Create("subtitle", "hdmv_pgs_subtitle", 2, isDefault: true));

        var tracks = BatchPlanner.SelectOutputTracks(media, BatchMediaKind.Video);

        Assert.IsFalse(tracks.Any(track => track.Kind == "subtitle"));
    }

    [TestMethod]
    public void BuildArguments_MkvKeepsOriginalCodecsAndDefaultBitmapSubtitle()
    {
        var media = TestTracks.Media(
            TestTracks.Create("video", "hevc", 0),
            TestTracks.Create("audio", "flac", 1),
            TestTracks.Create("subtitle", "hdmv_pgs_subtitle", 2, isDefault: true));

        var arguments = BatchPlanner.BuildArguments(
            media,
            BatchMediaKind.Video,
            "mkv",
            @"C:\Out\movie.mkv");

        AssertArgumentValue(arguments, "-c:v:0", "copy");
        AssertArgumentValue(arguments, "-c:a:0", "copy");
        AssertArgumentValue(arguments, "-c:s:0", "copy");
    }

    [TestMethod]
    public void BuildArguments_WebmUsesVp9OpusAndWebVtt()
    {
        var media = TestTracks.Media(
            TestTracks.Create("video", "h264", 0),
            TestTracks.Create("audio", "aac", 1),
            TestTracks.Create("subtitle", "subrip", 2));

        var arguments = BatchPlanner.BuildArguments(
            media,
            BatchMediaKind.Video,
            "webm",
            @"C:\Out\movie.webm");

        AssertArgumentValue(arguments, "-c:v:0", "libvpx-vp9");
        AssertArgumentValue(arguments, "-c:a:0", "libopus");
        AssertArgumentValue(arguments, "-c:s:0", "webvtt");
    }

    [TestMethod]
    public void BuildArguments_AviUsesMpeg4Mp3AndSkipsSubtitle()
    {
        var media = TestTracks.Media(
            TestTracks.Create("video", "h264", 0),
            TestTracks.Create("audio", "aac", 1),
            TestTracks.Create("subtitle", "subrip", 2));

        var arguments = BatchPlanner.BuildArguments(
            media,
            BatchMediaKind.Video,
            "avi",
            @"C:\Out\movie.avi");

        AssertArgumentValue(arguments, "-c:v:0", "mpeg4");
        AssertArgumentValue(arguments, "-c:a:0", "libmp3lame");
        CollectionAssert.DoesNotContain(arguments.ToList(), "-c:s:0");
    }

    [TestMethod]
    public void SelectOutputTracks_AudioProducesSingleAacM4aJob()
    {
        var media = new MediaInfo(
            @"C:\Media\song.flac",
            "song.flac",
            "flac",
            10,
            1_000,
            [TestTracks.Create("audio", "flac", 0, 7, sourcePath: @"C:\Media\song.flac")]);

        var tracks = BatchPlanner.SelectOutputTracks(media, BatchMediaKind.Audio);
        var arguments = BatchPlanner.BuildArguments(
            media,
            BatchMediaKind.Audio,
            "m4a",
            @"C:\Out\song.m4a");

        Assert.HasCount(1, tracks);
        Assert.AreEqual(0, tracks[0].SourceIndex);
        CollectionAssert.Contains(arguments.ToList(), "0:0");
        CollectionAssert.Contains(arguments.ToList(), "aac");
        Assert.AreEqual("M4A（AAC）", BatchPlanner.OutputLabel(BatchMediaKind.Audio));
    }

    [TestMethod]
    public void BuildOutputPath_NeverOverwritesAnInputOrAnotherBatchOutput()
    {
        var media = new MediaInfo(
            @"C:\Out\clip.mp4",
            "clip.mp4",
            "mov,mp4,m4a,3gp,3g2,mj2",
            10,
            1_000,
            [TestTracks.Create("video", "h264", sourcePath: @"C:\Out\clip.mp4")]);
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(media.InputPath),
        };

        var first = BatchPlanner.BuildOutputPath(
            media,
            BatchMediaKind.Video,
            "mp4",
            @"C:\Out",
            reserved);
        var second = BatchPlanner.BuildOutputPath(
            media,
            BatchMediaKind.Video,
            "mp4",
            @"C:\Out",
            reserved);

        Assert.AreEqual(@"C:\Out\clip.batch.mp4", first);
        Assert.AreEqual(@"C:\Out\clip.batch2.mp4", second);
    }

    private static void AssertArgumentValue(
        IReadOnlyList<string> arguments,
        string argument,
        string expectedValue)
    {
        var index = Array.IndexOf(arguments.ToArray(), argument);
        Assert.IsTrue(index >= 0, $"缺少参数 {argument}");
        Assert.AreEqual(expectedValue, arguments[index + 1]);
    }
}
