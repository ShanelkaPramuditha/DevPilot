using System.Windows.Controls;
using DevPilot.Core;

namespace DevPilot.Navigation;

/// <summary>
/// A sidebar entry: a page (TabItem) of the page host, or an action such as "Shut Down WSL".
/// An action's TabItem is not in the page host; it only carries the header and the
/// PageInfo glyph and group, so both kinds look the same in the sidebar.
/// </summary>
public sealed class NavItem(TabItem tab, Action? action = null) : ObservableObject
{
    private bool _isEnabled = true;

    public TabItem Tab { get; } = tab;

    public string Group => PageInfo.GetGroup(Tab);

    /// <summary>Shown on hover (the only label when the sidebar shows icons only).</summary>
    public object ToolTip => Tab.ToolTip is string text ? $"{Tab.Header}: {text}" : Tab.Header;

    /// <summary>Runs instead of opening a page; null for pages.</summary>
    public Action? Action { get; } = action;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
    }

    /// <summary>A sidebar action, shown like a page.</summary>
    public static NavItem ForAction(string title, string glyph, string group, string toolTip, Action action)
    {
        var tab = new TabItem { Header = title, ToolTip = toolTip };
        PageInfo.SetGlyph(tab, glyph);
        PageInfo.SetGroup(tab, group);
        return new NavItem(tab, action);
    }
}
