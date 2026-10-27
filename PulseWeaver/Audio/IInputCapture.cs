using NAudio.Wave;

namespace PulseWeaver;

internal static class AudioBufferSettings
{
    /// <summary>
    /// WASAPI のクライアントバッファ長。OS が音声を書き込んでおける余裕のことで、
    /// 取り出しは常に最速で行うため遅延そのものは増えない。
    /// 小さすぎると他のアプリで CPU が埋まったときに取りこぼし（音切れ）が起きる。
    /// </summary>
    public const int CaptureLatencyMilliseconds = 100;

    /// <summary>入力ごとのキューの上限。出力側が一瞬詰まっても捨てずに済むよう余裕を持たせる。</summary>
    public const int QueueDurationMilliseconds = 500;

    /// <summary>
    /// 再生を始める前に溜めておく長さ。溜めた分だけ遅延は増えるが、
    /// 他のアプリで負荷が上がっても音が途切れにくくなる。
    /// </summary>
    public const int OutputPrebufferMilliseconds = 60;

    /// <summary>溜め終わるのを待つ最大時間。無音のアプリでも起動が遅れないようにする。</summary>
    public const int OutputPrebufferTimeoutMilliseconds = 150;
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
