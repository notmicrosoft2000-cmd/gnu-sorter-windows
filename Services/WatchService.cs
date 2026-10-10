using System.Collections.Concurrent;

namespace GnuSorter.Services;

/// <summary>
/// Background folder watcher built on <see cref="FileSystemWatcher"/>. New files
/// are queued and only moved once they have been stable for the configured
/// window, so half-written downloads are never touched.
/// </summary>
public sealed class WatchService : IDisposable
{
    private readonly SortEngine _engine;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly ConcurrentQueue<string> _queue = new();
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private volatile bool _running;

    public WatchService(SortEngine engine)
    {
        _engine = engine;
    }

    public bool IsRunning => _running;

    /// <summary>Raised (on a background thread) after a file is sorted.</summary>
    public event Action<string, string>? Sorted;

    public void Start(IEnumerable<string> roots)
    {
        if (_running)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }
            var watcher = new FileSystemWatcher(root)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
                    | NotifyFilters.Size,
                IncludeSubdirectories = false,
                InternalBufferSize = 64 * 1024,
            };
            watcher.Created += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += (_, _) => { /* buffer overflow → next sweep catches up */ };
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }

        _running = true;
        _worker = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }
        _running = false;
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
        _cts?.Cancel();
        try
        {
            _worker?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            // ignore shutdown races
        }
        _cts?.Dispose();
        _cts = null;
        _worker = null;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.FullPath))
        {
            _queue.Enqueue(e.FullPath);
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.FullPath))
        {
            _queue.Enqueue(e.FullPath);
        }
    }

    private async Task RunAsync(CancellationToken token)
    {
        var settings = _engine.Config.LoadSettings();
        var minStable = settings.MinStableSeconds;

        while (!token.IsCancellationRequested)
        {
            if (!_queue.TryDequeue(out var path))
            {
                try
                {
                    await Task.Delay(250, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                continue;
            }

            try
            {
                await HandleAsync(path, minStable, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // never let one bad file stop the watcher
            }
        }
    }

    private async Task HandleAsync(string path, double minStable, CancellationToken token)
    {
        if (!File.Exists(path) || SortEngine.IsHidden(path)
            || SortEngine.IsSymlink(path) || SortEngine.LooksInProgress(path))
        {
            return;
        }

        var root = RootFor(path);
        if (root is null)
        {
            return;
        }

        // Wait out the stability window (up to a minute).
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline && !token.IsCancellationRequested)
        {
            if (SortEngine.IsStable(path, minStable, interval: 1.0))
            {
                break;
            }
            await Task.Delay(200, token);
        }

        if (!File.Exists(path) || token.IsCancellationRequested)
        {
            return;
        }

        var category = _engine.SortFile(path, root);
        Sorted?.Invoke(path, category);
    }

    private string? RootFor(string filePath)
    {
        foreach (var root in _engine.Config.LoadRoots())
        {
            if (!root.Enabled)
            {
                continue;
            }
            var full = ConfigStore.NormalizeKey(root.Path);
            var parent = Path.GetDirectoryName(ConfigStore.NormalizeKey(filePath));
            if (string.Equals(full, parent, StringComparison.OrdinalIgnoreCase))
            {
                return root.Path;
            }
        }
        return null;
    }

    public void Dispose() => Stop();
}
