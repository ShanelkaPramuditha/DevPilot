using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace DevPilot.Theming;

/// <summary>
/// Applies the light or dark theme. Standard controls use WPF's Fluent theme;
/// the app's own colours are brushes in the application resources, referenced
/// with DynamicResource so they switch together.
/// </summary>
public static class ThemeManager
{
    private static readonly Dictionary<string, (string Light, string Dark)> Palette = new()
    {
        ["Brush.TextPrimary"] = ("#0B0B0B", "#F0EFEC"),
        ["Brush.TextSecondary"] = ("#52514E", "#C3C2B7"),
        ["Brush.TextMuted"] = ("#6E6D69", "#A09F99"),
        ["Brush.Card"] = ("#FCFCFB", "#2B2B2A"),
        ["Brush.Sidebar"] = ("#EBEBEA", "#1A1A1A"),
        ["Brush.NavHover"] = ("#E0E0DF", "#262626"),
        ["Brush.NavSelected"] = ("#DADAD9", "#2E2E2E"),
        ["Brush.CardStroke"] = ("#E3E2DE", "#3A3A38"),
        ["Brush.Accent"] = ("#2A78D6", "#3987E5"),
        ["Brush.AccentTrack"] = ("#DCE8F8", "#203752"),
        ["Brush.Warning"] = ("#FAB219", "#FAB219"),
        ["Brush.WarningText"] = ("#8A5A00", "#FAB219"),
        ["Brush.Critical"] = ("#D03B3B", "#E5605F"),
        ["Brush.Series2"] = ("#EB6834", "#D95926"),
        ["Brush.Success"] = ("#0B7A33", "#5FD38A"),
        ["Brush.Error"] = ("#B00020", "#FF8A80"),
        ["Brush.NoteBackground"] = ("#F3F7FD", "#1D2A3A"),
        ["Brush.NoteStroke"] = ("#C9DCF5", "#2C4A6E"),
        ["Brush.Badge.Available"] = ("#E8F0FE", "#1D2F4A"),
        ["Brush.Badge.AvailableText"] = ("#1A4FA0", "#9CC3F5"),
        ["Brush.Badge.Installed"] = ("#E6F4EA", "#1C3324"),
        ["Brush.Badge.InstalledText"] = ("#1E6B34", "#8FD4A4"),
        ["Brush.Badge.Running"] = ("#D2F0DC", "#174A2A"),
        ["Brush.Badge.RunningText"] = ("#0B5A25", "#A6E8B9"),
        ["Brush.Badge.Disabled"] = ("#F1F1F1", "#2C2C2B"),
        ["Brush.Badge.DisabledText"] = ("#777777", "#A09F99"),
    };

    private static bool _listening;

    public static AppTheme Current { get; private set; } = AppTheme.System;

    public static bool IsDark { get; private set; }

    public static event EventHandler? ThemeChanged;

    public static void Apply(AppTheme theme)
    {
        Current = theme;
        IsDark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => SystemUsesDarkTheme()
        };

#pragma warning disable WPF0001 // Fluent theme support is marked experimental in WPF.
        Application.Current.ThemeMode = IsDark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001

        var resources = Application.Current.Resources;
        foreach (var (key, (light, dark)) in Palette)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(IsDark ? dark : light));
            brush.Freeze();
            resources[key] = brush;
        }

        if (!_listening)
        {
            // Follow Windows when it switches between light and dark.
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category == UserPreferenceCategory.General && Current == AppTheme.System)
                {
                    Application.Current.Dispatcher.Invoke(() => Apply(AppTheme.System));
                }
            };
            _listening = true;
        }

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Looks up one of the theme brushes from code.</summary>
    public static Brush Brush(string key) =>
        Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;

    private static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }
}
