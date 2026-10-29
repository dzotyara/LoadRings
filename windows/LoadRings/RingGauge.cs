using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LoadRings;

/// What sits inside each ring.
public enum IconStyle { IconAndLabel, Labels, Outline, Filled }

public static class IconStyleExtensions
{
    public static string Title(this IconStyle style) => style switch
    {
        IconStyle.IconAndLabel => "Icon + Label",
        IconStyle.Labels => "Labels Only",
        IconStyle.Outline => "Icons",
        _ => "Filled Icons",
    };
}

/// One ring: a faint track, an animated progress arc, and icon / label / percentage in the middle.
public sealed class RingGauge : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double LineWidth = 6.5;
    private static readonly Brush Track = Frozen(new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)));
    private static readonly Brush Dim = Frozen(new SolidColorBrush(Color.FromArgb(191, 255, 255, 255)));
    private static readonly Brush Warning = Frozen(new SolidColorBrush(Color.FromRgb(255, 199, 51)));
    private static readonly Brush Critical = Frozen(new SolidColorBrush(Color.FromRgb(255, 92, 84)));
    private static readonly FontFamily Font = new("Segoe UI Variable Display, Segoe UI");

    private IconStyle _iconStyle;
    private double? _value;

    public RingGauge(MetricKind kind) => Kind = kind;

    public MetricKind Kind { get; }

    public IconStyle IconStyle
    {
        get => _iconStyle;
        set { _iconStyle = value; InvalidateVisual(); }
    }

    /// Animated arc length, 0...1.
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public void Update(double? value)
    {
        _value = value;
        BeginAnimation(ProgressProperty, new DoubleAnimation(value ?? 0, TimeSpan.FromMilliseconds(600))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= LineWidth) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = (size - LineWidth) / 2;

        dc.DrawEllipse(null, new Pen(Track, LineWidth), center, radius, radius);
        DrawArc(dc, center, radius, Math.Clamp(Progress, 0.005, 1), Tint(_value ?? 0));
        DrawCenter(dc, center);
    }

    private static void DrawArc(DrawingContext dc, Point center, double radius, double fraction, Brush brush)
    {
        var pen = new Pen(brush, LineWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (fraction >= 0.999)
        {
            dc.DrawEllipse(null, pen, center, radius, radius);
            return;
        }
        var angle = fraction * 2 * Math.PI;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, isFilled: false, isClosed: false);
            ctx.ArcTo(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    /// Stacks the style's parts vertically and centers the stack in the ring.
    private void DrawCenter(DrawingContext dc, Point center)
    {
        var parts = new List<(double Height, Action<double> Draw)>();

        void AddIcon(double size, bool filled) =>
            parts.Add((size, top => Icons.Draw(dc, Kind, filled, new Rect(center.X - size / 2, top, size, size))));

        void AddText(string text, double fontSize, FontWeight weight, Brush brush)
        {
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(Font, FontStyles.Normal, weight, FontStretches.Normal), fontSize, brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            parts.Add((formatted.Height, top => dc.DrawText(formatted, new Point(center.X - formatted.Width / 2, top))));
        }

        switch (IconStyle)
        {
            case IconStyle.IconAndLabel:
                AddIcon(11, filled: false);
                AddText(Kind.Label(), 9, FontWeights.Bold, Brushes.White);
                break;
            case IconStyle.Labels:
                AddText(Kind.Label(), 13, FontWeights.Bold, Brushes.White);
                break;
            default:
                AddIcon(17, filled: IconStyle == IconStyle.Filled);
                break;
        }
        AddText(_value is double v ? $"{Math.Round(v * 100)}%" : "–", 10, FontWeights.SemiBold, Dim);

        const double spacing = 1;
        var y = center.Y - (parts.Sum(p => p.Height) + spacing * (parts.Count - 1)) / 2;
        foreach (var (height, draw) in parts)
        {
            draw(y);
            y += height + spacing;
        }
    }

    /// White like the macOS battery widget, warming up under heavy load.
    private static Brush Tint(double value) => value switch
    {
        < 0.75 => Brushes.White,
        < 0.9 => Warning,
        _ => Critical,
    };

    internal static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

/// Vector icons drawn on a 16×16 grid, so they look the same on every Windows version.
internal static class Icons
{
    // Cut-outs in filled icons use the widget's background color.
    private static readonly Brush Hole = RingGauge.Frozen(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30)));

    public static void Draw(DrawingContext dc, MetricKind kind, bool filled, Rect box)
    {
        dc.PushTransform(new TranslateTransform(box.X, box.Y));
        dc.PushTransform(new ScaleTransform(box.Width / 16, box.Height / 16));
        var pen = MakePen(Brushes.White);
        var holePen = MakePen(Hole);

        switch (kind)
        {
            case MetricKind.Cpu: Cpu(dc, pen, holePen, filled); break;
            case MetricKind.Gpu: Cube(dc, pen, holePen, filled); break;
            case MetricKind.Ram: MemoryStick(dc, pen, filled); break;
            case MetricKind.Ssd: Drive(dc, pen, holePen, filled); break;
        }

        dc.Pop();
        dc.Pop();
    }

    private static Pen MakePen(Brush brush) => new(brush, 1.3)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
        LineJoin = PenLineJoin.Round,
    };

    private static void Cpu(DrawingContext dc, Pen pen, Pen holePen, bool filled)
    {
        var body = new Rect(4, 4, 8, 8);
        var core = new Rect(6.4, 6.4, 3.2, 3.2);
        if (filled)
        {
            dc.DrawRoundedRectangle(Brushes.White, null, body, 1.5, 1.5);
            dc.DrawRectangle(null, holePen, core);
        }
        else
        {
            dc.DrawRoundedRectangle(null, pen, body, 1.5, 1.5);
            dc.DrawRectangle(null, pen, core);
        }
        foreach (var p in new[] { 6.0, 8.0, 10.0 })
        {
            dc.DrawLine(pen, new Point(p, 1.5), new Point(p, 4));
            dc.DrawLine(pen, new Point(p, 12), new Point(p, 14.5));
            dc.DrawLine(pen, new Point(1.5, p), new Point(4, p));
            dc.DrawLine(pen, new Point(12, p), new Point(14.5, p));
        }
    }

    private static void Cube(DrawingContext dc, Pen pen, Pen holePen, bool filled)
    {
        Point top = new(8, 1.5), left = new(2, 5), right = new(14, 5), middle = new(8, 8.5);
        Point bottomLeft = new(2, 11.5), bottomRight = new(14, 11.5), bottom = new(8, 15);
        var outline = Polygon(top, right, bottomRight, bottom, bottomLeft, left);
        var edges = filled ? holePen : pen;

        dc.DrawGeometry(filled ? Brushes.White : null, pen, outline);
        dc.DrawLine(edges, left, middle);
        dc.DrawLine(edges, right, middle);
        dc.DrawLine(edges, middle, bottom);
    }

    private static void MemoryStick(DrawingContext dc, Pen pen, bool filled)
    {
        var body = new Rect(1.5, 4.5, 13, 7);
        dc.DrawRoundedRectangle(filled ? Brushes.White : null, pen, body, 1, 1);
        var chipBrush = filled ? Hole : Brushes.White;
        foreach (var x in new[] { 3.5, 6.8, 10.1 })
            dc.DrawRectangle(chipBrush, null, new Rect(x, 6.5, 2.4, 3));
        for (var x = 3.0; x <= 13; x += 2)
            dc.DrawLine(pen, new Point(x, 11.5), new Point(x, 13.5));
    }

    private static void Drive(DrawingContext dc, Pen pen, Pen holePen, bool filled)
    {
        var body = new Rect(1.5, 4, 13, 8);
        dc.DrawRoundedRectangle(filled ? Brushes.White : null, pen, body, 2.5, 2.5);
        var detail = filled ? holePen : pen;
        var dot = filled ? Hole : Brushes.White;
        dc.DrawLine(detail, new Point(1.5, 8), new Point(14.5, 8));
        dc.DrawEllipse(dot, null, new Point(10.8, 10), 0.75, 0.75);
        dc.DrawEllipse(dot, null, new Point(12.6, 10), 0.75, 0.75);
    }

    private static Geometry Polygon(params Point[] points)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], isFilled: true, isClosed: true);
            ctx.PolyLineTo(points[1..], isStroked: true, isSmoothJoin: true);
        }
        geometry.Freeze();
        return geometry;
    }
}
