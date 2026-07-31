using FFmpegGui.Core;

namespace FFmpegGui.Infrastructure;

public sealed class InvocationFactory(ToolLocator toolLocator)
{
    public ProcessInvocation CreateMux(
        IReadOnlyList<MediaInfo> media,
        IReadOnlyList<TrackInfo> selectedTracks,
        string outputContainer,
        string outputPath) =>
        new(
            toolLocator.RequireFfmpeg(),
            MuxPlanner.BuildArguments(media, selectedTracks, outputContainer, outputPath));

    public ProcessInvocation CreateExtract(
        TrackInfo track,
        OutputTarget target,
        string outputPath)
    {
        var mkvExtract = toolLocator.FindMkvExtract();
        if (mkvExtract is not null && ExtractPlanner.ShouldUseMkvExtract(track, target))
        {
            return new ProcessInvocation(
                mkvExtract,
                ExtractPlanner.BuildMkvExtractArguments(track, outputPath));
        }

        return new ProcessInvocation(
            toolLocator.RequireFfmpeg(),
            ExtractPlanner.BuildFfmpegArguments(track, target, outputPath));
    }
}
