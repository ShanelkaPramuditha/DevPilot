using DevPilot.Core;
using DevPilot.Services.Platform;

namespace DevPilot.Models;

/// <summary>A distribution shown in the "Available" list.</summary>
public sealed class OnlineDistro : ObservableObject
{
    private long? _downloadBytes;
    private bool _downloadSizeLoading;
    private string _status = "";

    public OnlineDistro(CatalogEntry entry, string defaultRam)
    {
        Entry = entry;
        DefaultRam = defaultRam;
    }

    public CatalogEntry Entry { get; }

    public string Name => Entry.Name;

    public string FriendlyName => Entry.FriendlyName;

    public string DisplayName => Entry.IsRecommended ? $"{Entry.FriendlyName}  (recommended)" : Entry.FriendlyName;

    public string Version => Entry.Version;

    public string Publisher => Entry.Publisher;

    public string Architecture => (Entry.Amd64, Entry.Arm64) switch
    {
        (true, true) => "x64 / ARM64",
        (true, false) => "x64",
        (false, true) => "ARM64",
        _ => "—"
    };

    public string Support => Entry.Support;

    public string DefaultRam { get; }

    /// <summary>Download URL for this PC's architecture, if the catalog has one.</summary>
    public string? DownloadUrl => SystemInfo.IsArm64 ? Entry.Arm64Url : Entry.Amd64Url;

    public bool IsSupportedOnThisPc => SystemInfo.IsArm64 ? Entry.Arm64 : Entry.Amd64;

    public long? DownloadBytes
    {
        get => _downloadBytes;
        set
        {
            SetField(ref _downloadBytes, value);
            OnPropertyChanged(nameof(DownloadSizeText));
        }
    }

    public bool DownloadSizeLoading
    {
        get => _downloadSizeLoading;
        set
        {
            SetField(ref _downloadSizeLoading, value);
            OnPropertyChanged(nameof(DownloadSizeText));
        }
    }

    public string DownloadSizeText => DownloadSizeLoading ? "…" : Format.Size(DownloadBytes);

    /// <summary>Available, Installed, Running or Not for this PC.</summary>
    public string Status
    {
        get => _status;
        set
        {
            SetField(ref _status, value);
            OnPropertyChanged(nameof(IsInstalled));
        }
    }

    public bool IsInstalled => Status is "Installed" or "Running";
}
