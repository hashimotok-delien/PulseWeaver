using System.Reflection;

namespace PulseWeaver;

internal static class AppInfo
{
    public const string RepositoryOwner = "hashimotok-delien";
    public const string RepositoryName = "PulseWeaver";
    public const string VirtualCableUrl = "https://vb-audio.com/Cable/";

    public static string RepositoryUrl => $"https://github.com/{RepositoryOwner}/{RepositoryName}";
    public static string ReleasesUrl => $"{RepositoryUrl}/releases";
    public static string ReleasesApiUrl => $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases?per_page=15";

    public static string Version { get; } = ResolveVersion();
    public static string DisplayVersion => $"v{Version}";

    private static string ResolveVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var metadataIndex = informational.IndexOf('+');
            return metadataIndex > 0 ? informational[..metadataIndex] : informational;
        }

        var version = assembly.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
