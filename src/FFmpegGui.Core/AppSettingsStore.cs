using System.Text.Json;

namespace FFmpegGui.Core;

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _settingsPath;

    public AppSettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FFmpeg GUI",
            "settings.json");
    }

    public WorkMode LoadWorkMode()
    {
        if (!File.Exists(_settingsPath))
        {
            return WorkMode.SingleFile;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<SettingsPayload>(
                File.ReadAllText(_settingsPath),
                JsonOptions);
            return Enum.TryParse<WorkMode>(payload?.WorkMode, ignoreCase: true, out var mode)
                && Enum.IsDefined(mode)
                    ? mode
                    : WorkMode.SingleFile;
        }
        catch (Exception error) when (error is IOException
                                      or UnauthorizedAccessException
                                      or JsonException
                                      or NotSupportedException)
        {
            return WorkMode.SingleFile;
        }
    }

    public void SaveWorkMode(WorkMode mode)
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var payload = new SettingsPayload(mode.ToString());
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(payload, JsonOptions));
        }
        catch (Exception error) when (error is IOException
                                      or UnauthorizedAccessException
                                      or NotSupportedException)
        {
            // 设置保存失败不应阻止应用退出或模式切换。
        }
    }

    private sealed record SettingsPayload(string WorkMode);
}
