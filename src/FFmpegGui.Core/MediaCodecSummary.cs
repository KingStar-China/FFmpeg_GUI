namespace FFmpegGui.Core;

public static class MediaCodecSummary
{
    public static string Build(MediaInfo media)
    {
        var codecs = media.Tracks
            .Where(track => !track.IsCover && track.Kind is "video" or "audio" or "subtitle")
            .OrderBy(track => track.StreamIndex)
            .ThenBy(track => track.TrackKey, StringComparer.Ordinal)
            .Select(track => DisplayCodec(track.Codec))
            .Where(codec => !string.IsNullOrWhiteSpace(codec))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return codecs.Length == 0 ? "未知编码" : string.Join(", ", codecs);
    }

    private static string DisplayCodec(string codec) =>
        codec.Trim().ToLowerInvariant() switch
        {
            "h264" => "H.264",
            "h265" or "hevc" => "H.265",
            "av1" => "AV1",
            "vp8" => "VP8",
            "vp9" => "VP9",
            "mpeg4" => "MPEG-4",
            "mpeg2video" => "MPEG-2",
            "mpeg1video" => "MPEG-1",
            "mjpeg" => "MJPEG",
            "aac" => "AAC",
            "mp3" => "MP3",
            "ac3" => "AC-3",
            "eac3" => "E-AC-3",
            "truehd" => "TrueHD",
            "dts" => "DTS",
            "flac" => "FLAC",
            "alac" => "ALAC",
            "opus" => "Opus",
            "vorbis" => "Vorbis",
            "subrip" or "srt" => "SRT",
            "ass" => "ASS",
            "ssa" => "SSA",
            "webvtt" => "WebVTT",
            "mov_text" or "tx3g" => "MOV_TEXT",
            "hdmv_pgs_subtitle" => "PGS",
            "dvd_subtitle" => "VobSub",
            "dvb_subtitle" => "DVB Subtitle",
            "xsub" => "XSUB",
            var value when value.StartsWith("pcm_", StringComparison.Ordinal) =>
                value.Replace('_', ' ').ToUpperInvariant(),
            var value => value.Replace('_', ' ').ToUpperInvariant(),
        };
}
