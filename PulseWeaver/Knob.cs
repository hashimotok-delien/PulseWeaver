using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PulseWeaver;

/// <summary>
/// 0〜10 のつまみ。上下のドラッグで回し、ダブルクリックで 0 に戻す。
/// </summary>
public sealed class Knob : FrameworkElement
{
    private const double StartAngle = 135d;
    private const double SweepAngle = 270d;

    private static readonly Brush TrackBrush = Frozen("#2C374B");
    private static readonly Brush AccentBrush = Frozen("#4F7DFF");
    private static readonly Brush TextBrush = Frozen("#C6D0DE");

    private bool _isDragging;
    private double _dragStartValue;
    private double _dragStartY;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(double),
        typeof(Knob),
        new FrameworkPropertyMetadata(
            0d,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender,
            null,
            CoerceKnobValue));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum),
        typeof(double),
        typeof(Knob),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(double),
        typeof(Knob),
        new FrameworkPropertyMetadata(10d, FrameworkPropertyMetadataOptions.AffectsRender));

    public Knob()
    {
        Cursor = Cursors.Hand;
        MinWidth = 34d;
        MinHeight = 34d;
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0d || height <= 0d)
        {
            return;
        }

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0d, 0d, width, height));

        var center = new Point(width / 2d, height / 2d);
        var radius = (Math.Min(width, height) / 2d) - 4d;
        if (radius <= 2d)
        {
            return;
        }

        dc.DrawGeometry(null, new Pen(TrackBrush, 3.5d) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
            CreateArc(center, radius, StartAngle, SweepAngle));

        var range = Maximum - Minimum;
        var ratio = range <= 0d ? 0d : Math.Clamp((Value - Minimum) / range, 0d, 1d);
        if (ratio > 0.001d)
        {
            dc.DrawGeometry(null, new Pen(AccentBrush, 3.5d) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                CreateArc(center, radius, StartAngle, SweepAngle * ratio));
        }

        var text = new FormattedText(
            FormatValue(Value),
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            11d,
            TextBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(center.X - (text.Width / 2d), center.Y - (text.Height / 2d)));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (e.ClickCount >= 2)
        {
            Value = 0d;
            e.Handled = true;
            return;
        }

        _isDragging = true;
        _dragStartValue = Value;
        _dragStartY = e.GetPosition(this).Y;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isDragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var delta = (_dragStartY - e.GetPosition(this).Y) / 110d * (Maximum - Minimum);
        Value = Math.Round(_dragStartValue + delta, 1);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _isDragging = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Value = Math.Round(Value + (e.Delta > 0 ? 0.5d : -0.5d), 1);
        e.Handled = true;
    }

    private static string FormatValue(double value)
    {
        var text = value.ToString("0.0", CultureInfo.InvariantCulture);
        return text.EndsWith(".0", StringComparison.Ordinal) ? text[..^2] : text;
    }

    private static Geometry CreateArc(Point center, double radius, double startAngle, double sweepAngle)
    {
        Point PointAt(double angle)
        {
            var radians = angle * Math.PI / 180d;
            return new Point(center.X + (radius * Math.Cos(radians)), center.Y + (radius * Math.Sin(radians)));
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(PointAt(startAngle), isFilled: false, isClosed: false);
            context.ArcTo(
                PointAt(startAngle + sweepAngle),
                new Size(radius, radius),
                0d,
                sweepAngle > 180d,
                SweepDirection.Clockwise,
                isStroked: true,
                isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static object CoerceKnobValue(DependencyObject d, object baseValue)
    {
        var knob = (Knob)d;
        var value = (double)baseValue;
        return double.IsNaN(value) ? knob.Minimum : Math.Clamp(value, knob.Minimum, knob.Maximum);
    }

    private static Brush Frozen(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
