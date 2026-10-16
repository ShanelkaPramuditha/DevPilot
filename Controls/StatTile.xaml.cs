using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DevPilot.Controls;

/// <summary>
/// A labelled value with an optional meter and trend line.
/// </summary>
public partial class StatTile : UserControl
{
    private double _meterPercent;
    private IReadOnlyList<double> _history = [];
    private double _historyMax = 100;

    public StatTile()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateMeterWidth();
    }

    public string Label
    {
        get => LabelText.Text;
        set => LabelText.Text = value;
    }

    /// <summary>
    /// Shows a value. Pass a percent to show the meter, and a history to
    /// show the trend line (scaled to <paramref name="historyMax"/>).
    /// </summary>
    public void Update(string value, string detail, double? percent = null,
        IReadOnlyList<double>? history = null, double historyMax = 100, string? historyTooltip = null)
    {
        ValueText.Text = value;
        DetailText.Text = detail;
        DetailText.ToolTip = string.IsNullOrEmpty(detail) ? null : detail;

        if (percent is { } p)
        {
            _meterPercent = Math.Clamp(p, 0, 100);
            Meter.Visibility = Visibility.Visible;

            // Severity is shown with a word as well as a colour. Colours are
            // theme resources so they follow light/dark mode.
            (string fill, string? status, string statusBrush) = _meterPercent switch
            {
                >= 90 => ("Brush.Critical", "▲ Critical", "Brush.Critical"),
                >= 75 => ("Brush.Warning", "▲ High", "Brush.WarningText"),
                _ => ("Brush.Accent", null, "Brush.TextSecondary")
            };
            MeterFill.SetResourceReference(Border.BackgroundProperty, fill);
            StatusText.Text = status;
            StatusText.SetResourceReference(TextBlock.ForegroundProperty, statusBrush);
            StatusText.Visibility = status is null ? Visibility.Collapsed : Visibility.Visible;
            UpdateMeterWidth();
        }
        else
        {
            Meter.Visibility = Visibility.Collapsed;
            StatusText.Visibility = Visibility.Collapsed;
        }

        _history = history ?? [];
        _historyMax = historyMax > 0 ? historyMax : 100;
        Sparkline.Visibility = _history.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Sparkline.ToolTip = historyTooltip;
        DrawSparkline();
    }

    /// <summary>Shows a placeholder when no data is available.</summary>
    public void Clear(string detail)
    {
        Update("—", detail);
    }

    private void UpdateMeterWidth()
    {
        double width = Meter.ActualWidth;
        MeterFill.Width = width > 0 ? width * _meterPercent / 100 : 0;
    }

    private void Sparkline_SizeChanged(object sender, SizeChangedEventArgs e) => DrawSparkline();

    private void DrawSparkline()
    {
        SparklineLine.Points.Clear();
        double width = Sparkline.ActualWidth;
        double height = Sparkline.ActualHeight;
        if (_history.Count == 0 || width <= 0 || height <= 0)
        {
            SparklineDot.Visibility = Visibility.Collapsed;
            return;
        }

        // Leave room for the 2px line and the end dot.
        const double pad = 4;
        double step = _history.Count > 1 ? (width - 2 * pad) / (_history.Count - 1) : 0;
        var points = new PointCollection();
        for (int i = 0; i < _history.Count; i++)
        {
            double ratio = Math.Clamp(_history[i] / _historyMax, 0, 1);
            points.Add(new Point(pad + i * step, pad + (height - 2 * pad) * (1 - ratio)));
        }

        SparklineLine.Points = points;

        var last = points[^1];
        Canvas.SetLeft(SparklineDot, last.X - SparklineDot.Width / 2);
        Canvas.SetTop(SparklineDot, last.Y - SparklineDot.Height / 2);
        SparklineDot.Visibility = Visibility.Visible;
    }
}
