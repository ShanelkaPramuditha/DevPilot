using System.Windows;
using DevPilot.Services;
using DevPilot.Theming;

namespace DevPilot;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static AppPreferences Preferences { get; } = AppPreferences.Load();

    protected override void OnStartup(StartupEventArgs e)
    {
        // Apply the theme before the first window is created.
        ThemeManager.Apply(Preferences.Theme);
        base.OnStartup(e);
    }
}
