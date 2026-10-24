using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using NAudio.CoreAudioApi;

namespace PulseWeaver;

public partial class MainWindow : Window
{
    private readonly MMDeviceEnumerator _deviceEnumerator = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly UpdateService _updateService = new();
    private MixerEngine? _mixer;
    private UpdateRelease? _availableRelease;
    private bool _isApplyingSettings;
    private bool _isCheckingUpdates;
    private bool _isUpdating;

    public MainWindow()
    {
        InitializeComponent();
        ApplySettingsToUi();
        LoadDevices();
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_settings.AutoCheckForUpdates && _settings.IsAutoCheckDue())
        {
            await CheckForUpdatesAsync(userInitiated: false);
        }
        else
        {
            SetUpdateStatus("次回の自動確認待ちです。「最新かどうかを調べる」からいつでも確認できます。");
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _mixer?.Dispose();
        _updateService.Dispose();
        _deviceEnumerator.Dispose();
    }

    private void ApplySettingsToUi()
    {
        _isApplyingSettings = true;
        try
        {
            VersionText.Text = AppInfo.DisplayVersion;
            CurrentVersionText.Text = AppInfo.DisplayVersion;
            AboutVersionText.Text = $"PulseWeaver {AppInfo.DisplayVersion}";
            AutoCheckCheckBox.IsChecked = _settings.AutoCheckForUpdates;

            foreach (var item in LatencyCombo.Items.OfType<ComboBoxItem>())
            {
                if (int.TryParse(item.Tag?.ToString(), out var latency) && latency == _settings.OutputLatencyMs)
                {
                    LatencyCombo.SelectedItem = item;
                    break;
                }
            }

            if (LatencyCombo.SelectedItem is null && LatencyCombo.Items.Count > 1)
            {
                LatencyCombo.SelectedIndex = 1;
            }

            SettingsNavList.SelectedIndex = 0;
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private void LoadDevices()
    {
        try
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

            var defaultRenderId = GetDefaultDeviceId(DataFlow.Render, Role.Multimedia);
            var defaultCaptureId = GetDefaultDeviceId(DataFlow.Capture, Role.Communications);

            SystemAudioDeviceCombo.SelectedItem = renderDevices.FirstOrDefault(device => device.Id == defaultRenderId)
                ?? renderDevices.FirstOrDefault();
            MicrophoneDeviceCombo.SelectedItem = captureDevices.FirstOrDefault(device => device.Id == defaultCaptureId)
                ?? captureDevices.FirstOrDefault();
            OutputDeviceCombo.SelectedItem = PickOutputDevice(
                renderDevices,
                (SystemAudioDeviceCombo.SelectedItem as AudioDeviceOption)?.Id);

            if (captureDevices.Length == 0)
            {
                StatusText.Text = "利用可能なマイク入力がありません。マイクを接続してアプリを再起動してください。";
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("音声デバイスの列挙に失敗しました。", exception);
            StatusText.Text = $"音声デバイスを取得できませんでした。{exception.Message}";
        }
    }

    private void MixerTab_Checked(object sender, RoutedEventArgs e)
    {
        if (MixerPanel is null || SettingsPanel is null)
        {
            return;
        }

        MixerPanel.Visibility = Visibility.Visible;
        SettingsPanel.Visibility = Visibility.Collapsed;
    }

    private void SettingsTab_Checked(object sender, RoutedEventArgs e)
    {
        if (MixerPanel is null || SettingsPanel is null)
        {
            return;
        }

        MixerPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;
        if (SettingsNavList.SelectedIndex < 0)
        {
            SettingsNavList.SelectedIndex = 0;
        }
    }

    private string? GetDefaultDeviceId(DataFlow dataFlow, Role role)
    {
        try
        {
            using var device = _deviceEnumerator.GetDefaultAudioEndpoint(dataFlow, role);
            return device.ID;
        }
        catch (Exception exception)
        {
            AppLog.Error($"既定の{dataFlow}デバイスを取得できませんでした。", exception);
            return null;
        }
    }

    private static AudioDeviceOption? PickOutputDevice(AudioDeviceOption[] renderDevices, string? systemAudioDeviceId)
    {
        var candidates = renderDevices
            .Where(device => !string.Equals(device.Id, systemAudioDeviceId, StringComparison.Ordinal))
            .ToArray();

        return candidates.FirstOrDefault(device => device.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(device => device.Name.Contains("Voicemeeter Input", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault()
            ?? renderDevices.FirstOrDefault();
    }

    private void SettingsNavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GeneralSettingsPanel is null)
        {
            return;
        }

        var index = SettingsNavList.SelectedIndex;
        GeneralSettingsPanel.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        AudioSettingsPanel.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSettingsPanel.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        AboutSettingsPanel.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AutoCheckCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        _settings.AutoCheckForUpdates = AutoCheckCheckBox.IsChecked == true;
        _settings.Save();
    }

    private void LatencyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingSettings || LatencyCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        if (int.TryParse(item.Tag?.ToString(), out var latency))
        {
            _settings.OutputLatencyMs = latency;
            _settings.Save();
        }
    }

    private void OpenUpdateSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsTabButton.IsChecked = true;
        SettingsNavList.SelectedIndex = 2;
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e) =>
        await CheckForUpdatesAsync(userInitiated: true);

    private async Task CheckForUpdatesAsync(bool userInitiated)
    {
        if (_isCheckingUpdates || _isUpdating)
        {
            return;
        }

        _isCheckingUpdates = true;
        CheckUpdatesButton.IsEnabled = false;
        SetUpdateStatus("最新のバージョンを確認しています…");

        try
        {
            var latest = await _updateService.GetLatestAsync(CancellationToken.None);
            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            _settings.Save();

            if (latest is null)
            {
                _availableRelease = null;
                UpdateActionsPanel.Visibility = Visibility.Collapsed;
                UpdateBanner.Visibility = Visibility.Collapsed;
                SetUpdateStatus("公開されているリリースが見つかりませんでした。");
                return;
            }

            if (VersionComparer.Compare(latest.Version, AppInfo.Version) > 0)
            {
                _availableRelease = latest;
                var hasInstaller = latest.InstallerUrl is not null;
                InstallUpdateButton.IsEnabled = hasInstaller;
                InstallUpdateButton.Content = hasInstaller ? "アップデートする" : "インストーラーがありません";
                UpdateActionsPanel.Visibility = Visibility.Visible;
                UpdateBannerText.Text = $"新しいバージョン {latest.TagName} が利用できます（現在 {AppInfo.DisplayVersion}）。";
                UpdateBanner.Visibility = Visibility.Visible;
                SetUpdateStatus($"新しいバージョン {latest.TagName} が利用できます。");
            }
            else
            {
                _availableRelease = null;
                UpdateActionsPanel.Visibility = Visibility.Collapsed;
                UpdateBanner.Visibility = Visibility.Collapsed;
                SetUpdateStatus($"最新です（{AppInfo.DisplayVersion}）。");
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("アップデートの確認に失敗しました。", exception);
            SetUpdateStatus(userInitiated
                ? $"確認できませんでした。{exception.Message}"
                : "自動確認に失敗しました。ネットワーク接続を確認してください。");
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
            _isCheckingUpdates = false;
        }
    }

    private async void InstallUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_availableRelease is null || _isUpdating)
        {
            return;
        }

        var answer = MessageBox.Show(
            $"{_availableRelease.TagName} にアップデートします。\nダウンロード後にインストーラーが起動し、PulseWeaver は終了します。よろしいですか？",
            "アップデートの確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _isUpdating = true;
        InstallUpdateButton.IsEnabled = false;
        CheckUpdatesButton.IsEnabled = false;
        UpdateProgressBar.Visibility = Visibility.Visible;
        UpdateProgressBar.Value = 0;

        try
        {
            SetUpdateStatus("インストーラーをダウンロードしています…");
            var progress = new Progress<double>(value =>
            {
                UpdateProgressBar.Value = value * 100;
                SetUpdateStatus($"インストーラーをダウンロードしています… {value:P0}");
            });

            var installerPath = await _updateService.DownloadInstallerAsync(_availableRelease, progress, CancellationToken.None);
            SetUpdateStatus("インストーラーを起動します。まもなくアプリを終了します…");
            UpdateService.RunInstaller(installerPath);
            Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            AppLog.Error("アップデートに失敗しました。", exception);
            SetUpdateStatus($"アップデートに失敗しました。{exception.Message}");
            UpdateProgressBar.Visibility = Visibility.Collapsed;
            InstallUpdateButton.IsEnabled = true;
            CheckUpdatesButton.IsEnabled = true;
            _isUpdating = false;
        }
    }

    private void OpenReleasePageButton_Click(object sender, RoutedEventArgs e) =>
        OpenUrl(_availableRelease?.HtmlUrl ?? AppInfo.ReleasesUrl);

    private void OpenRepository_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.RepositoryUrl);

    private void OpenVirtualCable_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.VirtualCableUrl);

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.LogDirectory) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLog.Error("ログフォルダを開けませんでした。", exception);
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

        _mixer?.Dispose();
        _mixer = null;

        try
        {
            _mixer = new MixerEngine(systemAudio.Id, microphone.Id, output.Id, _settings.OutputLatencyMs);
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
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            AppLog.Error("音声の開始に失敗しました。", exception);

            MessageBox.Show($"{exception.Message}\n\nログ: {AppPaths.LogDirectory}",
                "音声を開始できません", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "音声を開始できませんでした。設定タブのログフォルダで詳細を確認できます。";
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

    private void SetUpdateStatus(string message) => UpdateStatusText.Text = message;

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLog.Error($"URL を開けませんでした: {url}", exception);
        }
    }
}
