namespace FFmpegGui.Core;

public static class CommandLineFormatter
{
    public static string Format(string program, IReadOnlyList<string> arguments) =>
        string.Join(" ", new[] { program }.Concat(arguments).Select(Quote));

    private static string Quote(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        return value.Any(char.IsWhiteSpace) || value.Contains('"')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
    }
}
