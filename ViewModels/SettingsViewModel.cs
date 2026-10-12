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

    /// <summary>0 = system default, 1 = light, 2 = dark (bound to the ComboBox).</summary>
    [ObservableProperty]
    private int _colorSchemeIndex;

    /// <summary>0 = follow Windows, 1 = green, … (bound to the ComboBox).</summary>
    [ObservableProperty]
    private int _accentIndex = 1;

    /// <summary>Journal retention (bound to NumberBox, hence double).</summary>
    [ObservableProperty]
    private double _historyMaxEntries = 2500;

    [ObservableProperty]
    private double _historyMaxBytes = 1_000_000;

    [ObservableProperty]
    private bool _startOnLogin;

    [ObservableProperty]
    private string _configPath = "";

    /// <summary>Version string for the About pane, from the single source.</summary>
    public string VersionDisplay => AppInfo.VersionDisplay;

    /// <summary>The release channel this build belongs to (indev/alpha/…/release).</summary>
    public string Channel => AppInfo.Channel;

    /// <summary>"default" | "light" | "dark", derived from the ComboBox index.</summary>
    public string ColorScheme => ColorSchemeIndex switch
    {
        1 => ThemeService.Light,
        2 => ThemeService.Dark,
        _ => ThemeService.Default,
    };

    /// <summary>Accent key derived from the ComboBox index (see AccentService).</summary>
    public string Accent =>
        AccentIndex >= 0 && AccentIndex < AccentService.OrderedKeys.Count
            ? AccentService.OrderedKeys[AccentIndex]
            : AccentService.DefaultKey;

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
        ColorSchemeIndex = settings.ColorScheme switch
        {
            "light" => 1,
            "dark" => 2,
            _ => 0,
        };
        AccentIndex = AccentIndexFor(settings.Accent);
        HistoryMaxEntries = settings.HistoryMaxEntries;
        HistoryMaxBytes = settings.HistoryMaxBytes;
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
            ColorScheme = ColorScheme,
            Accent = Accent,
            HistoryMaxEntries = (int)Math.Max(1, HistoryMaxEntries),
            HistoryMaxBytes = (long)Math.Max(1000, HistoryMaxBytes),
        });
    }

    private static int AccentIndexFor(string? accent)
    {
        var key = (accent ?? "").Trim().ToLowerInvariant();
        for (var i = 0; i < AccentService.OrderedKeys.Count; i++)
        {
            if (AccentService.OrderedKeys[i] == key)
            {
                return i;
            }
        }
        return 1; // default to Funnel green
    }
}
