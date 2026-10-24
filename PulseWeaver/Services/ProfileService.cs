using System.IO;

namespace PulseWeaver;

internal static class ProfileService
{
    private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

    public static string Directory => AppPaths.ProfilesDirectory;

    public static void EnsureDirectory() => System.IO.Directory.CreateDirectory(AppPaths.ProfilesDirectory);

    public static IReadOnlyList<ProfileEntry> ListProfiles()
    {
        try
        {
            EnsureDirectory();
            return System.IO.Directory
                .EnumerateFiles(AppPaths.ProfilesDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.CurrentCultureIgnoreCase)
                .Select(path => new ProfileEntry(Path.GetFileNameWithoutExtension(path), path))
                .ToArray();
        }
        catch (Exception exception)
        {
            AppLog.Error("構成ファイルの一覧を取得できませんでした。", exception);
            return Array.Empty<ProfileEntry>();
        }
    }

    public static string SanitizeName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new InvalidOperationException("構成名を入力してください。");
        }

        var sanitized = new string(trimmed.Select(c => InvalidNameChars.Contains(c) ? '_' : c).ToArray());
        return sanitized;
    }

    public static string GetPath(string name) =>
        Path.Combine(AppPaths.ProfilesDirectory, $"{SanitizeName(name)}.json");

    public static void SaveProfile(string name, AppSettings settings)
    {
        EnsureDirectory();
        AppSettings.SaveTo(settings, GetPath(name));
        AppLog.Info($"構成を保存しました: {GetPath(name)}");
    }

    public static AppSettings LoadFromFile(string path) => AppSettings.LoadFrom(path);

    public static void Export(AppSettings settings, string path) => AppSettings.SaveTo(settings, path);

    public static AppSettings Import(string path) => AppSettings.LoadFrom(path);
}

internal sealed record ProfileEntry(string Name, string Path)
{
    public override string ToString() => Name;
}
