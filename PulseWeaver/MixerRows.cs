using System.ComponentModel;

namespace PulseWeaver;

internal sealed class MixerChannelRow : INotifyPropertyChanged
{
    private readonly bool[] _routing;
    private string _name;
    private double _volume;
    private AudioDeviceOption? _selectedDevice;

    public MixerChannelRow(
        int index,
        string defaultName,
        string kindLabel,
        bool isVirtual,
        AudioDeviceOption[] options,
        AudioDeviceOption? selectedDevice,
        double volume,
        bool[] routing,
        string? savedName)
    {
        Index = index;
        DefaultName = defaultName;
        KindLabel = kindLabel;
        IsVirtual = isVirtual;
        Options = options;
        _selectedDevice = selectedDevice;
        _volume = volume;
        _routing = routing;
        _name = string.IsNullOrWhiteSpace(savedName) ? defaultName : savedName.Trim();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index { get; }

    public string DefaultName { get; }

    public string KindLabel { get; }

    public bool IsVirtual { get; }

    public AudioDeviceOption[] Options { get; }

    public string Name
    {
        get => _name;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? DefaultName : value.Trim();
            if (normalized == _name)
            {
                return;
            }

            _name = normalized;
            OnPropertyChanged();
        }
    }

    public AudioDeviceOption? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (ReferenceEquals(_selectedDevice, value))
            {
                return;
            }

            _selectedDevice = value;
            OnPropertyChanged();
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            var clamped = Math.Clamp(value, 0d, 1d);
            if (Math.Abs(clamped - _volume) < 0.0001)
            {
                return;
            }

            _volume = clamped;
            OnPropertyChanged();
        }
    }

    public bool RouteA1
    {
        get => _routing[0];
        set => SetRoute(0, value);
    }

    public bool RouteA2
    {
        get => _routing[1];
        set => SetRoute(1, value);
    }

    public bool RouteA3
    {
        get => _routing[2];
        set => SetRoute(2, value);
    }

    public bool RouteA4
    {
        get => _routing[3];
        set => SetRoute(3, value);
    }

    public bool GetRoute(int outputIndex) => _routing[outputIndex];

    public bool[] RoutingSnapshot() => (bool[])_routing.Clone();

    public bool HasAnyRoute => _routing.Any(enabled => enabled);

    public void SetRouteSilently(int outputIndex, bool enabled)
    {
        if (_routing[outputIndex] == enabled)
        {
            return;
        }

        _routing[outputIndex] = enabled;
        OnPropertyChanged(RoutePropertyName(outputIndex));
    }

    private void SetRoute(int outputIndex, bool enabled)
    {
        if (_routing[outputIndex] == enabled)
        {
            return;
        }

        _routing[outputIndex] = enabled;
        OnPropertyChanged(RoutePropertyName(outputIndex));
    }

    private static string RoutePropertyName(int outputIndex) => outputIndex switch
    {
        0 => nameof(RouteA1),
        1 => nameof(RouteA2),
        2 => nameof(RouteA3),
        _ => nameof(RouteA4)
    };

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class MixerOutputRow : INotifyPropertyChanged
{
    private string _name;
    private AudioDeviceOption? _selectedDevice;

    public MixerOutputRow(int index, string defaultName, AudioDeviceOption[] options, AudioDeviceOption? selectedDevice, string? savedName)
    {
        Index = index;
        DefaultName = defaultName;
        Options = options;
        _selectedDevice = selectedDevice;
        _name = string.IsNullOrWhiteSpace(savedName) ? defaultName : savedName.Trim();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index { get; }

    public string DefaultName { get; }

    public AudioDeviceOption[] Options { get; }

    public string Name
    {
        get => _name;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? DefaultName : value.Trim();
            if (normalized == _name)
            {
                return;
            }

            _name = normalized;
            OnPropertyChanged();
        }
    }

    public AudioDeviceOption? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (ReferenceEquals(_selectedDevice, value))
            {
                return;
            }

            _selectedDevice = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed record ReleaseHistoryItem(string Title, string Note, string PublishedText, string HtmlUrl);
