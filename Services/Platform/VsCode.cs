using System.Diagnostics;
using System.IO;

namespace DevPilot.Services.Platform;

/// <summary>
/// Opens folders in VS Code connected to WSL (the WSL extension's remote mode).
/// </summary>
public static class VsCode
{
    private static readonly Lazy<string?> Launcher = new(FindLauncher);

    public static bool IsInstalled => Launcher.Value is not null;

    /// <summary>Opens a Linux folder of a distribution in VS Code.</summary>
    public static void OpenInWsl(string distro, string linuxPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Launcher.Value ?? throw new InvalidOperationException("VS Code was not found."),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--remote");
        startInfo.ArgumentList.Add($"wsl+{distro}");
        startInfo.ArgumentList.Add(linuxPath);
        Process.Start(startInfo);
    }

    /// <summary>
    /// Finds VS Code's command-line launcher (code.cmd) on the PATH.
    /// </summary>
    private static string? FindLauncher()
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        return path?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(dir => Path.Combine(dir, "code.cmd"))
            .FirstOrDefault(File.Exists);
    }
}
