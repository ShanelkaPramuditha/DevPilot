using System.Windows;
using System.Windows.Controls;
using DevPilot.Theming;

namespace DevPilot.Views;

/// <summary>
/// The "more" menu, theme, keyboard shortcuts and window size.
/// </summary>
public partial class MainWindow
{
    private void More_Click(object sender, RoutedEventArgs e)
    {
        MoreMenu.PlacementTarget = MoreButton;
        MoreMenu.IsOpen = true;
    }

    private void MoreMenu_Opened(object sender, RoutedEventArgs e)
    {
        WslSettingsItem.IsEnabled = _wslInstalled && !_busy;
        UpdateWslItem.IsEnabled = _wslInstalled && !_busy;
        ShutdownWslItem.IsEnabled = _wslInstalled && !_busy;

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
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        App.Preferences.WindowWidth = bounds.Width;
        App.Preferences.WindowHeight = bounds.Height;
        App.Preferences.WindowMaximized = WindowState == WindowState.Maximized;
        App.Preferences.Save();
    }
}
