namespace DevPilot.Models;

/// <summary>A tool and its state on the selected target (Windows or one distribution).</summary>
public sealed record ToolRow(DevTool Tool)
{
    /// <summary>The tool can be installed on this target.</summary>
    public bool Supported { get; init; }

    public bool Installed { get; init; }

    public string? Version { get; init; }

    /// <summary>A newer version, when the package manager reports one.</summary>
    public string? UpdateVersion { get; init; }

    /// <summary>The installed winget id (may differ from the default, such as Python.Python.3.12).</summary>
    public string? InstalledPackageId { get; init; }

    /// <summary>Installed by something else (such as Docker Desktop's WSL integration): shown, not managed.</summary>
    public string? ManagedElsewhere { get; init; }

    /// <summary>Why the tool cannot be installed here, or a hint about it.</summary>
    public string? Note { get; init; }

    public string Name => Tool.Name;

    public string Description => Tool.Description;

    public string VersionText => Version ?? (Installed ? "installed" : "—");

    /// <summary>Available, Installed, Update available, Not installed.</summary>
    public string Status => !Supported ? "Not available here"
        : ManagedElsewhere is not null ? "Installed"
        : UpdateVersion is not null ? "Update available"
        : Installed ? "Installed"
        : "Not installed";

    public string ActionText => !Supported ? "Unavailable"
        : ManagedElsewhere is not null ? "Managed elsewhere"
        : UpdateVersion is not null ? $"Update to {UpdateVersion}"
        : Installed ? "Up to date"
        : "Install";

    /// <summary>The main button does something (install or update).</summary>
    public bool CanAct => Supported && ManagedElsewhere is null && (!Installed || UpdateVersion is not null);

    public bool CanUpdate => Supported && ManagedElsewhere is null && Installed;

    public bool CanUninstall => Supported && ManagedElsewhere is null && Installed;

    public string Tooltip => ManagedElsewhere ?? Note ?? Tool.Description;
}
