namespace GnuSorter.Models;

/// <summary>User preferences. Mirrors the Python settings.json schema.</summary>
public sealed class AppSettings
{
    public double MinStableSeconds { get; set; } = 3;

    public double StabilityInterval { get; set; } = 0.5;

    public double WatchInterval { get; set; } = 2;

    public bool Notifications { get; set; } = true;

    public bool DryRunDefault { get; set; } = false;

    public bool CautiousDownloads { get; set; } = true;

    /// <summary>Look for a newer build in this channel shortly after launch.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>
    /// Appearance: "default" (follow Windows), "light" or "dark".
    /// </summary>
    public string ColorScheme { get; set; } = "default";

    /// <summary>
    /// Brand accent: "system" (follow Windows) or a Funnel palette key
    /// ("green", "lime", "teal", …). Same key as the Python app's settings.json.
    /// </summary>
    public string Accent { get; set; } = "green";

    /// <summary>
    /// Journal retention: once history.log passes <see cref="HistoryMaxBytes"/>,
    /// trim it down to the most recent <see cref="HistoryMaxEntries"/> entries.
    /// </summary>
    public int HistoryMaxEntries { get; set; } = 2500;

    public long HistoryMaxBytes { get; set; } = 1_000_000;

    public AppSettings Clone() => new()
    {
        MinStableSeconds = MinStableSeconds,
        StabilityInterval = StabilityInterval,
        WatchInterval = WatchInterval,
        Notifications = Notifications,
        DryRunDefault = DryRunDefault,
        CautiousDownloads = CautiousDownloads,
        CheckForUpdates = CheckForUpdates,
        ColorScheme = ColorScheme,
        Accent = Accent,
        HistoryMaxEntries = HistoryMaxEntries,
        HistoryMaxBytes = HistoryMaxBytes,
    };
}
