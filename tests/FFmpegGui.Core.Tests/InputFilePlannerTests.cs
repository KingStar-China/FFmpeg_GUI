using FFmpegGui.Core;

namespace FFmpegGui.Core.Tests;

[TestClass]
public sealed class InputFilePlannerTests
{
    [TestMethod]
    public void SelectDistinctExistingFiles_RemovesAlreadyLoadedAndRepeatedPaths()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"FFmpegGui_Input_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var existing = Path.Combine(directory, "existing.mp4");
            var fresh = Path.Combine(directory, "fresh.mp4");
            File.WriteAllText(existing, "existing");
            File.WriteAllText(fresh, "fresh");

            var selection = InputFilePlanner.SelectDistinctExistingFiles(
                [existing, existing, fresh, fresh],
                [existing]);

            Assert.HasCount(1, selection.Paths);
            Assert.AreEqual(Path.GetFullPath(fresh), selection.Paths[0]);
            Assert.AreEqual(3, selection.DuplicateCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SelectDistinctExistingFiles_IgnoresMissingFiles()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mp4");

        var selection = InputFilePlanner.SelectDistinctExistingFiles([missing, missing]);

        Assert.HasCount(0, selection.Paths);
        Assert.AreEqual(0, selection.DuplicateCount);
    }
}
