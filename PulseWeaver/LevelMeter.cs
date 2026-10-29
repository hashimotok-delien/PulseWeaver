using System.Windows;
using System.Windows.Media;

namespace PulseWeaver;

/// <summary>
/// 緑・黄・赤の 3 段階で光るレベルメーター。
/// 緑が下半分、黄と赤で上半分を占め、上へ行くほど 1 目盛りあたりのレベル幅が広くなるため
/// 赤の一番上までは届きにくい。
/// </summary>
public sealed class LevelMeter : FrameworkElement
{
    /// <summary>緑がここまで（高さに対する割合）。</summary>
    private const double GreenTop = 0.5d;

    /// <summary>黄がここまで。ここから上は赤。</summary>
    private const double YellowTop = 0.7d;

    /// <summary>緑の上端にあたる振幅（-16.5 dB 相当）。</summary>
    private const double GreenLevelTop = 0.15d;

    /// <summary>黄の上端にあたる振幅（-6.9 dB 相当）。ここから上は赤。</summary>
    private const double YellowLevelTop = 0.45d;

    private static readonly Brush TrackBrush = Frozen("#232B3A");
    private static readonly Brush SeparatorBrush = Frozen("#10141C");
    private static readonly Brush MeterBrush = CreateMeterBrush();

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level),
        typeof(double),
        typeof(LevelMeter),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0d || height <= 0d)
        {
            return;
        }

        var radius = Math.Min(width, height) / 2d;
        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0d, 0d, width, height), radius, radius);

        var level = Math.Clamp(double.IsNaN(Level) ? 0d : Level, 0d, 1d);
        var filled = ToDisplayPosition(level) * height;
        if (filled <= 0.5d)
        {
            return;
        }

        dc.PushClip(new RectangleGeometry(new Rect(0d, height - filled, width, filled), radius, radius));
        dc.DrawRectangle(MeterBrush, null, new Rect(0d, 0d, width, height));

        // 色の変わり目に薄い区切りを入れて、段の位置が分かるようにする
        DrawSeparator(dc, height, width, GreenTop);
        DrawSeparator(dc, height, width, YellowTop);
        dc.Pop();
    }

    private static void DrawSeparator(DrawingContext dc, double height, double width, double ratio)
    {
        var y = height - (height * ratio);
        dc.DrawRectangle(SeparatorBrush, null, new Rect(0d, y - 0.5d, width, 1d));
    }

    /// <summary>
    /// 振幅を表示位置（0〜1）へ。緑が下半分、黄が 50〜70%、赤が 70〜100% を占め、
    /// 上の帯ほどレベルの幅が広くなるので赤の頂上はフルスケール付近でしか届かない。
    /// </summary>
    private static double ToDisplayPosition(double level)
    {
        if (level <= 0d)
        {
            return 0d;
        }

        if (level >= 1d)
        {
            return 1d;
        }

        if (level <= GreenLevelTop)
        {
            return level / GreenLevelTop * GreenTop;
        }

        if (level <= YellowLevelTop)
        {
            return GreenTop + ((level - GreenLevelTop) / (YellowLevelTop - GreenLevelTop) * (YellowTop - GreenTop));
        }

        return YellowTop + ((level - YellowLevelTop) / (1d - YellowLevelTop) * (1d - YellowTop));
    }

    private static Brush CreateMeterBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0d, 1d),
            EndPoint = new Point(0d, 0d)
        };

        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x3D, 0xC9, 0x8F), 0d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x52, 0xD8, 0xA0), GreenTop - 0.001d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xE8, 0xC5, 0x4A), GreenTop));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xE8, 0xC5, 0x4A), YellowTop - 0.001d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xE0, 0x57, 0x4F), YellowTop));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xF2, 0x6B, 0x62), 1d));
        brush.Freeze();
        return brush;
    }

    private static Brush Frozen(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
