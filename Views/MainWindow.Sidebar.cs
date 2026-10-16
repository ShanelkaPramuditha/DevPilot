using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Animation;
using DevPilot.Navigation;

namespace DevPilot.Views;

/// <summary>
/// The collapsible sidebar. Its entries are built from the TabItems of the
/// page host (DistroTabs), so a new page only needs a new TabItem with
/// PageInfo.Glyph / PageInfo.Group / PageInfo.Subtitle set.
/// </summary>
public partial class MainWindow
{
    private const double SidebarExpandedWidth = 232;
    private const double SidebarCollapsedWidth = 54;

    /// <summary>Windows narrower than this collapse the sidebar to icons.</summary>
    private const double AutoCollapseWidth = 960;

    public static readonly DependencyProperty IsSidebarExpandedProperty = DependencyProperty.Register(
        nameof(IsSidebarExpanded), typeof(bool), typeof(MainWindow),
        new PropertyMetadata(true, (d, _) => ((MainWindow)d).OnSidebarStateChanged()));

    public static readonly DependencyProperty IsSidebarCollapsedProperty = DependencyProperty.Register(
        nameof(IsSidebarCollapsed), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    private bool _syncingNav;

    public bool IsSidebarExpanded
    {
        get => (bool)GetValue(IsSidebarExpandedProperty);
        set => SetValue(IsSidebarExpandedProperty, value);
    }

    /// <summary>The opposite of IsSidebarExpanded, for bindings that show things only when collapsed.</summary>
    public bool IsSidebarCollapsed
    {
        get => (bool)GetValue(IsSidebarCollapsedProperty);
        private set => SetValue(IsSidebarCollapsedProperty, value);
    }

    /// <summary>The user's choice; the window may still collapse it when narrow.</summary>
    private bool PreferExpanded => !App.Preferences.SidebarCollapsed;

    private void InitializeSidebar()
    {
        var items = DistroTabs.Items.OfType<TabItem>().Select(tab => new NavItem(tab)).ToList();
        var view = new ListCollectionView(items);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(NavItem.Group)));
        NavList.ItemsSource = view;

        IsSidebarExpanded = PreferExpanded;
        Sidebar.Width = IsSidebarExpanded ? SidebarExpandedWidth : SidebarCollapsedWidth;
        SyncNavSelection();
    }

    private void SidebarToggle_Click(object sender, RoutedEventArgs e)
    {
        IsSidebarExpanded = !IsSidebarExpanded;
        App.Preferences.SidebarCollapsed = !IsSidebarExpanded;
        App.Preferences.Save();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged)
        {
            return;
        }

        bool shouldExpand = PreferExpanded && e.NewSize.Width >= AutoCollapseWidth;
        if (shouldExpand != IsSidebarExpanded)
        {
            IsSidebarExpanded = shouldExpand;
        }
    }

    private void OnSidebarStateChanged()
    {
        IsSidebarCollapsed = !IsSidebarExpanded;
        if (SidebarToggle is null || Sidebar is null)
        {
            return;
        }

        SidebarToggle.ToolTip = IsSidebarExpanded ? "Collapse menu" : "Expand menu";

        var animation = new DoubleAnimation(IsSidebarExpanded ? SidebarExpandedWidth : SidebarCollapsedWidth, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Sidebar.BeginAnimation(WidthProperty, animation);
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingNav && NavList.SelectedItem is NavItem item && DistroTabs.SelectedItem != item.Tab)
        {
            DistroTabs.SelectedItem = item.Tab;
        }
    }

    /// <summary>Selects the sidebar entry of the current page (pages can also change from code or shortcuts).</summary>
    private void SyncNavSelection()
    {
        if (NavList?.ItemsSource is not ListCollectionView view)
        {
            return;
        }

        _syncingNav = true;
        NavList.SelectedItem = view.OfType<NavItem>().FirstOrDefault(i => i.Tab == DistroTabs.SelectedItem);
        _syncingNav = false;
    }
}
