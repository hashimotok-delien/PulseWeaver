using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PulseWeaver;

/// <summary>
/// 音源の定位を前後左右に動かすパッド。左右は音量バランス、前後は距離感（減衰と高域の丸まり）として効く。
/// 右クリックで中央へ戻る。
/// </summary>
public sealed class PanPad : FrameworkElement
{
    private static readonly Brush BackgroundBrush = Frozen("#10141C");
    private static readonly Pen BorderPen = FrozenPen("#2C374B", 1.2d);
    private static readonly Pen GuidePen = FrozenPen("#232B3A", 1d);
    private static readonly Brush DotBrush = Frozen("#4F7DFF");
    private static readonly Brush DotRingBrush = Frozen("#E8EDF7");

    private bool _isDragging;

    public static readonly DependencyProperty PanXProperty = DependencyProperty.Register(
        nameof(PanX),
        typeof(double),
        typeof(PanPad),
        new FrameworkPropertyMetadata(
            0d,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender,
            null,
            CoercePan));

    public static readonly DependencyProperty PanYProperty = DependencyProperty.Register(
        nameof(PanY),
        typeof(double),
        typeof(PanPad),
        new FrameworkPropertyMetadata(
            0d,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender,
            null,
            CoercePan));

    public PanPad()
    {
        Cursor = Cursors.Cross;
        MinWidth = 40d;
        MinHeight = 40d;
    }

    /// <summary>左右の定位（-1 = 左、+1 = 右）。</summary>
    public double PanX
    {
        get => (double)GetValue(PanXProperty);
        set => SetValue(PanXProperty, value);
    }

    /// <summary>前後の定位（-1 = 背面、+1 = 正面）。</summary>
    public double PanY
    {
        get => (double)GetValue(PanYProperty);
        set => SetValue(PanYProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0d || height <= 0d)
        {
            return;
        }

        var bounds = new Rect(0d, 0d, width, height);
        dc.DrawRoundedRectangle(BackgroundBrush, BorderPen, bounds, 8d, 8d);

        dc.DrawLine(GuidePen, new Point(width / 2d, 4d), new Point(width / 2d, height - 4d));
        dc.DrawLine(GuidePen, new Point(4d, height / 2d), new Point(width - 4d, height / 2d));

        var x = ToPadCoordinate(4d, width, PanX, true);
        var y = ToPadCoordinate(4d, height, PanY, false);
        dc.DrawEllipse(DotBrush, new Pen(DotRingBrush, 1.6d), new Point(x, y), 6d, 6d);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _isDragging = true;
        CaptureMouse();
        SetFromPoint(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isDragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        SetFromPoint(e.GetPosition(this));
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

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        PanX = 0d;
        PanY = 0d;
        e.Handled = true;
    }

    private void SetFromPoint(Point point)
    {
        var spanX = Math.Max(1d, ActualWidth - 8d);
        var spanY = Math.Max(1d, ActualHeight - 8d);
        PanX = ((point.X - 4d) / spanX * 2d) - 1d;
        PanY = 1d - ((point.Y - 4d) / spanY * 2d);
    }

    /// <summary>0..1 の位置をパッド内座標へ（前 = 上、左 = 左）。</summary>
    private static double ToPadCoordinate(double start, double length, double value, bool leftToRight)
    {
        var ratio = leftToRight ? (value + 1d) / 2d : (1d - value) / 2d;
        return start + (Math.Clamp(ratio, 0d, 1d) * (length - (start * 2d)));
    }

    private static object CoercePan(DependencyObject d, object baseValue)
    {
        var value = (double)baseValue;
        return double.IsNaN(value) ? 0d : Math.Clamp(value, -1d, 1d);
    }

    private static Brush Frozen(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(string color, double thickness)
    {
        var pen = new Pen(Frozen(color), thickness);
        pen.Freeze();
        return pen;
    }
}
