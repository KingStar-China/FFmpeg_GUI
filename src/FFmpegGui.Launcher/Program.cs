using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FFmpegGui.Launcher;

internal static class Program
{
    private const string ManagedAssemblyName = "FFmpeg GUI.App.dll";
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
        var managedAssemblyPath = Path.Combine(baseDirectory, ManagedAssemblyName);
        if (!File.Exists(managedAssemblyPath))
        {
            ShowError(
                "找不到 FFmpeg GUI 主程序文件。请确认已完整解压发布包，不要只复制入口 EXE。",
                "FFmpeg GUI");
            return 1;
        }

        if (HasDesktopRuntime())
        {
            return LaunchManaged(managedAssemblyPath, baseDirectory, args);
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
        var installedAssemblyPath = Path.Combine(installedDirectory, ManagedAssemblyName);
        return File.Exists(installedAssemblyPath)
            ? LaunchManaged(installedAssemblyPath, installedDirectory, args)
            : LaunchManaged(managedAssemblyPath, baseDirectory, args);
    }

    private static bool HasDesktopRuntime()
    {
        var dotnetPath = FindDotnetHostPath();
        if (dotnetPath is not null)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = dotnetPath,
                    WorkingDirectory = AppContext.BaseDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                startInfo.ArgumentList.Add("--list-runtimes");

                using var process = Process.Start(startInfo);
                if (process is not null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode == 0 && output.Contains(
                            "Microsoft.WindowsDesktop.App 10.",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // Fall back to the standard runtime directories below.
            }
        }

        var roots = new List<string>();
        AddRuntimeRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddRuntimeRoot(roots, Environment.GetEnvironmentVariable("ProgramW6432"));
        AddRuntimeRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        AddRuntimeRoot(
            roots,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft"));
        AddRuntimeRoot(
            roots,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".dotnet"));

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

    private static string? FindDotnetHostPath()
    {
        var candidates = new List<string>();
        AddDotnetHostCandidate(candidates, Environment.GetEnvironmentVariable("DOTNET_ROOT_X64"));
        AddDotnetHostCandidate(candidates, Environment.GetEnvironmentVariable("DOTNET_ROOT"));
        AddDotnetInstallRootCandidate(candidates, Environment.GetEnvironmentVariable("ProgramW6432"));
        AddDotnetInstallRootCandidate(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddDotnetHostCandidate(
            candidates,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "dotnet"));
        AddDotnetHostCandidate(
            candidates,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "dotnet"));
        AddDotnetHostCandidate(
            candidates,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".dotnet"));

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var pathEntry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                AddDotnetHostCandidate(candidates, pathEntry);
            }
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);
    }

    private static void AddDotnetHostCandidate(ICollection<string> candidates, string? root)
    {
        if (!string.IsNullOrWhiteSpace(root))
        {
            candidates.Add(Path.Combine(root, "dotnet.exe"));
        }
    }

    private static void AddDotnetInstallRootCandidate(ICollection<string> candidates, string? root)
    {
        if (!string.IsNullOrWhiteSpace(root))
        {
            candidates.Add(Path.Combine(root, "dotnet", "dotnet.exe"));
        }
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

    private static int LaunchManaged(string assemblyPath, string workingDirectory, IReadOnlyList<string> args)
    {
        var dotnetPath = FindDotnetHostPath();
        if (dotnetPath is null)
        {
            ShowError(
                "找不到 dotnet.exe，无法启动 FFmpeg GUI。请重新安装 .NET 10 Desktop Runtime。",
                "FFmpeg GUI");
            return 1;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = dotnetPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(assemblyPath);
        foreach (var argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            return Process.Start(startInfo) is null ? 1 : 0;
        }
        catch (Exception exception)
        {
            ShowError($"启动 FFmpeg GUI 失败：{exception.Message}", "FFmpeg GUI");
            return 1;
        }
    }

    private static void ShowError(string text, string caption) =>
        MessageBox(0, text, caption, MessageBoxIconError);
}
