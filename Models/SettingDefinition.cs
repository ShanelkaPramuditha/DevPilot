namespace DevPilot.Models;

/// <summary>
/// One setting in wsl.conf or .wslconfig, shown as a row in the settings window.
/// </summary>
public sealed record SettingDefinition(
    string Group,
    string Section,
    string Key,
    string Label,
    string Description,
    SettingKind Kind,
    string DefaultText,
    IReadOnlyList<string>? Choices = null);
