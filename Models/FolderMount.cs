namespace DevPilot.Models;

/// <summary>A Windows folder mounted into a distribution.</summary>
public sealed record FolderMount(string WindowsPath, string LinuxPath, bool IsMounted, bool IsPersistent, bool IsReadOnly)
{
    public string MountedText => IsMounted ? "Mounted" : "Not mounted";

    public string PersistentText => IsPersistent ? "Yes" : "No";

    public string AccessText => IsReadOnly ? "Read-only" : "Read/write";
}
