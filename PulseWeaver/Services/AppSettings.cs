using System.IO;
using System.Text.Json;

namespace PulseWeaver;

internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public bool AutoCheckForUpdates { get; set; } = true;

    public int OutputLatencyMs { get; set; } = 100;

    public DateTime? LastUpdateCheckUtc { get; set; }

    public static TimeSpan AutoCheckInterval { get; } = TimeSpan.FromHours(24);

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("設定の読み込みに失敗しました。既定値を使用します。", exception);
        }

        return new AppSettings();
    }

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
}
