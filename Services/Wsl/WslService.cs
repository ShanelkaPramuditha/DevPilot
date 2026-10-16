using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DevPilot.Models;
using Microsoft.Win32;

namespace DevPilot.Services.Wsl;

/// <summary>
/// Wraps wsl.exe commands.
/// </summary>
public static partial class WslService
{
    /// <summary>
    /// Path to wsl.exe. A 32-bit process is redirected to SysWOW64,
    /// which has no wsl.exe, so use Sysnative in that case.
    /// </summary>
    public static string WslPath
    {
        get
        {
            string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string folder = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                ? "Sysnative"
                : "System32";
            return Path.Combine(windir, folder, "wsl.exe");
        }
    }

    /// <summary>
    /// Runs wsl.exe hidden and captures its output.
    /// </summary>
    public static Task<WslResult> RunAsync(TimeSpan? timeout, params string[] args) =>
        RunCoreAsync(args, standardInput: null, timeout);

    private static async Task<WslResult> RunCoreAsync(IEnumerable<string> args, string? standardInput, TimeSpan? timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = WslPath,
            UseShellExecute = false,
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        if (standardInput is not null)
        {
            startInfo.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }

        // Ask WSL for UTF-8 output. Older versions ignore this and write
        // UTF-16, which is handled by stripping the null characters below.
        startInfo.Environment["WSL_UTF8"] = "1";

        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start wsl.exe.");

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (standardInput is not null)
        {
            // Linux shells need LF line endings.
            await process.StandardInput.WriteAsync(standardInput.Replace("\r\n", "\n"));
            process.StandardInput.Close();
        }

        using var cts = timeout is null ? new CancellationTokenSource() : new CancellationTokenSource(timeout.Value);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            throw new TimeoutException($"wsl.exe {string.Join(' ', args)} did not respond in time.");
        }

        string output = (await stdout).Replace("\0", "").Trim();
        string error = (await stderr).Replace("\0", "").Trim();
        return new WslResult(process.ExitCode, (output + "\n" + error).Trim()) { StandardOutput = output };
    }

    /// <summary>
    /// Runs a shell script inside a distribution. The script is sent on
    /// standard input so it needs no quoting; values are passed as $1, $2...
    /// Throws with the script's output when it fails.
    /// </summary>
    public static async Task<string> RunScriptAsync(string distro, string script, bool asRoot, params string[] args)
    {
        var wslArgs = new List<string> { "--distribution", distro };
        if (asRoot)
        {
            wslArgs.AddRange(["--user", "root"]);
        }

        wslArgs.AddRange(["--exec", "sh", "-s", "--"]);
        wslArgs.AddRange(args);

        var result = await RunCoreAsync(wslArgs, script, TimeSpan.FromMinutes(2));
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(result.Output)
                    ? $"The command failed with exit code {result.ExitCode}."
                    : result.Output);
        }

        return result.StandardOutput;
    }

    public static Task<WslResult> RunAsync(params string[] args) => RunAsync(null, args);

    /// <summary>
    /// Runs wsl.exe and throws with its output when it fails.
    /// </summary>
    public static async Task<string> RunCheckedAsync(params string[] args)
    {
        var result = await RunAsync(args);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(result.Output)
                    ? $"wsl.exe exited with code {result.ExitCode}."
                    : result.Output);
        }

        return result.Output;
    }

    /// <summary>
    /// Opens an interactive terminal in a distribution. Arguments are passed
    /// through ArgumentList, which only quotes values that need it: wsl.exe
    /// exits immediately (code -1) for an interactive session when the
    /// distribution name is quoted, e.g. -d "Ubuntu".
    /// The process gets its own console window because this app has none.
    /// </summary>
    public static Process OpenTerminal(string distro, params string[] extraArgs)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = WslPath,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("--distribution");
        startInfo.ArgumentList.Add(distro);
        foreach (string arg in extraArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start wsl.exe.");
    }

    /// <summary>
    /// Starts a distribution without opening a window. WSL stops a
    /// distribution shortly after its last process exits, so a hidden
    /// "sleep infinity" keeps it running until it is stopped or WSL shuts down.
    /// Returns once the distribution reports Running.
    /// </summary>
    public static async Task StartInBackgroundAsync(string distro)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = WslPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string arg in new[] { "--distribution", distro, "--exec", "sleep", "infinity" })
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var keepAlive = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start wsl.exe.");

        for (int attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(500);

            if (keepAlive.HasExited)
            {
                throw new InvalidOperationException(
                    $"{distro} could not be started (wsl.exe exited with code {keepAlive.ExitCode}).");
            }

            var distros = await GetInstalledAsync();
            if (distros.Any(d => d.Name.Equals(distro, StringComparison.OrdinalIgnoreCase) && d.IsRunning))
            {
                return;
            }
        }

        throw new TimeoutException($"{distro} did not start within 15 seconds.");
    }

    /// <summary>
    /// Runs wsl.exe in a visible console window so the user can follow the
    /// progress and answer prompts. The window stays open on failure.
    /// </summary>
    public static Process StartInConsole(string arguments, bool elevated = false, bool alwaysPause = false)
    {
        string pause = alwaysPause ? "& pause" : "|| pause";
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {WslPath} {arguments} {pause}",
            UseShellExecute = true
        };

        if (elevated)
        {
            startInfo.Verb = "runas";
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the console window.");
    }

    /// <summary>
    /// Returns true when WSL is installed. Windows 11 ships a wsl.exe stub
    /// even when WSL is not installed, so the file existing is not enough.
    /// "wsl --version" works on the Store version of WSL; "wsl --status"
    /// covers the older inbox version.
    /// </summary>
    public static async Task<bool> IsInstalledAsync()
    {
        if (!File.Exists(WslPath))
        {
            return false;
        }

        try
        {
            var timeout = TimeSpan.FromSeconds(15);
            return (await RunAsync(timeout, "--version")).Succeeded
                || (await RunAsync(timeout, "--status")).Succeeded;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Lists installed distributions. Returns an empty list when none are
    /// installed (wsl.exe exits with an error in that case).
    /// </summary>
    public static async Task<List<WslDistro>> GetInstalledAsync()
    {
        var result = await RunAsync(TimeSpan.FromSeconds(30), "--list", "--verbose");
        var distros = new List<WslDistro>();

        if (!result.Succeeded)
        {
            return distros;
        }

        // First line is the header: NAME  STATE  VERSION
        foreach (string line in SplitLines(result.Output).Skip(1))
        {
            string trimmed = line.Trim();
            bool isDefault = trimmed.StartsWith('*');
            if (isDefault)
            {
                trimmed = trimmed[1..].Trim();
            }

            string[] parts = Whitespace().Split(trimmed);
            if (parts.Length < 3)
            {
                continue;
            }

            string name = string.Join(' ', parts[..^2]);
            var (location, diskBytes, vhdPath) = GetStorage(name);

            distros.Add(new WslDistro(name, State: parts[^2], Version: parts[^1], isDefault)
            {
                Location = location,
                DiskBytes = diskBytes,
                VhdPath = vhdPath
            });
        }

        return distros;
    }

    /// <summary>
    /// Finds where a distribution is stored and measures its virtual disk,
    /// using the registration WSL keeps in the registry.
    /// </summary>
    private static (string? Location, long? DiskBytes, string? VhdPath) GetStorage(string name)
    {
        try
        {
            using var lxss = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
            if (lxss is null)
            {
                return (null, null, null);
            }

            foreach (string id in lxss.GetSubKeyNames())
            {
                using var key = lxss.OpenSubKey(id);
                if (key?.GetValue("DistributionName") is not string distroName
                    || !distroName.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (key.GetValue("BasePath") is not string basePath)
                {
                    return (null, null, null);
                }

                if (basePath.StartsWith(@"\\?\", StringComparison.Ordinal))
                {
                    basePath = basePath[4..];
                }

                string vhd = Path.Combine(basePath, key.GetValue("VhdFileName") as string ?? "ext4.vhdx");
                bool exists = File.Exists(vhd);
                return (basePath, exists ? new FileInfo(vhd).Length : null, exists ? vhd : null);
            }
        }
        catch
        {
            // Storage details are informational only.
        }

        return (null, null, null);
    }

    /// <summary>
    /// Lists distributions available from the online catalog.
    /// </summary>
    public static async Task<List<(string Name, string FriendlyName)>> GetOnlineAsync()
    {
        string output = await RunCheckedAsync("--list", "--online");
        var distros = new List<(string, string)>();
        bool inTable = false;

        foreach (string line in SplitLines(output))
        {
            // The table header ("NAME   FRIENDLY NAME") is the first line
            // with columns separated by several spaces; the intro text is not.
            if (!inTable)
            {
                inTable = TableHeader().IsMatch(line);
                continue;
            }

            var match = TableRow().Match(line.Trim());
            if (match.Success)
            {
                distros.Add((match.Groups[1].Value, match.Groups[2].Value.Trim()));
            }
        }

        return distros;
    }

    private static IEnumerable<string> SplitLines(string text) =>
        text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l));

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^\S+\s{2,}\S")]
    private static partial Regex TableHeader();

    [GeneratedRegex(@"^(\S+)\s+(.+)$")]
    private static partial Regex TableRow();
}
