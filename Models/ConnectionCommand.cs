namespace DevPilot.Models;

/// <summary>A command another computer runs to connect, with what it is for.</summary>
public sealed record ConnectionCommand(string Command, string Label);
