namespace DevPilot.Models;

/// <summary>An IPv4 address of this PC that other computers can use, with the adapter it belongs to.</summary>
public sealed record NetworkAddress(string Address, string Adapter);
