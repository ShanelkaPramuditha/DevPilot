using System.Windows;
using System.Windows.Input;
using DevPilot.Core;
using DevPilot.Models;

namespace DevPilot.Views.Dialogs;

/// <summary>
/// The command palette: type to find any page, action, distribution or SSH host, then
/// press Enter to run it.
/// </summary>
public partial class CommandPaletteWindow : Window
{
    /// <summary>How many results are shown at most.</summary>
    private const int MaxResults = 12;

    private readonly IReadOnlyList<PaletteCommand> _commands;
    private bool _closing;

    public CommandPaletteWindow(Window owner, IReadOnlyList<PaletteCommand> commands)
    {
        InitializeComponent();
        Owner = owner;
        _commands = commands;

        // Near the top of the owner window, centred, like editors show it.
        Left = owner.Left + Math.Max(0, (owner.ActualWidth - Width) / 2);
        Top = owner.Top + 70;

        Filter();
        Loaded += (_, _) => QueryBox.Focus();
    }

    /// <summary>The command to run after the palette closes, if one was chosen.</summary>
    public PaletteCommand? Chosen { get; private set; }

    private void QueryBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        QueryHint.Visibility = QueryBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Filter();
    }

    private void Filter()
    {
        string query = QueryBox.Text;
        // Matching the title counts most; matching only the category ("SSH", "Page") counts less.
        var results = _commands
            .Select(c => (Command: c, Score: FuzzyMatcher.Score(query, c.Title) ?? FuzzyMatcher.Score(query, c.Category) - 500))
            .Where(r => r.Score is not null)
            .OrderByDescending(r => r.Score)
            .Take(MaxResults)
            .Select(r => r.Command)
            .ToList();

        ResultList.ItemsSource = results;
        ResultList.SelectedIndex = results.Count > 0 ? 0 : -1;
        EmptyText.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Close();
                break;
            case Key.Down:
                e.Handled = true;
                Move(1);
                break;
            case Key.Up:
                e.Handled = true;
                Move(-1);
                break;
            case Key.Enter:
                e.Handled = true;
                RunSelected();
                break;
        }
    }

    private void Move(int step)
    {
        int count = ResultList.Items.Count;
        if (count == 0)
        {
            return;
        }

        ResultList.SelectedIndex = (ResultList.SelectedIndex + step + count) % count;
        ResultList.ScrollIntoView(ResultList.SelectedItem);
    }

    private void ResultList_MouseUp(object sender, MouseButtonEventArgs e) => RunSelected();

    private void RunSelected()
    {
        if (ResultList.SelectedItem is PaletteCommand command)
        {
            Chosen = command;
            Close();
        }
    }

    /// <summary>Clicking elsewhere closes the palette, like a menu.</summary>
    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_closing)
        {
            Close();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        base.OnClosing(e);
    }
}
