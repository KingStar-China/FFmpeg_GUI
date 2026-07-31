using FFmpegGui.Core;

namespace FFmpegGui.Core.Tests;

[TestClass]
public sealed class FfprobeJsonParserTests
{
    [TestMethod]
    public void Parse_MapsStreamsTagsAndDisposition()
    {
        const string json = """
            {
              "streams": [
                {
                  "index": 0,
                  "codec_name": "h264",
                  "codec_long_name": "H.264",
                  "codec_type": "video",
                  "disposition": { "default": 1, "attached_pic": 0 },
                  "tags": { "language": "jpn", "title": "Main" }
                }
              ],
              "format": {
                "format_name": "matroska,webm",
                "duration": "12.500000",
                "size": "4096"
              }
            }
            """;

        var media = FfprobeJsonParser.Parse(json, @"C:\Media\movie.mkv", 2);

        Assert.AreEqual("matroska,webm", media.FormatName);
        Assert.AreEqual(12.5, media.DurationSeconds);
        Assert.AreEqual(4096, media.SizeBytes);
        var track = media.Tracks.Single();
        Assert.AreEqual("2:0", track.TrackKey);
        Assert.AreEqual("jpn", track.Language);
        Assert.AreEqual("Main", track.Title);
        Assert.IsTrue(track.Disposition.IsDefault);
    }

    [TestMethod]
    public void Parse_MarksStandaloneImageAsCover()
    {
        const string json = """
            {
              "streams": [
                { "index": 0, "codec_name": "png", "codec_type": "video" }
              ],
              "format": { "format_name": "png_pipe" }
            }
            """;

        var media = FfprobeJsonParser.Parse(json, @"C:\Media\cover.png", 0);

        Assert.IsTrue(media.Tracks.Single().IsCover);
        Assert.AreEqual("封面图", media.Tracks.Single().KindLabel);
    }
}
