namespace FFmpegGui.Infrastructure;

public sealed class ToolLocator
{
    private readonly string _baseDirectory;

    public ToolLocator(string? baseDirectory = null)
    {
        _baseDirectory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
    }

    public string? FindFfmpeg() => FindExecutable("ffmpeg.exe");

    public string? FindFfprobe() => FindExecutable("ffprobe.exe");

    public string? FindMkvExtract()
    {
        var candidates = BuildBundledCandidates("mkvextract.exe").ToList();
        AddPathCandidate(candidates, "mkvextract.exe");

        foreach (var environmentName in new[] { "ProgramFiles", "ProgramFiles(x86)" })
        {
            var programFiles = Environment.GetEnvironmentVariable(environmentName);
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                candidates.Add(Path.Combine(programFiles, "MKVToolNix", "mkvextract.exe"));
            }
        }

        return FirstExisting(candidates);
    }

    public string RequireFfmpeg() => FindFfmpeg()
        ?? throw new ToolNotFoundException("未找到 ffmpeg，可将 ffmpeg.exe 放入程序 tools 目录或配置到系统 PATH。");

    public string RequireFfprobe() => FindFfprobe()
        ?? throw new ToolNotFoundException("未找到 ffprobe，可将 ffprobe.exe 放入程序 tools 目录或配置到系统 PATH。");

    public string DescribeAvailability()
    {
        var ffmpeg = (FindFfmpeg() is not null, FindFfprobe() is not null) switch
        {
            (true, true) => "FFmpeg / FFprobe 已就绪",
            (false, true) => "缺少 FFmpeg",
            (true, false) => "缺少 FFprobe",
            _ => "缺少 FFmpeg / FFprobe",
        };
        var mkvExtract = FindMkvExtract() is not null ? "MKVToolNix 已就绪" : "未找到 mkvextract";
        return $"{ffmpeg} · {mkvExtract}";
    }

    private string? FindExecutable(string executableName)
    {
        var candidates = BuildBundledCandidates(executableName).ToList();
        AddPathCandidate(candidates, executableName);
        return FirstExisting(candidates);
    }

    private IEnumerable<string> BuildBundledCandidates(string executableName)
    {
        yield return Path.Combine(_baseDirectory, "tools", executableName);
        yield return Path.Combine(_baseDirectory, executableName);
        yield return Path.Combine(Environment.CurrentDirectory, "tools", executableName);
    }

    private static void AddPathCandidate(ICollection<string> candidates, string executableName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var normalizedDirectory = directory.Trim().Trim('"');
            if (normalizedDirectory.Length > 0)
            {
                candidates.Add(Path.Combine(normalizedDirectory, executableName));
            }
        }
    }

    private static string? FirstExisting(IEnumerable<string> candidates)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(candidate);
            }
            catch (Exception) when (candidate.Length > 0)
            {
                continue;
            }

            if (seen.Add(fullPath) && File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }
}

public sealed class ToolNotFoundException(string message) : InvalidOperationException(message);
