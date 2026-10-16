using System.Windows.Controls;

namespace DevPilot.Navigation;

/// <summary>A sidebar entry for one page (TabItem) of the page host.</summary>
public sealed class NavItem(TabItem tab)
{
    public TabItem Tab { get; } = tab;

    public string Group => PageInfo.GetGroup(Tab);
}
