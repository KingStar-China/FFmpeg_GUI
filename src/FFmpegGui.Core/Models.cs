namespace FFmpegGui.Core;

public enum WorkMode
{
    SingleFile,
    Batch,
}

public enum SingleFileOperation
{
    None,
    Extract,
    Convert,
    Mux,
    AudioMix,
}

public sealed record TrackDisposition(
    bool IsDefault = false,
    bool IsForced = false,
    bool IsHearingImpaired = false,
    bool IsVisualImpaired = false,
    bool IsAttachedPicture = false)
{
    public string Label
    {
        get
        {
            var labels = new List<string>();
            if (IsDefault) labels.Add("default");
            if (IsForced) labels.Add("forced");
            if (IsHearingImpaired) labels.Add("hearing");
            if (IsVisualImpaired) labels.Add("visual");
            if (IsAttachedPicture) labels.Add("attached_pic");
            return labels.Count == 0 ? "-" : string.Join(" / ", labels);
        }
    }
}

public sealed record TrackInfo(
    string TrackKey,
    int SourceIndex,
    string SourcePath,
    string SourceFileName,
    int StreamIndex,
    string Kind,
    string Codec,
    string? CodecLongName,
    string? Language,
    string? Title,
    bool IsSupported,
    string? SupportNote,
    TrackDisposition Disposition)
{
    public bool IsCover => Disposition.IsAttachedPicture;

    public string KindLabel => IsCover
        ? "封面图"
        : Kind switch
        {
            "video" => "视频",
            "audio" => "音频",
            "subtitle" => "字幕",
            "data" => "数据",
            "attachment" => "附件",
            "chapter" => "章节",
            _ => "未知",
        };

    public string ConvertGroup => IsCover ? "cover" : Kind;
}

public sealed record MediaInfo(
    string InputPath,
    string FileName,
    string FormatName,
    double? DurationSeconds,
    long? SizeBytes,
    IReadOnlyList<TrackInfo> Tracks);

public sealed record OutputTarget(
    string Id,
    string Label,
    string Extension,
    string Mode,
    IReadOnlyList<string> CodecArguments)
{
    public static OutputTarget Copy(string id, string label, string extension) =>
        new(id, label, extension, "copy", []);

    public static OutputTarget Transcode(
        string id,
        string label,
        string extension,
        params string[] codecArguments) =>
        new(id, label, extension, "transcode", codecArguments);
}

public sealed record ProcessInvocation(string Program, IReadOnlyList<string> Arguments);

public sealed record MediaJob(
    ProcessInvocation Invocation,
    string OutputPath,
    bool IsCoverExtraction,
    long DurationMilliseconds);
