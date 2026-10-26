using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace PulseWeaver;

internal sealed record MixerInputDefinition(
    string Label,
    InputSourceKind Kind,
    string? DeviceId,
    int? ProcessId,
    string? ProcessName,
    bool IncludeProcessTree,
    float Volume,
    bool Muted,
    bool Solo,
    string? MonitorOutputId,
    float MonitorVolume,
    IReadOnlyDictionary<string, bool> Routes);

internal sealed record MixerOutputDefinition(string Id, string Label, string? DeviceId, float Volume);

internal sealed class MixerEngine : IDisposable
{
    internal const int InternalSampleRate = 48_000;

    private readonly MMDeviceEnumerator _deviceEnumerator = new();
    private readonly List<MMDevice> _devices = new();
    private readonly List<string> _startupIssues = new();
    private readonly object _issueLock = new();
    private readonly InputSource?[] _inputs;
    private readonly OutputTarget?[] _outputTargets;
    private readonly string[] _outputIds;
    private bool _started;
    private bool _disposed;

    public MixerEngine(
        IReadOnlyList<MixerInputDefinition> inputs,
        IReadOnlyList<MixerOutputDefinition> outputs,
        int outputLatencyMs = 100)
    {
        _inputs = new InputSource?[inputs.Count];
        _outputTargets = new OutputTarget?[outputs.Count];
        _outputIds = outputs.Select(output => output.Id).ToArray();

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
                        Volume = (float)Math.Clamp(outputs[i].Volume, 0f, MixerLimits.MaxVolume)
                    };
                }
                catch (Exception exception)
                {
                    AddIssue($"{outputs[i].Label} を開けませんでした。{AudioErrorDescriber.Describe(exception)}");
                }
            }

            for (var i = 0; i < inputs.Count; i++)
            {
                if (!HasSource(inputs[i]))
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

            ApplyAllGains();
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

    public IReadOnlyList<string> StartupIssues
    {
        get
        {
            lock (_issueLock)
            {
                return _startupIssues.ToArray();
            }
        }
    }

    public int ActiveOutputCount => _outputTargets.Count(target => target?.Output is not null);

    private static bool HasSource(MixerInputDefinition definition) => definition.Kind switch
    {
        InputSourceKind.Process => definition.ProcessId is > 0,
        _ => !string.IsNullOrWhiteSpace(definition.DeviceId)
    };

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
                $"音声出力を開始できませんでした。{string.Join(" / ", StartupIssues)}",
                new InvalidOperationException("起動できた出力デバイスがありません。"));
        }

        _started = true;
        AppLog.Info($"ミックスを開始しました（入力 {_inputs.Count(input => input is not null)} 件 / 出力 {startedOutputs} 件）。");
    }

    /// <summary>入力ごとのピーク値（0〜1）。読み出すと 0 にリセットされる。</summary>
    public float[] GetInputLevels()
    {
        var levels = new float[_inputs.Length];
        for (var i = 0; i < _inputs.Length; i++)
        {
            if (_inputs[i] is not { } input)
            {
                continue;
            }

            levels[i] = input.Peak;
            input.Peak = 0f;
        }

        return levels;
    }

    /// <summary>出力ごとのピーク値（0〜1）。読み出すと 0 にリセットされる。</summary>
    public float[] GetOutputLevels()
    {
        var levels = new float[_outputTargets.Length];
        for (var i = 0; i < _outputTargets.Length; i++)
        {
            if (_outputTargets[i]?.Meter is not { } meter)
            {
                continue;
            }

            levels[i] = meter.ConsumePeak();
        }

        return levels;
    }

    public void SetInputVolume(int inputIndex, float volume)
    {
        if (!TryGetInput(inputIndex, out var input))
        {
            return;
        }

        input.Volume = (float)Math.Clamp(volume, 0f, MixerLimits.MaxVolume);
        ApplyAllGains();
    }

    public void SetInputMuted(int inputIndex, bool muted)
    {
        if (!TryGetInput(inputIndex, out var input))
        {
            return;
        }

        input.Muted = muted;
        ApplyAllGains();
    }

    public void SetInputSolo(int inputIndex, bool solo)
    {
        if (!TryGetInput(inputIndex, out var input))
        {
            return;
        }

        input.Solo = solo;
        ApplyAllGains();
    }

    public void SetMonitorVolume(int inputIndex, float volume)
    {
        if (!TryGetInput(inputIndex, out var input))
        {
            return;
        }

        input.MonitorVolume = (float)Math.Clamp(volume, 0f, MixerLimits.MaxVolume);
        ApplyAllGains();
    }

    public void SetMonitorTarget(int inputIndex, string? outputId)
    {
        if (!TryGetInput(inputIndex, out var input))
        {
            return;
        }

        input.MonitorOutputId = string.IsNullOrWhiteSpace(outputId) ? null : outputId;
        ApplyAllGains();
    }

    public void SetOutputVolume(int outputIndex, float volume)
    {
        if (outputIndex < 0 || outputIndex >= _outputTargets.Length || _outputTargets[outputIndex] is not { } target)
        {
            return;
        }

        target.Volume = (float)Math.Clamp(volume, 0f, MixerLimits.MaxVolume);
        if (target.Master is not null)
        {
            target.Master.Volume = target.Volume;
        }
    }

    public void SetRouting(int inputIndex, int outputIndex, bool enabled)
    {
        if (!TryGetInput(inputIndex, out var input))
        {
            return;
        }

        if (outputIndex < 0 || outputIndex >= _outputIds.Length)
        {
            return;
        }

        input.Routes[_outputIds[outputIndex]] = enabled;
        ApplyGains(input);
    }

    private bool TryGetInput(int inputIndex, out InputSource input)
    {
        if (inputIndex >= 0 && inputIndex < _inputs.Length && _inputs[inputIndex] is { } found)
        {
            input = found;
            return true;
        }

        input = null!;
        return false;
    }

    private void ApplyAllGains()
    {
        var anySolo = _inputs.OfType<InputSource>().Any(input => input.Solo);
        foreach (var input in _inputs.OfType<InputSource>())
        {
            ApplyGains(input, anySolo);
        }
    }

    private void ApplyGains(InputSource input) => ApplyGains(input, _inputs.OfType<InputSource>().Any(other => other.Solo));

    private void ApplyGains(InputSource input, bool anySolo)
    {
        var effective = input.Muted || (anySolo && !input.Solo) ? 0f : input.Volume;

        for (var i = 0; i < input.Gains.Length; i++)
        {
            if (input.Gains[i] is not { } gain)
            {
                continue;
            }

            var outputId = _outputIds[i];
            if (!input.Routes.TryGetValue(outputId, out var routed) || !routed)
            {
                gain.Volume = 0f;
                continue;
            }

            gain.Volume = string.Equals(input.MonitorOutputId, outputId, StringComparison.Ordinal)
                ? effective * input.MonitorVolume
                : effective;
        }
    }

    private InputSource CreateInput(MixerInputDefinition definition)
    {
        IInputCapture capture = definition.Kind switch
        {
            InputSourceKind.Process => new ProcessLoopbackCapture(definition.ProcessId ?? 0, definition.IncludeProcessTree),
            InputSourceKind.Loopback => new DeviceCaptureSource(OpenDevice(definition.DeviceId!), loopback: true),
            _ => new DeviceCaptureSource(OpenDevice(definition.DeviceId!), loopback: false)
        };

        var buffers = new BufferedWaveProvider?[_outputTargets.Length];
        var gains = new VolumeSampleProvider?[_outputTargets.Length];

        for (var i = 0; i < _outputTargets.Length; i++)
        {
            if (_outputTargets[i] is null)
            {
                continue;
            }

            var buffer = new BufferedWaveProvider(capture.WaveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(2),
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };
            buffers[i] = buffer;
            gains[i] = new VolumeSampleProvider(CreateInternalChain(buffer));
        }

        var input = new InputSource
        {
            Label = definition.Label,
            Kind = definition.Kind,
            Capture = capture,
            Buffers = buffers,
            Gains = gains,
            Routes = new Dictionary<string, bool>(definition.Routes, StringComparer.Ordinal),
            Volume = (float)Math.Clamp(definition.Volume, 0f, MixerLimits.MaxVolume),
            Muted = definition.Muted,
            Solo = definition.Solo,
            MonitorOutputId = definition.MonitorOutputId,
            MonitorVolume = (float)Math.Clamp(definition.MonitorVolume, 0f, MixerLimits.MaxVolume),
            ProcessName = definition.ProcessName,
            IncludeProcessTree = definition.IncludeProcessTree,
            ProcessId = definition.ProcessId ?? 0
        };

        AttachCapture(input, capture);
        AppLog.Info($"{definition.Label} の入力形式: {capture.WaveFormat}");
        return input;
    }

    private void AttachCapture(InputSource input, IInputCapture capture)
    {
        var format = capture.WaveFormat;
        capture.DataAvailable += (_, args) =>
        {
            foreach (var buffer in input.Buffers)
            {
                buffer?.AddSamples(args.Buffer, 0, args.BytesRecorded);
            }

            var peak = PeakOf(args.Buffer, args.BytesRecorded, format);
            if (peak > input.Peak)
            {
                input.Peak = peak;
            }
        };

        capture.RecordingStopped += (_, args) =>
        {
            if (args.Exception is not null)
            {
                AppLog.Error($"{input.Label} の録音が停止しました。", args.Exception);
            }

            if (input.Kind == InputSourceKind.Process && _started && !_disposed)
            {
                BeginRecapture(input);
            }
        };
    }

    private static float PeakOf(byte[] buffer, int bytes, WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            var peak = 0f;
            var step = 4 * Math.Max(1, format.Channels);
            for (var i = 0; i + 4 <= bytes; i += step)
            {
                var value = Math.Abs(BitConverter.ToSingle(buffer, i));
                if (value > peak)
                {
                    peak = value;
                }
            }

            return Math.Min(1f, peak);
        }

        switch (format.BitsPerSample)
        {
            case 16:
            {
                var peak = 0f;
                for (var i = 0; i + 2 <= bytes; i += 2)
                {
                    var value = Math.Abs(BitConverter.ToInt16(buffer, i)) / 32768f;
                    if (value > peak)
                    {
                        peak = value;
                    }
                }

                return Math.Min(1f, peak);
            }

            case 24:
            {
                var peak = 0f;
                for (var i = 0; i + 3 <= bytes; i += 3)
                {
                    var sample = (buffer[i] << 8) | (buffer[i + 1] << 16) | (buffer[i + 2] << 24);
                    var value = Math.Abs(sample) / 2147483648f;
                    if (value > peak)
                    {
                        peak = value;
                    }
                }

                return Math.Min(1f, peak);
            }

            case 32:
            {
                var peak = 0f;
                for (var i = 0; i + 4 <= bytes; i += 4)
                {
                    var value = Math.Abs(BitConverter.ToInt32(buffer, i)) / 2147483648f;
                    if (value > peak)
                    {
                        peak = value;
                    }
                }

                return Math.Min(1f, peak);
            }

            default:
                return 0f;
        }
    }

    private void BeginRecapture(InputSource input)
    {
        if (string.IsNullOrWhiteSpace(input.ProcessName))
        {
            AddIssue($"「{input.Label}」のアプリが終了しました。");
            return;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            for (var attempt = 0; attempt < 15; attempt++)
            {
                Thread.Sleep(2000);

                if (_disposed || !_started)
                {
                    return;
                }

                var processId = AudioProcessCatalog.FindProcessIdByName(input.ProcessName);
                if (processId is null)
                {
                    continue;
                }

                try
                {
                    var capture = new ProcessLoopbackCapture(processId.Value, input.IncludeProcessTree);
                    if (!capture.WaveFormat.Equals(input.Capture.WaveFormat))
                    {
                        capture.Dispose();
                        AddIssue($"「{input.Label}」を再捕捉できませんでした（音声形式が変わりました）。");
                        return;
                    }

                    var old = input.Capture;
                    input.Capture = capture;
                    input.ProcessId = processId.Value;
                    AttachCapture(input, capture);
                    capture.StartRecording();
                    old.Dispose();

                    AddIssue($"「{input.Label}」のアプリが再起動したため、自動で再捕捉しました。");
                    AppLog.Info($"「{input.Label}」を PID {processId.Value} で再捕捉しました。");
                    return;
                }
                catch (Exception exception)
                {
                    AppLog.Error($"「{input.Label}」の再捕捉に失敗しました。", exception);
                }
            }

            AddIssue($"「{input.Label}」のアプリが見つからないため、取り込みを停止しました。");
        });
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
                target.Meter = new OutputLevelMeter(target.Master);
                var output = new WasapiOut(target.Device, AudioClientShareMode.Shared, true, Math.Clamp(outputLatencyMs, 20, 500));
                output.Init(CreateOutputWaveProvider(target.Meter, target.MixFormat));
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
        lock (_issueLock)
        {
            _startupIssues.Add(message);
        }

        AppLog.Info($"[警告] {message}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var input in _inputs.OfType<InputSource>())
        {
            try
            {
                input.Capture.StopRecording();
            }
            catch (Exception)
            {
                // 停止できなくても破棄を続ける
            }

            try
            {
                input.Capture.Dispose();
            }
            catch (Exception exception)
            {
                AppLog.Error($"{input.Label} の入力を解放できませんでした。", exception);
            }
        }

        foreach (var target in _outputTargets.OfType<OutputTarget>())
        {
            try
            {
                target.Output?.Stop();
                target.Output?.Dispose();
            }
            catch (Exception exception)
            {
                AppLog.Error($"{target.Label} の出力を解放できませんでした。", exception);
            }
        }

        foreach (var device in _devices)
        {
            try
            {
                device.Dispose();
            }
            catch (Exception)
            {
                // 既定のデバイスなどは解放できないことがある
            }
        }

        _deviceEnumerator.Dispose();
    }

    private sealed class InputSource
    {
        public required string Label { get; init; }

        public required InputSourceKind Kind { get; init; }

        public required IInputCapture Capture { get; set; }

        public required BufferedWaveProvider?[] Buffers { get; init; }

        public required VolumeSampleProvider?[] Gains { get; init; }

        public required Dictionary<string, bool> Routes { get; init; }

        public float Volume { get; set; }

        public bool Muted { get; set; }

        public bool Solo { get; set; }

        public string? MonitorOutputId { get; set; }

        public float MonitorVolume { get; set; }

        public string? ProcessName { get; set; }

        public bool IncludeProcessTree { get; set; }

        public int ProcessId { get; set; }

        public float Peak;
    }

    private sealed class OutputTarget
    {
        public required string Label { get; init; }

        public required MMDevice Device { get; init; }

        public required WaveFormat MixFormat { get; init; }

        public required float Volume { get; set; }

        public VolumeSampleProvider? Master { get; set; }

        public OutputLevelMeter? Meter { get; set; }

        public WasapiOut? Output { get; set; }
    }
}
