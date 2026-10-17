using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using DevPilot.Theming;

namespace DevPilot.Views;

/// <summary>
/// The "more" menu, theme, keyboard shortcuts and window size.
/// </summary>
[SuppressMessage("Reliability", "CA1001", Justification = "The tray icon lives as long as the window and is disposed when it closes.")]
public partial class MainWindow
{
    private void More_Click(object sender, RoutedEventArgs e)
    {
        MoreMenu.PlacementTarget = MoreButton;
        MoreMenu.IsOpen = true;
    }

    private void MoreMenu_Opened(object sender, RoutedEventArgs e)
    {
        ThemeSystemItem.IsChecked = ThemeManager.Current == AppTheme.System;
        ThemeLightItem.IsChecked = ThemeManager.Current == AppTheme.Light;
        ThemeDarkItem.IsChecked = ThemeManager.Current == AppTheme.Dark;
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse(tag, out AppTheme theme))
        {
            ThemeManager.Apply(theme);
            App.Preferences.Theme = theme;
            App.Preferences.Save();
        }
    }

    private void Shortcuts_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "Ctrl+K\tCommand palette: find any page, action, distribution or SSH host\n" +
            "Ctrl+,\tSettings\n" +
            "F5\tRefresh\n" +
            "Ctrl+1, 2, 3\tSwitch between Installed, Available and Usage\n" +
            "Ctrl+F\tSearch available distributions\n" +
            "Enter\tOpen a terminal in the selected distribution\n" +
            "Shift+F10\tShow the actions menu for the selected row\n" +
            "Double-click\tOpen a terminal / install the distribution",
            "Keyboard Shortcuts", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var ctrl = System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control;

        switch (e.Key)
        {
            case System.Windows.Input.Key.F5 when !_busy:
                e.Handled = true;
                _catalogLoaded = false;
                await RunBusyAsync("Refreshing...", RefreshAllAsync);
                break;
            case System.Windows.Input.Key.K when ctrl:
            case System.Windows.Input.Key.P when System.Windows.Input.Keyboard.Modifiers == (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift):
                e.Handled = true;
                ShowCommandPalette();
                break;
            case System.Windows.Input.Key.OemComma when ctrl:
                e.Handled = true;
                DistroTabs.SelectedItem = SettingsTab;
                break;
            case System.Windows.Input.Key.F when ctrl:
                e.Handled = true;
                DistroTabs.SelectedItem = AvailableTab;
                SearchBox.Focus();
                SearchBox.SelectAll();
                break;
            case System.Windows.Input.Key.D1 when ctrl:
                e.Handled = true;
                DistroTabs.SelectedItem = InstalledTab;
                break;
            case System.Windows.Input.Key.D2 when ctrl:
                e.Handled = true;
                DistroTabs.SelectedItem = AvailableTab;
                break;
            case System.Windows.Input.Key.D3 when ctrl:
                e.Handled = true;
                DistroTabs.SelectedItem = UsageTab;
                break;
            case System.Windows.Input.Key.Enter when InstalledList.IsKeyboardFocusWithin && SelectedDistro is not null:
                e.Handled = true;
                OpenTerminal_Click(sender, e);
                break;
        }
    }

    private void SetStatusDot(string brushKey) =>
        StatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brushKey);

    /// <summary>Restores the last window size, kept within the screen.</summary>
    private void RestoreWindowSize()
    {
        var prefs = App.Preferences;
        if (prefs.WindowWidth is { } width && prefs.WindowHeight is { } height)
        {
            Width = Math.Clamp(width, MinWidth, SystemParameters.WorkArea.Width);
            Height = Math.Clamp(height, MinHeight, SystemParameters.WorkArea.Height);
        }

        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (prefs.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // With the tray option on, closing only hides the window.
        if (!_exiting && App.Preferences.CloseToTray && _tray is not null)
        {
            e.Cancel = true;
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray.Notify("DevPilot is still running", "Open it from the notification area. Right-click the icon to exit.");
            }

            return;
        }

        _tray?.Dispose();
        _tray = null;

        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        App.Preferences.WindowWidth = bounds.Width;
        App.Preferences.WindowHeight = bounds.Height;
        App.Preferences.WindowMaximized = WindowState == WindowState.Maximized;
        App.Preferences.Save();
    }

    #region Notification area and startup

    private TrayIcon? _tray;
    private bool _exiting;
    private bool _trayHintShown;

    /// <summary>Normal or maximized, before the window was minimized: restored from the notification area.</summary>
    private WindowState? _restoreState;

    private void InitializeTray()
    {
        UpdateTrayIcon();
        Loaded += (_, _) => _restoreState ??= WindowState == WindowState.Minimized ? null : WindowState;
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized)
            {
                _restoreState = WindowState;
            }

            // "Minimize to the notification area": hide instead of a taskbar button.
            if (WindowState == WindowState.Minimized && App.Preferences.MinimizeToTray && !_exiting)
            {
                _tray ??= new TrayIcon(this);
                Hide();
            }
        };
    }

    /// <summary>The notification area icon is shown while an option uses it, or while the window is hidden there.</summary>
    private void UpdateTrayIcon()
    {
        bool needed = App.Preferences.CloseToTray || App.Preferences.MinimizeToTray || (IsLoaded && !IsVisible);
        if (needed)
        {
            _tray ??= new TrayIcon(this);
        }
        else
        {
            _tray?.Dispose();
            _tray = null;
        }
    }

    /// <summary>Started with Windows, hidden: only the notification area icon is shown.</summary>
    public void StartInNotificationArea() => _tray ??= new TrayIcon(this);

    /// <summary>Shows the window (from the tray, or when DevPilot is started a second time).</summary>
    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = _restoreState ?? (App.Preferences.WindowMaximized ? WindowState.Maximized : WindowState.Normal);
        }

        Activate();
        // Bring it in front of the app that has the focus.
        Topmost = true;
        Topmost = false;
        Focus();
    }

    /// <summary>Closes DevPilot for real (the tray's Exit).</summary>
    public void ExitApplication()
    {
        _exiting = true;
        Close();
        Application.Current.Shutdown();
    }

    /// <summary>After an action from the tray menu, show the new state if the window is open.</summary>
    public async Task RefreshAfterTrayActionAsync()
    {
        if (IsVisible && !_busy && _wslInstalled)
        {
            await RunBusyAsync("Refreshing...", RefreshInstalledAsync);
        }
    }

    /// <summary>
    /// Tells the user that long work finished while they were elsewhere: a notification
    /// when the tray icon is on, otherwise a flashing taskbar button.
    /// </summary>
    private void NotifyIfInBackground(string title, string message)
    {
        if (IsActive || !App.Preferences.NotifyWhenDone)
        {
            return;
        }

        if (_tray is not null)
        {
            _tray.Notify(title, message);
        }
        else
        {
            WindowAttention.Flash(this);
        }
    }

    #endregion
}
