using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace PulseWeaver;

internal sealed record MixerInputDefinition(string Label, string? DeviceId, bool IsLoopback, float Volume, bool[] Routing);

internal sealed record MixerOutputDefinition(string Label, string? DeviceId, float Volume = 0.8f);

internal sealed class MixerEngine : IDisposable
{
    internal const int InternalSampleRate = 48_000;

    private readonly MMDeviceEnumerator _deviceEnumerator = new();
    private readonly List<MMDevice> _devices = new();
    private readonly List<string> _startupIssues = new();
    private readonly InputSource?[] _inputs;
    private readonly OutputTarget?[] _outputTargets;
    private bool _started;
    private bool _disposed;

    public MixerEngine(IReadOnlyList<MixerInputDefinition> inputs, IReadOnlyList<MixerOutputDefinition> outputs, int outputLatencyMs = 100)
    {
        _inputs = new InputSource?[inputs.Count];
        _outputTargets = new OutputTarget?[outputs.Count];

        try
        {
            for (var i = 0; i < outputs.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(outputs[i].DeviceId))
                {
                    continue;
                }

                try
                {
                    var device = OpenDevice(outputs[i].DeviceId!);
                    _outputTargets[i] = new OutputTarget
                    {
                        Label = outputs[i].Label,
                        Device = device,
                        MixFormat = device.AudioClient.MixFormat,
                        Volume = Math.Clamp(outputs[i].Volume, 0f, 1f)
                    };
                }
                catch (Exception exception)
                {
                    AddIssue($"{outputs[i].Label} を開けませんでした。{AudioErrorDescriber.Describe(exception)}");
                }
            }

            for (var i = 0; i < inputs.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(inputs[i].DeviceId))
                {
                    continue;
                }

                try
                {
                    _inputs[i] = CreateInput(inputs[i]);
                }
                catch (Exception exception)
                {
                    AddIssue($"{inputs[i].Label} を開けませんでした。{AudioErrorDescriber.Describe(exception)}");
                }
            }

            BuildOutputs(outputLatencyMs);
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

    public IReadOnlyList<string> StartupIssues => _startupIssues;

    public int ActiveOutputCount => _outputTargets.Count(target => target?.Output is not null);

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        foreach (var input in _inputs.OfType<InputSource>())
        {
            try
            {
                input.Capture.StartRecording();
            }
            catch (Exception exception)
            {
                AddIssue($"{input.Label} の録音を開始できませんでした。{AudioErrorDescriber.Describe(exception)}");
            }
        }

        var startedOutputs = 0;
        foreach (var target in _outputTargets.OfType<OutputTarget>())
        {
            if (target.Output is null)
            {
                continue;
            }

            try
            {
                target.Output.Play();
                startedOutputs++;
            }
            catch (Exception exception)
            {
                AddIssue($"{target.Label} の再生を開始できませんでした。{AudioErrorDescriber.Describe(exception)}");
            }
        }

        if (startedOutputs == 0)
        {
            throw new AudioStartupException(
                $"音声出力を開始できませんでした。{string.Join(" / ", _startupIssues)}",
                new InvalidOperationException("起動できた出力デバイスがありません。"));
        }

        _started = true;
        AppLog.Info($"ミックスを開始しました（入力 {_inputs.Count(input => input is not null)} 件 / 出力 {startedOutputs} 件）。");
    }

    public void SetInputVolume(int inputIndex, float volume)
    {
        if (inputIndex < 0 || inputIndex >= _inputs.Length || _inputs[inputIndex] is not { } input)
        {
            return;
        }

        input.Volume = Math.Clamp(volume, 0f, 1f);
        ApplyGains(input);
    }

    public void SetOutputVolume(int outputIndex, float volume)
    {
        if (outputIndex < 0 || outputIndex >= _outputTargets.Length || _outputTargets[outputIndex] is not { } target)
        {
            return;
        }

        target.Volume = Math.Clamp(volume, 0f, 1f);
        if (target.Master is not null)
        {
            target.Master.Volume = target.Volume;
        }
    }

    public void SetRouting(int inputIndex, int outputIndex, bool enabled)
    {
        if (inputIndex < 0 || inputIndex >= _inputs.Length || _inputs[inputIndex] is not { } input)
        {
            return;
        }

        if (outputIndex < 0 || outputIndex >= input.Routing.Length)
        {
            return;
        }

        input.Routing[outputIndex] = enabled;
        ApplyGains(input);
    }

    private static void ApplyGains(InputSource input)
    {
        for (var i = 0; i < input.Gains.Length; i++)
        {
            if (input.Gains[i] is { } gain)
            {
                gain.Volume = input.Routing[i] ? input.Volume : 0f;
            }
        }
    }

    private InputSource CreateInput(MixerInputDefinition definition)
    {
        var device = OpenDevice(definition.DeviceId!);
        WasapiCapture capture;
        try
        {
            capture = definition.IsLoopback
                ? new WasapiLoopbackCapture(device)
                : new WasapiCapture(device);
        }
        catch (Exception exception)
        {
            throw new AudioStartupException($"{definition.Label} を開けませんでした。{AudioErrorDescriber.Describe(exception)}", exception);
        }

        var buffers = new BufferedWaveProvider?[_outputTargets.Length];
        for (var i = 0; i < _outputTargets.Length; i++)
        {
            if (_outputTargets[i] is null)
            {
                continue;
            }

            buffers[i] = new BufferedWaveProvider(capture.WaveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(2),
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };
        }

        capture.DataAvailable += (_, args) =>
        {
            foreach (var buffer in buffers)
            {
                buffer?.AddSamples(args.Buffer, 0, args.BytesRecorded);
            }
        };
        capture.RecordingStopped += (_, args) =>
        {
            if (args.Exception is not null)
            {
                AppLog.Error($"{definition.Label} の録音が停止しました。", args.Exception);
            }
        };

        var gains = new VolumeSampleProvider?[buffers.Length];
        for (var i = 0; i < buffers.Length; i++)
        {
            if (buffers[i] is { } buffer)
            {
                gains[i] = new VolumeSampleProvider(CreateInternalChain(buffer));
            }
        }

        var input = new InputSource
        {
            Label = definition.Label,
            Capture = capture,
            Buffers = buffers,
            Gains = gains,
            Volume = Math.Clamp(definition.Volume, 0f, 1f),
            Routing = definition.Routing.Length == buffers.Length ? definition.Routing : new bool[buffers.Length]
        };

        ApplyGains(input);
        AppLog.Info($"{definition.Label} の入力形式: {capture.WaveFormat}");
        return input;
    }

    private void BuildOutputs(int outputLatencyMs)
    {
        for (var i = 0; i < _outputTargets.Length; i++)
        {
            if (_outputTargets[i] is not { } target)
            {
                continue;
            }

            var gains = _inputs
                .OfType<InputSource>()
                .Select(input => input.Gains[i])
                .Where(gain => gain is not null)
                .Select(gain => gain!)
                .ToArray();

            if (gains.Length == 0)
            {
                AddIssue($"{target.Label} に送る入力がありません。");
                continue;
            }

            try
            {
                var mixer = new MixingSampleProvider(gains) { ReadFully = true };
                target.Master = new VolumeSampleProvider(mixer) { Volume = target.Volume };
                var output = new WasapiOut(target.Device, AudioClientShareMode.Shared, true, Math.Clamp(outputLatencyMs, 20, 500));
                output.Init(CreateOutputWaveProvider(target.Master, target.MixFormat));
                target.Output = output;
                AppLog.Info($"{target.Label} の出力: {target.Device.FriendlyName} ({target.MixFormat}), 入力 {gains.Length} 件");
            }
            catch (Exception exception)
            {
                AddIssue($"{target.Label} を開けませんでした。{AudioErrorDescriber.Describe(exception)}");
            }
        }
    }

    private MMDevice OpenDevice(string deviceId)
    {
        var device = _deviceEnumerator.GetDevice(deviceId);
        _devices.Add(device);
        return device;
    }

    private static ISampleProvider CreateInternalChain(BufferedWaveProvider buffer)
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

        return provider;
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

    private void AddIssue(string message)
    {
        _startupIssues.Add(message);
        AppLog.Info($"[警告] {message}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var target in _outputTargets.OfType<OutputTarget>())
        {
            try
            {
                target.Output?.Stop();
            }
            catch (Exception exception)
            {
                AppLog.Error($"{target.Label} の停止に失敗しました。", exception);
            }

            target.Output?.Dispose();
        }

        foreach (var input in _inputs.OfType<InputSource>())
        {
            try
            {
                input.Capture.StopRecording();
            }
            catch (Exception exception)
            {
                AppLog.Error($"{input.Label} の録音停止に失敗しました。", exception);
            }

            input.Capture.Dispose();
        }

        foreach (var device in _devices)
        {
            device.Dispose();
        }

        _devices.Clear();
        _deviceEnumerator.Dispose();
    }

    private sealed class InputSource
    {
        public required string Label { get; init; }
        public required WasapiCapture Capture { get; init; }
        public required BufferedWaveProvider?[] Buffers { get; init; }
        public required VolumeSampleProvider?[] Gains { get; init; }
        public required bool[] Routing { get; init; }
        public float Volume { get; set; }
    }

    private sealed class OutputTarget
    {
        public required string Label { get; init; }
        public required MMDevice Device { get; init; }
        public required WaveFormat MixFormat { get; init; }
        public float Volume { get; set; }
        public VolumeSampleProvider? Master { get; set; }
        public WasapiOut? Output { get; set; }
    }
}
