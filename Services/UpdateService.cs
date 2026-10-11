using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GnuSorter.Services;

/// <summary>
/// Self-update support: checks GitHub Releases for a newer build of the
/// self-contained single-file executable and, after a confirmation, downloads
/// it and swaps it in. Because a running exe cannot replace itself, the swap
/// is done by a tiny detached .cmd that kills the app, moves the new file over
/// the old one and relaunches it.
/// </summary>
public sealed class UpdateService
{
    private const string OwnerAndRepo = "notmicrosoft2000-cmd/gnu-sorter-windows";
    private const string LatestApiUrl =
        $"https://api.github.com/repos/{OwnerAndRepo}/releases/latest";
    private const string AssetName = "gnu-sorter-windows.exe";
    private const int MaxReplaceAttempts = 10;

    private static readonly HttpClient Http = CreateClient();

    private sealed class ReleaseInfo
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

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
        client.DefaultRequestHeaders.UserAgent.ParseAdd("gnu-sorter-windows/" +
            CurrentVersion());
        return client;
    }

    /// <summary>The running build's version (major/minor/build from the assembly).</summary>
    public static Version CurrentVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version
                ?? new Version(0, 0, 0);
        return new Version(Math.Max(v.Major, 0), Math.Max(v.Minor, 0),
            Math.Max(v.Build, 0));
    }

    private static Version ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return new Version(0, 0, 0);
        }
        var parts = tag.TrimStart('v', 'V').Split('.');
        int Major = parts.Length > 0 && int.TryParse(parts[0], out var m) ? m : 0;
        int Minor = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0;
        int Build = parts.Length > 2 && int.TryParse(parts[2], out var b) ? b : 0;
        return new Version(Major, Minor, Build);
    }

    /// <summary>
    /// The newest release's version plus the direct download URL of the
    /// self-contained exe. Returns null when there is no release yet or the
    /// network is unavailable.
    /// </summary>
    public async Task<(Version Latest, string? DownloadUrl)?> CheckAsync()
    {
        try
        {
            using var resp = await Http.GetAsync(LatestApiUrl);
            if (!resp.IsSuccessStatusCode)
            {
                return null; // 404 → no release yet
            }
            var json = await resp.Content.ReadAsStringAsync();
            var info = JsonSerializer.Deserialize<ReleaseInfo>(json);
            if (info is null)
            {
                return null;
            }
            var url = info.Assets.FirstOrDefault(a => string.Equals(
                a.Name, AssetName, StringComparison.OrdinalIgnoreCase))
                ?.BrowserDownloadUrl;
            return (ParseTag(info.TagName), url);
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