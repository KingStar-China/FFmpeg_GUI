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
    public void Validate_AllowsMultipleAudioTracksForM4aMix()
    {
        var tracks = new[]
        {
            TestTracks.Create("audio", "aac", 0, 0, sourcePath: @"C:\Media\voice.m4a"),
            TestTracks.Create("audio", "aac", 0, 1, sourcePath: @"C:\Media\music.m4a"),
        };

        var issues = MuxPlanner.Validate(tracks, "m4a");

        Assert.AreEqual(0, issues.Count);
    }

    [TestMethod]
    public void Validate_RejectsVideoForAudioMixOutput()
    {
        var tracks = new[]
        {
            TestTracks.Create("video", "h264"),
            TestTracks.Create("audio", "aac", 1),
        };

        var issues = MuxPlanner.Validate(tracks, "mp3");

        Assert.HasCount(1, issues);
        StringAssert.Contains(issues[0], "只能选择音频轨道");
    }

    [TestMethod]
    public void BuildDefaultOutputPath_UsesM4aForSingleAacTrack()
    {
        var audio = TestTracks.Create("audio", "aac", 1);

        var outputPath = MuxPlanner.BuildDefaultOutputPath([TestTracks.Media(audio)], "mp4", [audio]);

        Assert.EndsWith(".m4a", outputPath, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void BuildDefaultOutputPath_UsesM4aForMultipleAudioTracks()
    {
        var voice = TestTracks.Create("audio", "aac", 0, sourcePath: @"C:\Media\voice.m4a");
        var music = TestTracks.Create("audio", "aac", 0, 1, sourcePath: @"C:\Media\music.m4a");
        var media = new[]
        {
            TestTracks.Media(voice),
            new MediaInfo(@"C:\Media\music.m4a", "music.m4a", "mov,mp4,m4a,3gp,3g2,mj2", 10, 1_000, [music]),
        };

        var outputPath = MuxPlanner.BuildDefaultOutputPath(media, "mp4", [voice, music]);

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

    [TestMethod]
    public void BuildArguments_MixesMultipleAudioTracksIntoSingleM4aStream()
    {
        var voice = TestTracks.Create("audio", "aac", 0, 0, sourcePath: @"C:\Media\voice.m4a");
        var music = TestTracks.Create("audio", "aac", 0, 1, sourcePath: @"C:\Media\music.m4a");
        var media = new[]
        {
            TestTracks.Media(voice),
            new MediaInfo(@"C:\Media\music.m4a", "music.m4a", "mov,mp4,m4a,3gp,3g2,mj2", 10, 1_000, [music]),
        };

        var arguments = MuxPlanner.BuildArguments(media, [voice, music], "m4a", @"C:\Out\mix.m4a");

        var filterIndex = Array.IndexOf(arguments.ToArray(), "-filter_complex");
        Assert.IsTrue(filterIndex >= 0);
        StringAssert.Contains(arguments[filterIndex + 1], "amix=inputs=2");
        StringAssert.Contains(arguments[filterIndex + 1], "[0:0][1:0]");
        CollectionAssert.Contains(arguments.ToList(), "[audio_mix]");
        CollectionAssert.Contains(arguments.ToList(), "aac");
        Assert.AreEqual(1, arguments.Count(value => value == "-map"));
    }

    [TestMethod]
    public void BuildArguments_AppliesPerTrackTargetCodecForMux()
    {
        var video = TestTracks.Create("video", "h264");
        var audio = TestTracks.Create("audio", "aac", 1);
        var media = new[] { TestTracks.Media(video, audio) };
        var target = ExtractPlanner.ListConvertTargets(video).Single(item => item.Id == "video-mp4-h264");

        var arguments = MuxPlanner.BuildArguments(
            media,
            [video, audio],
            "mp4",
            @"C:\Out\result.mp4",
            new Dictionary<string, OutputTarget>
            {
                [video.TrackKey] = target,
            });

        var codecIndex = Array.IndexOf(arguments.ToArray(), "-c:v:0");
        Assert.IsTrue(codecIndex >= 0);
        Assert.AreEqual("libx264", arguments[codecIndex + 1]);
        CollectionAssert.Contains(arguments.ToList(), "-c:a");
        CollectionAssert.Contains(arguments.ToList(), "copy");
    }
}
