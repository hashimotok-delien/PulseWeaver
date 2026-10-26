using System.Windows;
using System.Windows.Input;
using NAudio.CoreAudioApi;

namespace PulseWeaver;

internal partial class DevicePickerWindow : Window
{
    private readonly DataFlow _dataFlow;
    private readonly MMDeviceEnumerator _deviceEnumerator = new();

    public DevicePickerWindow(string title, DataFlow dataFlow, string? currentId, string description)
    {
        InitializeComponent();
        Title = title;
        HeadingText.Text = title;
        DescriptionText.Text = description;
        _dataFlow = dataFlow;
        Reload(currentId);
    }

    public string? SelectedDeviceId { get; private set; }

    private void Reload(string? selectId)
    {
        var options = new List<AudioDeviceOption> { new(string.Empty, "（なし）") };

        try
        {
            options.AddRange(_deviceEnumerator
                .EnumerateAudioEndPoints(_dataFlow, DeviceState.Active)
                .Select(device => new AudioDeviceOption(device.ID, device.FriendlyName)));
            DeviceStatusText.Text = $"検出したデバイス: {options.Count - 1} 件";
        }
        catch (Exception exception)
        {
            AppLog.Error("音声デバイスの列挙に失敗しました。", exception);
            DeviceStatusText.Text = $"デバイスを取得できませんでした。{exception.Message}";
        }

        DeviceList.ItemsSource = options;
        DeviceList.SelectedItem = options.FirstOrDefault(option => option.Id == (selectId ?? string.Empty)) ?? options[0];
        DeviceList.ScrollIntoView(DeviceList.SelectedItem);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        Reload((DeviceList.SelectedItem as AudioDeviceOption)?.Id);

    private void DeviceList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Accept();

    private void SelectButton_Click(object sender, RoutedEventArgs e) => Accept();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Accept()
    {
        if (DeviceList.SelectedItem is not AudioDeviceOption option)
        {
            return;
        }

        SelectedDeviceId = option.Id;
        DialogResult = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _deviceEnumerator.Dispose();
    }
}
