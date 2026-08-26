using System.Text;

namespace FFmpegGui.Core;

public static class ExtractPlanner
{
    private static readonly IReadOnlyDictionary<string, string> AudioCopyExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["aac"] = "aac",
            ["mp3"] = "mp3",
            ["flac"] = "flac",
            ["ac3"] = "ac3",
            ["eac3"] = "eac3",
            ["opus"] = "opus",
            ["vorbis"] = "ogg",
        };

    private static readonly IReadOnlyDictionary<string, string> TextSubtitleExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ass"] = "ass",
            ["ssa"] = "ass",
            ["subrip"] = "srt",
            ["srt"] = "srt",
            ["webvtt"] = "vtt",
            ["mov_text"] = "srt",
            ["text"] = "srt",
            ["tx3g"] = "srt",
        };

    private static readonly HashSet<string> ImageSubtitleCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "hdmv_pgs_subtitle", "dvd_subtitle", "xsub", "dvb_subtitle",
    };

    private static readonly IReadOnlyDictionary<string, (string Extension, string Label)> RawSubtitleExtensions =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["ass"] = ("ass", "原始格式（ASS） (*.ass)"),
            ["ssa"] = ("ass", "原始格式（ASS/SSA） (*.ass)"),
            ["subrip"] = ("srt", "原始格式（SRT） (*.srt)"),
            ["srt"] = ("srt", "原始格式（SRT） (*.srt)"),
            ["webvtt"] = ("vtt", "原始格式（WebVTT） (*.vtt)"),
            ["hdmv_pgs_subtitle"] = ("sup", "原始格式（SUP） (*.sup)"),
        };

    private static readonly HashSet<string> MatroskaExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mkv", ".mka", ".mks", ".mk3d" };

    private static readonly HashSet<string> RawSubtitleOutputExtensions =
        new(StringComparer.OrdinalIgnoreCase) { "ass", "srt", "vtt", "sup" };

    public static IReadOnlyList<string> Validate(IReadOnlyList<TrackInfo> selectedTracks, WorkMode mode) =>
        selectedTracks.Count > 0
            ? []
            : [mode == WorkMode.Batch
                ? "批量模式暂未开放。"
                : "单文件模式下至少要勾选 1 条轨道。"];

    public static IReadOnlyList<OutputTarget> ListExtractTargets(TrackInfo? track)
    {
        if (track is null)
        {
            return [];
        }

        var codec = track.Codec.Trim().ToLowerInvariant();
        if (track.IsCover)
        {
            return [DefaultCoverTarget(codec)];
        }

        return track.Kind switch
        {
            "audio" => [DefaultAudioTarget(codec)],
            "subtitle" => [RawSubtitleTarget(codec) ?? DefaultSubtitleTarget(codec)],
            "video" => [DefaultVideoTarget(codec)],
            _ => [],
        };
    }

    public static IReadOnlyList<OutputTarget> ListConvertTargets(TrackInfo? track)
    {
        if (track is null)
        {
            return [];
        }

        var codec = track.Codec.Trim().ToLowerInvariant();
        if (track.IsCover)
        {
            return
            [
                OutputTarget.Transcode("cover-png", "转换为 PNG (*.png)", "png", "-c:v", "png", "-frames:v", "1"),
                OutputTarget.Transcode("cover-jpg", "转换为 JPG (*.jpg)", "jpg", "-c:v", "mjpeg", "-frames:v", "1"),
                OutputTarget.Transcode("cover-webp", "转换为 WebP (*.webp)", "webp", "-c:v", "libwebp", "-frames:v", "1"),
            ];
        }

        if (track.Kind == "audio")
        {
            return
            [
                OutputTarget.Transcode("audio-m4a", "转换为 M4A (*.m4a)", "m4a", "-c:a", "aac"),
                OutputTarget.Transcode("audio-mp3", "转换为 MP3 (*.mp3)", "mp3", "-c:a", "libmp3lame"),
                OutputTarget.Transcode("audio-aac", "转换为 AAC (*.aac)", "aac", "-c:a", "aac"),
                OutputTarget.Transcode("audio-flac", "转换为 FLAC (*.flac)", "flac", "-c:a", "flac"),
                OutputTarget.Transcode("audio-wav", "转换为 WAV (*.wav)", "wav", "-c:a", "pcm_s16le"),
                OutputTarget.Transcode("audio-opus", "转换为 Opus (*.opus)", "opus", "-c:a", "libopus"),
            ];
        }

        if (track.Kind == "subtitle")
        {
            if (ImageSubtitleCodecs.Contains(codec))
            {
                return [];
            }

            return
            [
                OutputTarget.Transcode("sub-srt", "转换为 SRT (*.srt)", "srt", "-c:s", "srt"),
                OutputTarget.Transcode("sub-ass", "转换为 ASS (*.ass)", "ass", "-c:s", "ass"),
                OutputTarget.Transcode("sub-vtt", "转换为 WebVTT (*.vtt)", "vtt", "-c:s", "webvtt"),
            ];
        }

        if (track.Kind == "video")
        {
            return
            [
                OutputTarget.Transcode(
                    "video-mp4-h264", "转换为 MP4 (H.264) (*.mp4)", "mp4",
                    "-c:v", "libx264", "-pix_fmt", "yuv420p", "-movflags", "+faststart"),
                OutputTarget.Transcode(
                    "video-webm-vp9", "转换为 WebM (VP9) (*.webm)", "webm",
                    "-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0"),
                OutputTarget.Transcode(
                    "video-avi-mpeg4", "转换为 AVI (MPEG-4) (*.avi)", "avi",
                    "-c:v", "mpeg4", "-q:v", "5"),
            ];
        }

        return [];
    }

    public static IReadOnlyList<OutputTarget> CommonConvertTargets(IReadOnlyList<TrackInfo> tracks)
    {
        if (tracks.Count == 0 || tracks.Any(track => track.ConvertGroup != tracks[0].ConvertGroup))
        {
            return [];
        }

        var first = ListConvertTargets(tracks[0]);
        var commonIds = first.Select(target => target.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var track in tracks.Skip(1))
        {
            commonIds.IntersectWith(ListConvertTargets(track).Select(target => target.Id));
        }

        return first.Where(target => commonIds.Contains(target.Id)).ToArray();
    }

    public static string BuildOutputPath(TrackInfo track, OutputTarget target)
    {
        var sourceDirectory = Path.GetDirectoryName(track.SourcePath) ?? string.Empty;
        var sourceStem = Path.GetFileNameWithoutExtension(track.SourcePath);
        var kind = SanitizeName(track.KindLabel);
        var codec = SanitizeName(string.IsNullOrWhiteSpace(track.Codec) ? "unknown" : track.Codec);
        var fileName = $"{sourceStem}.src{track.SourceIndex + 1}.{kind}.{codec}.track{track.StreamIndex}.{target.Extension}";
        return Path.Combine(sourceDirectory, fileName);
    }

    public static string BuildOutputPathInDirectory(
        TrackInfo track,
        OutputTarget target,
        string outputDirectory) =>
        Path.Combine(outputDirectory, Path.GetFileName(BuildOutputPath(track, target)));

    public static IReadOnlyList<string> BuildFfmpegArguments(
        TrackInfo track,
        OutputTarget target,
        string outputPath)
    {
        var arguments = new List<string>
        {
            "-y", "-nostdin", "-progress", "pipe:1", "-nostats",
            "-i", track.SourcePath,
            "-map", $"0:{track.StreamIndex}",
            "-map_metadata", "-1",
            "-map_chapters", "-1",
        };

        if (target.Mode == "copy")
        {
            arguments.AddRange(["-c", "copy"]);
        }
        else
        {
            arguments.AddRange(target.CodecArguments);
        }

        if (track.IsCover && target.Extension is "png" or "jpg" or "webp")
        {
            if (!arguments.Contains("-frames:v", StringComparer.Ordinal))
            {
                arguments.AddRange(["-frames:v", "1"]);
            }

            arguments.AddRange(["-f", "image2", "-update", "1"]);
        }

        arguments.Add(outputPath);
        return arguments;
    }

    public static bool ShouldUseMkvExtract(TrackInfo track, OutputTarget target) =>
        track.Kind == "subtitle"
        && target.Mode == "copy"
        && RawSubtitleOutputExtensions.Contains(target.Extension)
        && MatroskaExtensions.Contains(Path.GetExtension(track.SourcePath));

    public static IReadOnlyList<string> BuildMkvExtractArguments(
        TrackInfo track,
        string outputPath) =>
        ["tracks", track.SourcePath, $"{track.StreamIndex}:{outputPath}"];

    private static OutputTarget DefaultCoverTarget(string codec) => codec switch
    {
        "mjpeg" or "jpeg" or "jpg" => OutputTarget.Transcode(
            "cover-default-jpg", "默认输出（JPG） (*.jpg)", "jpg", "-c:v", "mjpeg", "-frames:v", "1"),
        "webp" => OutputTarget.Transcode(
            "cover-default-webp", "默认输出（WebP） (*.webp)", "webp", "-c:v", "libwebp", "-frames:v", "1"),
        _ => OutputTarget.Transcode(
            "cover-default-png", "默认输出（PNG） (*.png)", "png", "-c:v", "png", "-frames:v", "1"),
    };

    private static OutputTarget DefaultAudioTarget(string codec)
    {
        if (codec.StartsWith("pcm", StringComparison.OrdinalIgnoreCase))
        {
            return OutputTarget.Copy("audio-copy-wav", "默认输出（WAV） (*.wav)", "wav");
        }

        return AudioCopyExtensions.TryGetValue(codec, out var extension)
            ? OutputTarget.Copy($"audio-copy-{extension}", $"默认输出（原格式） (*.{extension})", extension)
            : OutputTarget.Copy("audio-copy-mka", "默认输出（安全回退 MKA） (*.mka)", "mka");
    }

    private static OutputTarget? RawSubtitleTarget(string codec) =>
        RawSubtitleExtensions.TryGetValue(codec, out var target)
            ? OutputTarget.Copy($"sub-raw-{target.Extension}", target.Label, target.Extension)
            : null;

    private static OutputTarget DefaultSubtitleTarget(string codec)
    {
        var rawTarget = RawSubtitleTarget(codec);
        if (rawTarget is not null)
        {
            return rawTarget;
        }

        if (!TextSubtitleExtensions.TryGetValue(codec, out var extension))
        {
            return OutputTarget.Copy("sub-default-mks", "默认输出（安全回退 MKS） (*.mks)", "mks");
        }

        return extension switch
        {
            "ass" when codec == "ass" => OutputTarget.Copy("sub-default-ass", "默认输出（ASS） (*.ass)", "ass"),
            "ass" => OutputTarget.Transcode("sub-default-ass", "默认输出（ASS） (*.ass)", "ass", "-c:s", "ass"),
            "srt" when codec is "subrip" or "srt" => OutputTarget.Copy("sub-default-srt", "默认输出（SRT） (*.srt)", "srt"),
            "srt" => OutputTarget.Transcode("sub-default-srt", "默认输出（SRT） (*.srt)", "srt", "-c:s", "srt"),
            "vtt" when codec == "webvtt" => OutputTarget.Copy("sub-default-vtt", "默认输出（WebVTT） (*.vtt)", "vtt"),
            _ => OutputTarget.Transcode("sub-default-vtt", "默认输出（WebVTT） (*.vtt)", "vtt", "-c:s", "webvtt"),
        };
    }

    private static OutputTarget DefaultVideoTarget(string codec) => codec switch
    {
        "h264" or "avc1" or "hevc" or "h265" =>
            OutputTarget.Copy("video-default-mp4-copy", "默认输出（MP4） (*.mp4)", "mp4"),
        "vp8" or "vp9" =>
            OutputTarget.Copy("video-default-webm-copy", "默认输出（WebM） (*.webm)", "webm"),
        "mpeg4" or "msmpeg4" or "msmpeg4v2" or "msmpeg4v3" or "xvid" or "divx" or "mjpeg" =>
            OutputTarget.Copy("video-default-avi-copy", "默认输出（AVI） (*.avi)", "avi"),
        "av1" => OutputTarget.Copy("video-default-mkv-av1", "默认输出（MKV） (*.mkv)", "mkv"),
        _ => OutputTarget.Copy("video-copy-mkv", "默认输出（单轨 MKV） (*.mkv)", "mkv"),
    };

    private static string SanitizeName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_');
        }

        var result = builder.ToString().Trim('_');
        return result.Length == 0 ? "unknown" : result;
    }
}
