using NAudio.Wave;

namespace PulseWeaver;

/// <summary>
/// 出力のピーク値を測りながら素通しするサンプルプロバイダー。
/// 音量が 100% を超えると 1.0 を超えるサンプルが出るため、16bit 変換で折り返さないようここでクリップする。
/// </summary>
internal sealed class OutputLevelMeter : ISampleProvider
{
    private readonly ISampleProvider _source;
    private float _peak;

    public OutputLevelMeter(ISampleProvider source) => _source = source;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        var peak = 0f;
        for (var i = offset; i < offset + read; i++)
        {
            var value = buffer[i];
            var magnitude = Math.Abs(value);
            if (magnitude > peak)
            {
                peak = magnitude;
            }

            if (value > 1f)
            {
                buffer[i] = 1f;
            }
            else if (value < -1f)
            {
                buffer[i] = -1f;
            }
        }

        if (peak > Volatile.Read(ref _peak))
        {
            Volatile.Write(ref _peak, peak);
        }

        return read;
    }

    /// <summary>前回の読み出し以降のピーク値（0〜1）。</summary>
    public float ConsumePeak() => Math.Min(1f, Interlocked.Exchange(ref _peak, 0f));
}
