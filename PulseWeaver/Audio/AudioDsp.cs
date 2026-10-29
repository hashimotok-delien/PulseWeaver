using NAudio.Wave;

namespace PulseWeaver;

/// <summary>入力ストリップの DSP 設定。UI スレッドが書き、音声スレッドが読む。</summary>
internal sealed class InputDspParameters
{
    private double _gate;
    private double _comp;
    private double _panX;
    private double _panY;
    private int _mono;

    /// <summary>ノイズゲートの効き（0 = 切、10 = 最大）。</summary>
    public double Gate
    {
        get => Volatile.Read(ref _gate);
        set => Volatile.Write(ref _gate, Math.Clamp(value, 0d, 10d));
    }

    /// <summary>コンプレッサーの効き（0 = 切、10 = 最大）。</summary>
    public double Comp
    {
        get => Volatile.Read(ref _comp);
        set => Volatile.Write(ref _comp, Math.Clamp(value, 0d, 10d));
    }

    /// <summary>左右の定位（-1 = 左、0 = 中央、+1 = 右）。</summary>
    public double PanX
    {
        get => Volatile.Read(ref _panX);
        set => Volatile.Write(ref _panX, Math.Clamp(value, -1d, 1d));
    }

    /// <summary>前後の定位（-1 = 背面、0 = 中央、+1 = 正面）。</summary>
    public double PanY
    {
        get => Volatile.Read(ref _panY);
        set => Volatile.Write(ref _panY, Math.Clamp(value, -1d, 1d));
    }

    public bool Mono
    {
        get => Volatile.Read(ref _mono) != 0;
        set => Volatile.Write(ref _mono, value ? 1 : 0);
    }
}

/// <summary>出力バスの DSP 設定。</summary>
internal sealed class OutputDspParameters
{
    private int _mono;
    private int _eqEnabled;
    private double _eqLow;
    private double _eqMid;
    private double _eqHigh;

    public bool Mono
    {
        get => Volatile.Read(ref _mono) != 0;
        set => Volatile.Write(ref _mono, value ? 1 : 0);
    }

    public bool EqEnabled
    {
        get => Volatile.Read(ref _eqEnabled) != 0;
        set => Volatile.Write(ref _eqEnabled, value ? 1 : 0);
    }

    /// <summary>低域（120 Hz シェルフ）の増減。0 dB で変化なし。</summary>
    public double EqLow
    {
        get => Volatile.Read(ref _eqLow);
        set => Volatile.Write(ref _eqLow, Math.Clamp(value, -12d, 12d));
    }

    /// <summary>中域（1 kHz ピーク）の増減。</summary>
    public double EqMid
    {
        get => Volatile.Read(ref _eqMid);
        set => Volatile.Write(ref _eqMid, Math.Clamp(value, -12d, 12d));
    }

    /// <summary>高域（8 kHz シェルフ）の増減。</summary>
    public double EqHigh
    {
        get => Volatile.Read(ref _eqHigh);
        set => Volatile.Write(ref _eqHigh, Math.Clamp(value, -12d, 12d));
    }
}

/// <summary>
/// 入力ストリップの音声処理。ゲート → コンプ → モノラル化 → 3D 定位の順に適用する。
/// 定位は左右のバランスと、前後を距離感（減衰 + 高域の丸まり）で表す簡易 3D パン。
/// </summary>
internal sealed class InputDspSampleProvider : ISampleProvider
{
    private const float GateAttackMs = 1f;
    private const float GateReleaseMs = 140f;
    private const float CompAttackMs = 10f;
    private const float CompReleaseMs = 220f;

    private readonly ISampleProvider _source;
    private readonly InputDspParameters _parameters;

    private float _gateEnvelope;
    private float _gateGain = 1f;
    private float _compEnvelope;
    private float _compGain = 1f;
    private float _lowPassL;
    private float _lowPassR;
    private float _lowPassCoefficient = 1f;

    public InputDspSampleProvider(ISampleProvider source, InputDspParameters parameters)
    {
        _source = source;
        _parameters = parameters;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        var channels = WaveFormat.Channels;
        if (channels < 2)
        {
            return read;
        }

        var gateKnob = _parameters.Gate;
        var compKnob = _parameters.Comp;
        var panX = _parameters.PanX;
        var panY = _parameters.PanY;
        var mono = _parameters.Mono;

        var sampleRate = WaveFormat.SampleRate;
        var gateAttack = Coef(GateAttackMs, sampleRate);
        var gateRelease = Coef(GateReleaseMs, sampleRate);
        var compAttack = Coef(CompAttackMs, sampleRate);
        var compRelease = Coef(CompReleaseMs, sampleRate);

        var gateThreshold = Math.Pow(10d, (-70d + (4d * gateKnob)) / 20d);
        var gateCloseThreshold = gateThreshold * 0.7d;
        var compThresholdDb = -6d - (3d * compKnob);
        var compRatio = 1d + (0.5d * compKnob);
        var compMakeupDb = 1.5d * compKnob;

        var leftGain = panX > 0d ? 1f - (float)panX : 1f;
        var rightGain = panX < 0d ? 1f + (float)panX : 1f;
        var distanceGain = 1f - (0.35f * (float)((1d - panY) / 2d));
        var lowPassTarget = panY >= 0d
            ? 1f
            : (float)(1d - Math.Exp(-2d * Math.PI * Lerp(20000d, 2500d, -panY) / sampleRate));
        _lowPassCoefficient = lowPassTarget;

        for (var i = 0; i + channels <= read; i += channels)
        {
            var left = buffer[offset + i];
            var right = buffer[offset + i + 1];
            var peak = Math.Max(Math.Abs(left), Math.Abs(right));

            if (gateKnob > 0.01d)
            {
                _gateEnvelope += (peak - _gateEnvelope) * (peak > _gateEnvelope ? gateAttack : gateRelease);
                var open = _gateGain > 0.5f
                    ? _gateEnvelope > gateCloseThreshold
                    : _gateEnvelope > gateThreshold;
                var target = open ? 1f : 0f;
                _gateGain += (target - _gateGain) * (target > _gateGain ? gateAttack : gateRelease);
                left *= _gateGain;
                right *= _gateGain;
            }
            else
            {
                _gateGain = 1f;
                _gateEnvelope = 0f;
            }

            if (compKnob > 0.01d)
            {
                _compEnvelope += (peak - _compEnvelope) * (peak > _compEnvelope ? compAttack : compRelease);
                var levelDb = 20d * Math.Log10(Math.Max(_compEnvelope, 1e-6f));
                var gainDb = levelDb > compThresholdDb
                    ? -(levelDb - compThresholdDb) * (1d - (1d / compRatio))
                    : 0d;
                var target = (float)Math.Pow(10d, (gainDb + compMakeupDb) / 20d);
                _compGain += (target - _compGain) * (target > _compGain ? compAttack : compRelease);
                left *= _compGain;
                right *= _compGain;
            }
            else
            {
                _compGain = 1f;
                _compEnvelope = 0f;
            }

            if (mono)
            {
                var mixed = (left + right) * 0.5f;
                left = mixed;
                right = mixed;
            }

            if (_lowPassCoefficient < 1f)
            {
                _lowPassL += (left - _lowPassL) * _lowPassCoefficient;
                _lowPassR += (right - _lowPassR) * _lowPassCoefficient;
                left = _lowPassL;
                right = _lowPassR;
            }

            buffer[offset + i] = left * leftGain * distanceGain;
            buffer[offset + i + 1] = right * rightGain * distanceGain;
        }

        return read;
    }

    private static float Coef(float milliseconds, int sampleRate) =>
        (float)(1d - Math.Exp(-1d / (milliseconds * 0.001d * sampleRate)));

    private static double Lerp(double from, double to, double amount) => from + ((to - from) * amount);
}

/// <summary>出力バスの音声処理。モノラル化と 3 バンド EQ。</summary>
internal sealed class OutputDspSampleProvider : ISampleProvider
{
    private const double LowFrequency = 120d;
    private const double MidFrequency = 1000d;
    private const double HighFrequency = 8000d;

    private readonly ISampleProvider _source;
    private readonly OutputDspParameters _parameters;
    private readonly Biquad _low;
    private readonly Biquad _mid;
    private readonly Biquad _high;
    private bool _applied;
    private double _appliedLow;
    private double _appliedMid;
    private double _appliedHigh;

    public OutputDspSampleProvider(ISampleProvider source, OutputDspParameters parameters)
    {
        _source = source;
        _parameters = parameters;
        _low = new Biquad();
        _mid = new Biquad();
        _high = new Biquad();
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        var channels = WaveFormat.Channels;

        var mono = _parameters.Mono;
        var eqEnabled = _parameters.EqEnabled;
        var eqLow = _parameters.EqLow;
        var eqMid = _parameters.EqMid;
        var eqHigh = _parameters.EqHigh;

        if (eqEnabled && (!_applied || eqLow != _appliedLow || eqMid != _appliedMid || eqHigh != _appliedHigh))
        {
            var sampleRate = WaveFormat.SampleRate;
            _low.SetLowShelf(sampleRate, LowFrequency, eqLow);
            _mid.SetPeak(sampleRate, MidFrequency, eqMid, 0.9d);
            _high.SetHighShelf(sampleRate, HighFrequency, eqHigh);
            _applied = true;
            _appliedLow = eqLow;
            _appliedMid = eqMid;
            _appliedHigh = eqHigh;
        }

        if (!mono && !eqEnabled)
        {
            return read;
        }

        for (var i = 0; i + channels <= read; i += channels)
        {
            if (eqEnabled)
            {
                for (var channel = 0; channel < channels; channel++)
                {
                    var value = buffer[offset + i + channel];
                    value = _low.Process(value);
                    value = _mid.Process(value);
                    value = _high.Process(value);
                    buffer[offset + i + channel] = value;
                }
            }

            if (mono && channels > 1)
            {
                var sum = 0f;
                for (var channel = 0; channel < channels; channel++)
                {
                    sum += buffer[offset + i + channel];
                }

                sum /= channels;
                for (var channel = 0; channel < channels; channel++)
                {
                    buffer[offset + i + channel] = sum;
                }
            }
        }

        return read;
    }
}

/// <summary>RBJ Audio EQ Cookbook の双二次フィルター。</summary>
internal sealed class Biquad
{
    private double _b0 = 1d;
    private double _b1;
    private double _b2;
    private double _a1;
    private double _a2;
    private double _z1;
    private double _z2;

    public void SetLowShelf(double sampleRate, double frequency, double gainDb)
    {
        var a = Math.Pow(10d, gainDb / 40d);
        var w0 = 2d * Math.PI * frequency / sampleRate;
        var cos = Math.Cos(w0);
        var alpha = Math.Sin(w0) / 2d * Math.Sqrt(2d);
        var sqrtA = Math.Sqrt(a);

        var b0 = a * ((a + 1d) - ((a - 1d) * cos) + (2d * sqrtA * alpha));
        var b1 = 2d * a * ((a - 1d) - ((a + 1d) * cos));
        var b2 = a * ((a + 1d) - ((a - 1d) * cos) - (2d * sqrtA * alpha));
        var a0 = (a + 1d) + ((a - 1d) * cos) + (2d * sqrtA * alpha);
        var a1 = -2d * ((a - 1d) + ((a + 1d) * cos));
        var a2 = (a + 1d) + ((a - 1d) * cos) - (2d * sqrtA * alpha);

        Set(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }

    public void SetPeak(double sampleRate, double frequency, double gainDb, double q)
    {
        var a = Math.Pow(10d, gainDb / 40d);
        var w0 = 2d * Math.PI * frequency / sampleRate;
        var cos = Math.Cos(w0);
        var alpha = Math.Sin(w0) / (2d * q);

        var b0 = 1d + (alpha * a);
        var b1 = -2d * cos;
        var b2 = 1d - (alpha * a);
        var a0 = 1d + (alpha / a);
        var a1 = -2d * cos;
        var a2 = 1d - (alpha / a);

        Set(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }

    public void SetHighShelf(double sampleRate, double frequency, double gainDb)
    {
        var a = Math.Pow(10d, gainDb / 40d);
        var w0 = 2d * Math.PI * frequency / sampleRate;
        var cos = Math.Cos(w0);
        var alpha = Math.Sin(w0) / 2d * Math.Sqrt(2d);
        var sqrtA = Math.Sqrt(a);

        var b0 = a * ((a + 1d) + ((a - 1d) * cos) + (2d * sqrtA * alpha));
        var b1 = -2d * a * ((a - 1d) + ((a + 1d) * cos));
        var b2 = a * ((a + 1d) + ((a - 1d) * cos) - (2d * sqrtA * alpha));
        var a0 = (a + 1d) - ((a - 1d) * cos) + (2d * sqrtA * alpha);
        var a1 = 2d * ((a - 1d) - ((a + 1d) * cos));
        var a2 = (a + 1d) - ((a - 1d) * cos) - (2d * sqrtA * alpha);

        Set(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }

    public float Process(float input)
    {
        var output = (_b0 * input) + _z1;
        _z1 = (_b1 * input) - (_a1 * output) + _z2;
        _z2 = (_b2 * input) - (_a2 * output);
        return (float)output;
    }

    private void Set(double b0, double b1, double b2, double a1, double a2)
    {
        _b0 = b0;
        _b1 = b1;
        _b2 = b2;
        _a1 = a1;
        _a2 = a2;
    }
}
