using System.IO;
using System.Windows;

namespace FFmpegGui.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        var inputFiles = e.Args.Where(File.Exists).ToArray();
        if (inputFiles.Length > 0)
        {
            await window.LoadInitialFilesAsync(inputFiles);
        }
    }
}
