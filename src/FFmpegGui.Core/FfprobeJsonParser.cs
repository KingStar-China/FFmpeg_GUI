using System.Globalization;
using System.Text.Json;

namespace FFmpegGui.Core;

public static class FfprobeJsonParser
{
    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif" };

    public static MediaInfo Parse(string json, string inputPath, int sourceIndex)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var root = document.RootElement;
        var tracks = new List<TrackInfo>();

        if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (var stream in streams.EnumerateArray())
            {
                tracks.Add(ParseTrack(stream, inputPath, sourceIndex));
            }
        }

        if (ImageExtensions.Contains(Path.GetExtension(inputPath))
            && tracks.Count == 1
            && tracks[0].Kind == "video")
        {
            var track = tracks[0];
            tracks[0] = track with
            {
                IsSupported = true,
                SupportNote = null,
                Disposition = track.Disposition with { IsAttachedPicture = true },
            };
        }

        var formatName = "unknown";
        double? duration = null;
        long? size = null;
        if (root.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.Object)
        {
            formatName = GetString(format, "format_name") ?? "unknown";
            duration = ParseDouble(GetString(format, "duration"));
            size = ParseLong(GetString(format, "size"));
        }

        return new MediaInfo(
            inputPath,
            Path.GetFileName(inputPath),
            formatName,
            duration,
            size,
            tracks);
    }

    private static TrackInfo ParseTrack(JsonElement stream, string inputPath, int sourceIndex)
    {
        var codecType = GetString(stream, "codec_type") ?? "unknown";
        var kind = codecType is "video" or "audio" or "subtitle" or "data" or "attachment"
            ? codecType
            : "unknown";
        var supported = kind is "video" or "audio" or "subtitle";
        var streamIndex = GetInt32(stream, "index") ?? -1;
        var tags = stream.TryGetProperty("tags", out var tagElement) ? tagElement : default;
        var disposition = stream.TryGetProperty("disposition", out var dispositionElement)
            ? dispositionElement
            : default;

        return new TrackInfo(
            $"{sourceIndex}:{streamIndex}",
            sourceIndex,
            inputPath,
            Path.GetFileName(inputPath),
            streamIndex,
            kind,
            GetString(stream, "codec_name") ?? "unknown",
            GetString(stream, "codec_long_name"),
            tags.ValueKind == JsonValueKind.Object ? GetString(tags, "language") : null,
            tags.ValueKind == JsonValueKind.Object ? GetString(tags, "title") : null,
            supported,
            supported ? null : "v1 不支持此类轨道",
            new TrackDisposition(
                IsDefault: GetInt32(disposition, "default") == 1,
                IsForced: GetInt32(disposition, "forced") == 1,
                IsHearingImpaired: GetInt32(disposition, "hearing_impaired") == 1,
                IsVisualImpaired: GetInt32(disposition, "visual_impaired") == 1,
                IsAttachedPicture: GetInt32(disposition, "attached_pic") == 1));
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static int? GetInt32(JsonElement element, string propertyName)
    {
        var value = GetString(element, propertyName);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static long? ParseLong(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? (long)result : null;
}
