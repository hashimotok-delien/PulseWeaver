using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace PulseWeaver;

internal sealed record UpdateRelease(
    string Version,
    string TagName,
    string Name,
    string HtmlUrl,
    DateTimeOffset? PublishedAt,
    bool IsPrerelease,
    string? InstallerUrl,
    string? InstallerName,
    string? ChecksumsUrl);

internal sealed class UpdateService : IDisposable
{
    private readonly HttpClient _httpClient;

    public UpdateService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"PulseWeaver/{AppInfo.Version}");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<IReadOnlyList<UpdateRelease>> GetReleasesAsync(CancellationToken cancellationToken)
    {
        using var stream = await _httpClient.GetStreamAsync(AppInfo.ReleasesApiUrl, cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var releases = new List<UpdateRelease>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            var tagName = release.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
            var version = VersionComparer.Normalize(tagName);
            if (string.IsNullOrEmpty(version))
            {
                continue;
            }

            string? installerUrl = null;
            string? installerName = null;
            string? checksumsUrl = null;
            if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
                    var url = asset.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() : null;
                    if (url is null)
                    {
                        continue;
                    }

                    if (name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        installerUrl = url;
                        installerName = name;
                    }
                    else if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        checksumsUrl = url;
                    }
                }
            }

            var name2 = release.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() : null;
            var htmlUrl = release.TryGetProperty("html_url", out var htmlElement) ? htmlElement.GetString() : null;
            var isPrerelease = release.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True;
            DateTimeOffset? publishedAt = null;
            if (release.TryGetProperty("published_at", out var publishedElement) &&
                DateTimeOffset.TryParse(publishedElement.GetString(), out var parsed))
            {
                publishedAt = parsed;
            }

            releases.Add(new UpdateRelease(
                version,
                tagName ?? $"v{version}",
                name2 ?? $"v{version}",
                htmlUrl ?? AppInfo.ReleasesUrl,
                publishedAt,
                isPrerelease,
                installerUrl,
                installerName,
                checksumsUrl));
        }

        return releases
            .OrderByDescending(release => release, Comparer<UpdateRelease>.Create((a, b) => VersionComparer.Compare(a.Version, b.Version)))
            .ToArray();
    }

    public async Task<UpdateRelease?> GetLatestAsync(CancellationToken cancellationToken)
    {
        var releases = await GetReleasesAsync(cancellationToken);
        return releases.Count == 0 ? null : releases[0];
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetChangelogAsync(CancellationToken cancellationToken)
    {
        foreach (var branch in new[] { "main", "master" })
        {
            try
            {
                var url = string.Format(AppInfo.ChangelogUrlTemplate, branch);
                var markdown = await _httpClient.GetStringAsync(url, cancellationToken);
                return ParseChangelog(markdown);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                AppLog.Info($"CHANGELOG を取得できませんでした ({branch}): {exception.Message}");
            }
        }

        AppLog.Info("同梱の CHANGELOG を使用します。");
        return LoadEmbeddedChangelog();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> LoadEmbeddedChangelog()
    {
        try
        {
            var assembly = typeof(UpdateService).Assembly;
            var name = assembly.GetManifestResourceNames()
                .FirstOrDefault(resource => resource.EndsWith("CHANGELOG.md", StringComparison.OrdinalIgnoreCase));
            if (name is null)
            {
                return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            }

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            return ParseChangelog(reader.ReadToEnd());
        }
        catch (Exception exception)
        {
            AppLog.Error("同梱 CHANGELOG の読み込みに失敗しました。", exception);
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseChangelog(string markdown)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        string? currentVersion = null;
        string? currentSection = null;
        var items = new List<string>();

        void Commit()
        {
            if (currentVersion is not null)
            {
                result[currentVersion] = items.ToArray();
            }
        }

        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Commit();
                currentVersion = ExtractVersion(line[3..]);
                currentSection = null;
                items = new List<string>();
                continue;
            }

            if (currentVersion is null)
            {
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                currentSection = line[4..].Trim();
                continue;
            }

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                var text = trimmed[2..].Trim();
                if (text.Length > 0)
                {
                    items.Add(currentSection is null ? text : $"{currentSection}: {text}");
                }
            }
        }

        Commit();
        return result;
    }

    private static string? ExtractVersion(string heading)
    {
        var text = heading.Trim();
        if (text.StartsWith('['))
        {
            var end = text.IndexOf(']');
            if (end > 1)
            {
                return VersionComparer.Normalize(text[1..end]);
            }
        }

        var token = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return token is null ? null : VersionComparer.Normalize(token);
    }

    public async Task<string> DownloadInstallerAsync(UpdateRelease release, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (release.InstallerUrl is null || release.InstallerName is null)
        {
            throw new InvalidOperationException("このリリースにはインストーラーが含まれていません。リリースページから手動でインストールしてください。");
        }

        Directory.CreateDirectory(AppPaths.UpdatesDirectory);
        var destination = Path.Combine(AppPaths.UpdatesDirectory, release.InstallerName);
        AppLog.Info($"インストーラーをダウンロードします: {release.InstallerUrl}");

        using (var response = await _httpClient.GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var totalLength = response.Content.Headers.ContentLength;

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = File.Create(destination);

            var buffer = new byte[81920];
            long totalRead = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                totalRead += read;
                if (totalLength is > 0)
                {
                    progress?.Report((double)totalRead / totalLength.Value);
                }
            }
        }

        await VerifyChecksumAsync(destination, release, cancellationToken);
        AppLog.Info($"ダウンロード完了: {destination}");
        return destination;
    }

    public static void RunInstaller(string installerPath)
    {
        var startInfo = new ProcessStartInfo(installerPath, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS")
        {
            UseShellExecute = true
        };
        Process.Start(startInfo);
    }

    private async Task VerifyChecksumAsync(string filePath, UpdateRelease release, CancellationToken cancellationToken)
    {
        if (release.ChecksumsUrl is null || release.InstallerName is null)
        {
            AppLog.Info("チェックサム情報がないため検証をスキップしました。");
            return;
        }

        var checksums = await _httpClient.GetStringAsync(release.ChecksumsUrl, cancellationToken);
        foreach (var line in checksums.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !parts[^1].Equals(release.InstallerName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var stream = File.OpenRead(filePath);
            using var sha256 = SHA256.Create();
            var actual = Convert.ToHexString(await sha256.ComputeHashAsync(stream, cancellationToken)).ToLowerInvariant();
            if (!actual.Equals(parts[0], StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(filePath);
                throw new InvalidOperationException("ダウンロードしたファイルのチェックサムが一致しませんでした。");
            }

            AppLog.Info("チェックサム検証に成功しました。");
            return;
        }

        AppLog.Info("チェックサムの記載が見つからないため検証をスキップしました。");
    }

    public void Dispose() => _httpClient.Dispose();
}
