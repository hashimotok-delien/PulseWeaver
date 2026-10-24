using System.IO;
using System.Text.Json;

namespace PulseWeaver;

internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public bool AutoCheckForUpdates { get; set; } = true;

    public bool AutoStartWithWindows { get; set; }

    public bool BackgroundStart { get; set; }

    public bool AutoStartMix { get; set; }

    public string? DefaultProfile { get; set; }

    public int OutputLatencyMs { get; set; } = 100;

    public DateTime? LastUpdateCheckUtc { get; set; }

    public string?[] HardwareInputIds { get; set; } = new string?[6];

    public string?[] VirtualInputIds { get; set; } = new string?[3];

    public string?[] HardwareOutputIds { get; set; } = new string?[4];

    public string?[] InputNames { get; set; } = new string?[9];

    public string?[] OutputNames { get; set; } = new string?[4];

    public double[] InputVolumes { get; set; } = Enumerable.Repeat(0.8, 9).ToArray();

    public double[] OutputVolumes { get; set; } = Enumerable.Repeat(0.8, 4).ToArray();

    public bool[] InputRouting { get; set; } = new bool[9 * 4];

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
        var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
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

        File.WriteAllText(path, JsonSerializer.Serialize(settings, SerializerOptions));
    }

    public AppSettings Clone() => new()
    {
        AutoCheckForUpdates = AutoCheckForUpdates,
        AutoStartWithWindows = AutoStartWithWindows,
        BackgroundStart = BackgroundStart,
        AutoStartMix = AutoStartMix,
        DefaultProfile = DefaultProfile,
        OutputLatencyMs = OutputLatencyMs,
        LastUpdateCheckUtc = LastUpdateCheckUtc,
        HardwareInputIds = (string?[])HardwareInputIds.Clone(),
        VirtualInputIds = (string?[])VirtualInputIds.Clone(),
        HardwareOutputIds = (string?[])HardwareOutputIds.Clone(),
        InputNames = (string?[])InputNames.Clone(),
        OutputNames = (string?[])OutputNames.Clone(),
        InputVolumes = (double[])InputVolumes.Clone(),
        OutputVolumes = (double[])OutputVolumes.Clone(),
        InputRouting = (bool[])InputRouting.Clone()
    };

    public bool IsAutoCheckDue() =>
        LastUpdateCheckUtc is null || DateTime.UtcNow - LastUpdateCheckUtc.Value >= AutoCheckInterval;

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
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
        HardwareInputIds = Resize(HardwareInputIds, 6);
        VirtualInputIds = Resize(VirtualInputIds, 3);
        HardwareOutputIds = Resize(HardwareOutputIds, 4);
        InputNames = Resize(InputNames, 9);
        OutputNames = Resize(OutputNames, 4);

        if (InputVolumes.Length != 9)
        {
            InputVolumes = Enumerable.Repeat(0.8, 9).ToArray();
        }

        if (OutputVolumes is null || OutputVolumes.Length != 4)
        {
            OutputVolumes = Enumerable.Repeat(0.8, 4).ToArray();
        }

        if (InputRouting.Length != 9 * 4)
        {
            InputRouting = new bool[9 * 4];
        }
    }

    private static string?[] Resize(string?[]? values, int length)
    {
        var result = new string?[length];
        if (values is not null)
        {
            Array.Copy(values, result, Math.Min(values.Length, length));
        }

        return result;
    }
}
