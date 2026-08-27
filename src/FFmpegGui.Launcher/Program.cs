using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FFmpegGui.Launcher;

internal static class Program
{
    private const string ManagedApplicationName = "FFmpeg GUI.App.exe";
    private const string InstallerScriptName = "install_windows_native.ps1";
    private const uint MessageBoxYesNo = 0x00000004;
    private const uint MessageBoxIconWarning = 0x00000030;
    private const uint MessageBoxIconError = 0x00000010;
    private const int MessageBoxYes = 6;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBox(nint hWnd, string text, string caption, uint type);

    private static int Main(string[] args)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var managedApplicationPath = Path.Combine(baseDirectory, ManagedApplicationName);
        if (!File.Exists(managedApplicationPath))
        {
            ShowError(
                "找不到 FFmpeg GUI 主程序文件。请确认已完整解压发布包，不要只复制启动器。",
                "FFmpeg GUI");
            return 1;
        }

        if (HasDesktopRuntime())
        {
            return Launch(managedApplicationPath, baseDirectory, args);
        }

        var answer = MessageBox(
            0,
            "未检测到 .NET 10 Desktop Runtime。\n\n点击“是”立即安装，点击“否”退出。",
            "FFmpeg GUI 需要安装运行时",
            MessageBoxYesNo | MessageBoxIconWarning);
        if (answer != MessageBoxYes)
        {
            return 0;
        }

        var installerScriptPath = Path.Combine(baseDirectory, InstallerScriptName);
        if (!File.Exists(installerScriptPath))
        {
            ShowError(
                "找不到运行时安装脚本。请确认已完整解压发布包。",
                "FFmpeg GUI");
            return 1;
        }

        var installerExitCode = RunInstaller(installerScriptPath, baseDirectory);
        if (installerExitCode != 0)
        {
            ShowError(
                $".NET 10 Desktop Runtime 安装未完成（退出码 {installerExitCode}）。",
                "FFmpeg GUI");
            return installerExitCode;
        }

        if (!HasDesktopRuntime())
        {
            ShowError(
                "安装程序已结束，但仍未检测到 .NET 10 Desktop Runtime。",
                "FFmpeg GUI");
            return 1;
        }

        var installedDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "FFmpeg GUI");
        var installedApplicationPath = Path.Combine(installedDirectory, ManagedApplicationName);
        return File.Exists(installedApplicationPath)
            ? Launch(installedApplicationPath, installedDirectory, args)
            : Launch(managedApplicationPath, baseDirectory, args);
    }

    private static bool HasDesktopRuntime()
    {
        var roots = new List<string>();
        AddRuntimeRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddRuntimeRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        AddRuntimeRoot(roots, Environment.GetEnvironmentVariable("ProgramFiles(x86)"));

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (Directory.EnumerateDirectories(root, "10.*", SearchOption.TopDirectoryOnly).Any())
                {
                    return true;
                }
            }
            catch (IOException)
            {
                // Continue checking the other standard installation locations.
            }
            catch (UnauthorizedAccessException)
            {
                // Continue checking the other standard installation locations.
            }
        }

        return false;
    }

    private static void AddRuntimeRoot(ICollection<string> roots, string? parent)
    {
        if (!string.IsNullOrWhiteSpace(parent))
        {
            roots.Add(Path.Combine(
                parent,
                "dotnet",
                "shared",
                "Microsoft.WindowsDesktop.App"));
        }
    }

    private static int RunInstaller(string scriptPath, string workingDirectory)
    {
        var powershellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(powershellPath))
        {
            powershellPath = "powershell.exe";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = powershellPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-SourceDir");
        startInfo.ArgumentList.Add(workingDirectory);
        startInfo.ArgumentList.Add("-DesktopShortcut");

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return 1;
        }

        process.WaitForExit();
        return process.ExitCode;
    }

    private static int Launch(string applicationPath, string workingDirectory, IReadOnlyList<string> args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = applicationPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
        };
        foreach (var argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo) is null ? 1 : 0;
    }

    private static void ShowError(string text, string caption) =>
        MessageBox(0, text, caption, MessageBoxIconError);
}
