using Cuentitas.App.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Cuentitas.App.Controls;

/// <summary>Una serie de un gráfico de barras: nombre, color y un valor por columna.</summary>
public sealed record BarSeries(string Name, Brush Brush, IReadOnlyList<long> Values);

/// <summary>Datos de un gráfico de barras: etiquetas de columnas + series.</summary>
public sealed record BarChartData(IReadOnlyList<string> Labels, IReadOnlyList<BarSeries> Series, bool Stacked = false);

/// <summary>Una porción de la dona.</summary>
public sealed record DonutSlice(string Label, long Value, Brush Brush);

/// <summary>Base: dibuja en un Canvas cuando cambian los datos o el tamaño. Montos en centavos.</summary>
public abstract partial class ChartBase : UserControl
{
    protected readonly Canvas Surface = new();

    protected ChartBase()
    {
        Content = Surface;
        Surface.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        SizeChanged += (_, _) => Redraw();
        ActualThemeChanged += (_, _) => Redraw();
    }

    protected Brush AxisBrush => Ui.ThemeResource("SecondaryTextBrush");
    protected Brush GridBrush => new SolidColorBrush(Windows.UI.Color.FromArgb(0x26, 0x80, 0x80, 0x80));

    protected void Redraw()
    {
        Surface.Children.Clear();
        if (ActualWidth < 40 || ActualHeight < 40) return;
        Draw(ActualWidth, ActualHeight);
    }

    protected abstract void Draw(double width, double height);

    protected TextBlock Label(string text, double x, double y, double size = 11, bool right = false, bool center = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, Foreground = AxisBrush };
        t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(t, right ? x - t.DesiredSize.Width : center ? x - t.DesiredSize.Width / 2 : x);
        Canvas.SetTop(t, y - t.DesiredSize.Height / 2);
        Surface.Children.Add(t);
        return t;
    }

    /// <summary>Escala "linda" para el eje Y: máximo redondeado y 4 divisiones.</summary>
    protected static (long Min, long Max) NiceRange(long min, long max)
    {
        if (max <= min) max = min + 100_00;
        var span = (double)(max - Math.Min(0, min));
        var step = Math.Pow(10, Math.Floor(Math.Log10(span / 4)));
        foreach (var m in new[] { 1, 2, 2.5, 5, 10 })
        {
            if (span / (step * m) <= 4)
            {
                step *= m;
                break;
            }
        }
        var lo = min < 0 ? (long)(Math.Floor(min / step) * step) : 0;
        var hi = (long)(Math.Ceiling(max / step) * step);
        return (lo, Math.Max(hi, lo + (long)step));
    }

    /// <summary>Ejes horizontales con etiquetas; devuelve la función valor → Y.</summary>
    protected Func<long, double> DrawYAxis(long min, long max, double left, double top, double bottom, double width)
    {
        double Y(long v) => bottom - (v - min) / (double)(max - min) * (bottom - top);
        for (var i = 0; i <= 4; i++)
        {
            var v = min + (max - min) * i / 4;
            var y = Y(v);
            Surface.Children.Add(new Line { X1 = left, X2 = width, Y1 = y, Y2 = y, Stroke = GridBrush, StrokeThickness = 1 });
            Label(Ui.ShortMoney(v), left - 6, y, right: true);
        }
        return Y;
    }
}

/// <summary>Barras agrupadas (o apiladas) por mes, con tooltip en cada barra.</summary>
public sealed partial class BarChart : ChartBase
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(object), typeof(BarChart), new PropertyMetadata(null, (d, _) => ((BarChart)d).Redraw()));

    public BarChartData? Data
    {
        get => (BarChartData?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override void Draw(double width, double height)
    {
        if (Data is not { Labels.Count: > 0 } data || data.Series.Count == 0) return;
        const double left = 70, top = 8, bottomPad = 24;
        var bottom = height - bottomPad;

        var totals = data.Stacked
            ? Enumerable.Range(0, data.Labels.Count).Select(i => data.Series.Sum(s => Math.Max(0, s.Values[i]))).ToList()
            : data.Series.SelectMany(s => s.Values).ToList();
        var (min, max) = NiceRange(Math.Min(0, data.Series.SelectMany(s => s.Values).DefaultIfEmpty().Min()), totals.DefaultIfEmpty().Max());
        var Y = DrawYAxis(min, max, left, top, bottom, width);

        var groupWidth = (width - left) / data.Labels.Count;
        var bars = data.Stacked ? 1 : data.Series.Count;
        var barWidth = Math.Max(3, Math.Min(28, groupWidth * 0.7 / bars));
        for (var i = 0; i < data.Labels.Count; i++)
        {
            var gx = left + groupWidth * i + (groupWidth - barWidth * bars) / 2;
            long stack = 0;
            for (var s = 0; s < data.Series.Count; s++)
            {
                var series = data.Series[s];
                var v = series.Values[i];
                var x = data.Stacked ? gx : gx + s * barWidth;
                double y1, y2;
                if (data.Stacked)
                {
                    y1 = Y(stack + v);
                    y2 = Y(stack);
                    stack += v;
                }
                else
                {
                    y1 = Y(Math.Max(v, 0));
                    y2 = Y(Math.Min(v, 0));
                }
                var rect = new Rectangle
                {
                    Width = Math.Max(1, barWidth - (data.Stacked ? 0 : 2)),
                    Height = Math.Max(0, y2 - y1),
                    Fill = series.Brush,
                    RadiusX = 2,
                    RadiusY = 2,
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y1);
                ToolTipService.SetToolTip(rect, $"{data.Labels[i]} · {series.Name}: {Cuentitas.Core.Money.Format(v, compact: true)}");
                Surface.Children.Add(rect);
            }
            Label(data.Labels[i], left + groupWidth * i + groupWidth / 2, bottom + 12, center: true);
        }
    }
}

/// <summary>Línea con puntos (ej. evolución del patrimonio).</summary>
public sealed partial class LineChart : ChartBase
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(object), typeof(LineChart),
        new PropertyMetadata(null, (d, _) => ((LineChart)d).Redraw()));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(LineChart), new PropertyMetadata(null, (d, _) => ((LineChart)d).Redraw()));

    public IReadOnlyList<(string Label, long Value)>? Points
    {
        get => (IReadOnlyList<(string Label, long Value)>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override void Draw(double width, double height)
    {
        if (Points is not { Count: > 0 } pts) return;
        const double left = 70, top = 8, bottomPad = 24, pad = 16;
        var bottom = height - bottomPad;
        var (min, max) = NiceRange(Math.Min(0, pts.Min(p => p.Value)), pts.Max(p => p.Value));
        var Y = DrawYAxis(min, max, left, top, bottom, width);
        var step = pts.Count > 1 ? (width - left - pad * 2) / (pts.Count - 1) : 0;
        var brush = Stroke ?? Ui.ThemeResource("IncomeBrush");

        var line = new Polyline { Stroke = brush, StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };
        var area = new Polygon { Fill = brush, Opacity = 0.12 };
        for (var i = 0; i < pts.Count; i++)
        {
            var x = left + pad + step * i;
            var y = Y(pts[i].Value);
            line.Points.Add(new Point(x, y));
            area.Points.Add(new Point(x, y));
            var dot = new Ellipse { Width = 8, Height = 8, Fill = brush };
            Canvas.SetLeft(dot, x - 4);
            Canvas.SetTop(dot, y - 4);
            ToolTipService.SetToolTip(dot, $"{pts[i].Label}: {Cuentitas.Core.Money.Format(pts[i].Value, compact: true)}");
            Surface.Children.Add(dot);
            Label(pts[i].Label, x, bottom + 12, center: true);
        }
        area.Points.Add(new Point(left + pad + step * (pts.Count - 1), Y(Math.Max(min, 0))));
        area.Points.Add(new Point(left + pad, Y(Math.Max(min, 0))));
        Surface.Children.Insert(0, area);
        Surface.Children.Insert(1, line);
    }
}

/// <summary>Gráfico de dona con texto central.</summary>
public sealed partial class DonutChart : ChartBase
{
    public static readonly DependencyProperty SlicesProperty = DependencyProperty.Register(
        nameof(Slices), typeof(object), typeof(DonutChart), new PropertyMetadata(null, (d, _) => ((DonutChart)d).Redraw()));

    public static readonly DependencyProperty CenterTextProperty = DependencyProperty.Register(
        nameof(CenterText), typeof(string), typeof(DonutChart), new PropertyMetadata("", (d, _) => ((DonutChart)d).Redraw()));

    public IReadOnlyList<DonutSlice>? Slices
    {
        get => (IReadOnlyList<DonutSlice>?)GetValue(SlicesProperty);
        set => SetValue(SlicesProperty, value);
    }

    public string CenterText
    {
        get => (string)GetValue(CenterTextProperty);
        set => SetValue(CenterTextProperty, value);
    }

    protected override void Draw(double width, double height)
    {
        if (Slices is not { Count: > 0 } slices) return;
        var total = slices.Sum(s => Math.Max(0, s.Value));
        if (total <= 0) return;
        var size = Math.Min(width, height);
        var r = size / 2 - 4;
        var thickness = r * 0.32;
        var cx = width / 2;
        var cy = height / 2;
        var angle = -90.0;
        foreach (var s in slices.Where(s => s.Value > 0))
        {
            var sweep = 360.0 * s.Value / total;
            var path = new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = Arc(cx, cy, r - thickness / 2, angle, Math.Min(sweep, 359.99)),
                Stroke = s.Brush,
                StrokeThickness = thickness,
            };
            ToolTipService.SetToolTip(path, $"{s.Label}: {Cuentitas.Core.Money.Format(s.Value, compact: true)} ({100.0 * s.Value / total:0} %)");
            Surface.Children.Add(path);
            angle += sweep;
        }
        if (!string.IsNullOrEmpty(CenterText))
        {
            var t = new TextBlock
            {
                Text = CenterText,
                FontSize = Math.Max(12, r * 0.2),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Width = r * 1.2,
            };
            t.Measure(new Size(r * 1.2, double.PositiveInfinity));
            Canvas.SetLeft(t, cx - r * 0.6);
            Canvas.SetTop(t, cy - t.DesiredSize.Height / 2);
            Surface.Children.Add(t);
        }
    }

    private static Geometry Arc(double cx, double cy, double radius, double startDeg, double sweepDeg)
    {
        static Point P(double cx, double cy, double r, double deg)
        {
            var rad = deg * Math.PI / 180;
            return new Point(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
        }
        var figure = new PathFigure { StartPoint = P(cx, cy, radius, startDeg), IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = P(cx, cy, radius, startDeg + sweepDeg),
            Size = new Size(radius, radius),
            IsLargeArc = sweepDeg > 180,
            SweepDirection = SweepDirection.Clockwise,
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}
