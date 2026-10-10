namespace GnuSorter.Services;

/// <summary>
/// Tiny composition root shared by the app and every page. It keeps a single
/// <see cref="ConfigStore"/>, <see cref="SortEngine"/> and <see cref="WatchService"/>
/// alive for the whole process.
/// </summary>
public sealed class AppServices
{
    public AppServices()
    {
        Config = new ConfigStore();
        Config.EnsureDefaults();
        Engine = new SortEngine(Config);
        Watcher = new WatchService(Engine);
    }

    public ConfigStore Config { get; }

    public SortEngine Engine { get; }

    public WatchService Watcher { get; }

    public List<string> EnabledRoots() =>
        Config.LoadRoots().Where(r => r.Enabled).Select(r => r.Path).ToList();
}
