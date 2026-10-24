using System.IO;

namespace PulseWeaver;

internal static class AppPaths
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PulseWeaver");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");
    public static string UpdatesDirectory => Path.Combine(DataDirectory, "updates");
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
}
