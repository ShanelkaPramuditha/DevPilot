namespace DevPilot.Models;

/// <summary>A port a distribution is listening on.</summary>
public sealed record PortInfo(string Protocol, int Port, string Address, string Process, int? Pid)
{
    /// <summary>
    /// TCP ports bound to all interfaces or to localhost are reachable from
    /// Windows at localhost (localhost forwarding). DNS (53) is left out.
    /// </summary>
    public bool CanOpenInBrowser => Protocol == "TCP" && Port != 53
        && Address is "0.0.0.0" or "*" or "::" or "127.0.0.1" or "::1" or "";

    public string AddressText => Address is "0.0.0.0" or "*" or "::" or "" ? "All interfaces" : Address;

    /// <summary>
    /// ss cannot always name the owner, for example for WSL's own DNS proxy
    /// or sockets in another network namespace.
    /// </summary>
    public string ProcessText => Pid is { } pid ? $"{Process} ({pid})" : Process.Length > 0 ? Process : "unknown";

    public string Url => $"http://localhost:{Port}";
}
