using System.ComponentModel;

namespace PulseWeaver;

internal sealed class MixerChannelRow : INotifyPropertyChanged
{
    private double _volume;

    public MixerChannelRow(
        int index,
        string label,
        string kindLabel,
        bool isVirtual,
        AudioDeviceOption[] options,
        AudioDeviceOption? selectedDevice,
        double volume,
        bool[] routing)
    {
        Index = index;
        Label = label;
        KindLabel = kindLabel;
        IsVirtual = isVirtual;
        Options = options;
        SelectedDevice = selectedDevice;
        _volume = volume;
        Routing = routing;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index { get; }

    public string Label { get; }

    public string KindLabel { get; }

    public bool IsVirtual { get; }

    public AudioDeviceOption[] Options { get; }

    public AudioDeviceOption? SelectedDevice { get; set; }

    public double Volume
    {
        get => _volume;
        set
        {
            if (Math.Abs(_volume - value) < 0.0001)
            {
                return;
            }

            _volume = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Volume)));
        }
    }

    public bool[] Routing { get; }
}

internal sealed class MixerOutputRow
{
    public MixerOutputRow(int index, string label, AudioDeviceOption[] options, AudioDeviceOption? selectedDevice)
    {
        Index = index;
        Label = label;
        Options = options;
        SelectedDevice = selectedDevice;
    }

    public int Index { get; }

    public string Label { get; }

    public AudioDeviceOption[] Options { get; }

    public AudioDeviceOption? SelectedDevice { get; set; }
}

internal sealed record ReleaseHistoryItem(string Title, string Note, string PublishedText, string HtmlUrl);
