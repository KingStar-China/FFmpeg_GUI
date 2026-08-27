using FFmpegGui.Core;

namespace FFmpegGui.Core.Tests;

[TestClass]
public sealed class BatchPlannerTests
{
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
    public void SelectOutputTracks_VideoUsesDefaultVideoAndAudioAndNormalizesInputIndex()
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
            ]);

        var tracks = BatchPlanner.SelectOutputTracks(media, BatchMediaKind.Video);
        var arguments = MuxPlanner.BuildArguments([media], tracks, "mp4", @"C:\Out\movie.mp4");

        Assert.HasCount(2, tracks);
        Assert.AreEqual(2, tracks[0].StreamIndex);
        Assert.AreEqual(1, tracks[1].StreamIndex);
        Assert.IsTrue(tracks.All(track => track.SourceIndex == 0));
        CollectionAssert.Contains(arguments.ToList(), "0:2");
        CollectionAssert.Contains(arguments.ToList(), "0:1");
        CollectionAssert.Contains(arguments.ToList(), "libx264");
        CollectionAssert.Contains(arguments.ToList(), "aac");
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
        var arguments = MuxPlanner.BuildArguments([media], tracks, "m4a", @"C:\Out\song.m4a");

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

        var first = BatchPlanner.BuildOutputPath(media, BatchMediaKind.Video, @"C:\Out", reserved);
        var second = BatchPlanner.BuildOutputPath(media, BatchMediaKind.Video, @"C:\Out", reserved);

        Assert.AreEqual(@"C:\Out\clip.batch.mp4", first);
        Assert.AreEqual(@"C:\Out\clip.batch2.mp4", second);
    }
}
