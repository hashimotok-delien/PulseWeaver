using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PulseWeaver;

internal sealed class AppSettings
{
    /// <summary>入力・出力を可変ストリップで持つ形式。1 以下は 0.0.6 以前の固定長配列。</summary>
    public const int CurrentVersion = 2;

    private const int LegacyHardwareInputCount = 6;
    private const int LegacyVirtualInputCount = 3;
    private const int LegacyOutputCount = 4;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public int Version { get; set; } = CurrentVersion;

    public bool AutoCheckForUpdates { get; set; } = true;

    public bool AutoStartWithWindows { get; set; }

    public bool BackgroundStart { get; set; }

    public bool AutoStartMix { get; set; }

    public string? DefaultProfile { get; set; }

    public int OutputLatencyMs { get; set; } = 50;

    public DateTime? LastUpdateCheckUtc { get; set; }

    public List<MixerInputConfig> Inputs { get; set; } = CreateDefaultInputs();

    public List<MixerOutputConfig> Outputs { get; set; } = CreateDefaultOutputs();

    // ---- 0.0.6 以前の形式。読み込み時に Inputs / Outputs へ移行する ----

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string?[]? HardwareInputIds { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string?[]? VirtualInputIds { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string?[]? HardwareOutputIds { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string?[]? InputNames { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string?[]? OutputNames { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? InputVolumes { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? OutputVolumes { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool[]? InputRouting { get; set; }

    public static TimeSpan AutoCheckInterval { get; } = TimeSpan.FromHours(24);

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                return LoadFrom(AppPaths.SettingsFile);
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("設定の読み込みに失敗しました。既定値を使用します。", exception);
        }

        return new AppSettings();
    }

    public static AppSettings LoadFrom(string path)
    {
        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
        settings.EnsureShape();
        return settings;
    }

    public static void SaveTo(AppSettings settings, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        settings.EnsureShape();
        File.WriteAllText(path, JsonSerializer.Serialize(settings, SerializerOptions));
    }

    public AppSettings Clone()
    {
        EnsureShape();
        return new AppSettings
        {
            Version = Version,
            AutoCheckForUpdates = AutoCheckForUpdates,
            AutoStartWithWindows = AutoStartWithWindows,
            BackgroundStart = BackgroundStart,
            AutoStartMix = AutoStartMix,
            DefaultProfile = DefaultProfile,
            OutputLatencyMs = OutputLatencyMs,
            LastUpdateCheckUtc = LastUpdateCheckUtc,
            Inputs = Inputs.Select(input => input.Clone()).ToList(),
            Outputs = Outputs.Select(output => output.Clone()).ToList()
        };
    }

    public bool IsAutoCheckDue() =>
        LastUpdateCheckUtc is null || DateTime.UtcNow - LastUpdateCheckUtc.Value >= AutoCheckInterval;

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            EnsureShape();
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, SerializerOptions));
        }
        catch (Exception exception)
        {
            AppLog.Error("設定の保存に失敗しました。", exception);
        }
    }

    /// <summary>
    /// 構成（プロファイル）を適用するときに、アプリの動作設定は現在のものを引き継ぐ。
    /// </summary>
    public void CopyBehaviorFrom(AppSettings other)
    {
        AutoCheckForUpdates = other.AutoCheckForUpdates;
        AutoStartWithWindows = other.AutoStartWithWindows;
        BackgroundStart = other.BackgroundStart;
        AutoStartMix = other.AutoStartMix;
        DefaultProfile = other.DefaultProfile;
        LastUpdateCheckUtc = other.LastUpdateCheckUtc;
    }

    public void EnsureShape()
    {
        if (Version < CurrentVersion || HasLegacyLayout)
        {
            MigrateFromLegacy();
        }

        Inputs ??= CreateDefaultInputs();
        Outputs ??= CreateDefaultOutputs();

        foreach (var input in Inputs)
        {
            if (string.IsNullOrEmpty(input.Id))
            {
                input.Id = Guid.NewGuid().ToString("N");
            }

            input.Routes ??= new Dictionary<string, bool>(StringComparer.Ordinal);
            input.RouteSlots ??= new Dictionary<string, bool>(StringComparer.Ordinal);
        }

        foreach (var output in Outputs)
        {
            if (string.IsNullOrEmpty(output.Id))
            {
                output.Id = Guid.NewGuid().ToString("N");
            }
        }

        Version = CurrentVersion;
    }

    private void MigrateFromLegacy()
    {
        if (!HasLegacyLayout)
        {
            return;
        }

        var outputs = new List<MixerOutputConfig>();
        for (var i = 0; i < LegacyOutputCount; i++)
        {
            outputs.Add(new MixerOutputConfig
            {
                Id = $"out{i + 1}",
                Role = OutputRole.Physical,
                DeviceId = At(HardwareOutputIds, i),
                Name = At(OutputNames, i),
                Volume = At(OutputVolumes, i, 0.8)
            });
        }

        var inputs = new List<MixerInputConfig>();
        for (var i = 0; i < LegacyHardwareInputCount; i++)
        {
            inputs.Add(CreateLegacyInput(i, InputSourceKind.Hardware, outputs, At(HardwareInputIds, i)));
        }

        for (var i = 0; i < LegacyVirtualInputCount; i++)
        {
            inputs.Add(CreateLegacyInput(LegacyHardwareInputCount + i, InputSourceKind.Loopback, outputs, At(VirtualInputIds, i)));
        }

        Inputs = inputs;
        Outputs = outputs;
        HardwareInputIds = null;
        VirtualInputIds = null;
        HardwareOutputIds = null;
        InputNames = null;
        OutputNames = null;
        InputVolumes = null;
        OutputVolumes = null;
        InputRouting = null;
        AppLog.Info("0.0.6 以前の設定形式を新しい形式へ移行しました。");
    }

    private MixerInputConfig CreateLegacyInput(int legacyIndex, InputSourceKind kind, List<MixerOutputConfig> outputs, string? deviceId)
    {
        var routes = new Dictionary<string, bool>(StringComparer.Ordinal);
        for (var o = 0; o < outputs.Count; o++)
        {
            var index = legacyIndex * LegacyOutputCount + o;
            routes[outputs[o].Id] = InputRouting is not null && index < InputRouting.Length && InputRouting[index];
        }

        return new MixerInputConfig
        {
            Id = $"in{legacyIndex + 1}",
            Kind = kind,
            DeviceId = deviceId,
            Name = At(InputNames, legacyIndex),
            Volume = At(InputVolumes, legacyIndex, 0.8),
            Routes = routes
        };
    }

    private bool HasLegacyLayout =>
        HardwareInputIds is not null || VirtualInputIds is not null || HardwareOutputIds is not null;

    private static string? At(string?[]? values, int index) =>
        values is not null && index < values.Length ? values[index] : null;

    private static double At(double[]? values, int index, double fallback) =>
        values is not null && index < values.Length ? values[index] : fallback;

    private static List<MixerInputConfig> CreateDefaultInputs()
    {
        var inputs = new List<MixerInputConfig>();

        for (var i = 0; i < 2; i++)
        {
            inputs.Add(new MixerInputConfig { Id = $"in{i + 1}", Kind = InputSourceKind.Hardware });
        }

        for (var i = 0; i < 3; i++)
        {
            inputs.Add(new MixerInputConfig { Id = $"in{3 + i}", Kind = InputSourceKind.Loopback });
        }

        return inputs;
    }

    private static List<MixerOutputConfig> CreateDefaultOutputs() => new()
    {
        new MixerOutputConfig { Id = "out1", Role = OutputRole.Physical },
        new MixerOutputConfig { Id = "out2", Role = OutputRole.Physical },
        new MixerOutputConfig { Id = "out3", Role = OutputRole.Physical },
        new MixerOutputConfig { Id = "out4", Role = OutputRole.Virtual },
        new MixerOutputConfig { Id = "out5", Role = OutputRole.Virtual }
    };
}
