using System.Globalization;

namespace PulseWeaver;

/// <summary>音量（リニアゲイン）と dB 表示の相互変換。</summary>
internal static class MixerGain
{
    /// <summary>フェーダー下端の dB。ここ以下は無音（-∞）として扱う。</summary>
    public const double MinDb = -60d;

    /// <summary>フェーダー上端の dB。MixerLimits.MaxVolume（2.0 ≒ +6 dB）と揃える。</summary>
    public const double MaxDb = 6d;

    /// <summary>操作できる最小単位（dB）。</summary>
    public const double StepDb = 0.1d;

    public static double ToDb(double gain) =>
        gain <= 0.0001d ? double.NegativeInfinity : 20d * Math.Log10(gain);

    public static double FromDb(double db) =>
        double.IsNegativeInfinity(db) || db <= MinDb
            ? 0d
            : Math.Clamp(Math.Pow(10d, db / 20d), 0d, MixerLimits.MaxVolume);

    /// <summary>0.1 dB 単位へ丸める。無音は -∞ のまま返す。</summary>
    public static double RoundDb(double db)
    {
        if (double.IsNegativeInfinity(db) || db <= MinDb)
        {
            return double.NegativeInfinity;
        }

        return Math.Round(db, 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>-∞ は「-∞」、整数値は小数点を省いて表示する（例: 0、-1、3.5）。</summary>
    public static string Format(double db)
    {
        if (double.IsNegativeInfinity(db) || db <= MinDb)
        {
            return "-∞";
        }

        var rounded = RoundDb(db);
        var text = rounded.ToString("0.0", CultureInfo.InvariantCulture);
        return text.EndsWith(".0", StringComparison.Ordinal)
            ? text[..^2]
            : text;
    }

    /// <summary>右クリック入力の文字列を解析する。-∞ や空文字は無音として扱う。</summary>
    public static bool TryParseDb(string? text, out double db)
    {
        db = double.NegativeInfinity;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim().Replace('−', '-').Replace("dB", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        if (normalized is "-∞" or "-inf" or "-infinity")
        {
            return true;
        }

        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        db = Math.Clamp(RoundDb(parsed), MinDb, MaxDb);
        return true;
    }
}
