namespace DevPilot.Models;

/// <summary>
/// How a tool is installed inside a WSL distribution with its package manager.
/// </summary>
/// <param name="Command">The program that shows the tool is installed, for example "git".</param>
/// <param name="VersionArguments">Arguments that print its version, for example "--version".</param>
/// <param name="Packages">Packages to install, by package manager (apt-get, dnf, pacman, zypper), space separated.</param>
/// <param name="OptionalPackages">Extra packages installed when the distribution has them (for example Docker Compose).</param>
/// <param name="PostInstall">
/// Shell commands run as root after installing; $1 is the distribution's default user.
/// </param>
public sealed record WslPackage(
    string Command,
    string VersionArguments,
    IReadOnlyDictionary<string, string> Packages,
    IReadOnlyDictionary<string, string>? OptionalPackages = null,
    string? PostInstall = null);
