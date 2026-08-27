using FFmpegGui.Core;

namespace FFmpegGui.Core.Tests;

[TestClass]
public sealed class MediaCodecSummaryTests
{
    [TestMethod]
    public void Build_ListsVideoAudioAndSubtitleCodecsOnce()
    {
        var media = TestTracks.Media(
            TestTracks.Create("video", "h264", 0),
            TestTracks.Create("audio", "aac", 1),
            TestTracks.Create("audio", "aac", 2),
            TestTracks.Create("subtitle", "subrip", 3),
            TestTracks.Create("video", "mjpeg", 4, isCover: true),
            TestTracks.Create("data", "bin_data", 5));

        Assert.AreEqual("H.264, AAC, SRT", MediaCodecSummary.Build(media));
    }

    [TestMethod]
    public void Build_UsesReadableNamesForAdditionalCodecsAndBitmapSubtitles()
    {
        var media = TestTracks.Media(
            TestTracks.Create("video", "hevc", 0),
            TestTracks.Create("audio", "eac3", 1),
            TestTracks.Create("subtitle", "hdmv_pgs_subtitle", 2));

        Assert.AreEqual("H.265, E-AC-3, PGS", MediaCodecSummary.Build(media));
    }

    [TestMethod]
    public void Build_NoMediaTracksReturnsUnknownCodec()
    {
        var media = TestTracks.Media(TestTracks.Create("attachment", "ttf", 0));

        Assert.AreEqual("未知编码", MediaCodecSummary.Build(media));
    }
}
