namespace DevPilot.Models;

/// <summary>
/// How a tool is installed on Windows with winget.
/// </summary>
/// <param name="InstallId">The winget package installed when the tool is missing, for example "Git.Git".</param>
/// <param name="InstalledPattern">
/// Regular expression for winget ids that count as the tool being installed, so other
/// editions are recognised too (Python.Python.3.12 for Python, OpenJS.NodeJS for Node.js).
/// </param>
public sealed record WindowsPackage(string InstallId, string InstalledPattern);
