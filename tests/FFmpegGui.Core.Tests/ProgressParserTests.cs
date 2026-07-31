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
}
