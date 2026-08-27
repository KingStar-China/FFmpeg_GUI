namespace FFmpegGui.Core;

public sealed record InputFileSelection(
    IReadOnlyList<string> Paths,
    int DuplicateCount);

public static class InputFilePlanner
{
    public static InputFileSelection SelectDistinctExistingFiles(
        IEnumerable<string> requestedPaths,
        IEnumerable<string>? existingPaths = null)
    {
        ArgumentNullException.ThrowIfNull(requestedPaths);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (existingPaths is not null)
        {
            foreach (var path in existingPaths)
            {
                if (TryNormalize(path, out var normalized))
                {
                    seen.Add(normalized);
                }
            }
        }

        var paths = new List<string>();
        var duplicateCount = 0;
        foreach (var path in requestedPaths)
        {
            if (!File.Exists(path) || !TryNormalize(path, out var normalized))
            {
                continue;
            }

            if (!seen.Add(normalized))
            {
                duplicateCount++;
                continue;
            }

            paths.Add(normalized);
        }

        return new InputFileSelection(paths, duplicateCount);
    }

    private static bool TryNormalize(string path, out string normalized)
    {
        try
        {
            normalized = Path.GetFullPath(path);
            return true;
        }
        catch (Exception error) when (error is ArgumentException
                                      or NotSupportedException
                                      or PathTooLongException)
        {
            normalized = string.Empty;
            return false;
        }
    }
}
