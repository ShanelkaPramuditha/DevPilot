using Microsoft.Win32;

namespace DevPilot.Services.Platform;

/// <summary>
/// Starts DevPilot when the user signs in to Windows, through the per-user "Run" key
/// (no admin rights needed; Task Manager → Startup apps lists it too).
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DevPilot";

    /// <summary>Command-line switch used when Windows starts DevPilot: start hidden in the tray.</summary>
    public const string MinimizedSwitch = "--minimized";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string command && command.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string ExePath => Environment.ProcessPath ?? throw new InvalidOperationException("The DevPilot executable was not found.");

    public static void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, $"\"{ExePath}\" {MinimizedSwitch}");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
