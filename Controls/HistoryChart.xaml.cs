using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DevPilot.Controls;

/// <summary>
/// A single-series line chart of recent samples with a value axis, a time
/// axis and a hover readout. One series, so the title names it (no legend).
/// </summary>
public partial class HistoryChart : UserControl
{
    private IReadOnlyList<(DateTime Time, double Value)> _samples = [];
    private double _max = 100;
    private Func<double, string> _format = v => $"{v:0}%";
    private List<Point> _points = [];

    public HistoryChart()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => TitleText.Text;
        set => TitleText.Text = value;
    }

    /// <summary>
    /// Shows the samples, oldest first, scaled from 0 to <paramref name="max"/>.
    /// </summary>
    public void Update(IReadOnlyList<(DateTime Time, double Value)> samples, double max, Func<double, string> format)
    {
        _samples = samples;
        _max = max > 0 ? max : 1;
        _format = format;

        CurrentText.Text = samples.Count > 0 ? format(samples[^1].Value) : "—";
        MaxLabel.Text = format(_max);
        MidLabel.Text = format(_max / 2);
        MinLabel.Text = format(0);
        StartLabel.Text = samples.Count > 1 ? $"{FormatAge(samples[^1].Time - samples[0].Time)} ago" : "";

        Draw();
    }

    public void Clear()
    {
        Update([], _max, _format);
        CurrentText.Text = "—";
    }

    private void Plot_SizeChanged(object sender, SizeChangedEventArgs e) => Draw();

    private void Draw()
    {
        double width = Plot.ActualWidth;
        double height = Plot.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        SetLine(GridTop, 0, 0, width, 0);
        SetLine(GridMid, 0, height / 2, width, height / 2);
        SetLine(GridBottom, 0, height, width, height);

        bool hasData = _samples.Count >= 2;
        EmptyText.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;
        Canvas.SetLeft(EmptyText, 8);
        Canvas.SetTop(EmptyText, height / 2 - 18);

        _points = [];
        if (!hasData)
        {
            Series.Points = [];
            Area.Points = [];
            Dot.Visibility = Visibility.Collapsed;
            HideReadout();
            return;
        }

        // Place samples by time so gaps (tab closed) show as a jump, not a squeeze.
        DateTime start = _samples[0].Time;
        double span = Math.Max(1, (_samples[^1].Time - start).TotalSeconds);
        foreach (var (time, value) in _samples)
        {
            double x = (time - start).TotalSeconds / span * width;
            double y = height - Math.Clamp(value / _max, 0, 1) * height;
            _points.Add(new Point(x, y));
        }

        Series.Points = new PointCollection(_points);
        var area = new PointCollection(_points) { new Point(_points[^1].X, height), new Point(_points[0].X, height) };
        Area.Points = area;

        var last = _points[^1];
        Canvas.SetLeft(Dot, last.X - Dot.Width / 2);
        Canvas.SetTop(Dot, last.Y - Dot.Height / 2);
        Dot.Visibility = Visibility.Visible;
    }

    /// <summary>The crosshair snaps to the sample nearest the pointer.</summary>
    private void Plot_MouseMove(object sender, MouseEventArgs e)
    {
        if (_points.Count < 2)
        {
            return;
        }

        double x = e.GetPosition(Plot).X;
        int index = 0;
        for (int i = 1; i < _points.Count; i++)
        {
            if (Math.Abs(_points[i].X - x) < Math.Abs(_points[index].X - x))
            {
                index = i;
            }
        }

        var point = _points[index];
        var (time, value) = _samples[index];

        SetLine(Crosshair, point.X, 0, point.X, Plot.ActualHeight);
        Crosshair.Visibility = Visibility.Visible;
        Canvas.SetLeft(Dot, point.X - Dot.Width / 2);
        Canvas.SetTop(Dot, point.Y - Dot.Height / 2);

        ReadoutValue.Text = _format(value);
        ReadoutTime.Text = $"{time:T}";
        Readout.Visibility = Visibility.Visible;
        Readout.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        // Keep the readout inside the plot, on the side away from the pointer.
        double left = point.X + 10;
        if (left + Readout.DesiredSize.Width > Plot.ActualWidth)
        {
            left = point.X - Readout.DesiredSize.Width - 10;
        }

        Canvas.SetLeft(Readout, Math.Max(0, left));
        Canvas.SetTop(Readout, 0);
    }

    private void Plot_MouseLeave(object sender, MouseEventArgs e)
    {
        HideReadout();
        Draw();
    }

    private void HideReadout()
    {
        Crosshair.Visibility = Visibility.Collapsed;
        Readout.Visibility = Visibility.Collapsed;
    }

    private static void SetLine(System.Windows.Shapes.Line line, double x1, double y1, double x2, double y2)
    {
        line.X1 = x1;
        line.Y1 = y1;
        line.X2 = x2;
        line.Y2 = y2;
    }

    private static string FormatAge(TimeSpan age) => age.TotalMinutes >= 1
        ? $"{(int)age.TotalMinutes} min"
        : $"{(int)age.TotalSeconds} s";
}
