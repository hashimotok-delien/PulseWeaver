using System.Diagnostics;
using System.IO;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.ComponentModel;
using NAudio.CoreAudioApi;

namespace PulseWeaver;

public partial class MainWindow : Window
{
    private readonly MMDeviceEnumerator _deviceEnumerator = new();
    private AppSettings _settings = AppSettings.Load();
    private readonly UpdateService _updateService = new();
    private readonly List<MixerChannelRow> _channelRows = new();
    private readonly List<MixerOutputRow> _outputRows = new();
    private readonly DispatcherTimer _levelTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
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
    private WinForms.NotifyIcon? _trayIcon;
    private bool _isExiting;
    private readonly bool _startHidden;

    public MainWindow(bool startHidden = false)
    {
        _startHidden = startHidden;
        InitializeComponent();
        ApplySettingsToUi();
        LoadDevices();
        BuildMixerRows(reloadDevices: false);
        RefreshVirtualDevices();
        RefreshProfiles();
        _levelTimer.Tick += LevelTimer_Tick;
        _levelTimer.Start();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyDefaultProfileIfNeeded();

        if (_startHidden && _settings.BackgroundStart)
        {
            HideToTray(showBalloon: false);
        }

        if (_settings.AutoStartMix)
        {
            StartMixing(showValidationMessages: false);
        }

        if (_settings.AutoCheckForUpdates && _settings.IsAutoCheckDue())
        {
            await CheckForUpdatesAsync(userInitiated: false);
        }
        else
        {
            SetUpdateStatus("次回の自動確認待ちです。「最新かどうかを調べる」からいつでも確認できます。");
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExiting || !_settings.BackgroundStart)
        {
            return;
        }

        e.Cancel = true;
        HideToTray(showBalloon: true);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _levelTimer.Stop();
        _levelTimer.Tick -= LevelTimer_Tick;

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

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
            _settings.AutoStartWithWindows = StartupService.IsEnabled();
            AutoCheckCheckBox.IsChecked = _settings.AutoCheckForUpdates;
            AutoStartCheckBox.IsChecked = _settings.AutoStartWithWindows;
            BackgroundStartCheckBox.IsChecked = _settings.BackgroundStart;
            AutoStartMixCheckBox.IsChecked = _settings.AutoStartMix;

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
            UnsubscribeRows();
            _channelRows.Clear();
            _outputRows.Clear();

            if (reloadDevices)
            {
                LoadDevices();
            }

            var none = new AudioDeviceOption(string.Empty, "（なし）");
            var renderOptions = new[] { none }.Concat(_renderDevices).ToArray();
            var captureOptions = new[] { none }.Concat(_captureDevices).ToArray();
            var virtualRenderOptions = OrderForVirtual(renderOptions);

            var defaultRenderId = GetDefaultDeviceId(DataFlow.Render, Role.Multimedia);
            var defaultCommunicationCaptureId = GetDefaultDeviceId(DataFlow.Capture, Role.Communications);
            var defaultMultimediaCaptureId = GetDefaultDeviceId(DataFlow.Capture, Role.Multimedia);

            var outputDefaults = PickOutputDefaults(renderOptions, defaultRenderId, _settings.Outputs);
            var outputLabels = new string[_settings.Outputs.Count];
            var physicalOutputs = 0;
            var virtualOutputs = 0;

            for (var i = 0; i < _settings.Outputs.Count; i++)
            {
                var config = _settings.Outputs[i];
                var isVirtual = config.Role == OutputRole.Virtual;
                outputLabels[i] = isVirtual ? $"B{++virtualOutputs}" : $"A{++physicalOutputs}";

                var options = isVirtual ? virtualRenderOptions : renderOptions;
                var selected = ResolveDevice(options, config.DeviceId, config.DeviceName, outputDefaults[i], out var missing);
                if (!missing)
                {
                    config.DeviceId = ToSavedId(selected);
                    config.DeviceName = selected.Id.Length > 0 ? selected.Name : null;
                }

                var row = new MixerOutputRow(i, config, outputLabels[i], options);
                row.PropertyChanged += OutputRow_PropertyChanged;
                _outputRows.Add(row);
            }

            var routeTargets = _outputRows
                .Select((row, index) => (Id: row.Config.Id, Label: outputLabels[index]))
                .ToArray();

            var hardwareInputs = 0;
            var loopbackInputs = 0;
            var appInputs = 0;

            for (var i = 0; i < _settings.Inputs.Count; i++)
            {
                var config = _settings.Inputs[i];
                var options = config.Kind == InputSourceKind.Loopback ? renderOptions : captureOptions;

                string defaultName;
                switch (config.Kind)
                {
                    case InputSourceKind.Hardware:
                        defaultName = $"H/W入力 {++hardwareInputs}";
                        break;

                    case InputSourceKind.Loopback:
                        defaultName = $"バーチャル入力 {++loopbackInputs}";
                        break;

                    default:
                        defaultName = string.IsNullOrWhiteSpace(config.ProcessName) ? $"アプリ {++appInputs}" : config.ProcessName!;
                        break;
                }

                string? defaultId = config.Kind switch
                {
                    InputSourceKind.Hardware => hardwareInputs switch
                    {
                        1 => defaultCommunicationCaptureId ?? defaultMultimediaCaptureId,
                        2 => defaultMultimediaCaptureId != defaultCommunicationCaptureId ? defaultMultimediaCaptureId : null,
                        _ => null
                    },
                    InputSourceKind.Loopback => loopbackInputs == 1 ? defaultRenderId : null,
                    _ => null
                };

                AudioDeviceOption? selected = null;
                if (config.Kind != InputSourceKind.Process)
                {
                    selected = ResolveDevice(options, config.DeviceId, config.DeviceName, defaultId, out var missing);
                    if (!missing)
                    {
                        config.DeviceId = ToSavedId(selected);
                        config.DeviceName = selected.Id.Length > 0 ? selected.Name : null;
                    }
                }

                var row = new MixerChannelRow(i, config, defaultName, options);
                row.BuildRoutes(routeTargets);
                row.BuildMonitorTargets(routeTargets);
                row.PropertyChanged += ChannelRow_PropertyChanged;
                row.RouteChanged += ChannelRow_RouteChanged;
                _channelRows.Add(row);
            }

            InputList.ItemsSource = _channelRows.ToArray();
            OutputList.ItemsSource = _outputRows.ToArray();

            SyncFeedbackRoutes();
            EnsureRoutes();
            RefreshMixerChrome();
            SaveMixerSettings();
        }
        finally
        {
            _isInitializingUi = false;
        }
    }

    private void UnsubscribeRows()
    {
        foreach (var row in _channelRows)
        {
            row.PropertyChanged -= ChannelRow_PropertyChanged;
            row.RouteChanged -= ChannelRow_RouteChanged;
        }

        foreach (var row in _outputRows)
        {
            row.PropertyChanged -= OutputRow_PropertyChanged;
        }
    }

    private void EnsureRoutes()
    {
        if (_outputRows.Count == 0)
        {
            return;
        }

        foreach (var row in _channelRows)
        {
            // 保存済みの ON/OFF（全 OFF を含む）は尊重し、未設定の入力だけ自動で振り分ける
            if (row.HasRouteSettings)
            {
                continue;
            }

            var inputId = row.SelectedDevice?.Id;
            var target = _outputRows.FirstOrDefault(output =>
                             string.IsNullOrEmpty(inputId) ||
                             !string.Equals(output.SelectedDevice?.Id, inputId, StringComparison.Ordinal))
                         ?? _outputRows[0];

            row.SetRouteSilently(target.Config.Id, true);
        }
    }

    private void RefreshMixerChrome()
    {
        InputSectionTitle.Text = $"入力（{_channelRows.Count}）";
        OutputSectionTitle.Text = $"出力（{_outputRows.Count}）";
        InputEmptyText.Visibility = _channelRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        OutputEmptyText.Visibility = _outputRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var monitorable = _channelRows.Where(row => row.IsDeviceSource).ToArray();
        MonitorList.ItemsSource = monitorable;
        MonitorEmptyText.Visibility = monitorable.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshVirtualDevices()
    {
        try
        {
            var detected = VirtualDeviceDetector.Detect(_renderDevices, _captureDevices);
            VirtualDeviceList.ItemsSource = detected;
            VirtualDeviceEmptyText.Visibility = detected.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            AppLog.Error("仮想オーディオデバイスの検出に失敗しました。", exception);
        }
    }

    private static AudioDeviceOption[] OrderForVirtual(AudioDeviceOption[] renderOptions) =>
        new[] { renderOptions[0] }
            .Concat(renderOptions.Skip(1).Where(option => VirtualDeviceDetector.IsVirtual(option.Name)))
            .Concat(renderOptions.Skip(1).Where(option => !VirtualDeviceDetector.IsVirtual(option.Name)))
            .ToArray();

    private void RebuildAfterStructureChange()
    {
        _settings.Save();
        BuildMixerRows(reloadDevices: false);
        RefreshVirtualDevices();
        RestartMixerIfRunning();
    }

    private static T? ResolveRow<T>(object sender) where T : class =>
        (sender as FrameworkElement)?.DataContext as T;



    private static IEnumerable<string> FindFeedbackLoops(MixerInputDefinition[] inputs, MixerOutputDefinition[] outputs)
    {
        foreach (var input in inputs)
        {
            if (input.Kind != InputSourceKind.Loopback || string.IsNullOrEmpty(input.DeviceId))
            {
                continue;
            }

            foreach (var output in outputs)
            {
                if (string.IsNullOrEmpty(output.DeviceId))
                {
                    continue;
                }

                if (!input.Routes.TryGetValue(output.Id, out var routed) || !routed)
                {
                    continue;
                }

                if (string.Equals(input.DeviceId, output.DeviceId, StringComparison.Ordinal))
                {
                    yield return $"「{input.Label}」の音を同じデバイス「{output.Label}」へ送っているため、ハウリングする可能性があります。";
                }
            }
        }
    }

    /// <summary>
    /// 保存された ID からデバイスを選ぶ。見つからないときは保存名で照合し、
    /// それでも無ければ missing を true にして「（なし）」を返す（保存値は消さない）。
    /// </summary>
    private static AudioDeviceOption ResolveDevice(
        AudioDeviceOption[] options,
        string? savedId,
        string? savedName,
        string? defaultId,
        out bool missing)
    {
        missing = false;

        if (!string.IsNullOrEmpty(savedId))
        {
            var match = options.FirstOrDefault(option => option.Id == savedId);
            if (match is not null)
            {
                return match;
            }

            // Bluetooth などでデバイス ID が振り直されることがあるため、保存した名前でも探す
            if (!string.IsNullOrWhiteSpace(savedName))
            {
                var byName = options.FirstOrDefault(option =>
                    option.Id.Length > 0 &&
                    string.Equals(option.Name, savedName, StringComparison.OrdinalIgnoreCase));
                if (byName is not null)
                {
                    return byName;
                }
            }

            missing = true;
            return options[0];
        }

        if (savedId is not null)
        {
            return options[0];
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

    private static string?[] PickOutputDefaults(
        AudioDeviceOption[] renderOptions,
        string? defaultRenderId,
        IReadOnlyList<MixerOutputConfig> outputs)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var result = new string?[outputs.Count];

        for (var i = 0; i < outputs.Count; i++)
        {
            string? picked = null;

            if (i == 0 && !string.IsNullOrEmpty(defaultRenderId))
            {
                picked = defaultRenderId;
            }
            else if (outputs[i].Role == OutputRole.Virtual)
            {
                picked = renderOptions
                    .FirstOrDefault(option => option.Id.Length > 0 && !used.Contains(option.Id) &&
                                              VirtualDeviceDetector.IsVirtual(option.Name))?.Id;
            }

            picked ??= renderOptions
                .FirstOrDefault(option => option.Id.Length > 0 && !used.Contains(option.Id) &&
                                          !VirtualDeviceDetector.IsVirtual(option.Name))?.Id;

            picked ??= renderOptions
                .FirstOrDefault(option => option.Id.Length > 0 && !used.Contains(option.Id))?.Id;

            if (picked is not null)
            {
                used.Add(picked);
            }

            result[i] = picked;
        }

        return result;
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

    private void SaveMixerSettings() => _settings.Save();

    private static string? ToSavedId(AudioDeviceOption? option) =>
        option is null ? null : option.Id.Length == 0 ? string.Empty : option.Id;

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

    private void GeneralOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        _settings.AutoCheckForUpdates = AutoCheckCheckBox.IsChecked == true;
        _settings.BackgroundStart = BackgroundStartCheckBox.IsChecked == true;
        _settings.AutoStartMix = AutoStartMixCheckBox.IsChecked == true;

        var autoStart = AutoStartCheckBox.IsChecked == true;
        try
        {
            if (autoStart != _settings.AutoStartWithWindows || autoStart)
            {
                StartupService.SetEnabled(autoStart, _settings.BackgroundStart);
            }

            _settings.AutoStartWithWindows = autoStart;
        }
        catch (Exception exception)
        {
            AppLog.Error("自動起動の設定を変更できませんでした。", exception);
            _isApplyingSettings = true;
            AutoStartCheckBox.IsChecked = _settings.AutoStartWithWindows;
            _isApplyingSettings = false;

            MessageBox.Show(
                $"自動起動の設定を変更できませんでした。{Environment.NewLine}{exception.Message}",
                "自動起動",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        _settings.Save();
        UpdateTrayIcon();
    }

    private void DefaultProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingSettings || DefaultProfileCombo.SelectedItem is not ProfileEntry entry)
        {
            return;
        }

        _settings.DefaultProfile = string.IsNullOrEmpty(entry.Name) ? null : entry.Name;
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

        if (e.PropertyName == nameof(MixerChannelRow.Level))
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(MixerChannelRow.Volume):
                _mixer?.SetInputVolume(row.Index, (float)row.Volume);
                break;

            case nameof(MixerChannelRow.Muted):
                _mixer?.SetInputMuted(row.Index, row.Muted);
                break;

            case nameof(MixerChannelRow.Solo):
                _mixer?.SetInputSolo(row.Index, row.Solo);
                break;

            case nameof(MixerChannelRow.Mono):
                _mixer?.SetInputMono(row.Index, row.Mono);
                break;

            case nameof(MixerChannelRow.Gate):
                _mixer?.SetInputGate(row.Index, row.Gate);
                break;

            case nameof(MixerChannelRow.Comp):
                _mixer?.SetInputComp(row.Index, row.Comp);
                break;

            case nameof(MixerChannelRow.PanX):
            case nameof(MixerChannelRow.PanY):
                _mixer?.SetInputPan(row.Index, row.PanX, row.PanY);
                break;

            case nameof(MixerChannelRow.MonitorVolume):
                _mixer?.SetMonitorVolume(row.Index, (float)row.MonitorVolume);
                break;

            case nameof(MixerChannelRow.MonitorOutputId):
                _mixer?.SetMonitorTarget(row.Index, row.MonitorOutputId);
                break;

            case nameof(MixerChannelRow.SelectedDevice):
                SyncFeedbackRoutes();
                NotifyDeviceChanged("入力");
                break;
        }

        SaveMixerSettings();
    }

    private void ChannelRow_RouteChanged(object? sender, string outputId)
    {
        if (_isInitializingUi || _isSyncingRoutes || sender is not MixerChannelRow row)
        {
            return;
        }

        var outputIndex = _outputRows.FindIndex(output => output.Config.Id == outputId);
        if (outputIndex >= 0)
        {
            _mixer?.SetRouting(row.Index, outputIndex, row.GetRoute(outputId));
        }

        SaveMixerSettings();
    }

    private void OutputRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isInitializingUi || _isSyncingRoutes || sender is not MixerOutputRow row)
        {
            return;
        }

        if (e.PropertyName == nameof(MixerOutputRow.Level))
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(MixerOutputRow.SelectedDevice):
                SyncFeedbackRoutes();
                NotifyDeviceChanged("出力先");
                break;

            case nameof(MixerOutputRow.Volume):
                _mixer?.SetOutputVolume(row.Index, (float)row.Volume);
                break;

            case nameof(MixerOutputRow.Muted):
                _mixer?.SetOutputMuted(row.Index, row.Muted);
                break;

            case nameof(MixerOutputRow.Mono):
                _mixer?.SetOutputMono(row.Index, row.Mono);
                break;

            case nameof(MixerOutputRow.EqEnabled):
            case nameof(MixerOutputRow.EqLow):
            case nameof(MixerOutputRow.EqMid):
            case nameof(MixerOutputRow.EqHigh):
                _mixer?.SetOutputEq(row.Index, row.EqEnabled, row.EqLow, row.EqMid, row.EqHigh);
                break;
        }

        SaveMixerSettings();
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
            foreach (var row in _channelRows.Where(row => row.Config.Kind == InputSourceKind.Loopback))
            {
                var inputId = row.SelectedDevice?.Id;
                if (string.IsNullOrEmpty(inputId))
                {
                    continue;
                }

                foreach (var output in _outputRows)
                {
                    var outputId = output.SelectedDevice?.Id;
                    if (!string.IsNullOrEmpty(outputId) && string.Equals(inputId, outputId, StringComparison.Ordinal))
                    {
                        row.SetRouteSilently(output.Config.Id, false);
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
                row.Config.Kind,
                string.IsNullOrEmpty(row.SelectedDevice?.Id) ? null : row.SelectedDevice!.Id,
                row.Config.ProcessId,
                row.Config.ProcessName,
                row.Config.IncludeProcessTree,
                (float)row.Volume,
                row.Muted,
                row.Solo,
                row.MonitorOutputId,
                (float)row.MonitorVolume,
                new Dictionary<string, bool>(row.Config.Routes, StringComparer.Ordinal),
                row.Mono,
                row.Gate,
                row.Comp,
                row.PanX,
                row.PanY))
            .ToArray();

        var outputs = _outputRows
            .Select(row => new MixerOutputDefinition(
                row.Config.Id,
                row.Name,
                string.IsNullOrEmpty(row.SelectedDevice?.Id) ? null : row.SelectedDevice!.Id,
                (float)row.Volume,
                row.Muted,
                row.Mono,
                row.EqEnabled,
                row.EqLow,
                row.EqMid,
                row.EqHigh))
            .ToArray();

        if (outputs.All(output => output.DeviceId is null))
        {
            if (showValidationMessages)
            {
                MessageBox.Show("出力を1つ以上選択してください。", "出力が未選択",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        if (inputs.All(input => input.Kind == InputSourceKind.Process ? input.ProcessId is null : input.DeviceId is null))
        {
            if (showValidationMessages)
            {
                MessageBox.Show("入力を1つ以上選択してください。", "入力が未選択",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        for (var i = 0; i < inputs.Length; i++)
        {
            if (inputs[i].Kind != InputSourceKind.Process || string.IsNullOrWhiteSpace(inputs[i].ProcessName))
            {
                continue;
            }

            var processId = AudioProcessCatalog.FindProcessIdByName(inputs[i].ProcessName);
            if (processId is null || processId == inputs[i].ProcessId)
            {
                continue;
            }

            _channelRows[i].Config.ProcessId = processId;
            _channelRows[i].RefreshProcessLabel();
            inputs[i] = inputs[i] with { ProcessId = processId };
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
        foreach (var row in _channelRows)
        {
            row.Level = 0d;
        }

        foreach (var row in _outputRows)
        {
            row.Level = 0d;
        }

        StatusText.Text = "停止しました。";
    }

    private void LevelTimer_Tick(object? sender, EventArgs e)
    {
        if (_mixer is null)
        {
            foreach (var row in _channelRows)
            {
                row.Level = 0d;
            }

            foreach (var row in _outputRows)
            {
                row.Level = 0d;
            }

            return;
        }

        var levels = _mixer.GetInputLevels();
        var outputLevels = _mixer.GetOutputLevels();

        // トレイに格納中や最小化中はメーターが描画されないため、更新を省いて負荷を下げる
        if (!IsVisible || WindowState == WindowState.Minimized)
        {
            return;
        }

        for (var i = 0; i < _channelRows.Count && i < levels.Length; i++)
        {
            var row = _channelRows[i];
            var peak = Math.Clamp(levels[i] * 1.6d, 0d, 1d);
            row.Level = Math.Max(peak, row.Level * 0.72d);
        }

        for (var i = 0; i < _outputRows.Count && i < outputLevels.Length; i++)
        {
            var row = _outputRows[i];
            var peak = Math.Clamp(outputLevels[i], 0d, 1d);
            row.Level = Math.Max(peak, row.Level * 0.72d);
        }
    }

    private void AddHardwareInput_Click(object sender, RoutedEventArgs e) => AddInput(InputSourceKind.Hardware);

    private void AddLoopbackInput_Click(object sender, RoutedEventArgs e) => AddInput(InputSourceKind.Loopback);

    private void AddProcessInput_Click(object sender, RoutedEventArgs e)
    {
        var candidate = PickProcess();
        if (candidate is null)
        {
            return;
        }

        var config = new MixerInputConfig
        {
            Kind = InputSourceKind.Process,
            ProcessId = candidate.ProcessId,
            ProcessName = candidate.ProcessName,
            Name = candidate.ProcessName
        };

        AddInputConfig(config);
    }

    private void AddInput(InputSourceKind kind)
    {
        var config = new MixerInputConfig { Kind = kind };

        if (kind == InputSourceKind.Hardware &&
            _settings.Inputs.All(input => input.Kind != InputSourceKind.Hardware))
        {
            var micId = GetDefaultDeviceId(DataFlow.Capture, Role.Communications)
                ?? GetDefaultDeviceId(DataFlow.Capture, Role.Multimedia);
            config.DeviceId = micId;
        }

        AddInputConfig(config);
    }

    private void AddInputConfig(MixerInputConfig config)
    {
        var firstOutputId = _outputRows.FirstOrDefault()?.Config.Id;
        if (firstOutputId is not null)
        {
            config.SetRoute(firstOutputId, true);
        }

        _settings.Inputs.Add(config);
        RebuildAfterStructureChange();
    }

    private void ChangeProcessButton_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveRow<MixerChannelRow>(sender) is not { } row)
        {
            return;
        }

        var candidate = PickProcess();
        if (candidate is null)
        {
            return;
        }

        row.SetProcess(candidate);
        SaveMixerSettings();
        RestartMixerIfRunning();
    }

    private void ChangeInputDevice_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveRow<MixerChannelRow>(sender) is not { } row || row.Config.Kind == InputSourceKind.Process)
        {
            return;
        }

        var isLoopback = row.Config.Kind == InputSourceKind.Loopback;
        var window = new DevicePickerWindow(
            isLoopback ? "バーチャル入力のデバイスを選択" : "入力デバイスを選択",
            isLoopback ? DataFlow.Render : DataFlow.Capture,
            row.Config.DeviceId,
            row.Config.DeviceName,
            "新しく接続したデバイスが一覧に無いときは「再スキャン」を押してください。")
        {
            Owner = this
        };

        if (window.ShowDialog() == true)
        {
            ApplyDeviceSelection(row.Config.DeviceId, window.SelectedDeviceId, id => row.Config.DeviceId = id);
        }
    }

    private void ChangeOutputDevice_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveRow<MixerOutputRow>(sender) is not { } row)
        {
            return;
        }

        var window = new DevicePickerWindow(
            row.IsVirtual ? "仮想出力のデバイスを選択" : "出力デバイスを選択",
            DataFlow.Render,
            row.Config.DeviceId,
            row.Config.DeviceName,
            "CABLE Input などの仮想デバイスもここから選べます。接続したデバイスを探し直すときは「再スキャン」を押してください。")
        {
            Owner = this
        };

        if (window.ShowDialog() == true)
        {
            ApplyDeviceSelection(row.Config.DeviceId, window.SelectedDeviceId, id => row.Config.DeviceId = id);
        }
    }

    /// <summary>デバイスを選び直したあと、デバイス一覧と行を作り直してミックスを再開する。</summary>
    private void ApplyDeviceSelection(string? currentId, string? selectedId, Action<string> apply)
    {
        var newId = selectedId ?? string.Empty;
        if (string.Equals(currentId ?? string.Empty, newId, StringComparison.Ordinal))
        {
            return;
        }

        var wasRunning = _mixer is not null;
        apply(newId);
        SaveMixerSettings();
        BuildMixerRows(reloadDevices: true);
        RefreshVirtualDevices();

        if (wasRunning)
        {
            RestartMixerIfRunning();
        }
        else
        {
            StatusText.Text = "デバイスを変更しました。「ミックス開始」で反映されます。";
        }
    }

    private void MoveInputUp_Click(object sender, RoutedEventArgs e) => MoveInput(sender, -1);

    private void MoveInputDown_Click(object sender, RoutedEventArgs e) => MoveInput(sender, 1);

    private void MoveInput(object sender, int delta) =>
        MoveRow(_settings.Inputs, ResolveRow<MixerChannelRow>(sender)?.Config, delta);

    private void RemoveInput_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveRow<MixerChannelRow>(sender) is not { } row || !_settings.Inputs.Remove(row.Config))
        {
            return;
        }

        RebuildAfterStructureChange();
    }

    private void AddPhysicalOutput_Click(object sender, RoutedEventArgs e) => AddOutput(OutputRole.Physical);

    private void AddVirtualOutput_Click(object sender, RoutedEventArgs e) => AddOutput(OutputRole.Virtual);

    private void AddOutput(OutputRole role)
    {
        _settings.Outputs.Add(new MixerOutputConfig { Role = role });
        RebuildAfterStructureChange();
    }

    private void AddVirtualDeviceOutput_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not VirtualAudioDevice device)
        {
            return;
        }

        _settings.Outputs.Add(new MixerOutputConfig
        {
            Role = OutputRole.Virtual,
            DeviceId = device.Playback.Id,
            Name = device.PlaybackName
        });

        RebuildAfterStructureChange();
        StatusText.Text = $"「{device.PlaybackName}」を仮想出力として追加しました。{device.SuggestText}";
    }

    private void MoveOutputUp_Click(object sender, RoutedEventArgs e) => MoveOutput(sender, -1);

    private void MoveOutputDown_Click(object sender, RoutedEventArgs e) => MoveOutput(sender, 1);

    private void MoveOutput(object sender, int delta) =>
        MoveRow(_settings.Outputs, ResolveRow<MixerOutputRow>(sender)?.Config, delta);

    private void RemoveOutput_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveRow<MixerOutputRow>(sender) is not { } row || !_settings.Outputs.Remove(row.Config))
        {
            return;
        }

        RebuildAfterStructureChange();
    }

    private void MoveRow<T>(List<T> list, T? item, int delta) where T : class
    {
        if (item is null)
        {
            return;
        }

        var index = list.IndexOf(item);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= list.Count)
        {
            return;
        }

        (list[index], list[target]) = (list[target], list[index]);
        RebuildAfterStructureChange();
    }

    private AudioProcessCandidate? PickProcess()
    {
        IReadOnlyList<AudioProcessCandidate> candidates;
        try
        {
            candidates = AudioProcessCatalog.GetCandidates();
        }
        catch (Exception exception)
        {
            AppLog.Error("アプリ一覧を取得できませんでした。", exception);
            MessageBox.Show($"アプリ一覧を取得できませんでした。{exception.Message}", "アプリの選択",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        if (candidates.Count == 0)
        {
            MessageBox.Show("取り込めるアプリが見つかりませんでした。音を再生しているアプリを起動してから再度お試しください。",
                "アプリの選択", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        var window = new ProcessPickerWindow(candidates) { Owner = this };
        return window.ShowDialog() == true ? window.SelectedCandidate : null;
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
            $"{_availableRelease.TagName} にアップデートします。{Environment.NewLine}ダウンロード後にインストーラーが起動し、PulseWeaver は終了します。インストール完了後に自動で起動し直します。よろしいですか？",
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
            SetUpdateStatus("インストーラーを起動します。インストール完了後に自動で起動し直します…");
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

    private void ApplyDefaultProfileIfNeeded()
    {
        var name = _settings.DefaultProfile;
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            var path = ProfileService.GetPath(name);
            if (!File.Exists(path))
            {
                AppLog.Info($"既定の構成が見つかりません: {path}");
                return;
            }

            ApplySettings(ProfileService.LoadFromFile(path));
            AppLog.Info($"既定の構成を適用しました: {name}");
            StatusText.Text = $"既定の構成「{name}」を適用しました。";
        }
        catch (Exception exception)
        {
            AppLog.Error($"既定の構成「{name}」を適用できませんでした。", exception);
        }
    }

    private void UpdateTrayIcon()
    {
        if (!_settings.BackgroundStart)
        {
            if (_trayIcon is not null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            return;
        }

        if (_trayIcon is not null)
        {
            return;
        }

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("表示", null, (_, _) => ShowFromTray());
        menu.Items.Add("ミックス開始/停止", null, (_, _) => ToggleMixingFromTray());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => ExitApplication());

        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "PulseWeaver",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == WinForms.MouseButtons.Left)
            {
                ShowFromTray();
            }
        };
    }

    private static Drawing.Icon LoadAppIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(new Uri("Assets/PulseWeaver.ico", UriKind.Relative));
            if (resource is not null)
            {
                using var stream = resource.Stream;
                return new Drawing.Icon(stream);
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("トレイアイコンを読み込めませんでした。", exception);
        }

        return Drawing.SystemIcons.Application;
    }

    private void HideToTray(bool showBalloon)
    {
        UpdateTrayIcon();
        if (_trayIcon is null)
        {
            // トレイに格納できないと復帰手段がなくなるため、その場合は表示したままにする
            AppLog.Error("トレイアイコンを作成できませんでした。ウィンドウを表示したままにします。");
            Show();
            return;
        }

        Hide();

        if (showBalloon && _trayIcon is not null)
        {
            _trayIcon.ShowBalloonTip(2000, "PulseWeaver", "バックグラウンドで動作しています。タスクトレイから再表示できます。",
                WinForms.ToolTipIcon.Info);
        }
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ToggleMixingFromTray()
    {
        if (_mixer is null)
        {
            StartMixing(showValidationMessages: false);
        }
        else
        {
            _mixer.Dispose();
            _mixer = null;
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            StatusText.Text = "停止しました。";
        }
    }

    private void ExitApplication()
    {
        _isExiting = true;
        Close();
    }

    private void RefreshProfiles(string? selectName = null)
    {
        _isRefreshingProfiles = true;
        var wasApplying = _isApplyingSettings;
        _isApplyingSettings = true;
        try
        {
            var profiles = ProfileService.ListProfiles();
            ProfileCombo.ItemsSource = profiles;
            ProfileCombo.SelectedItem = selectName is null
                ? null
                : profiles.FirstOrDefault(profile => string.Equals(profile.Name, selectName, StringComparison.OrdinalIgnoreCase));
            OverwriteProfileButton.IsEnabled = ProfileCombo.SelectedItem is not null;

            var defaultOptions = new List<ProfileEntry> { new("（現在の設定）", string.Empty) };
            defaultOptions.AddRange(profiles);
            DefaultProfileCombo.ItemsSource = defaultOptions;
            var currentDefault = _settings.DefaultProfile ?? string.Empty;
            DefaultProfileCombo.SelectedItem = defaultOptions
                .FirstOrDefault(option => string.Equals(option.Name, currentDefault, StringComparison.OrdinalIgnoreCase))
                ?? defaultOptions[0];

            ProfileStatusText.Text = profiles.Count == 0
                ? "保存済みの構成はありません。「現在の構成を保存」で作成できます。"
                : $"{profiles.Count} 件の構成があります。選択すると即座に切り替わります。";
        }
        finally
        {
            _isApplyingSettings = wasApplying;
            _isRefreshingProfiles = false;
        }
    }

    private void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingProfiles || ProfileCombo.SelectedItem is not ProfileEntry entry)
        {
            return;
        }

        ProfileNameBox.Text = entry.Name;
        OverwriteProfileButton.IsEnabled = true;

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
            if (File.Exists(ProfileService.GetPath(name)))
            {
                var answer = MessageBox.Show(
                    $"「{name}」は既に存在します。上書きしますか？",
                    "上書きの確認",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes)
                {
                    return;
                }
            }

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

    private void OverwriteProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not ProfileEntry entry)
        {
            return;
        }

        var answer = MessageBox.Show(
            $"「{entry.Name}」を現在の設定で上書きしますか？",
            "上書きの確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            ProfileService.SaveProfile(entry.Name, _settings);
            RefreshProfiles(entry.Name);
            ProfileStatusText.Text = $"「{entry.Name}」を上書きしました。";
        }
        catch (Exception exception)
        {
            AppLog.Error("構成を上書きできませんでした。", exception);
            ProfileStatusText.Text = $"上書きできませんでした: {exception.Message}";
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
        settings.CopyBehaviorFrom(_settings);
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
