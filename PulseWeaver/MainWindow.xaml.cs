using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using NAudio.CoreAudioApi;

namespace PulseWeaver;

public partial class MainWindow : Window
{
    private static readonly string[] VirtualInputLabels = { "Windows デフォルト出力", "Discord 出力", "音声出力先" };
    private const int HardwareInputCount = 6;
    private const int VirtualInputCount = 3;
    private const int OutputCount = 4;
    private const int ChannelCount = HardwareInputCount + VirtualInputCount;

    private readonly MMDeviceEnumerator _deviceEnumerator = new();
    private AppSettings _settings = AppSettings.Load();
    private readonly UpdateService _updateService = new();
    private readonly List<MixerChannelRow> _channelRows = new();
    private readonly List<MixerOutputRow> _outputRows = new();
    private MixerEngine? _mixer;
    private IReadOnlyList<UpdateRelease> _releases = Array.Empty<UpdateRelease>();
    private IReadOnlyDictionary<string, IReadOnlyList<string>> _changelog =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
    private UpdateRelease? _availableRelease;
    private bool _isInitializingUi;
    private bool _isSyncingRoutes;
    private bool _isApplyingSettings;
    private bool _isCheckingUpdates;
    private bool _isUpdating;
    private bool _isRefreshingProfiles;

    public MainWindow()
    {
        InitializeComponent();
        ApplySettingsToUi();
        LoadDevices();
        BuildMixerRows(reloadDevices: false);
        RefreshProfiles();
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

            if (SettingsNavList.SelectedIndex < 0)
            {
                SettingsNavList.SelectedIndex = 0;
            }
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
            _renderDevices = _deviceEnumerator
                .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Select(device => new AudioDeviceOption(device.ID, device.FriendlyName))
                .ToArray();
            _captureDevices = _deviceEnumerator
                .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .Select(device => new AudioDeviceOption(device.ID, device.FriendlyName))
                .ToArray();

            if (_captureDevices.Length == 0)
            {
                StatusText.Text = "録音デバイスが見つかりません。マイクを接続してアプリを再起動してください。";
            }
        }
        catch (Exception exception)
        {
            _renderDevices = Array.Empty<AudioDeviceOption>();
            _captureDevices = Array.Empty<AudioDeviceOption>();
            AppLog.Error("音声デバイスの列挙に失敗しました。", exception);
            StatusText.Text = $"音声デバイスを取得できませんでした。{exception.Message}";
        }
    }

    private AudioDeviceOption[] _renderDevices = Array.Empty<AudioDeviceOption>();
    private AudioDeviceOption[] _captureDevices = Array.Empty<AudioDeviceOption>();

    private void BuildMixerRows(bool reloadDevices)
    {
        _isInitializingUi = true;
        try
        {
            foreach (var row in _channelRows)
            {
                row.PropertyChanged -= ChannelRow_PropertyChanged;
            }

            foreach (var row in _outputRows)
            {
                row.PropertyChanged -= OutputRow_PropertyChanged;
            }

            _channelRows.Clear();
            _outputRows.Clear();

            if (reloadDevices)
            {
                LoadDevices();
            }

            var none = new AudioDeviceOption(string.Empty, "（なし）");
            var renderOptions = new[] { none }.Concat(_renderDevices).ToArray();
            var captureOptions = new[] { none }.Concat(_captureDevices).ToArray();

            var defaultRenderId = GetDefaultDeviceId(DataFlow.Render, Role.Multimedia);
            var defaultCommunicationCaptureId = GetDefaultDeviceId(DataFlow.Capture, Role.Communications);
            var defaultMultimediaCaptureId = GetDefaultDeviceId(DataFlow.Capture, Role.Multimedia);

            var virtualDefaults = PickVirtualDefaults(renderOptions, defaultRenderId);
            var outputDefaults = PickOutputDefaults(renderOptions, defaultRenderId);

            for (var i = 0; i < HardwareInputCount; i++)
            {
                var defaultId = i switch
                {
                    0 => defaultCommunicationCaptureId ?? defaultMultimediaCaptureId,
                    1 => defaultMultimediaCaptureId != defaultCommunicationCaptureId ? defaultMultimediaCaptureId : null,
                    _ => null
                };
                var selected = ResolveDevice(captureOptions, _settings.HardwareInputIds[i], defaultId);
                _channelRows.Add(new MixerChannelRow(i, $"H/W入力 {i + 1}", "H/W入力", false, captureOptions,
                    selected, _settings.InputVolumes[i], GetRouting(i), _settings.InputNames[i]));
            }

            for (var i = 0; i < VirtualInputCount; i++)
            {
                var index = HardwareInputCount + i;
                var selected = ResolveDevice(renderOptions, _settings.VirtualInputIds[i], virtualDefaults[i]);
                _channelRows.Add(new MixerChannelRow(index, VirtualInputLabels[i], $"バーチャル入力 {i + 1}", true,
                    renderOptions, selected, _settings.InputVolumes[index], GetRouting(index), _settings.InputNames[index]));
            }

            for (var i = 0; i < OutputCount; i++)
            {
                var selected = ResolveDevice(renderOptions, _settings.HardwareOutputIds[i], outputDefaults[i]);
                _outputRows.Add(new MixerOutputRow(i, $"A{i + 1}", renderOptions, selected, _settings.OutputNames[i],
                    _settings.OutputVolumes[i]));
            }

            foreach (var row in _channelRows)
            {
                row.PropertyChanged += ChannelRow_PropertyChanged;
            }

            foreach (var row in _outputRows)
            {
                row.PropertyChanged += OutputRow_PropertyChanged;
            }

            HardwareInputList.ItemsSource = _channelRows.Take(HardwareInputCount).ToArray();
            VirtualInputList.ItemsSource = _channelRows.Skip(HardwareInputCount).ToArray();
            HardwareOutputList.ItemsSource = _outputRows.ToArray();

            SyncFeedbackRoutes();
            SaveMixerSettings();
        }
        finally
        {
            _isInitializingUi = false;
        }
    }


    private static IEnumerable<string> FindFeedbackLoops(MixerInputDefinition[] inputs, MixerOutputDefinition[] outputs)
    {
        for (var i = 0; i < inputs.Length; i++)
        {
            var input = inputs[i];
            if (!input.IsLoopback || input.DeviceId is null)
            {
                continue;
            }

            for (var output = 0; output < outputs.Length; output++)
            {
                if (outputs[output].DeviceId is null || !input.Routing[output])
                {
                    continue;
                }

                if (string.Equals(input.DeviceId, outputs[output].DeviceId, StringComparison.Ordinal))
                {
                    yield return $"「{input.Label}」の音を同じデバイス「{outputs[output].Label}」へ送っているため、ハウリングする可能性があります。";
                }
            }
        }
    }

    private bool[] GetRouting(int channelIndex)
    {
        var routing = new bool[OutputCount];
        var saved = false;
        for (var i = 0; i < OutputCount; i++)
        {
            routing[i] = _settings.InputRouting[channelIndex * OutputCount + i];
            saved |= routing[i];
        }

        if (!saved)
        {
            routing[0] = true;
        }

        return routing;
    }

    private static AudioDeviceOption ResolveDevice(AudioDeviceOption[] options, string? savedId, string? defaultId)
    {
        if (savedId is not null)
        {
            return options.FirstOrDefault(option => option.Id == savedId) ?? options[0];
        }

        if (!string.IsNullOrEmpty(defaultId))
        {
            var match = options.FirstOrDefault(option => option.Id == defaultId);
            if (match is not null)
            {
                return match;
            }
        }

        return options[0];
    }

    private static string?[] PickVirtualDefaults(AudioDeviceOption[] renderOptions, string? defaultRenderId)
    {
        var used = new HashSet<string?>();
        var result = new string?[VirtualInputCount];

        result[0] = defaultRenderId ?? renderOptions.Skip(1).Select(option => option.Id).FirstOrDefault();
        used.Add(result[0]);

        result[1] = FirstMatching(renderOptions, used, "CABLE Input")
            ?? renderOptions.Skip(1).Select(option => option.Id).FirstOrDefault(id => !used.Contains(id));
        used.Add(result[1]);

        result[2] = FirstMatching(renderOptions, used, "Voicemeeter Input")
            ?? renderOptions.Skip(1).Select(option => option.Id).FirstOrDefault(id => !used.Contains(id));

        return result;
    }

    private static string?[] PickOutputDefaults(AudioDeviceOption[] renderOptions, string? defaultRenderId)
    {
        var used = new HashSet<string?>();
        var result = new string?[OutputCount];

        result[0] = defaultRenderId ?? renderOptions.Skip(1).Select(option => option.Id).FirstOrDefault();
        used.Add(result[0]);

        result[1] = FirstMatching(renderOptions, used, "CABLE Input");
        used.Add(result[1]);

        result[2] = renderOptions.Skip(1).Select(option => option.Id).FirstOrDefault(id => !used.Contains(id));
        used.Add(result[2]);

        result[3] = renderOptions.Skip(1).Select(option => option.Id).FirstOrDefault(id => !used.Contains(id));
        return result;
    }

    private static string? FirstMatching(AudioDeviceOption[] options, HashSet<string?> used, string keyword) =>
        options.FirstOrDefault(option => option.Id.Length > 0 && !used.Contains(option.Id) &&
                                         option.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))?.Id;

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

    private void SaveMixerSettings()
    {
        for (var i = 0; i < HardwareInputCount; i++)
        {
            _settings.HardwareInputIds[i] = ToSavedId(_channelRows[i].SelectedDevice);
        }

        for (var i = 0; i < VirtualInputCount; i++)
        {
            _settings.VirtualInputIds[i] = ToSavedId(_channelRows[HardwareInputCount + i].SelectedDevice);
        }

        for (var i = 0; i < OutputCount; i++)
        {
            _settings.HardwareOutputIds[i] = ToSavedId(_outputRows[i].SelectedDevice);
            _settings.OutputNames[i] = ToSavedName(_outputRows[i].Name, _outputRows[i].DefaultName);
            _settings.OutputVolumes[i] = _outputRows[i].Volume;
        }

        for (var channel = 0; channel < ChannelCount; channel++)
        {
            _settings.InputNames[channel] = ToSavedName(_channelRows[channel].Name, _channelRows[channel].DefaultName);
            _settings.InputVolumes[channel] = _channelRows[channel].Volume;
            for (var output = 0; output < OutputCount; output++)
            {
                _settings.InputRouting[channel * OutputCount + output] = _channelRows[channel].GetRoute(output);
            }
        }

        _settings.Save();
    }

    private static string? ToSavedId(AudioDeviceOption? option) =>
        option is null ? null : option.Id.Length == 0 ? string.Empty : option.Id;

    private static string? ToSavedName(string name, string defaultName) =>
        string.Equals(name, defaultName, StringComparison.Ordinal) ? null : name;

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

    private void SettingsNavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GeneralSettingsPanel is null)
        {
            return;
        }

        var index = SettingsNavList.SelectedIndex;
        GeneralSettingsPanel.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        AudioSettingsPanel.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        ProfileSettingsPanel.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSettingsPanel.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        AboutSettingsPanel.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;
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

    private void ChannelRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isInitializingUi || _isSyncingRoutes || sender is not MixerChannelRow row)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(MixerChannelRow.Volume):
                _mixer?.SetInputVolume(row.Index, (float)row.Volume);
                SaveMixerSettings();
                break;

            case nameof(MixerChannelRow.RouteA1):
                ApplyRoute(row, 0);
                break;

            case nameof(MixerChannelRow.RouteA2):
                ApplyRoute(row, 1);
                break;

            case nameof(MixerChannelRow.RouteA3):
                ApplyRoute(row, 2);
                break;

            case nameof(MixerChannelRow.RouteA4):
                ApplyRoute(row, 3);
                break;

            case nameof(MixerChannelRow.SelectedDevice):
                SyncFeedbackRoutes();
                SaveMixerSettings();
                NotifyDeviceChanged("入力");
                break;

            case nameof(MixerChannelRow.Name):
                SaveMixerSettings();
                break;
        }
    }

    private void ApplyRoute(MixerChannelRow row, int outputIndex)
    {
        _mixer?.SetRouting(row.Index, outputIndex, row.GetRoute(outputIndex));
        SaveMixerSettings();
    }

    private void OutputRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isInitializingUi || _isSyncingRoutes || sender is not MixerOutputRow row)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(MixerOutputRow.SelectedDevice):
                SyncFeedbackRoutes();
                SaveMixerSettings();
                NotifyDeviceChanged("出力先");
                break;

            case nameof(MixerOutputRow.Volume):
                _mixer?.SetOutputVolume(row.Index, (float)row.Volume);
                SaveMixerSettings();
                break;

            case nameof(MixerOutputRow.Name):
                SaveMixerSettings();
                break;
        }
    }

    private void NotifyDeviceChanged(string kind)
    {
        if (_mixer is not null)
        {
            StatusText.Text = $"{kind}を変更しました。「停止」してから再度「ミックス開始」で反映されます。";
        }
    }

    private void SyncFeedbackRoutes()
    {
        _isSyncingRoutes = true;
        try
        {
            foreach (var row in _channelRows.Where(row => row.IsVirtual))
            {
                for (var output = 0; output < _outputRows.Count; output++)
                {
                    var inputId = row.SelectedDevice?.Id;
                    var outputId = _outputRows[output].SelectedDevice?.Id;
                    if (string.IsNullOrEmpty(inputId) || string.IsNullOrEmpty(outputId))
                    {
                        continue;
                    }

                    if (string.Equals(inputId, outputId, StringComparison.Ordinal))
                    {
                        row.SetRouteSilently(output, false);
                    }
                }

                if (!row.HasAnyRoute)
                {
                    for (var output = 0; output < _outputRows.Count; output++)
                    {
                        var outputId = _outputRows[output].SelectedDevice?.Id;
                        if (string.IsNullOrEmpty(outputId) ||
                            !string.Equals(outputId, row.SelectedDevice?.Id, StringComparison.Ordinal))
                        {
                            row.SetRouteSilently(output, true);
                            break;
                        }
                    }
                }
            }
        }
        finally
        {
            _isSyncingRoutes = false;
        }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e) => StartMixing(showValidationMessages: true);

    private void StartMixing(bool showValidationMessages)
    {
        var inputs = _channelRows
            .Select(row => new MixerInputDefinition(
                row.Name,
                string.IsNullOrEmpty(row.SelectedDevice?.Id) ? null : row.SelectedDevice!.Id,
                row.IsVirtual,
                (float)row.Volume,
                row.RoutingSnapshot()))
            .ToArray();
        var outputs = _outputRows
            .Select(row => new MixerOutputDefinition(
                row.Name,
                string.IsNullOrEmpty(row.SelectedDevice?.Id) ? null : row.SelectedDevice!.Id,
                (float)row.Volume))
            .ToArray();

        if (outputs.All(output => output.DeviceId is null))
        {
            if (showValidationMessages)
            {
                MessageBox.Show("H/W出力を1つ以上選択してください。", "出力が未選択",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        if (inputs.All(input => input.DeviceId is null))
        {
            if (showValidationMessages)
            {
                MessageBox.Show("H/W入力またはバーチャル入力を1つ以上選択してください。", "入力が未選択",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        _mixer?.Dispose();
        _mixer = null;

        var warnings = FindFeedbackLoops(inputs, outputs).ToList();

        try
        {
            _mixer = new MixerEngine(inputs, outputs, _settings.OutputLatencyMs);
            foreach (var row in _outputRows)
            {
                _mixer.SetOutputVolume(row.Index, (float)row.Volume);
            }

            _mixer.Start();

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;

            warnings.AddRange(_mixer.StartupIssues);
            StatusText.Text = warnings.Count == 0
                ? $"ミックス中です（出力 {_mixer.ActiveOutputCount} 件）。"
                : $"ミックス中です（出力 {_mixer.ActiveOutputCount} 件 / 警告 {warnings.Count} 件）。";

            if (warnings.Count > 0 && showValidationMessages)
            {
                MessageBox.Show(
                    string.Join(Environment.NewLine, warnings) + $"{Environment.NewLine}{Environment.NewLine}ログ: {AppPaths.LogDirectory}",
                    "確認してください",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception exception)
        {
            _mixer?.Dispose();
            _mixer = null;
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            AppLog.Error("音声の開始に失敗しました。", exception);

            if (showValidationMessages)
            {
                MessageBox.Show($"{exception.Message}{Environment.NewLine}{Environment.NewLine}ログ: {AppPaths.LogDirectory}",
                    "音声を開始できません", MessageBoxButton.OK, MessageBoxImage.Error);
            }

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

    private void RestartMixerIfRunning()
    {
        if (_mixer is null)
        {
            return;
        }

        _mixer.Dispose();
        _mixer = null;
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        StartMixing(showValidationMessages: false);
    }

    private void OpenUpdateSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsTabButton.IsChecked = true;
        SettingsNavList.SelectedIndex = 3;
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
            _releases = await _updateService.GetReleasesAsync(CancellationToken.None);
            _changelog = await _updateService.GetChangelogAsync(CancellationToken.None);
            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            _settings.Save();
            UpdateReleaseHistory();

            if (_releases.Count == 0)
            {
                _availableRelease = null;
                UpdateActionsPanel.Visibility = Visibility.Collapsed;
                UpdateBanner.Visibility = Visibility.Collapsed;
                SetUpdateStatus("公開されているリリースが見つかりませんでした。");
                return;
            }

            var latest = _releases[0];
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
                SetUpdateStatus($"最新です（{AppInfo.DisplayVersion}）。過去のリリースは下に表示しています。");
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

    private void UpdateReleaseHistory()
    {
        var items = _releases
            .Select(release => new ReleaseHistoryItem(
                release.IsPrerelease ? $"{release.TagName}（プレリリース）" : release.TagName,
                release.Name,
                release.PublishedAt is null ? string.Empty : release.PublishedAt.Value.ToLocalTime().ToString("yyyy/MM/dd"),
                release.HtmlUrl,
                GetChangeLines(release.Version)))
            .ToArray();

        ReleaseHistoryList.ItemsSource = items;
        ReleaseHistoryEmptyText.Visibility = items.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private IReadOnlyList<string> GetChangeLines(string version)
    {
        if (!_changelog.TryGetValue(version, out var lines) || lines.Count == 0)
        {
            return Array.Empty<string>();
        }

        return lines.Select(line => $"・{line}").ToArray();
    }

    private void ReleaseHistoryItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url } && !string.IsNullOrWhiteSpace(url))
        {
            OpenUrl(url);
        }
    }

    private async void InstallUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_availableRelease is null || _isUpdating)
        {
            return;
        }

        var answer = MessageBox.Show(
            $"{_availableRelease.TagName} にアップデートします。{Environment.NewLine}ダウンロード後にインストーラーが起動し、PulseWeaver は終了します。よろしいですか？",
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

    private void RefreshProfiles(string? selectName = null)
    {
        _isRefreshingProfiles = true;
        try
        {
            var profiles = ProfileService.ListProfiles();
            ProfileCombo.ItemsSource = profiles;
            ProfileCombo.SelectedItem = selectName is null
                ? null
                : profiles.FirstOrDefault(profile => string.Equals(profile.Name, selectName, StringComparison.OrdinalIgnoreCase));

            ProfileStatusText.Text = profiles.Count == 0
                ? "保存済みの構成はありません。「現在の構成を保存」で作成できます。"
                : $"{profiles.Count} 件の構成があります。選択すると即座に切り替わります。";
        }
        finally
        {
            _isRefreshingProfiles = false;
        }
    }

    private void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingProfiles || ProfileCombo.SelectedItem is not ProfileEntry entry)
        {
            return;
        }

        try
        {
            ApplySettings(ProfileService.LoadFromFile(entry.Path));
            ProfileStatusText.Text = $"「{entry.Name}」を適用しました。";
        }
        catch (Exception exception)
        {
            AppLog.Error($"構成「{entry.Name}」を読み込めませんでした。", exception);
            ProfileStatusText.Text = $"読み込めませんでした: {exception.Message}";
        }
    }

    private void SaveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = ProfileService.SanitizeName(ProfileNameBox.Text);
            ProfileService.SaveProfile(name, _settings);
            ProfileNameBox.Text = string.Empty;
            RefreshProfiles(name);
            ProfileStatusText.Text = $"「{name}」として保存しました。";
        }
        catch (Exception exception)
        {
            AppLog.Error("構成を保存できませんでした。", exception);
            ProfileStatusText.Text = $"保存できませんでした: {exception.Message}";
        }
    }

    private void ExportSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "構成をエクスポート",
            Filter = "PulseWeaver 構成 (*.json)|*.json|すべてのファイル (*.*)|*.*",
            DefaultExt = ".json",
            FileName = $"PulseWeaver-{DateTime.Now:yyyyMMdd-HHmm}.json",
            InitialDirectory = AppPaths.ProfilesDirectory
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ProfileService.Export(_settings, dialog.FileName);
            ProfileStatusText.Text = $"エクスポートしました: {dialog.FileName}";
        }
        catch (Exception exception)
        {
            AppLog.Error("構成をエクスポートできませんでした。", exception);
            ProfileStatusText.Text = $"エクスポートできませんでした: {exception.Message}";
        }
    }

    private void ImportSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "構成をインポート",
            Filter = "PulseWeaver 構成 (*.json)|*.json|すべてのファイル (*.*)|*.*",
            DefaultExt = ".json",
            InitialDirectory = AppPaths.ProfilesDirectory
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ApplySettings(ProfileService.Import(dialog.FileName));
            ProfileStatusText.Text = $"インポートしました: {dialog.FileName}";
        }
        catch (Exception exception)
        {
            AppLog.Error("構成をインポートできませんでした。", exception);
            ProfileStatusText.Text = $"インポートできませんでした: {exception.Message}";
        }
    }

    private void ReloadProfilesButton_Click(object sender, RoutedEventArgs e) => RefreshProfiles();

    private void OpenProfilesFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ProfileService.EnsureDirectory();
            Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.ProfilesDirectory) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLog.Error("構成フォルダを開けませんでした。", exception);
            ProfileStatusText.Text = $"フォルダを開けませんでした: {exception.Message}";
        }
    }

    private void ApplySettings(AppSettings settings)
    {
        settings.EnsureShape();
        _settings = settings;
        _settings.Save();
        ApplySettingsToUi();
        BuildMixerRows(reloadDevices: true);
        RestartMixerIfRunning();
        StatusText.Text = "構成を切り替えました。";
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
