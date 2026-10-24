using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace PulseWeaver;

internal sealed class MixerEngine : IDisposable
{
    internal const int InternalSampleRate = 48_000;

    private MMDeviceEnumerator? _deviceEnumerator;
    private MMDevice? _systemAudioDevice;
    private MMDevice? _microphoneDevice;
    private MMDevice? _outputDevice;
    private WasapiLoopbackCapture? _systemAudioCapture;
    private WasapiCapture? _microphoneCapture;
    private WasapiOut? _output;
    private VolumeSampleProvider? _systemVolume;
    private VolumeSampleProvider? _microphoneVolume;
    private bool _started;
    private bool _disposed;

    public MixerEngine(string systemAudioDeviceId, string microphoneDeviceId, string outputDeviceId, int outputLatencyMs = 100)
    {
        try
        {
            Initialize(systemAudioDeviceId, microphoneDeviceId, outputDeviceId, outputLatencyMs);
        }
        catch (Exception exception)
        {
            Dispose();
            if (exception is AudioStartupException)
            {
                throw;
            }

            throw new AudioStartupException($"音声エンジンを初期化できませんでした。{AudioErrorDescriber.Describe(exception)}", exception);
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        try
        {
            _systemAudioCapture!.StartRecording();
            _microphoneCapture!.StartRecording();
            _output!.Play();
            _started = true;
            AppLog.Info("ミックスを開始しました。");
        }
        catch (Exception exception)
        {
            throw new AudioStartupException($"音声ストリームを開始できませんでした。{AudioErrorDescriber.Describe(exception)}", exception);
        }
    }

    public void SetVolumes(float systemAudio, float microphone)
    {
        if (_systemVolume is not null)
        {
            _systemVolume.Volume = Math.Clamp(systemAudio, 0f, 1f);
        }

        if (_microphoneVolume is not null)
        {
            _microphoneVolume.Volume = Math.Clamp(microphone, 0f, 1f);
        }
    }

    private void Initialize(string systemAudioDeviceId, string microphoneDeviceId, string outputDeviceId, int outputLatencyMs)
    {
        _deviceEnumerator = new MMDeviceEnumerator();
        _systemAudioDevice = OpenDevice(systemAudioDeviceId, "システム音声（ループバック元）");
        _microphoneDevice = OpenDevice(microphoneDeviceId, "マイク");
        _outputDevice = OpenDevice(outputDeviceId, "ミックス出力");

        _systemAudioCapture = CreateCapture(() => new WasapiLoopbackCapture(_systemAudioDevice), "システム音声（ループバック元）");
        _microphoneCapture = CreateCapture(() => new WasapiCapture(_microphoneDevice), "マイク");

        var systemAudioBuffer = AttachBuffer(_systemAudioCapture, "システム音声");
        var microphoneBuffer = AttachBuffer(_microphoneCapture, "マイク");

        _systemVolume = new VolumeSampleProvider(CreateInputChain(systemAudioBuffer, "システム音声"));
        _microphoneVolume = new VolumeSampleProvider(CreateInputChain(microphoneBuffer, "マイク"));

        var mixer = new MixingSampleProvider(new ISampleProvider[] { _systemVolume, _microphoneVolume })
        {
            ReadFully = true
        };

        var outputFormat = GetOutputFormat(_outputDevice);
        try
        {
            _output = new WasapiOut(_outputDevice, AudioClientShareMode.Shared, true, Math.Clamp(outputLatencyMs, 20, 500));
            _output.Init(CreateOutputWaveProvider(mixer, outputFormat));
        }
        catch (Exception exception)
        {
            throw new AudioStartupException(
                $"出力デバイス「{_outputDevice.FriendlyName}」を開けませんでした。{AudioErrorDescriber.Describe(exception)}",
                exception);
        }

        AppLog.Info(
            $"ミキサー初期化: システム音声={_systemAudioDevice.FriendlyName} ({_systemAudioCapture.WaveFormat}), " +
            $"マイク={_microphoneDevice.FriendlyName} ({_microphoneCapture.WaveFormat}), " +
            $"出力={_outputDevice.FriendlyName} ({outputFormat}), 遅延={outputLatencyMs}ms");
    }

    private MMDevice OpenDevice(string deviceId, string label)
    {
        try
        {
            return _deviceEnumerator!.GetDevice(deviceId);
        }
        catch (Exception exception)
        {
            throw new AudioStartupException(
                $"{label}が見つかりません。デバイスを接続し直して選択してください。{AudioErrorDescriber.Describe(exception)}",
                exception);
        }
    }

    private static T CreateCapture<T>(Func<T> factory, string label)
        where T : WasapiCapture
    {
        try
        {
            return factory();
        }
        catch (Exception exception)
        {
            throw new AudioStartupException($"{label}を開けませんでした。{AudioErrorDescriber.Describe(exception)}", exception);
        }
    }

    private static BufferedWaveProvider AttachBuffer(WasapiCapture capture, string label)
    {
        var buffer = new BufferedWaveProvider(capture.WaveFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };

        capture.DataAvailable += (_, args) => buffer.AddSamples(args.Buffer, 0, args.BytesRecorded);
        capture.RecordingStopped += (_, args) =>
        {
            if (args.Exception is not null)
            {
                AppLog.Error($"{label}の録音が停止しました。", args.Exception);
            }
        };

        return buffer;
    }

    private static ISampleProvider CreateInputChain(BufferedWaveProvider buffer, string label)
    {
        var provider = buffer.ToSampleProvider();
        if (provider.WaveFormat.Channels == 1)
        {
            provider = new MonoToStereoSampleProvider(provider);
        }
        else if (provider.WaveFormat.Channels > 2)
        {
            provider = new MultiplexingSampleProvider(new[] { provider }, 2);
        }

        if (provider.WaveFormat.SampleRate != InternalSampleRate)
        {
            provider = new WdlResamplingSampleProvider(provider, InternalSampleRate);
        }

        AppLog.Info($"{label}の入力形式: {buffer.WaveFormat.Encoding} {buffer.WaveFormat.SampleRate}Hz {buffer.WaveFormat.Channels}ch -> 48kHz float stereo");
        return provider;
    }

    private static WaveFormat GetOutputFormat(MMDevice device)
    {
        try
        {
            return device.AudioClient.MixFormat;
        }
        catch (Exception exception)
        {
            throw new AudioStartupException(
                $"出力デバイス「{device.FriendlyName}」の音声形式を取得できませんでした。{AudioErrorDescriber.Describe(exception)}",
                exception);
        }
    }

    private static IWaveProvider CreateOutputWaveProvider(ISampleProvider mixer, WaveFormat outputFormat)
    {
        var provider = mixer;
        if (provider.WaveFormat.SampleRate != outputFormat.SampleRate)
        {
            provider = new WdlResamplingSampleProvider(provider, outputFormat.SampleRate);
        }

        if (provider.WaveFormat.Channels != outputFormat.Channels)
        {
            provider = outputFormat.Channels == 1
                ? new StereoToMonoSampleProvider(provider)
                : new MultiplexingSampleProvider(new[] { provider }, outputFormat.Channels);
        }

        var isFloat = outputFormat.Encoding == WaveFormatEncoding.IeeeFloat || outputFormat.BitsPerSample == 32;
        if (isFloat)
        {
            return new SampleToWaveProvider(provider);
        }

        return outputFormat.BitsPerSample == 24
            ? new SampleToWaveProvider24(provider)
            : new SampleToWaveProvider16(provider);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (_started)
            {
                _systemAudioCapture?.StopRecording();
                _microphoneCapture?.StopRecording();
                _output?.Stop();
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("音声ストリームの停止に失敗しました。", exception);
        }

        _output?.Dispose();
        _systemAudioCapture?.Dispose();
        _microphoneCapture?.Dispose();
        _systemAudioDevice?.Dispose();
        _microphoneDevice?.Dispose();
        _outputDevice?.Dispose();
        _deviceEnumerator?.Dispose();
    }
}
