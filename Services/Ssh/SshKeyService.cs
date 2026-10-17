using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DevPilot.Models;
using DevPilot.Services.Platform;

namespace DevPilot.Services.Ssh;

/// <summary>
/// SSH key pairs in %UserProfile%\.ssh (including subfolders), generated and managed with
/// the Windows OpenSSH tools (ssh-keygen, ssh-add) and the ssh-agent service.
/// </summary>
public static class SshKeyService
{
    /// <summary>How deep to look for keys below the .ssh folder (keys are often kept in subfolders).</summary>
    private const int SearchDepth = 3;

    public static string SshFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

    private static string? OpenSshTool(string name)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", name);
        return File.Exists(path) ? path : null;
    }

    public static string? SshKeygen => OpenSshTool("ssh-keygen.exe");

    public static string? SshAdd => OpenSshTool("ssh-add.exe");

    /// <summary>Finds the key pairs in a folder and its subfolders.</summary>
    public static List<SshKey> FindKeys(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = SearchDepth, IgnoreInaccessible = true };
        var files = Directory.EnumerateFiles(folder, "*", options).ToList();
        var publicKeys = files.Where(f => f.EndsWith(".pub", StringComparison.OrdinalIgnoreCase)).ToList();
        var privateKeys = files.Where(f => !f.EndsWith(".pub", StringComparison.OrdinalIgnoreCase) && IsPrivateKey(f)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var keys = new List<SshKey>();
        foreach (string publicPath in publicKeys)
        {
            string privatePath = publicPath[..^4];
            if (TryReadPublicKey(publicPath) is not { } info)
            {
                continue;
            }

            keys.Add(new SshKey(
                Name: Path.GetRelativePath(folder, privatePath),
                PrivatePath: privateKeys.Remove(privatePath) ? privatePath : null,
                PublicPath: publicPath,
                Type: info.Type,
                Bits: info.Bits,
                Fingerprint: info.Fingerprint,
                Comment: info.Comment,
                Created: File.GetCreationTime(publicPath)));
        }

        // Private keys whose .pub was deleted: shown so the public key can be recreated.
        foreach (string privatePath in privateKeys)
        {
            keys.Add(new SshKey(Path.GetRelativePath(folder, privatePath), privatePath, null, "Unknown", null, null, "", File.GetCreationTime(privatePath)));
        }

        return keys.OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public sealed record PublicKeyInfo(string Type, int? Bits, string Fingerprint, string Comment);

    public static PublicKeyInfo? TryReadPublicKey(string path)
    {
        try
        {
            return ParsePublicKey(File.ReadAllText(path));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads "ssh-ed25519 AAAA... comment". The fingerprint is the SHA-256 of the key data
    /// in unpadded Base64, as "ssh-keygen -l" prints it.
    /// </summary>
    public static PublicKeyInfo? ParsePublicKey(string text)
    {
        string[] parts = text.Trim().Split((char[])[' ', '\t'], 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(parts[1]);
        }
        catch (FormatException)
        {
            return null;
        }

        string fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(blob)).TrimEnd('=');
        string comment = parts.Length > 2 ? parts[2].Trim() : "";

        return parts[0] switch
        {
            "ssh-ed25519" => new("ED25519", 256, fingerprint, comment),
            "ssh-rsa" => new("RSA", RsaBits(blob), fingerprint, comment),
            "ecdsa-sha2-nistp256" => new("ECDSA", 256, fingerprint, comment),
            "ecdsa-sha2-nistp384" => new("ECDSA", 384, fingerprint, comment),
            "ecdsa-sha2-nistp521" => new("ECDSA", 521, fingerprint, comment),
            "sk-ssh-ed25519@openssh.com" => new("ED25519-SK", 256, fingerprint, comment),
            "sk-ecdsa-sha2-nistp256@openssh.com" => new("ECDSA-SK", 256, fingerprint, comment),
            "ssh-dss" => new("DSA", null, fingerprint, comment),
            _ => new(parts[0], null, fingerprint, comment)
        };
    }

    /// <summary>The modulus size of an RSA key blob: string "ssh-rsa", mpint e, mpint n.</summary>
    private static int? RsaBits(byte[] blob)
    {
        try
        {
            int offset = 0;
            ReadChunk(blob, ref offset);                     // "ssh-rsa"
            ReadChunk(blob, ref offset);                     // public exponent
            var modulus = ReadChunk(blob, ref offset);
            int start = 0;
            while (start < modulus.Length && modulus[start] == 0)
            {
                start++;                                     // mpint sign padding
            }

            if (start == modulus.Length)
            {
                return null;
            }

            int bits = (modulus.Length - start) * 8;
            for (byte top = modulus[start]; (top & 0x80) == 0; top <<= 1)
            {
                bits--;
            }

            return bits;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static ReadOnlySpan<byte> ReadChunk(byte[] blob, ref int offset)
    {
        int length = (int)BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(offset, 4));
        var chunk = blob.AsSpan(offset + 4, length);
        offset += 4 + length;
        return chunk;
    }

    private static bool IsPrivateKey(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var header = new byte[64];
            int read = stream.Read(header, 0, header.Length);
            string text = Encoding.ASCII.GetString(header, 0, read);
            return text.StartsWith("-----BEGIN", StringComparison.Ordinal) && text.Contains("PRIVATE KEY", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a key pair with ssh-keygen. Ed25519 is the modern default; RSA (4096 bits)
    /// is for old servers that do not support it.
    /// </summary>
    public static async Task GenerateAsync(string path, string type, string comment, string passphrase, CancellationToken cancellationToken = default)
    {
        string keygen = SshKeygen ?? throw new InvalidOperationException(SshService.MissingClientMessage);
        if (File.Exists(path) || File.Exists(path + ".pub"))
        {
            throw new InvalidOperationException($"{Path.GetFileName(path)} already exists. Choose another name.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        List<string> arguments = type == "rsa"
            ? ["-t", "rsa", "-b", "4096"]
            : ["-t", "ed25519"];
        // The passphrase is passed as an argument, the only non-interactive option ssh-keygen has;
        // it is visible to the user's own processes for the moment ssh-keygen runs.
        arguments.AddRange(["-C", comment, "-N", passphrase, "-f", path, "-q"]);

        var output = new StringBuilder();
        int exit = await ProcessRunner.RunAsync(keygen, arguments, line => output.AppendLine(line), cancellationToken);
        if (exit != 0)
        {
            throw new InvalidOperationException($"ssh-keygen failed: {output.ToString().Trim()}");
        }
    }

    /// <summary>Recreates a missing .pub from the private key (fails for keys with a passphrase).</summary>
    public static async Task CreatePublicKeyAsync(string privatePath, CancellationToken cancellationToken = default)
    {
        string keygen = SshKeygen ?? throw new InvalidOperationException(SshService.MissingClientMessage);
        var output = new List<string>();
        int exit = await ProcessRunner.RunAsync(keygen, ["-y", "-P", "", "-f", privatePath], output.Add, cancellationToken);
        string publicKey = output.FirstOrDefault(l => l.StartsWith("ssh-", StringComparison.Ordinal) || l.StartsWith("ecdsa-", StringComparison.Ordinal) || l.StartsWith("sk-", StringComparison.Ordinal)) ?? "";
        if (exit != 0 || publicKey.Length == 0)
        {
            throw new InvalidOperationException(
                "The public key could not be created. If the key has a passphrase, run this in a terminal instead:\n" +
                $"ssh-keygen -y -f \"{privatePath}\" > \"{privatePath}.pub\"");
        }

        await File.WriteAllTextAsync(privatePath + ".pub", publicKey + "\n", cancellationToken);
    }

    /// <summary>Fingerprints of the keys loaded in ssh-agent; null when the agent is not running.</summary>
    public static async Task<HashSet<string>?> GetAgentFingerprintsAsync(CancellationToken cancellationToken = default)
    {
        if (SshAdd is not { } sshAdd || !await IsAgentRunningAsync(cancellationToken))
        {
            return null;
        }

        var lines = new List<string>();
        await ProcessRunner.RunAsync(sshAdd, ["-l", "-E", "sha256"], lines.Add, cancellationToken);
        return lines
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length > 1 && p[1].StartsWith("SHA256:", StringComparison.Ordinal))
            .Select(p => p[1])
            .ToHashSet(StringComparer.Ordinal);
    }

    public static async Task<bool> IsAgentRunningAsync(CancellationToken cancellationToken = default)
    {
        var lines = new List<string>();
        await ProcessRunner.RunAsync("sc.exe", ["query", "ssh-agent"], lines.Add, cancellationToken);
        return lines.Any(l => l.Contains("STATE", StringComparison.Ordinal) && l.Contains("RUNNING", StringComparison.Ordinal));
    }

    /// <summary>
    /// Sets the ssh-agent service to start automatically and starts it. Needs admin rights,
    /// so Windows asks for them.
    /// </summary>
    public static async Task EnableAgentAsync()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -Command \"Set-Service ssh-agent -StartupType Automatic; Start-Service ssh-agent\"",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        }) ?? throw new InvalidOperationException("Could not start PowerShell.");
        await process.WaitForExitAsync();
    }

    /// <summary>Adds a key to ssh-agent in a terminal window, so ssh-add can ask for the passphrase.</summary>
    public static void AddToAgent(string privatePath)
    {
        string sshAdd = SshAdd ?? throw new InvalidOperationException(SshService.MissingClientMessage);
        // cmd keeps the window open long enough to read the result.
        var startInfo = new ProcessStartInfo { FileName = "cmd.exe", UseShellExecute = false };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add($"\"{sshAdd}\" \"{privatePath}\" & pause");
        Process.Start(startInfo);
    }
}
