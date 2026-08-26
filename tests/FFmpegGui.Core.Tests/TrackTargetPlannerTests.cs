using FFmpegGui.Core;

namespace FFmpegGui.Core.Tests;

[TestClass]
public sealed class TrackTargetPlannerTests
{
    [TestMethod]
    public void ListTargets_IncludesCopyAndVideoTranscodeChoices()
    {
        var track = TestTracks.Create("video", "h264");

        var targets = TrackTargetPlanner.ListTargets(track);

        Assert.IsTrue(targets.Count >= 2);
        StringAssert.Contains(targets[0].Label, "不转码");
        Assert.IsTrue(targets.Any(target => target.Id == "video-mp4-h264"));
        StringAssert.Contains(targets.Single(target => target.Id == "video-mp4-h264").Label, "H.264");
    }

    [TestMethod]
    public void ListTargets_AudioIncludesCommonConversionFormats()
    {
        var track = TestTracks.Create("audio", "aac");

        var targets = TrackTargetPlanner.ListTargets(track);

        CollectionAssert.IsSubsetOf(
            new[] { "audio-m4a", "audio-mp3", "audio-aac", "audio-wav", "audio-flac", "audio-opus" },
            targets.Select(target => target.Id).ToArray());
    }

    [TestMethod]
    public void Classify_UsesTargetCodecToChooseExtractOrConvert()
    {
        var track = TestTracks.Create("video", "h264");
        var copy = TrackTargetPlanner.ListTargets(track).First(target => target.Mode == "copy");
        var transcode = TrackTargetPlanner.ListTargets(track).Single(target => target.Id == "video-mp4-h264");

        Assert.AreEqual(
            SingleFileOperation.Extract,
            TrackTargetPlanner.Classify([track], new Dictionary<string, OutputTarget>
            {
                [track.TrackKey] = copy,
            }));
        Assert.AreEqual(
            SingleFileOperation.Convert,
            TrackTargetPlanner.Classify([track], new Dictionary<string, OutputTarget>
            {
                [track.TrackKey] = transcode,
            }));
    }

    [TestMethod]
    public void Classify_UsesMuxAndAudioMixForMultipleTracks()
    {
        var video = TestTracks.Create("video", "h264");
        var audio = TestTracks.Create("audio", "aac", 1);
        var voice = TestTracks.Create("audio", "aac", 0, 0, sourcePath: @"C:\Media\voice.m4a");
        var music = TestTracks.Create("audio", "aac", 0, 1, sourcePath: @"C:\Media\music.m4a");
        var videoTargets = new Dictionary<string, OutputTarget>();

        Assert.AreEqual(
            SingleFileOperation.Mux,
            TrackTargetPlanner.Classify([video, audio], videoTargets));
        Assert.AreEqual(
            SingleFileOperation.AudioMix,
            TrackTargetPlanner.Classify([voice, music], videoTargets));
    }
}
