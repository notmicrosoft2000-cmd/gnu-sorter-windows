using CommunityToolkit.Mvvm.ComponentModel;
using GnuSorter.Models;
using GnuSorter.Services;

namespace GnuSorter.ViewModels;

/// <summary>Editable view of <see cref="AppSettings"/>, loaded from and saved to disk.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;

    [ObservableProperty]
    private double _minStableSeconds = 3;

    [ObservableProperty]
    private double _stabilityInterval = 0.5;

    [ObservableProperty]
    private double _watchInterval = 2;

    [ObservableProperty]
    private bool _notifications = true;

    [ObservableProperty]
    private bool _dryRunDefault;

    [ObservableProperty]
    private bool _cautiousDownloads = true;

    [ObservableProperty]
    private bool _checkForUpdates = true;

    [ObservableProperty]
    private bool _startOnLogin;

    [ObservableProperty]
    private string _configPath = "";

    /// <summary>Version string for the About pane, from the single source.</summary>
    public string VersionDisplay => AppInfo.VersionDisplay;

    /// <summary>The release channel this build belongs to (indev/alpha/…/release).</summary>
    public string Channel => AppInfo.Channel;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        Reload();
    }

    public void Reload()
    {
        var settings = _services.Config.LoadSettings();
        MinStableSeconds = settings.MinStableSeconds;
        StabilityInterval = settings.StabilityInterval;
        WatchInterval = settings.WatchInterval;
        Notifications = settings.Notifications;
        DryRunDefault = settings.DryRunDefault;
        CautiousDownloads = settings.CautiousDownloads;
        CheckForUpdates = settings.CheckForUpdates;
        StartOnLogin = StartupService.IsEnabled();
        ConfigPath = _services.Config.ConfigDir;
    }

    public void Save()
    {
        _services.Config.SaveSettings(new AppSettings
        {
            MinStableSeconds = MinStableSeconds,
            StabilityInterval = StabilityInterval,
            WatchInterval = WatchInterval,
            Notifications = Notifications,
            DryRunDefault = DryRunDefault,
            CautiousDownloads = CautiousDownloads,
            CheckForUpdates = CheckForUpdates,
        });
    }
}
