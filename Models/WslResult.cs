namespace DevPilot.Models;

/// <summary>Result of a captured wsl.exe run.</summary>
public sealed record WslResult(int ExitCode, string Output)
{
    /// <summary>Standard output only, without error messages.</summary>
    public string StandardOutput { get; init; } = "";

    public bool Succeeded => ExitCode == 0;
}
