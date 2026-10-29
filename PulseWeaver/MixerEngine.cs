using System.Diagnostics;
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
    IReadOnlyDictionary<string, bool> Routes,
    bool Mono = false,
    double Gate = 0d,
    double Comp = 0d,
    double PanX = 0d,
    double PanY = 0d);

internal sealed record MixerOutputDefinition(
    string Id,
    string Label,
    string? DeviceId,
    float Volume,
    bool Muted = false,
    bool Mono = false,
    bool EqEnabled = false,
    double EqLow = 0d,
    double EqMid = 0d,
    double EqHigh = 0d);

internal sealed class MixerEngine : IDisposable
{
    internal const int InternalSampleRate = 48_000;

    private readonly MMDeviceEnumerator _deviceEnumerator = new();
    private readonly List<MMDevice> _devices = new();
    private readonly List<string> _startupIssues = new();
    private readonly object _issueLock = new();
    private readonly InputSource?[] _inputs;
    private readonly InputSource[] _activeInputs;
    private readonly float[] _inputLevels;
    private readonly float[] _outputLevels;
    private readonly OutputTarget?[] _outputTargets;
    private readonly string[] _outputIds;
    private bool _anySolo;
    private ProcessPriorityClass? _previousPriority;
    private bool _started;
    private bool _disposed;

    public MixerEngine(
        IReadOnlyList<MixerInputDefinition> inputs,
        IReadOnlyList<MixerOutputDefinition> outputs,
        int outputLatencyMs = 50)
    {
        _inputs = new InputSource?[inputs.Count];
        _activeInputs = Array.Empty<InputSource>();
        _inputLevels = new float[inputs.Count];
        _outputTargets = new OutputTarget?[outputs.Count];
        _outputLevels = new float[outputs.Count];
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
                        Volume = (float)Math.Clamp(outputs[i].Volume, 0f, MixerLimits.MaxVolume),
                        Muted = outputs[i].Muted,
                        Dsp = new OutputDspParameters
                        {
                            Mono = outputs[i].Mono,
                            EqEnabled = outputs[i].EqEnabled,
                            EqLow = outputs[i].EqLow,
                            EqMid = outputs[i].EqMid,
                            EqHigh = outputs[i].EqHigh
                        }
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

            _activeInputs = _inputs.OfType<InputSource>().ToArray();
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

        RaiseProcessPriority();

        foreach (var input in _activeInputs)
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
        WaitForOutputPrebuffer();

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
    /// <remarks>UI スレッドから一定間隔で読む前提で、配列を使い回して割り当てを抑える。</remarks>
    public float[] GetInputLevels()
    {
        for (var i = 0; i < _inputs.Length; i++)
        {
            if (_inputs[i] is not { } input)
            {
                _inputLevels[i] = 0f;
                continue;
            }

            _inputLevels[i] = input.ConsumePeak();
        }

        return _inputLevels;
    }

    /// <summary>出力ごとのピーク値（0〜1）。読み出すと 0 にリセットされる。</summary>
    public float[] GetOutputLevels()
    {
        for (var i = 0; i < _outputTargets.Length; i++)
        {
            _outputLevels[i] = _outputTargets[i]?.Meter?.ConsumePeak() ?? 0f;
        }

        return _outputLevels;
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
        ApplyOutputGain(target);
    }

    public void SetOutputMuted(int outputIndex, bool muted)
    {
        if (outputIndex < 0 || outputIndex >= _outputTargets.Length || _outputTargets[outputIndex] is not { } target)
        {
            return;
        }

        target.Muted = muted;
        ApplyOutputGain(target);
    }

    public void SetOutputMono(int outputIndex, bool mono)
    {
        if (outputIndex < 0 || outputIndex >= _outputTargets.Length || _outputTargets[outputIndex] is not { } target)
        {
            return;
        }

        target.Dsp.Mono = mono;
    }

    public void SetOutputEq(int outputIndex, bool enabled, double low, double mid, double high)
    {
        if (outputIndex < 0 || outputIndex >= _outputTargets.Length || _outputTargets[outputIndex] is not { } target)
        {
            return;
        }

        target.Dsp.EqLow = low;
        target.Dsp.EqMid = mid;
        target.Dsp.EqHigh = high;
        target.Dsp.EqEnabled = enabled;
    }

    public void SetInputMono(int inputIndex, bool mono)
    {
        if (TryGetInput(inputIndex, out var input))
        {
            input.Dsp.Mono = mono;
        }
    }

    public void SetInputGate(int inputIndex, double gate)
    {
        if (TryGetInput(inputIndex, out var input))
        {
            input.Dsp.Gate = gate;
        }
    }

    public void SetInputComp(int inputIndex, double comp)
    {
        if (TryGetInput(inputIndex, out var input))
        {
            input.Dsp.Comp = comp;
        }
    }

    public void SetInputPan(int inputIndex, double panX, double panY)
    {
        if (TryGetInput(inputIndex, out var input))
        {
            input.Dsp.PanX = panX;
            input.Dsp.PanY = panY;
        }
    }

    private static float OutputGain(OutputTarget target) => target.Muted ? 0f : target.Volume;

    private static void ApplyOutputGain(OutputTarget target)
    {
        if (target.Master is not null)
        {
            target.Master.Volume = OutputGain(target);
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
        _anySolo = _activeInputs.Any(input => input.Solo);
        foreach (var input in _activeInputs)
        {
            ApplyGains(input, _anySolo);
        }
    }

    private void ApplyGains(InputSource input) => ApplyGains(input, _anySolo);

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
            var routed = input.Routes.TryGetValue(outputId, out var enabled) && enabled;
            var volume = !routed
                ? 0f
                : string.Equals(input.MonitorOutputId, outputId, StringComparison.Ordinal)
                    ? effective * input.MonitorVolume
                    : effective;

            gain.Volume = volume;
            SetGainActive(input, i, volume > 0f);
        }
    }

    /// <summary>
    /// 聞こえない出力には音声を積まないようにする。積むのを再開するときは古い音が
    /// 一気に鳴らないようキューを空にする（ミュート解除時の唐突な再生も防げる）。
    /// </summary>
    private static void SetGainActive(InputSource input, int index, bool active)
    {
        if (input.Active[index] == active)
        {
            return;
        }

        if (active)
        {
            input.Buffers[index]?.ClearBuffer();
        }

        input.Active[index] = active;
    }

    /// <summary>
    /// 出力を鳴らし始める前に、入力キューへ少しだけ音を溜める。
    /// 再生と取り込みが同じ速さで進むため、一度でも空になると溜め直されず、
    /// 負荷が高い間ずっと音切れが続いてしまう。最初に余裕を作っておくと途切れにくい。
    /// </summary>
    private void WaitForOutputPrebuffer()
    {
        if (_activeInputs.Length == 0 || _outputTargets.All(target => target?.Output is null))
        {
            return;
        }

        var deadline = Environment.TickCount64 + AudioBufferSettings.OutputPrebufferTimeoutMilliseconds;
        while (!_disposed && Environment.TickCount64 < deadline)
        {
            var buffered = true;
            foreach (var input in _activeInputs)
            {
                for (var i = 0; i < input.Buffers.Length; i++)
                {
                    if (!input.Active[i] || input.Buffers[i] is not { } buffer)
                    {
                        continue;
                    }

                    var target = Math.Max(1, buffer.WaveFormat.AverageBytesPerSecond
                        * AudioBufferSettings.OutputPrebufferMilliseconds / 1000);
                    if (buffer.BufferedBytes < target)
                    {
                        buffered = false;
                    }
                }
            }

            if (buffered)
            {
                return;
            }

            Thread.Sleep(10);
        }
    }

    private void RaiseProcessPriority()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            using var process = Process.GetCurrentProcess();
            _previousPriority ??= process.PriorityClass;
            if (process.PriorityClass < ProcessPriorityClass.AboveNormal)
            {
                process.PriorityClass = ProcessPriorityClass.AboveNormal;
            }
        }
        catch (Exception exception)
        {
            // 優先度を上げられなくても音声処理は続けられる
            AppLog.Info($"プロセスの優先度を変更できませんでした。{AudioErrorDescriber.Describe(exception)}");
        }
    }

    private void RestoreProcessPriority()
    {
        if (_previousPriority is not { } previous)
        {
            return;
        }

        _previousPriority = null;
        try
        {
            using var process = Process.GetCurrentProcess();
            if (process.PriorityClass != previous)
            {
                process.PriorityClass = previous;
            }
        }
        catch (Exception exception)
        {
            AppLog.Info($"プロセスの優先度を戻せませんでした。{AudioErrorDescriber.Describe(exception)}");
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
        var dspParameters = new InputDspParameters
        {
            Gate = definition.Gate,
            Comp = definition.Comp,
            PanX = definition.PanX,
            PanY = definition.PanY,
            Mono = definition.Mono
        };

        for (var i = 0; i < _outputTargets.Length; i++)
        {
            if (_outputTargets[i] is null)
            {
                continue;
            }

            var buffer = new BufferedWaveProvider(capture.WaveFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(AudioBufferSettings.QueueDurationMilliseconds),
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };
            buffers[i] = buffer;
            gains[i] = new VolumeSampleProvider(
                new InputDspSampleProvider(CreateInternalChain(buffer), dspParameters));
        }

        var input = new InputSource
        {
            Label = definition.Label,
            Kind = definition.Kind,
            Capture = capture,
            Buffers = buffers,
            Gains = gains,
            Dsp = dspParameters,
            Active = new bool[_outputTargets.Length],
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
            var buffers = input.Buffers;
            var active = input.Active;
            for (var i = 0; i < buffers.Length; i++)
            {
                if (!active[i])
                {
                    continue;
                }

                buffers[i]?.AddSamples(args.Buffer, 0, args.BytesRecorded);
            }

            input.ReportPeak(PeakOf(args.Buffer, args.BytesRecorded, format));
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

            var gains = _activeInputs
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
                target.Master = new VolumeSampleProvider(new OutputDspSampleProvider(mixer, target.Dsp))
                {
                    Volume = OutputGain(target)
                };
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

        foreach (var input in _activeInputs)
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
        RestoreProcessPriority();
    }

    private sealed class InputSource
    {
        private float _peak;

        public required string Label { get; init; }

        public required InputSourceKind Kind { get; init; }

        public required IInputCapture Capture { get; set; }

        public required BufferedWaveProvider?[] Buffers { get; init; }

        public required VolumeSampleProvider?[] Gains { get; init; }

        /// <summary>出力ごとに音声を積んでいるか。UI スレッドが書き、取り込みスレッドが読む。</summary>
        public required bool[] Active { get; init; }

        public required Dictionary<string, bool> Routes { get; init; }

        public required InputDspParameters Dsp { get; init; }

        public float Volume { get; set; }

        public bool Muted { get; set; }

        public bool Solo { get; set; }

        public string? MonitorOutputId { get; set; }

        public float MonitorVolume { get; set; }

        public string? ProcessName { get; set; }

        public bool IncludeProcessTree { get; set; }

        public int ProcessId { get; set; }

        /// <summary>取り込みスレッドからピーク値を記録する（取りこぼしのないよう最大値を残す）。</summary>
        public void ReportPeak(float value)
        {
            if (value <= 0f)
            {
                return;
            }

            float current;
            do
            {
                current = Volatile.Read(ref _peak);
                if (value <= current)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref _peak, value, current) != current);
        }

        /// <summary>記録したピーク値を取り出して 0 に戻す。</summary>
        public float ConsumePeak() => Math.Min(1f, Interlocked.Exchange(ref _peak, 0f));
    }

    private sealed class OutputTarget
    {
        public required string Label { get; init; }

        public required MMDevice Device { get; init; }

        public required WaveFormat MixFormat { get; init; }

        public required float Volume { get; set; }

        public bool Muted { get; set; }

        public required OutputDspParameters Dsp { get; init; }

        public VolumeSampleProvider? Master { get; set; }

        public OutputLevelMeter? Meter { get; set; }

        public WasapiOut? Output { get; set; }
    }
}
