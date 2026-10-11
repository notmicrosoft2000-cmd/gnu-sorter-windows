namespace GnuSorter.Models;

/// <summary>User preferences. Mirrors the Python settings.json schema.</summary>
public sealed class AppSettings
{
    public double MinStableSeconds { get; set; } = 3;

    public double StabilityInterval { get; set; } = 0.5;

    public double WatchInterval { get; set; } = 2;

    public bool Notifications { get; set; } = true;

    public bool DryRunDefault { get; set; } = false;

    public AppSettings Clone() => new()
    {
        MinStableSeconds = MinStableSeconds,
        StabilityInterval = StabilityInterval,
        WatchInterval = WatchInterval,
        Notifications = Notifications,
        DryRunDefault = DryRunDefault,
    };
}
