using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DevPilot.Core;

namespace DevPilot.Services.Platform;

/// <summary>
/// Information about this PC that affects WSL.
/// </summary>
public static partial class SystemInfo
{
    public static bool IsArm64 => RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64;

    public static long? TotalMemoryBytes
    {
        get
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            return GlobalMemoryStatusEx(ref status) ? (long)status.TotalPhys : null;
        }
    }

    /// <summary>
    /// Memory available to the WSL 2 virtual machine, which is shared by all
    /// running distributions. Uses the "memory" setting in %UserProfile%\.wslconfig
    /// when present, otherwise the WSL default of 50% of the PC's memory.
    /// </summary>
    public static string WslMemoryText()
    {
        if (ReadWslConfigMemory() is { } configured)
        {
            return $"{Format.Size(configured)} (.wslconfig)";
        }

        return TotalMemoryBytes is { } total
            ? $"Up to {Format.Size(total / 2)}"
            : "50% of RAM";
    }

    private static long? ReadWslConfigMemory()
    {
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wslconfig");
            if (!File.Exists(path))
            {
                return null;
            }

            string section = "";
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Split('#')[0].Trim();
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    section = line[1..^1].Trim();
                    continue;
                }

                var match = MemorySetting().Match(line);
                if (section.Equals("wsl2", StringComparison.OrdinalIgnoreCase) && match.Success)
                {
                    double amount = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    long multiplier = match.Groups[2].Value.ToUpperInvariant() switch
                    {
                        "K" or "KB" => 1L << 10,
                        "M" or "MB" => 1L << 20,
                        "T" or "TB" => 1L << 40,
                        "B" => 1,
                        _ => 1L << 30
                    };
                    return (long)(amount * multiplier);
                }
            }
        }
        catch
        {
            // Unreadable config: fall back to the default.
        }

        return null;
    }

    [GeneratedRegex(@"^memory\s*=\s*([\d.]+)\s*([KMGT]?B?)$", RegexOptions.IgnoreCase)]
    private static partial Regex MemorySetting();

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
