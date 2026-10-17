using System.Collections.Concurrent;
using System.Text;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DevPilot.Controls;

/// <summary>
/// Shows a process's output in a read-only TextBox without slowing the window down.
/// Lines can be added from any thread; they are written in batches a few times a
/// second, the oldest lines are dropped past <see cref="MaxLines"/>, and the box only
/// follows new output while it is scrolled to the end, so the user can scroll back.
/// </summary>
public sealed class OutputLog
{
    /// <summary>Lines kept in the box; older lines are dropped.</summary>
    public const int MaxLines = 2000;

    private readonly TextBox _box;
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly DispatcherTimer _timer;
    private int _lineCount;

    public OutputLog(TextBox box)
    {
        _box = box;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Flush(), box.Dispatcher);
    }

    /// <summary>Adds a line (callable from any thread).</summary>
    public void Append(string line)
    {
        _pending.Enqueue(line);
        if (!_timer.IsEnabled)
        {
            _box.Dispatcher.BeginInvoke(() => _timer.Start());
        }
    }

    public void Clear()
    {
        _pending.Clear();
        _box.Clear();
        _lineCount = 0;
    }

    /// <summary>Writes the waiting lines now (on the UI thread).</summary>
    public void Flush()
    {
        if (_pending.IsEmpty)
        {
            _timer.Stop();
            return;
        }

        bool atEnd = _box.VerticalOffset + _box.ViewportHeight >= _box.ExtentHeight - 4;

        var batch = new StringBuilder();
        while (_pending.TryDequeue(out string? line))
        {
            batch.Append(line).Append(Environment.NewLine);
            _lineCount++;
        }

        // Trim in chunks, so the whole text is rebuilt rarely rather than on every line.
        if (_lineCount > MaxLines + (MaxLines / 4))
        {
            string[] lines = (_box.Text + batch).Split(Environment.NewLine);
            var kept = lines[^(MaxLines + 1)..];
            _box.Text = string.Join(Environment.NewLine, kept);
            _lineCount = MaxLines;
        }
        else
        {
            _box.AppendText(batch.ToString());
        }

        if (atEnd)
        {
            _box.ScrollToEnd();
        }
    }
}
