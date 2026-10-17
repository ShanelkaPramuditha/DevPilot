using System.Diagnostics;
using System.IO;
using System.Text;

namespace DevPilot.Services.Ssh;

/// <summary>
/// The SSH client config file and the Windows OpenSSH client (ssh.exe).
/// </summary>
public static class SshService
{
    /// <summary>Where OpenSSH looks by default: %UserProfile%\.ssh\config.</summary>
    public static string DefaultConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");

    /// <summary>The file to manage: the one the user chose, or the default.</summary>
    public static string ResolveConfigPath(string? chosenPath) =>
        string.IsNullOrWhiteSpace(chosenPath) ? DefaultConfigPath : chosenPath;

    public static bool IsDefaultConfig(string configPath) => PathsEqual(configPath, DefaultConfigPath);

    /// <summary>The Windows OpenSSH client, or null when the "OpenSSH Client" Windows feature is not installed.</summary>
    public static string? SshExe
    {
        get
        {
            string system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh.exe");
            if (File.Exists(system))
            {
                return system;
            }

            return Environment.GetEnvironmentVariable("PATH")?
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(dir => Path.Combine(dir, "ssh.exe"))
                .FirstOrDefault(File.Exists);
        }
    }

    public static SshConfigDocument Load(string path) =>
        SshConfigDocument.Parse(File.Exists(path) ? File.ReadAllText(path) : "");

    /// <summary>
    /// Saves the config, keeping the previous version as config.devpilot.bak. Written as
    /// UTF-8 without a byte-order mark, which OpenSSH would read as part of the first line.
    /// </summary>
    public static void Save(string path, SshConfigDocument document)
    {
        string? folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        if (File.Exists(path))
        {
            File.Copy(path, path + ".devpilot.bak", overwrite: true);
        }

        File.WriteAllText(path, document.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Opens an interactive SSH session in a new console window.</summary>
    public static void Connect(string alias, string configPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = SshExe ?? throw new InvalidOperationException(MissingClientMessage),
            // A console app started without shell execute gets its own window.
            UseShellExecute = false
        };
        AddConfigArguments(startInfo, configPath);
        startInfo.ArgumentList.Add(alias);
        Process.Start(startInfo);
    }

    /// <summary>
    /// Tries to log in without prompting (keys or agent only) and returns whether it
    /// worked, with SSH's own message when it did not.
    /// </summary>
    public static async Task<(bool Success, string Message)> TestAsync(string alias, string configPath, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = SshExe ?? throw new InvalidOperationException(MissingClientMessage),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true
        };
        AddConfigArguments(startInfo, configPath);
        foreach (string arg in new[] { "-o", "BatchMode=yes", "-o", "ConnectTimeout=10", alias, "exit" })
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start ssh.exe.");
        process.StandardInput.Close();
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        _ = process.StandardOutput.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            return (false, "No answer within 30 seconds.");
        }

        string message = (await errors).Trim();
        return process.ExitCode == 0
            ? (true, "Connected and logged in.")
            : (false, string.IsNullOrEmpty(message) ? $"ssh exited with code {process.ExitCode}." : message);
    }

    /// <summary>The command line to connect, for copying into a terminal.</summary>
    public static string CommandFor(string alias, string configPath) =>
        IsDefaultConfig(configPath) ? $"ssh {alias}" : $"ssh -F \"{configPath}\" {alias}";

    public const string MissingClientMessage =
        "The OpenSSH client (ssh.exe) was not found. Install it from Settings → System → Optional features → OpenSSH Client.";

    private static void AddConfigArguments(ProcessStartInfo startInfo, string configPath)
    {
        // The default file is used automatically; another file must be named.
        if (!IsDefaultConfig(configPath))
        {
            startInfo.ArgumentList.Add("-F");
            startInfo.ArgumentList.Add(configPath);
        }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
