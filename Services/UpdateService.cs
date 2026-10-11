using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using GnuSorter.Models;

namespace GnuSorter.Services;

/// <summary>
/// Self-update support: checks GitHub Releases for a newer build of the
/// self-contained single-file executable and, after a confirmation, downloads
/// it and swaps it in. Because a running exe cannot replace itself, the swap
/// is done by a tiny detached .cmd that kills the app, moves the new file over
/// the old one and relaunches it.
///
/// Checks are <b>channel-aware</b>: only releases whose channel matches this
/// build's channel (indev/alpha/beta/rc/release) are ever offered, and versions
/// are compared within that channel.
/// </summary>
public sealed class UpdateService
{
    private const string OwnerAndRepo = "notmicrosoft2000-cmd/gnu-sorter-windows";
    private const string ReleasesApiUrl =
        $"https://api.github.com/repos/{OwnerAndRepo}/releases?per_page=30";
    private const string AssetName = "gnu-sorter-windows.exe";
    private const int MaxReplaceAttempts = 10;

    private static readonly HttpClient Http = CreateClient();

    /// <summary>A build found for this channel (may or may not be newer).</summary>
    public sealed record UpdateCheck(string Tag, string DownloadUrl)
    {
        /// <summary>"3.0.1" for a friendly display (drops a leading "v").</summary>
        public string LatestDisplay => VersionInfo.Display(Tag);

        /// <summary>True when <see cref="Tag"/> is newer than the running build.</summary>
        public bool IsNewer => VersionInfo.IsNewer(Tag, AppInfo.Version);
    }

    private sealed class ReleaseInfo
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("assets")]
        public List<AssetInfo> Assets { get; set; } = new();
    }

    private sealed class AssetInfo
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // GitHub's API rejects requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "gnu-sorter-windows/" + AppInfo.Version);
        return client;
    }

    /// <summary>
    /// The newest non-draft release in this build's channel that carries the
    /// self-contained exe asset. Returns null when there is no such release or
    /// the network is unavailable.
    /// </summary>
    public async Task<UpdateCheck?> CheckAsync()
    {
        try
        {
            using var resp = await Http.GetAsync(ReleasesApiUrl);
            if (!resp.IsSuccessStatusCode)
            {
                return null; // 404 → no releases yet
            }
            var json = await resp.Content.ReadAsStringAsync();
            var releases = JsonSerializer.Deserialize<List<ReleaseInfo>>(json);
            if (releases is null)
            {
                return null;
            }

            var channel = AppInfo.Channel;
            ReleaseInfo? best = null;
            foreach (var rel in releases)
            {
                if (rel.Draft || string.IsNullOrWhiteSpace(rel.TagName))
                {
                    continue;
                }
                if (VersionInfo.ChannelOf(rel.TagName) != channel)
                {
                    continue;
                }
                if (best is null ||
                    VersionInfo.Compare(rel.TagName, best.TagName) > 0)
                {
                    best = rel;
                }
            }
            if (best is null)
            {
                return null;
            }

            var url = best.Assets.FirstOrDefault(a => string.Equals(
                a.Name, AssetName, StringComparison.OrdinalIgnoreCase))
                ?.BrowserDownloadUrl;
            return new UpdateCheck(best.TagName!, url ?? "");
        }
        catch (Exception)
        {
            return null; // offline / API hiccup
        }
    }

    /// <summary>Download the new exe under %LOCALAPPDATA%\gnu-sorter\update\.</summary>
    public async Task<(bool Ok, string Path, Exception? Error)> DownloadAsync(string url)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "gnu-sorter", "update");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, AssetName);
            using var resp = await Http.GetAsync(url);
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(dest);
            await resp.Content.CopyToAsync(fs);
            return (true, dest, null);
        }
        catch (Exception ex)
        {
            return (false, "", ex);
        }
    }

    /// <summary>
    /// Stage the downloaded exe so it replaces the running copy on exit. The
    /// app is killed by the updater script and relaunched automatically.
    /// </summary>
    public (bool Ok, string Message) Install(string downloadedPath)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(downloadedPath))
            {
                return (false, "Cannot locate the running executable.");
            }

            var exeName = Path.GetFileName(exe);
            var script = Path.Combine(Path.GetTempPath(),
                $"gs-update-{DateTime.Now:yyyyMMddHHmmss}.cmd");
            var contents =
                "@echo off\r\n" +
                $"set \"NEW={downloadedPath}\"\r\n" +
                $"set \"EXE={exe}\"\r\n" +
                "set /a tries=0\r\n" +
                ":loop\r\n" +
                "set /a tries+=1\r\n" +
                $"taskkill /im \"{exeName}\" /f >nul 2>&1\r\n" +
                "timeout /t 1 /nobreak >nul\r\n" +
                "move /y \"%NEW%\" \"%EXE%\" >nul 2>&1\r\n" +
                "if not exist \"%NEW%\" goto restart\r\n" +
                $"if %tries% lss {MaxReplaceAttempts} goto loop\r\n" +
                ":restart\r\n" +
                "start \"\" \"%EXE%\" >nul 2>&1\r\n" +
                "del \"%~f0\" >nul 2>&1\r\n";
            File.WriteAllText(script, contents);

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{script}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            });
            return (true, "Update staged — the app will restart automatically.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
