namespace DevPilot.Models;

/// <summary>An entry in the command palette (Ctrl+K).</summary>
/// <param name="Title">What the command does, for example "Open terminal: Ubuntu".</param>
/// <param name="Category">Shown on the right: Page, Distribution, SSH, App...</param>
/// <param name="Glyph">A Segoe Fluent Icons code point.</param>
/// <param name="Run">Runs the command.</param>
public sealed record PaletteCommand(string Title, string Category, string Glyph, Action Run);
