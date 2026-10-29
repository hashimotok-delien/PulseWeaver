using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PulseWeaver;

/// <summary>1 つの出力へ送るかどうかのトグル。</summary>
internal sealed class RouteToggle : INotifyPropertyChanged
{
    private readonly Action<RouteToggle> _onChanged;
    private bool _isChecked;

    public RouteToggle(string outputId, string label, bool isChecked, Action<RouteToggle> onChanged)
    {
        OutputId = outputId;
        Label = label;
        _isChecked = isChecked;
        _onChanged = onChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string OutputId { get; }

    public string Label { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
            {
                return;
            }

            _isChecked = value;
            OnPropertyChanged();
            _onChanged(this);
        }
    }

    public void SetSilently(bool value)
    {
        if (_isChecked == value)
        {
            return;
        }

        _isChecked = value;
        OnPropertyChanged(nameof(IsChecked));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed record MonitorTarget(string Id, string Label)
{
    public override string ToString() => Label;
}

internal sealed class MixerChannelRow : INotifyPropertyChanged
{
    private readonly MixerInputConfig _config;
    private double _level;

    public MixerChannelRow(
        int index,
        MixerInputConfig config,
        string defaultName,
        AudioDeviceOption[] options)
    {
        Index = index;
        _config = config;
        DefaultName = defaultName;
        Options = options;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<string>? RouteChanged;

    public int Index { get; }

    public MixerInputConfig Config => _config;

    public string DefaultName { get; }

    public AudioDeviceOption[] Options { get; }

    public ObservableCollection<RouteToggle> Routes { get; } = new();

    public bool IsProcessSource => _config.Kind == InputSourceKind.Process;

    public bool IsDeviceSource => _config.Kind != InputSourceKind.Process;

    public string KindLabel => _config.Kind switch
    {
        InputSourceKind.Process => "アプリ",
        InputSourceKind.Loopback => "バーチャル入力",
        _ => "H/W入力"
    };

    public string Name
    {
        get => string.IsNullOrWhiteSpace(_config.Name) ? DefaultName : _config.Name!;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (normalized == _config.Name)
            {
                return;
            }

            _config.Name = normalized;
            OnPropertyChanged();
        }
    }

    public AudioDeviceOption? SelectedDevice
    {
        get => _config.Kind == InputSourceKind.Process
            ? null
            : Options.FirstOrDefault(option => option.Id == (_config.DeviceId ?? string.Empty)) ?? Options[0];
        set
        {
            var id = value?.Id ?? string.Empty;
            if (id == (_config.DeviceId ?? string.Empty))
            {
                return;
            }

            _config.DeviceId = id;
            _config.DeviceName = string.IsNullOrEmpty(id) ? null : value?.Name;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedDeviceLabel));
        }
    }

    /// <summary>デバイス変更ボタンに表示する名前。</summary>
    public string SelectedDeviceLabel
    {
        get
        {
            var selected = SelectedDevice;
            if (selected is not null && selected.Id.Length > 0)
            {
                return selected.Name;
            }

            return string.IsNullOrWhiteSpace(_config.DeviceName) ? "（なし）" : $"{_config.DeviceName}（未接続）";
        }
    }

    public string ProcessLabel => string.IsNullOrWhiteSpace(_config.ProcessName)
        ? "（アプリ未選択）"
        : $"{_config.ProcessName} (PID {_config.ProcessId})";

    public double Volume
    {
        get => _config.Volume;
        set
        {
            var clamped = Math.Clamp(value, 0d, MixerLimits.MaxVolume);
            if (Math.Abs(clamped - _config.Volume) < 0.0001)
            {
                return;
            }

            _config.Volume = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GainDb));
            OnPropertyChanged(nameof(GainText));
        }
    }

    /// <summary>フェーダーの値（dB）。-∞ は無音。</summary>
    public double GainDb
    {
        get => MixerGain.RoundDb(MixerGain.ToDb(_config.Volume));
        set
        {
            var gain = MixerGain.FromDb(MixerGain.RoundDb(value));
            if (Math.Abs(gain - _config.Volume) < 0.00001)
            {
                OnPropertyChanged();
                return;
            }

            _config.Volume = gain;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Volume));
            OnPropertyChanged(nameof(GainText));
        }
    }

    public string GainText => MixerGain.Format(MixerGain.ToDb(_config.Volume));

    /// <summary>3D 定位の左右（-1 = 左、+1 = 右）。</summary>
    public double PanX
    {
        get => _config.PanX;
        set
        {
            var clamped = Math.Clamp(value, -1d, 1d);
            if (Math.Abs(clamped - _config.PanX) < 0.001)
            {
                return;
            }

            _config.PanX = clamped;
            OnPropertyChanged();
        }
    }

    /// <summary>3D 定位の前後（-1 = 背面、+1 = 正面）。</summary>
    public double PanY
    {
        get => _config.PanY;
        set
        {
            var clamped = Math.Clamp(value, -1d, 1d);
            if (Math.Abs(clamped - _config.PanY) < 0.001)
            {
                return;
            }

            _config.PanY = clamped;
            OnPropertyChanged();
        }
    }

    /// <summary>ノイズゲートの効き（0〜10）。</summary>
    public double Gate
    {
        get => _config.Gate;
        set
        {
            var clamped = Math.Clamp(value, 0d, 10d);
            if (Math.Abs(clamped - _config.Gate) < 0.001)
            {
                return;
            }

            _config.Gate = clamped;
            OnPropertyChanged();
        }
    }

    /// <summary>コンプレッサーの効き（0〜10）。</summary>
    public double Comp
    {
        get => _config.Comp;
        set
        {
            var clamped = Math.Clamp(value, 0d, 10d);
            if (Math.Abs(clamped - _config.Comp) < 0.001)
            {
                return;
            }

            _config.Comp = clamped;
            OnPropertyChanged();
        }
    }

    public bool Mono
    {
        get => _config.Mono;
        set
        {
            if (_config.Mono == value)
            {
                return;
            }

            _config.Mono = value;
            OnPropertyChanged();
        }
    }

    public bool Muted
    {
        get => _config.Muted;
        set
        {
            if (_config.Muted == value)
            {
                return;
            }

            _config.Muted = value;
            OnPropertyChanged();
        }
    }

    public bool Solo
    {
        get => _config.Solo;
        set
        {
            if (_config.Solo == value)
            {
                return;
            }

            _config.Solo = value;
            OnPropertyChanged();
        }
    }

    public bool IsMonitoring => !string.IsNullOrWhiteSpace(_config.MonitorOutputId);

    /// <summary>モニター先の候補（「（なし）」を含む）。</summary>
    public List<MonitorTarget> MonitorTargets { get; } = new();

    public MonitorTarget? SelectedMonitorTarget
    {
        get => MonitorTargets.FirstOrDefault(target => target.Id == (_config.MonitorOutputId ?? string.Empty));
        set
        {
            var id = value?.Id ?? string.Empty;
            if (id == (_config.MonitorOutputId ?? string.Empty))
            {
                return;
            }

            _config.MonitorOutputId = string.IsNullOrEmpty(id) ? null : id;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMonitoring));
            OnPropertyChanged(nameof(MonitorEnabled));
        }
    }

    public bool MonitorEnabled
    {
        get => IsMonitoring;
        set
        {
            if (value == IsMonitoring)
            {
                return;
            }

            if (!value)
            {
                MonitorOutputId = null;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedMonitorTarget));
                return;
            }

            var targetId = DefaultMonitorTargetId();
            if (targetId is null)
            {
                return;
            }

            if (!GetRoute(targetId))
            {
                SetRouteSilently(targetId, true);
                RouteChanged?.Invoke(this, targetId);
            }

            MonitorOutputId = targetId;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedMonitorTarget));
        }
    }

    private string? DefaultMonitorTargetId()
    {
        var routed = Routes.FirstOrDefault(toggle => toggle.IsChecked && toggle.OutputId.Length > 0);
        return routed?.OutputId
            ?? MonitorTargets.FirstOrDefault(target => target.Id.Length > 0)?.Id;
    }

    public void BuildMonitorTargets(IReadOnlyList<(string Id, string Label)> outputs)
    {
        MonitorTargets.Clear();
        MonitorTargets.Add(new MonitorTarget(string.Empty, "（モニターしない）"));
        foreach (var (id, label) in outputs)
        {
            MonitorTargets.Add(new MonitorTarget(id, label));
        }
    }

    public string? MonitorOutputId
    {
        get => _config.MonitorOutputId;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (normalized == _config.MonitorOutputId)
            {
                return;
            }

            _config.MonitorOutputId = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMonitoring));
        }
    }

    public double MonitorVolume
    {
        get => _config.MonitorVolume;
        set
        {
            var clamped = Math.Clamp(value, 0d, MixerLimits.MaxVolume);
            if (Math.Abs(clamped - _config.MonitorVolume) < 0.0001)
            {
                return;
            }

            _config.MonitorVolume = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MonitorGainDb));
            OnPropertyChanged(nameof(MonitorGainText));
        }
    }

    /// <summary>モニター音量の dB 表示用。</summary>
    public double MonitorGainDb
    {
        get => MixerGain.RoundDb(MixerGain.ToDb(_config.MonitorVolume));
        set
        {
            var gain = MixerGain.FromDb(MixerGain.RoundDb(value));
            if (Math.Abs(gain - _config.MonitorVolume) < 0.00001)
            {
                return;
            }

            _config.MonitorVolume = gain;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MonitorVolume));
            OnPropertyChanged(nameof(MonitorGainText));
        }
    }

    public string MonitorGainText => MixerGain.Format(MixerGain.ToDb(_config.MonitorVolume));

    /// <summary>レベルメーター表示用（0〜1）。</summary>
    public double Level
    {
        get => _level;
        set
        {
            if (Math.Abs(value - _level) < 0.001)
            {
                return;
            }

            _level = value;
            OnPropertyChanged();
        }
    }

    public bool GetRoute(string outputId) => _config.GetRoute(outputId);

    /// <summary>現在の出力に対応する ON/OFF 設定が 1 つでも保存されているか（全 OFF も「設定あり」）。</summary>
    public bool HasRouteSettings { get; private set; }

    public void BuildRoutes(IReadOnlyList<(string Id, string Label)> outputs)
    {
        Routes.Clear();
        HasRouteSettings = false;

        foreach (var (id, label) in outputs)
        {
            bool enabled;
            if (_config.Routes.TryGetValue(id, out var byId))
            {
                enabled = byId;
                HasRouteSettings = true;
                _config.SetRouteSlot(label, enabled);
            }
            else if (_config.RouteSlots.TryGetValue(label, out var bySlot))
            {
                // 出力 ID が振り直されても A1/B1 の ON/OFF を引き継ぐ
                enabled = bySlot;
                _config.SetRoute(id, enabled);
                HasRouteSettings = true;
            }
            else
            {
                enabled = false;
            }

            Routes.Add(new RouteToggle(id, label, enabled, OnRouteToggled));
        }

        var validIds = outputs.Select(output => output.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var staleId in _config.Routes.Keys.Where(key => !validIds.Contains(key)).ToArray())
        {
            _config.Routes.Remove(staleId);
        }
    }

    public void SetRouteSilently(string outputId, bool enabled)
    {
        _config.SetRoute(outputId, enabled);
        foreach (var toggle in Routes.Where(toggle => toggle.OutputId == outputId))
        {
            toggle.SetSilently(enabled);
            _config.SetRouteSlot(toggle.Label, enabled);
        }
    }

    public bool HasAnyRoute => _config.HasAnyRoute;

    public void SetProcess(AudioProcessCandidate candidate)
    {
        _config.ProcessId = candidate.ProcessId;
        _config.ProcessName = candidate.ProcessName;
        OnPropertyChanged(nameof(ProcessLabel));
    }

    public void RefreshProcessLabel() => OnPropertyChanged(nameof(ProcessLabel));

    private void OnRouteToggled(RouteToggle toggle)
    {
        _config.SetRoute(toggle.OutputId, toggle.IsChecked);
        _config.SetRouteSlot(toggle.Label, toggle.IsChecked);
        HasRouteSettings = true;
        RouteChanged?.Invoke(this, toggle.OutputId);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class MixerOutputRow : INotifyPropertyChanged
{
    private readonly MixerOutputConfig _config;
    private double _level;
    private bool _isDeviceUnavailable;

    public MixerOutputRow(
        int index,
        MixerOutputConfig config,
        string defaultName,
        AudioDeviceOption[] options)
    {
        Index = index;
        _config = config;
        DefaultName = defaultName;
        Options = options;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index { get; }

    public MixerOutputConfig Config => _config;

    public string DefaultName { get; }

    public AudioDeviceOption[] Options { get; }

    public bool IsVirtual => _config.Role == OutputRole.Virtual;

    public string KindLabel => IsVirtual ? "仮想出力（アプリへ送る）" : "物理出力";

    public string RoleCode => IsVirtual ? "B" : "A";

    public string Name
    {
        get => string.IsNullOrWhiteSpace(_config.Name) ? DefaultName : _config.Name!;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (normalized == _config.Name)
            {
                return;
            }

            _config.Name = normalized;
            OnPropertyChanged();
        }
    }

    public AudioDeviceOption? SelectedDevice
    {
        get => Options.FirstOrDefault(option => option.Id == (_config.DeviceId ?? string.Empty)) ?? Options[0];
        set
        {
            var id = value?.Id ?? string.Empty;
            if (id == (_config.DeviceId ?? string.Empty))
            {
                return;
            }

            _config.DeviceId = id;
            _config.DeviceName = string.IsNullOrEmpty(id) ? null : value?.Name;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedDeviceLabel));
        }
    }

    /// <summary>デバイス変更ボタンに表示する名前。</summary>
    public string SelectedDeviceLabel
    {
        get
        {
            var selected = SelectedDevice;
            if (selected is not null && selected.Id.Length > 0)
            {
                return selected.Name;
            }

            return string.IsNullOrWhiteSpace(_config.DeviceName) ? "（なし）" : $"{_config.DeviceName}（未接続）";
        }
    }

    /// <summary>選択中の出力デバイスに接続できていない場合に true（デバイス名を赤く点滅させる）。</summary>
    public bool IsDeviceUnavailable
    {
        get => _isDeviceUnavailable;
        set
        {
            if (_isDeviceUnavailable == value)
            {
                return;
            }

            _isDeviceUnavailable = value;
            OnPropertyChanged();
        }
    }

    public double Volume
    {
        get => _config.Volume;
        set
        {
            var clamped = Math.Clamp(value, 0d, MixerLimits.MaxVolume);
            if (Math.Abs(clamped - _config.Volume) < 0.0001)
            {
                return;
            }

            _config.Volume = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GainDb));
            OnPropertyChanged(nameof(GainText));
        }
    }

    /// <summary>フェーダーの値（dB）。-∞ は無音。</summary>
    public double GainDb
    {
        get => MixerGain.RoundDb(MixerGain.ToDb(_config.Volume));
        set
        {
            var gain = MixerGain.FromDb(MixerGain.RoundDb(value));
            if (Math.Abs(gain - _config.Volume) < 0.00001)
            {
                OnPropertyChanged();
                return;
            }

            _config.Volume = gain;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Volume));
            OnPropertyChanged(nameof(GainText));
        }
    }

    public string GainText => MixerGain.Format(MixerGain.ToDb(_config.Volume));

    public bool Muted
    {
        get => _config.Muted;
        set
        {
            if (_config.Muted == value)
            {
                return;
            }

            _config.Muted = value;
            OnPropertyChanged();
        }
    }

    public bool Mono
    {
        get => _config.Mono;
        set
        {
            if (_config.Mono == value)
            {
                return;
            }

            _config.Mono = value;
            OnPropertyChanged();
        }
    }

    public bool EqEnabled
    {
        get => _config.EqEnabled;
        set
        {
            if (_config.EqEnabled == value)
            {
                return;
            }

            _config.EqEnabled = value;
            OnPropertyChanged();
        }
    }

    public double EqLow
    {
        get => _config.EqLow;
        set => SetEq(value, _config.EqLow, v => _config.EqLow = v, nameof(EqLow));
    }

    public double EqMid
    {
        get => _config.EqMid;
        set => SetEq(value, _config.EqMid, v => _config.EqMid = v, nameof(EqMid));
    }

    public double EqHigh
    {
        get => _config.EqHigh;
        set => SetEq(value, _config.EqHigh, v => _config.EqHigh = v, nameof(EqHigh));
    }

    private void SetEq(double value, double current, Action<double> apply, string propertyName)
    {
        var clamped = Math.Clamp(Math.Round(value, 1), -12d, 12d);
        if (Math.Abs(clamped - current) < 0.001)
        {
            return;
        }

        apply(clamped);
        OnPropertyChanged(propertyName);
    }

    /// <summary>出力レベルメーター表示用（0〜1）。</summary>
    public double Level
    {
        get => _level;
        set
        {
            if (Math.Abs(value - _level) < 0.001)
            {
                return;
            }

            _level = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed record ReleaseHistoryItem(
    string Title,
    string Note,
    string PublishedText,
    string HtmlUrl,
    IReadOnlyList<string> Changes);
