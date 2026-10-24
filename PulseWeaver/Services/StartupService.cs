using System.Diagnostics;
using Microsoft.Win32;

namespace PulseWeaver;

internal static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PulseWeaver";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception exception)
        {
            AppLog.Error("自動起動の設定を読み取れませんでした。", exception);
            return false;
        }
    }

    public static void SetEnabled(bool enabled, bool startMinimized)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("自動起動の設定を保存できませんでした。");

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            AppLog.Info("自動起動を無効にしました。");
            return;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            var module = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(module))
            {
                throw new InvalidOperationException("実行ファイルのパスを取得できませんでした。");
            }

            executable = module;
        }

        var command = startMinimized ? $"\"{executable}\" --minimized" : $"\"{executable}\"";
        key.SetValue(ValueName, command, RegistryValueKind.String);
        AppLog.Info($"自動起動を有効にしました: {command}");
    }
}
