using System.Windows;
using NAudio.CoreAudioApi;

namespace PulseWeaver;

public partial class MainWindow : Window
{
    private readonly MMDeviceEnumerator _deviceEnumerator = new();
    private MixerEngine? _mixer;

    public MainWindow()
    {
        InitializeComponent();
        LoadDevices();
        Closed += (_, _) =>
        {
            _mixer?.Dispose();
            _deviceEnumerator.Dispose();
        };
    }

    private void LoadDevices()
    {
        var renderDevices = _deviceEnumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(device => new AudioDeviceOption(device.ID, device.FriendlyName))
            .ToArray();
        var captureDevices = _deviceEnumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(device => new AudioDeviceOption(device.ID, device.FriendlyName))
            .ToArray();

        SystemAudioDeviceCombo.ItemsSource = renderDevices;
        OutputDeviceCombo.ItemsSource = renderDevices;
        MicrophoneDeviceCombo.ItemsSource = captureDevices;

        if (renderDevices.Length > 0)
        {
            SystemAudioDeviceCombo.SelectedIndex = 0;
            OutputDeviceCombo.SelectedIndex = renderDevices.Length > 1 ? renderDevices.Length - 1 : 0;
        }

        if (captureDevices.Length > 0)
        {
            MicrophoneDeviceCombo.SelectedIndex = 0;
        }
        else
        {
            StatusText.Text = "利用可能なマイク入力がありません。マイクを接続してアプリを再起動してください。";
        }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (SystemAudioDeviceCombo.SelectedItem is not AudioDeviceOption systemAudio ||
            MicrophoneDeviceCombo.SelectedItem is not AudioDeviceOption microphone ||
            OutputDeviceCombo.SelectedItem is not AudioDeviceOption output)
        {
            MessageBox.Show("システム音声、マイク、出力の各デバイスを選択してください。",
                "デバイス未選択", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (systemAudio.Id == output.Id)
        {
            MessageBox.Show("入力ループやハウリングを避けるため、システム音声入力とミックス出力には別のデバイスを選んでください。",
                "デバイスの重複", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _mixer = new MixerEngine(systemAudio.Id, microphone.Id, output.Id);
            _mixer.SetVolumes((float)SystemVolumeSlider.Value, (float)MicrophoneVolumeSlider.Value);
            _mixer.Start();

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            StatusText.Text = "ミックス中です。Discord の入力デバイスに選択した仮想デバイスを指定してください。";
        }
        catch (Exception exception)
        {
            _mixer?.Dispose();
            _mixer = null;
            MessageBox.Show($"音声の開始に失敗しました。\n\n{exception.Message}",
                "起動エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _mixer?.Dispose();
        _mixer = null;
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        StatusText.Text = "停止しました。";
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SystemVolumeLabel is null || MicrophoneVolumeLabel is null)
        {
            return;
        }

        SystemVolumeLabel.Text = $"{SystemVolumeSlider.Value:P0}";
        MicrophoneVolumeLabel.Text = $"{MicrophoneVolumeSlider.Value:P0}";
        _mixer?.SetVolumes((float)SystemVolumeSlider.Value, (float)MicrophoneVolumeSlider.Value);
    }
}