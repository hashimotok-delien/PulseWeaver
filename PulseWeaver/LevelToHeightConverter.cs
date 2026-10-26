using System.Globalization;
using System.Windows.Data;

namespace PulseWeaver;

/// <summary>0〜1 のレベルを、指定した高さ（px）へ変換する。</summary>
public sealed class LevelToHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var level = value is double levelValue ? Math.Clamp(levelValue, 0d, 1d) : 0d;
        var maxHeight = double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var height)
            ? height
            : 0d;

        return level * maxHeight;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
