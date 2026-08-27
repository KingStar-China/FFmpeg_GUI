using FFmpegGui.Core;

namespace FFmpegGui.Core.Tests;

[TestClass]
public sealed class AppSettingsStoreTests
{
    [TestMethod]
    public void SaveAndLoadWorkMode_RestoresBatchMode()
    {
        var settingsPath = CreateSettingsPath();
        try
        {
            var store = new AppSettingsStore(settingsPath);

            store.SaveWorkMode(WorkMode.Batch);

            Assert.AreEqual(WorkMode.Batch, new AppSettingsStore(settingsPath).LoadWorkMode());
        }
        finally
        {
            DeleteSettingsDirectory(settingsPath);
        }
    }

    [TestMethod]
    public void LoadWorkMode_InvalidSettingsFallsBackToSingleFile()
    {
        var settingsPath = CreateSettingsPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, "{ invalid json");

            Assert.AreEqual(
                WorkMode.SingleFile,
                new AppSettingsStore(settingsPath).LoadWorkMode());
        }
        finally
        {
            DeleteSettingsDirectory(settingsPath);
        }
    }

    private static string CreateSettingsPath() => Path.Combine(
        Path.GetTempPath(),
        $"FFmpegGui.Settings.Tests.{Guid.NewGuid():N}",
        "settings.json");

    private static void DeleteSettingsDirectory(string settingsPath)
    {
        var directory = Path.GetDirectoryName(settingsPath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
