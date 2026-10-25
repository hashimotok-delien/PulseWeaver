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
        AudioDeviceOption[] options,
        AudioDeviceOption? selectedDevice)
    {
        Index = index;
        _config = config;
        DefaultName = defaultName;
        Options = options;
        SelectedDevice = selectedDevice;
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
            OnPropertyChanged();
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
            var clamped = Math.Clamp(value, 0d, 1d);
            if (Math.Abs(clamped - _config.Volume) < 0.0001)
            {
                return;
            }

            _config.Volume = clamped;
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
            var clamped = Math.Clamp(value, 0d, 1d);
            if (Math.Abs(clamped - _config.MonitorVolume) < 0.0001)
            {
                return;
            }

            _config.MonitorVolume = clamped;
            OnPropertyChanged();
        }
    }

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

    public void BuildRoutes(IReadOnlyList<(string Id, string Label)> outputs)
    {
        Routes.Clear();
        foreach (var (id, label) in outputs)
        {
            Routes.Add(new RouteToggle(id, label, _config.GetRoute(id), OnRouteToggled));
        }
    }

    public void SetRouteSilently(string outputId, bool enabled)
    {
        _config.SetRoute(outputId, enabled);
        foreach (var toggle in Routes.Where(toggle => toggle.OutputId == outputId))
        {
            toggle.SetSilently(enabled);
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
        RouteChanged?.Invoke(this, toggle.OutputId);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class MixerOutputRow : INotifyPropertyChanged
{
    private readonly MixerOutputConfig _config;

    public MixerOutputRow(
        int index,
        MixerOutputConfig config,
        string defaultName,
        AudioDeviceOption[] options,
        AudioDeviceOption? selectedDevice)
    {
        Index = index;
        _config = config;
        DefaultName = defaultName;
        Options = options;
        SelectedDevice = selectedDevice;
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
            OnPropertyChanged();
        }
    }

    public double Volume
    {
        get => _config.Volume;
        set
        {
            var clamped = Math.Clamp(value, 0d, 1d);
            if (Math.Abs(clamped - _config.Volume) < 0.0001)
            {
                return;
            }

            _config.Volume = clamped;
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
