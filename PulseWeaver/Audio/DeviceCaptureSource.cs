using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace PulseWeaver;

/// <summary>
/// WASAPI の録音デバイス、または再生デバイスのループバックを取り込む入力ソース。
/// </summary>
internal sealed class DeviceCaptureSource : IInputCapture
{
    private readonly WasapiCapture _capture;

    public DeviceCaptureSource(MMDevice device, bool loopback)
    {
        _capture = loopback
            ? new LowLatencyWasapiLoopbackCapture(device)
            : new WasapiCapture(device, useEventSync: false, AudioBufferSettings.CaptureLatencyMilliseconds);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
    }

    public WaveFormat WaveFormat => _capture.WaveFormat;

    public event EventHandler<WaveInEventArgs>? DataAvailable;

    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public void StartRecording() => _capture.StartRecording();

    public void StopRecording()
    {
        try
        {
            _capture.StopRecording();
        }
        catch (Exception exception)
        {
            AppLog.Error("録音の停止に失敗しました。", exception);
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args) => DataAvailable?.Invoke(this, args);

    private void OnRecordingStopped(object? sender, StoppedEventArgs args) => RecordingStopped?.Invoke(this, args);

    public void Dispose()
    {
        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
        _capture.Dispose();
    }

    private sealed class LowLatencyWasapiLoopbackCapture : WasapiCapture
    {
        public LowLatencyWasapiLoopbackCapture(MMDevice device)
            : base(device, useEventSync: false, AudioBufferSettings.CaptureLatencyMilliseconds)
        {
        }

        protected override AudioClientStreamFlags GetAudioClientStreamFlags() =>
            AudioClientStreamFlags.Loopback | base.GetAudioClientStreamFlags();
    }
}
