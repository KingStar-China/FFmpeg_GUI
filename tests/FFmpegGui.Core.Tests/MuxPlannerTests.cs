using FFmpegGui.Core;

namespace FFmpegGui.Core.Tests;

[TestClass]
public sealed class MuxPlannerTests
{
    [TestMethod]
    public void Validate_RejectsMultipleVideoTracks()
    {
        var tracks = new[]
        {
            TestTracks.Create("video", "h264", 0),
            TestTracks.Create("video", "hevc", 1),
        };

        var issues = MuxPlanner.Validate(tracks, "mkv");

        CollectionAssert.Contains(issues.ToList(), "封装模式下最多只能勾选 1 条视频轨。");
    }

    [TestMethod]
    public void Validate_RejectsImageSubtitleInMp4()
    {
        var subtitle = TestTracks.Create("subtitle", "hdmv_pgs_subtitle", 3);

        var issues = MuxPlanner.Validate([subtitle], "mp4");

        Assert.HasCount(1, issues);
        StringAssert.Contains(issues[0], "MP4 不支持当前字幕轨");
    }

    [TestMethod]
    public void BuildDefaultOutputPath_UsesM4aForSingleAacTrack()
    {
        var audio = TestTracks.Create("audio", "aac", 1);

        var outputPath = MuxPlanner.BuildDefaultOutputPath([TestTracks.Media(audio)], "mp4", [audio]);

        Assert.EndsWith(".m4a", outputPath, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void BuildDefaultOutputPath_NeverOverwritesInputWithSameContainer()
    {
        var video = TestTracks.Create("video", "h264");

        var outputPath = MuxPlanner.BuildDefaultOutputPath([TestTracks.Media(video)], "mkv", [video]);

        Assert.AreEqual(@"C:\Media\source.muxed.mkv", outputPath);
        Assert.IsTrue(MuxPlanner.IsOutputPathDistinct([TestTracks.Media(video)], outputPath));
        Assert.IsFalse(MuxPlanner.IsOutputPathDistinct([TestTracks.Media(video)], @"C:\Media\source.mkv"));
    }

    [TestMethod]
    public void BuildArguments_PreservesSelectedTrackOrderAndConvertsMp4Subtitles()
    {
        var audio = TestTracks.Create("audio", "aac", 2, 1, sourcePath: @"C:\Media\audio.mka");
        var video = TestTracks.Create("video", "h264", 0);
        var subtitle = TestTracks.Create("subtitle", "ass", 3);
        var media = new[]
        {
            TestTracks.Media(video, subtitle),
            new MediaInfo(@"C:\Media\audio.mka", "audio.mka", "matroska", 10, 1_000, [audio]),
        };

        var arguments = MuxPlanner.BuildArguments(media, [audio, video, subtitle], "mp4", @"C:\Out\result.mp4");

        var maps = arguments
            .Select((value, index) => (value, index))
            .Where(item => item.value == "-map")
            .Select(item => arguments[item.index + 1])
            .ToArray();
        CollectionAssert.AreEqual(new[] { "1:2", "0:0", "0:3" }, maps);
        CollectionAssert.Contains(arguments.ToList(), "mov_text");
    }
}
