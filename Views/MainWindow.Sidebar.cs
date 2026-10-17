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
    private static bool PreferExpanded => !App.Preferences.SidebarCollapsed;

    /// <summary>WSL-wide actions, listed under the WSL pages.</summary>
    private List<NavItem> _wslNavActions = [];

    private void InitializeSidebar()
    {
        var items = DistroTabs.Items.OfType<TabItem>().Select(tab => new NavItem(tab)).ToList();

        _wslNavActions =
        [
            NavItem.ForAction("WSL Settings", "\uE713", "WSL", "Resource limits, networking and other settings shared by all distributions",
                () => WslSettings_Click(this, new RoutedEventArgs())),
        ];
        int afterWslPages = items.FindLastIndex(i => i.Group == "WSL") + 1;
        items.InsertRange(afterWslPages, _wslNavActions);
        UpdateNavActions();

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
        if (_syncingNav || NavList.SelectedItem is not NavItem item)
        {
            return;
        }

        if (item.Action is { } action)
        {
            // An action, not a page: keep the current page selected and run it once the click is done.
            SyncNavSelection();
            Dispatcher.BeginInvoke(action);
        }
        else if (DistroTabs.SelectedItem != item.Tab)
        {
            DistroTabs.SelectedItem = item.Tab;
        }
    }

    /// <summary>With the sidebar collapsed, the status dot opens Update WSL and Shut Down WSL.</summary>
    private void StatusCard_Click(object sender, RoutedEventArgs e)
    {
        WslStatusMenu.PlacementTarget = StatusDotButton;
        WslStatusMenu.IsOpen = true;
    }

    private void WslStatusMenu_Opened(object sender, RoutedEventArgs e)
    {
        StatusUpdateWslItem.IsEnabled = _wslInstalled && !_busy;
        StatusShutdownWslItem.IsEnabled = _wslInstalled && !_busy;
    }

    /// <summary>The WSL actions need WSL, and wait while another operation runs.</summary>
    private void UpdateNavActions()
    {
        foreach (var item in _wslNavActions)
        {
            item.IsEnabled = _wslInstalled && !_busy;
        }

        StatusUpdateButton.IsEnabled = _wslInstalled && !_busy;
        StatusShutdownButton.IsEnabled = _wslInstalled && !_busy;
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
