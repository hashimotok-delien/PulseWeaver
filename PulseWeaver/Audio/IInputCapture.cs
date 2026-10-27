using NAudio.Wave;

namespace PulseWeaver;

internal static class AudioBufferSettings
{
    public const int CaptureLatencyMilliseconds = 20;
    public const int QueueDurationMilliseconds = 250;
}

/// <summary>
/// マイク・バーチャル入力・アプリ（プロセス）を同じ形で扱うための入力ソース。
/// </summary>
internal interface IInputCapture : IDisposable
{
    WaveFormat WaveFormat { get; }

    event EventHandler<WaveInEventArgs>? DataAvailable;

    event EventHandler<StoppedEventArgs>? RecordingStopped;

    void StartRecording();

    void StopRecording();
}
