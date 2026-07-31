using FFmpegGui.Core;

namespace FFmpegGui.Core.Tests;

[TestClass]
public sealed class ExtractPlannerTests
{
    [TestMethod]
    public void ListExtractTargets_PreservesRawPgsSubtitle()
    {
        var track = TestTracks.Create("subtitle", "hdmv_pgs_subtitle", 4);

        var target = ExtractPlanner.ListExtractTargets(track).Single();

        Assert.AreEqual("sup", target.Extension);
        Assert.AreEqual("copy", target.Mode);
        Assert.IsTrue(ExtractPlanner.ShouldUseMkvExtract(track, target));
    }

    [TestMethod]
    public void ShouldUseMkvExtract_RejectsNonMatroskaInput()
    {
        var track = TestTracks.Create(
            "subtitle",
            "subrip",
            2,
            sourcePath: @"C:\Media\source.mp4");
        var target = ExtractPlanner.ListExtractTargets(track).Single();

        Assert.IsFalse(ExtractPlanner.ShouldUseMkvExtract(track, target));
    }

    [TestMethod]
    public void BuildFfmpegArguments_AddsImageOutputFlagsForCover()
    {
        var track = TestTracks.Create("video", "mjpeg", isCover: true);
        var target = ExtractPlanner.ListExtractTargets(track).Single();

        var arguments = ExtractPlanner.BuildFfmpegArguments(track, target, @"C:\Out\cover.jpg");

        CollectionAssert.Contains(arguments.ToList(), "-frames:v");
        CollectionAssert.Contains(arguments.ToList(), "-update");
        CollectionAssert.Contains(arguments.ToList(), "image2");
    }

    [TestMethod]
    public void CommonConvertTargets_ReturnsOnlyTargetsSharedBySameGroup()
    {
        var aac = TestTracks.Create("audio", "aac", 1);
        var flac = TestTracks.Create("audio", "flac", 2);

        var targets = ExtractPlanner.CommonConvertTargets([aac, flac]);

        Assert.HasCount(6, targets);
        Assert.IsTrue(targets.All(target => target.Id.StartsWith("audio-", StringComparison.Ordinal)));
        Assert.IsEmpty(ExtractPlanner.CommonConvertTargets([aac, TestTracks.Create("video", "h264")]));
    }

    [TestMethod]
    public void BuildOutputPath_IncludesSourceAndTrackIdentity()
    {
        var track = TestTracks.Create("audio", "aac", 2, 1, sourcePath: @"C:\Media\movie.mkv");
        var target = ExtractPlanner.ListExtractTargets(track).Single();

        var outputPath = ExtractPlanner.BuildOutputPath(track, target);

        Assert.AreEqual(@"C:\Media\movie.src2.音频.aac.track2.aac", outputPath);
    }
}
