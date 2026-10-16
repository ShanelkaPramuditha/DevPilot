using DevPilot.Core;

namespace DevPilot.Models;

/// <summary>A distribution registered with WSL.</summary>
public sealed record WslDistro(string Name, string State, string Version, bool IsDefault)
{
    /// <summary>Size of the distribution's virtual disk, when it could be measured.</summary>
    public long? DiskBytes { get; init; }

    /// <summary>Folder where the distribution's files are stored.</summary>
    public string? Location { get; init; }

    /// <summary>Full path of the virtual disk file (WSL 2 only).</summary>
    public string? VhdPath { get; init; }

    public string DefaultMark => IsDefault ? "★" : "";

    public bool IsRunning => State.Equals("Running", StringComparison.OrdinalIgnoreCase);

    public string DiskSizeText => Format.Size(DiskBytes);
}
