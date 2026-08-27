namespace FFmpegGui.Core;

public static class TrackTargetPlanner
{
    public static IReadOnlyList<OutputTarget> ListTargets(TrackInfo track)
    {
        var targets = new List<OutputTarget>();
        var defaultTarget = ExtractPlanner.ListExtractTargets(track).FirstOrDefault();
        if (defaultTarget is not null)
        {
            var label = defaultTarget.Mode == "copy"
                ? $"不转码（复制） / {FormatCodec(track.Codec)}"
                : FormatTargetLabel(defaultTarget);
            targets.Add(defaultTarget with { Label = label });
        }

        foreach (var target in ExtractPlanner.ListConvertTargets(track))
        {
            targets.Add(target with { Label = FormatTargetLabel(target) });
        }

        return targets;
    }

    public static SingleFileOperation Classify(
        IReadOnlyList<TrackInfo> selectedTracks,
        IReadOnlyDictionary<string, OutputTarget> targetByTrackKey)
    {
        if (selectedTracks.Count == 0)
        {
            return SingleFileOperation.None;
        }

        if (selectedTracks.Count > 1
            && selectedTracks.All(track => track.Kind == "audio" && !track.IsCover))
        {
            return SingleFileOperation.AudioMix;
        }

        if (selectedTracks.Count == 1
            && targetByTrackKey.TryGetValue(selectedTracks[0].TrackKey, out var target)
            && target.Mode == "transcode"
            && !selectedTracks[0].IsCover)
        {
            return SingleFileOperation.Convert;
        }

        return selectedTracks.Count == 1
            ? SingleFileOperation.Extract
            : SingleFileOperation.Mux;
    }

    public static string OperationLabel(SingleFileOperation operation) => operation switch
    {
        SingleFileOperation.Extract => "提取",
        SingleFileOperation.Convert => "转换",
        SingleFileOperation.Mux => "封装",
        SingleFileOperation.AudioMix => "混音",
        _ => "处理",
    };

    private static string FormatTargetLabel(OutputTarget target) => target.Id switch
    {
        "video-mp4-h264" => "H.264（MP4）",
        "video-webm-vp9" => "VP9（WebM）",
        "video-avi-mpeg4" => "MPEG-4（AVI）",
        "audio-m4a" => "AAC（M4A）",
        "audio-mp3" => "MP3",
        "audio-aac" => "AAC（.aac）",
        "audio-flac" => "FLAC",
        "audio-wav" => "PCM（WAV）",
        "audio-opus" => "Opus",
        "sub-mp4-mov-text" => "MOV_TEXT（MP4）",
        "sub-srt" => "SRT",
        "sub-ass" => "ASS",
        "sub-vtt" => "WebVTT",
        "cover-png" or "cover-default-png" => "PNG",
        "cover-jpg" or "cover-default-jpg" => "JPEG",
        "cover-webp" or "cover-default-webp" => "WebP",
        _ => target.Label
            .Replace("转换为 ", string.Empty, StringComparison.Ordinal)
            .Replace("默认输出（", string.Empty, StringComparison.Ordinal)
            .Replace("）", string.Empty, StringComparison.Ordinal)
            .Replace(" (*.png)", string.Empty, StringComparison.Ordinal)
            .Replace(" (*.jpg)", string.Empty, StringComparison.Ordinal)
            .Replace(" (*.webp)", string.Empty, StringComparison.Ordinal),
    };

    private static string FormatCodec(string codec) =>
        string.IsNullOrWhiteSpace(codec)
            ? "未知编码"
            : codec.Trim().ToLowerInvariant() switch
            {
                "h264" or "avc1" => "H.264",
                "hevc" or "h265" => "H.265",
                "mpeg4" => "MPEG-4",
                "vp8" => "VP8",
                "vp9" => "VP9",
                "av1" => "AV1",
                "aac" => "AAC",
                "mp3" => "MP3",
                "flac" => "FLAC",
                "opus" => "Opus",
                "vorbis" => "Vorbis",
                "pcm_s16le" => "PCM S16LE",
                "pcm_s24le" => "PCM S24LE",
                "pcm_s32le" => "PCM S32LE",
                var value => value.ToUpperInvariant(),
            };
}
