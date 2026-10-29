using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PulseWeaver;

/// <summary>
/// dB で操作するフェーダー。丸いつまみの下だけを塗り、上は縁取りだけにする。
/// 0 dB を超えると縁と塗りが赤くなる。右クリックで数値（0.1 dB 単位）を入力できる。
/// </summary>
public sealed class VolumeFader : FrameworkElement
{
    private const double ThumbRadius = 8d;
    private const double TrackThickness = 11d;

    private static readonly Brush NormalBrush = Frozen("#4F7DFF");
    private static readonly Brush OverBrush = Frozen("#E0574F");
    private static readonly Brush TrackBrush = Frozen("#66748C");
    private static readonly Brush ThumbBrush = Frozen("#E8EDF7");

    private bool _isDragging;
    private double _dragStartValue;
    private double _dragStartPosition;
    private Popup? _editor;
    private bool _isClosingEditor;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(double),
        typeof(VolumeFader),
        new FrameworkPropertyMetadata(
            0d,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender,
            null,
            CoerceFaderValue));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum),
        typeof(double),
        typeof(VolumeFader),
        new FrameworkPropertyMetadata(MixerGain.MinDb, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(double),
        typeof(VolumeFader),
        new FrameworkPropertyMetadata(MixerGain.MaxDb, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation),
        typeof(Orientation),
        typeof(VolumeFader),
        new FrameworkPropertyMetadata(Orientation.Vertical, FrameworkPropertyMetadataOptions.AffectsRender));

    public VolumeFader()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
        MinWidth = 22;
        MinHeight = 22;
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

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    private bool IsVertical => Orientation == Orientation.Vertical;

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0d || height <= 0d)
        {
            return;
        }

        // 透明で塗っておくと、トラックの外側でもマウス操作を受け取れる
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0d, 0d, width, height));

        var position = ToPosition(Value);
        var over = Value > 0d && !double.IsNegativeInfinity(Value);
        var brush = over ? OverBrush : NormalBrush;
        var trackPen = new Pen(over ? OverBrush : TrackBrush, 1.6d);

        if (IsVertical)
        {
            var x = (width - TrackThickness) / 2d;
            var top = ThumbRadius;
            var bottom = height - ThumbRadius;
            if (bottom <= top)
            {
                return;
            }

            var track = new Rect(x, top, TrackThickness, bottom - top);
            dc.DrawRoundedRectangle(null, trackPen, track, TrackThickness / 2d, TrackThickness / 2d);

            var thumbY = bottom - (position * (bottom - top));
            var fill = new Rect(x, thumbY, TrackThickness, bottom - thumbY);
            if (fill.Height > 0.5d)
            {
                dc.DrawRoundedRectangle(brush, null, fill, TrackThickness / 2d, TrackThickness / 2d);
            }

            dc.DrawEllipse(ThumbBrush, null, new Point(width / 2d, thumbY), ThumbRadius, ThumbRadius);
        }
        else
        {
            var y = (height - TrackThickness) / 2d;
            var left = ThumbRadius;
            var right = width - ThumbRadius;
            if (right <= left)
            {
                return;
            }

            var track = new Rect(left, y, right - left, TrackThickness);
            dc.DrawRoundedRectangle(null, trackPen, track, TrackThickness / 2d, TrackThickness / 2d);

            var thumbX = left + (position * (right - left));
            var fill = new Rect(left, y, thumbX - left, TrackThickness);
            if (fill.Width > 0.5d)
            {
                dc.DrawRoundedRectangle(brush, null, fill, TrackThickness / 2d, TrackThickness / 2d);
            }

            dc.DrawEllipse(ThumbBrush, null, new Point(thumbX, height / 2d), ThumbRadius, ThumbRadius);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();

        var point = e.GetPosition(this);
        var thumb = ThumbPoint();
        var nearThumb = IsVertical
            ? Math.Abs(point.Y - thumb.Y) <= ThumbRadius + 3d
            : Math.Abs(point.X - thumb.X) <= ThumbRadius + 3d;

        if (!nearThumb)
        {
            SetFromPoint(point);
        }

        _isDragging = true;
        _dragStartValue = Value;
        _dragStartPosition = IsVertical ? point.Y : point.X;
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

        var point = e.GetPosition(this);
        var span = IsVertical
            ? Math.Max(1d, ActualHeight - (ThumbRadius * 2d))
            : Math.Max(1d, ActualWidth - (ThumbRadius * 2d));
        var delta = (IsVertical ? _dragStartPosition - point.Y : point.X - _dragStartPosition) / span;
        var range = Maximum - Minimum;
        var start = double.IsNegativeInfinity(_dragStartValue) ? Minimum : _dragStartValue;
        SetValue(delta == 0d ? _dragStartValue : start + (delta * range));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        EndDrag();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        EndDrag();
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        ShowEditor();
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var current = double.IsNegativeInfinity(Value) ? Minimum : Value;
        SetValue(current + (e.Delta > 0 ? 0.5d : -0.5d));
        e.Handled = true;
    }

    private void EndDrag()
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
    }

    private Point ThumbPoint()
    {
        var position = ToPosition(Value);
        return IsVertical
            ? new Point(ActualWidth / 2d, (ActualHeight - ThumbRadius) - (position * (ActualHeight - (ThumbRadius * 2d))))
            : new Point(ThumbRadius + (position * (ActualWidth - (ThumbRadius * 2d))), ActualHeight / 2d);
    }

    private void SetFromPoint(Point point)
    {
        var span = IsVertical
            ? ActualHeight - (ThumbRadius * 2d)
            : ActualWidth - (ThumbRadius * 2d);
        if (span <= 0d)
        {
            return;
        }

        var position = IsVertical
            ? ((ActualHeight - ThumbRadius) - point.Y) / span
            : (point.X - ThumbRadius) / span;

        position = Math.Clamp(position, 0d, 1d);
        SetValue(position <= 0.004d ? double.NegativeInfinity : Minimum + (position * (Maximum - Minimum)));
    }

    private void SetValue(double value)
    {
        var snapped = double.IsNegativeInfinity(value)
            ? double.NegativeInfinity
            : Math.Clamp(Math.Round(value / MixerGain.StepDb) * MixerGain.StepDb, Minimum, Maximum);
        Value = snapped;
    }

    private double ToPosition(double value)
    {
        var range = Maximum - Minimum;
        if (range <= 0d)
        {
            return 0d;
        }

        if (double.IsNegativeInfinity(value))
        {
            return 0d;
        }

        return Math.Clamp((value - Minimum) / range, 0d, 1d);
    }

    private void ShowEditor()
    {
        CloseEditor();

        var box = new TextBox
        {
            Text = MixerGain.Format(Value),
            Width = 78,
            Height = 26,
            FontSize = 13,
            TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x22, 0x30)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
            BorderThickness = new Thickness(0),
            CaretBrush = NormalBrush
        };

        var host = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0F, 0x16)),
            BorderBrush = NormalBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 4, 6, 4),
            Child = box
        };

        _editor = new Popup
        {
            Child = host,
            PlacementTarget = this,
            Placement = PlacementMode.Center,
            StaysOpen = false,
            AllowsTransparency = true
        };

        box.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                CommitEditor(box.Text);
                args.Handled = true;
            }
            else if (args.Key == Key.Escape)
            {
                CloseEditor();
                args.Handled = true;
            }
        };

        box.LostKeyboardFocus += (_, _) => CommitEditor(box.Text);
        _editor.Closed += (_, _) =>
        {
            _editor = null;
            CommitEditor(box.Text);
        };
        _editor.IsOpen = true;
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(() =>
            {
                box.Focus();
                box.SelectAll();
            }));
    }

    private void CommitEditor(string text)
    {
        if (_isClosingEditor)
        {
            return;
        }

        if (MixerGain.TryParseDb(text, out var db))
        {
            Value = db;
        }

        CloseEditor();
    }

    private void CloseEditor()
    {
        if (_editor is null)
        {
            return;
        }

        _isClosingEditor = true;
        try
        {
            _editor.IsOpen = false;
            _editor = null;
        }
        finally
        {
            _isClosingEditor = false;
        }
    }

    private static object CoerceFaderValue(DependencyObject d, object baseValue)
    {
        var fader = (VolumeFader)d;
        var value = (double)baseValue;
        if (double.IsNegativeInfinity(value) || double.IsNaN(value))
        {
            return double.NegativeInfinity;
        }

        return Math.Clamp(value, fader.Minimum, fader.Maximum);
    }

    private static Brush Frozen(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
