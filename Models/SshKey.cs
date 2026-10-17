namespace DevPilot.Models;

/// <summary>
/// An SSH key pair in the .ssh folder. Either file can be missing: a private key without
/// its .pub, or a public key on its own.
/// </summary>
public sealed record SshKey(
    string Name,
    string? PrivatePath,
    string? PublicPath,
    string Type,
    int? Bits,
    string? Fingerprint,
    string Comment,
    DateTime Created)
{
    /// <summary>The key is loaded in ssh-agent (set after asking the agent).</summary>
    public bool InAgent { get; init; }

    public string TypeText => Bits is { } bits && !Type.Equals("ED25519", StringComparison.OrdinalIgnoreCase) ? $"{Type} {bits}" : Type;

    public string AgentText => InAgent ? "Loaded" : "";

    public string CreatedText => Created.ToString("d MMM yyyy", System.Globalization.CultureInfo.CurrentCulture);

    public string? Problem => (PrivatePath, PublicPath) switch
    {
        (null, _) => "Public key only: the private key is not in this folder.",
        (_, null) => "The public key (.pub) is missing. Right-click → Create Public Key.",
        _ => null
    };
}
