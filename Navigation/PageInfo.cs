using System.Windows;

namespace DevPilot.Navigation;

/// <summary>
/// Describes a page of the main window for the sidebar. Pages are the
/// TabItems of the page host; set these on a TabItem to give it an icon,
/// a subtitle, a sidebar group and a badge. The sidebar is built from the
/// TabItems, so adding a feature page only needs a new TabItem:
/// <code>
/// &lt;TabItem x:Name="ToolsTab" Header="Tools"
///          nav:PageInfo.Glyph="&amp;#xE90F;"
///          nav:PageInfo.Group="Developer tools"
///          nav:PageInfo.Subtitle="Manage the tools installed in your distributions."&gt;
/// </code>
/// Glyphs are Segoe Fluent Icons code points.
/// </summary>
public static class PageInfo
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.RegisterAttached("Glyph", typeof(string), typeof(PageInfo), new PropertyMetadata(""));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.RegisterAttached("Subtitle", typeof(string), typeof(PageInfo), new PropertyMetadata(""));

    public static readonly DependencyProperty GroupProperty =
        DependencyProperty.RegisterAttached("Group", typeof(string), typeof(PageInfo), new PropertyMetadata(""));

    /// <summary>Short text shown next to the page name, such as a count. Empty hides it.</summary>
    public static readonly DependencyProperty BadgeProperty =
        DependencyProperty.RegisterAttached("Badge", typeof(string), typeof(PageInfo), new PropertyMetadata(""));

    public static string GetGlyph(DependencyObject element) => (string)element.GetValue(GlyphProperty);

    public static void SetGlyph(DependencyObject element, string value) => element.SetValue(GlyphProperty, value);

    public static string GetSubtitle(DependencyObject element) => (string)element.GetValue(SubtitleProperty);

    public static void SetSubtitle(DependencyObject element, string value) => element.SetValue(SubtitleProperty, value);

    public static string GetGroup(DependencyObject element) => (string)element.GetValue(GroupProperty);

    public static void SetGroup(DependencyObject element, string value) => element.SetValue(GroupProperty, value);

    public static string GetBadge(DependencyObject element) => (string)element.GetValue(BadgeProperty);

    public static void SetBadge(DependencyObject element, string value) => element.SetValue(BadgeProperty, value);
}
