using System.Windows;
using System.Windows.Controls;
using DevPilot.Models;
using DevPilot.Navigation;
using DevPilot.Services.Platform;
using DevPilot.Services.Ssh;
using DevPilot.Theming;
using DevPilot.Views.Dialogs;

namespace DevPilot.Views;

/// <summary>
/// The command palette (Ctrl+K): every page, the actions for each distribution, SSH hosts
/// and app commands, searchable by typing.
/// </summary>
public partial class MainWindow
{
    private void CommandPalette_Click(object sender, RoutedEventArgs e) => ShowCommandPalette();

    private void ShowCommandPalette()
    {
        if (!IsVisible)
        {
            return;
        }

        var palette = new CommandPaletteWindow(this, BuildPaletteCommands());
        palette.ShowDialog();
        palette.Chosen?.Run();
    }

    private List<PaletteCommand> BuildPaletteCommands()
    {
        var commands = new List<PaletteCommand>();
        RoutedEventArgs Args() => new();

        // Pages
        foreach (var tab in DistroTabs.Items.OfType<TabItem>())
        {
            commands.Add(new($"Go to {tab.Header}", "Page", PageInfo.GetGlyph(tab), () => DistroTabs.SelectedItem = tab));
        }

        // Distributions: select it, then use the same handlers as its right-click menu.
        foreach (var distro in _installed)
        {
            void OnDistro(Action action)
            {
                DistroTabs.SelectedItem = InstalledTab;
                InstalledList.SelectedItem = distro;
                action();
            }

            commands.Add(new($"Open terminal: {distro.Name}", "Distribution", "", () => OnDistro(() => OpenTerminal_Click(this, Args()))));
            if (VsCode.IsInstalled)
            {
                commands.Add(new($"Open in VS Code: {distro.Name}", "Distribution", "", () => OnDistro(() => OpenVsCode_Click(this, Args()))));
            }

            commands.Add(new($"Open files: {distro.Name}", "Distribution", "", () => OnDistro(() => OpenFiles_Click(this, Args()))));
            commands.Add(distro.IsRunning
                ? new($"Stop: {distro.Name}", "Distribution", "", () => OnDistro(() => StopDistro_Click(this, Args())))
                : new($"Start: {distro.Name}", "Distribution", "", () => OnDistro(() => StartDistro_Click(this, Args()))));
            commands.Add(new($"Settings: {distro.Name}", "Distribution", "", () => OnDistro(() => DistroSettings_Click(this, Args()))));
            commands.Add(new($"Folder mounts: {distro.Name}", "Distribution", "", () => OnDistro(() => FolderMounts_Click(this, Args()))));
            commands.Add(new($"Clone: {distro.Name}", "Distribution", "", () => OnDistro(() => Clone_Click(this, Args()))));
        }

        // SSH hosts
        foreach (var host in _sshHosts.Where(h => h.CanConnect))
        {
            commands.Add(new($"SSH: {host.Alias}", "SSH", "", () =>
            {
                try
                {
                    SshService.Connect(host.Alias!, SshConfigPath);
                }
                catch (Exception ex)
                {
                    ShowError(ex.Message);
                }
            }));
        }

        // App commands
        commands.AddRange(
        [
            new("WSL settings", "App", "", () => WslSettings_Click(this, Args())),
            new("Shut down WSL", "App", "", () => ShutdownWsl_Click(this, Args())),
            new("Refresh", "App", "", () => Refresh_Click(this, Args())),
            new("Add SSH host", "SSH", "", () => { DistroTabs.SelectedItem = SshTab; SshAddHost_Click(this, Args()); }),
            new("Generate SSH key", "SSH", "", () => { DistroTabs.SelectedItem = KeysTab; KeyGenerate_Click(this, Args()); }),
            new("Check for updates", "App", "", () => CheckForUpdates_Click(this, Args())),
            new("Theme: light", "App", "", () => SetTheme(AppTheme.Light)),
            new("Theme: dark", "App", "", () => SetTheme(AppTheme.Dark)),
            new("Theme: use Windows setting", "App", "", () => SetTheme(AppTheme.System)),
            new("Toggle sidebar", "App", "", () => SidebarToggle_Click(this, Args())),
            new("Keyboard shortcuts", "App", "", () => Shortcuts_Click(this, Args())),
            new("About DevPilot", "App", "", () => About_Click(this, Args())),
        ]);

        return commands;
    }

    private static void SetTheme(AppTheme theme)
    {
        ThemeManager.Apply(theme);
        App.Preferences.Theme = theme;
        App.Preferences.Save();
    }
}
