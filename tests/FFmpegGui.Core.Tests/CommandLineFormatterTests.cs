using FFmpegGui.Core;

namespace FFmpegGui.Core.Tests;

[TestClass]
public sealed class CommandLineFormatterTests
{
    [TestMethod]
    public void Format_QuotesPathsContainingSpaces()
    {
        var command = CommandLineFormatter.Format(
            @"C:\Program Files\ffmpeg\ffmpeg.exe",
            ["-i", @"C:\My Media\source.mkv", "out.mkv"]);

        Assert.AreEqual(
            "\"C:\\Program Files\\ffmpeg\\ffmpeg.exe\" -i \"C:\\My Media\\source.mkv\" out.mkv",
            command);
    }
}
