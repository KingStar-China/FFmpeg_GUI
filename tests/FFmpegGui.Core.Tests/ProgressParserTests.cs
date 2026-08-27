using FFmpegGui.Core;

namespace FFmpegGui.Core.Tests;

[TestClass]
public sealed class ProgressParserTests
{
    [TestMethod]
    public void Parse_FfmpegOutTime_ReturnsPercentage()
    {
        var progress = ProgressParser.Parse("out_time=00:00:05.000000", "ffmpeg", 10_000);

        Assert.AreEqual(50, progress);
    }

    [TestMethod]
    public void Parse_MkvExtractProgress_ReturnsPercentage()
    {
        var progress = ProgressParser.Parse("Progress: 73%", "mkvextract", 0);

        Assert.AreEqual(73, progress);
    }

    [TestMethod]
    public void Parse_ClampsRunningFfmpegToNinetyNine()
    {
        var progress = ProgressParser.Parse("out_time_us=15000000", "ffmpeg", 10_000);

        Assert.AreEqual(99, progress);
    }

    [TestMethod]
    public void Parse_FfmpegOutTimeMsTreatsValueAsMicrosecondsFromTheStart()
    {
        var progress = ProgressParser.Parse("out_time_ms=500000", "ffmpeg", 10_000);

        Assert.AreEqual(5, progress);
    }

    [TestMethod]
    public void IsFfmpegProgressProtocolLine_FiltersProtocolButKeepsNormalLogs()
    {
        Assert.IsTrue(ProgressParser.IsFfmpegProgressProtocolLine("out_time=00:00:05.000000", "ffmpeg"));
        Assert.IsTrue(ProgressParser.IsFfmpegProgressProtocolLine("progress=continue", "ffmpeg"));
        Assert.IsTrue(ProgressParser.IsFfmpegProgressProtocolLine("stream_0_3_q=23.0", "ffmpeg"));
        Assert.IsFalse(ProgressParser.IsFfmpegProgressProtocolLine("Error opening output file", "ffmpeg"));
        Assert.IsFalse(ProgressParser.IsFfmpegProgressProtocolLine("Progress: 50%", "mkvextract"));
    }
}
