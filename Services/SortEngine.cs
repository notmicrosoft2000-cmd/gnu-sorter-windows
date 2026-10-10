using System.Text;
using System.Text.RegularExpressions;
using GnuSorter.Models;

namespace GnuSorter.Services;

/// <summary>
/// The sorting engine: classifies a file against compiled rules, moves it into
/// a category folder inside a watched root, and journals every move so it can be
/// undone. Pure standard-library logic so the GUI and the watcher share it.
/// </summary>
public sealed class SortEngine
{
    private static readonly string[] InProgressSuffixes =
        { ".part", ".crdownload", ".download", ".tmp", ".partial" };

    private readonly ConfigStore _config;
    private readonly object _journalLock = new();

    public SortEngine(ConfigStore config)
    {
        _config = config;
    }

    public ConfigStore Config => _config;

    // -- rule compilation ------------------------------------------------

    public sealed class CompiledRule
    {
        public string Name { get; init; } = "Other";

        public Regex? NameRegex { get; init; }

        /// <summary>Lower-case extensions with a leading dot, longest first.</summary>
        public string[] Extensions { get; init; } = Array.Empty<string>();

        public bool IsNameMatch => NameRegex is not null;
    }

    public static List<CompiledRule> Compile(IEnumerable<SortRule> rules)
    {
        var compiled = new List<CompiledRule>();
        foreach (var rule in rules)
        {
            Regex? regex = null;
            if (!string.IsNullOrWhiteSpace(rule.MatchNameRegex))
            {
                try
                {
                    regex = new Regex(rule.MatchNameRegex, RegexOptions.IgnoreCase
                        | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                }
                catch (ArgumentException)
                {
                    regex = null;
                }
            }

            var extensions = rule.Extensions
                .Select(e => (e ?? "").Trim().TrimStart('.').ToLowerInvariant())
                .Where(e => e.Length > 0)
                .Select(e => "." + e)
                .OrderByDescending(e => e.Length)
                .ToArray();

            compiled.Add(new CompiledRule
            {
                Name = string.IsNullOrEmpty(rule.Name) ? "Other" : rule.Name,
                NameRegex = regex,
                Extensions = extensions,
            });
        }
        return compiled;
    }

    public List<CompiledRule> EffectiveRules(string rootPath) =>
        Compile(_config.RulesFor(rootPath).Rules);

    // -- classification --------------------------------------------------

    /// <summary>
    /// Destination folder for a file. <paramref name="rules"/> null means the
    /// template; an empty list means "no rules" (everything needs review).
    /// </summary>
    public string Categorize(string fileName, IReadOnlyList<CompiledRule>? rules)
    {
        var effective = rules ?? Compile(_config.LoadRules());
        var name = fileName.ToLowerInvariant();
        foreach (var rule in effective)
        {
            if (rule.NameRegex is not null && rule.NameRegex.IsMatch(name))
            {
                return rule.Name;
            }
            foreach (var ext in rule.Extensions)
            {
                if (name.EndsWith(ext, StringComparison.Ordinal))
                {
                    return rule.Name;
                }
            }
        }
        return "Needs-review";
    }

    public static bool LooksInProgress(string fileName)
    {
        var lowered = fileName.ToLowerInvariant();
        return InProgressSuffixes.Any(lowered.EndsWith);
    }

    public static bool IsHidden(string path) =>
        Path.GetFileName(path).StartsWith('.');

    public static bool IsSymlink(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            return attrs.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception)
        {
            return true; // unreadable → treat as something to skip
        }
    }

    /// <summary>True only if the file is old enough and its size held still.</summary>
    public static bool IsStable(string path, double minSeconds, double interval = 0.5)
    {
        long size1;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return false;
            }
            if (minSeconds > 0 &&
                (DateTime.UtcNow - info.LastWriteTimeUtc).TotalSeconds < minSeconds)
            {
                return false;
            }
            size1 = info.Length;
        }
        catch (Exception)
        {
            return false;
        }

        if (interval > 0)
        {
            Thread.Sleep(TimeSpan.FromSeconds(interval));
        }

        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length == size1;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static string UniquePath(string destination)
    {
        if (!File.Exists(destination))
        {
            return destination;
        }
        var dir = Path.GetDirectoryName(destination) ?? "";
        var stem = Path.GetFileNameWithoutExtension(destination);
        var ext = Path.GetExtension(destination);
        for (var n = 1; ; n++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    // -- moving ----------------------------------------------------------

    public string SortFile(string path, string root, bool dryRun = false)
    {
        var category = Categorize(Path.GetFileName(path), EffectiveRules(root));
        var destDir = Path.Combine(root, category);
        var destination = UniquePath(Path.Combine(destDir, Path.GetFileName(path)));

        if (dryRun)
        {
            return category;
        }

        Directory.CreateDirectory(destDir);
        File.Move(path, destination);
        Journal(path, destination);
        return category;
    }

    // -- sweeping --------------------------------------------------------

    public SortStats Sweep(IEnumerable<string> roots, bool dryRun = false)
    {
        var stats = new SortStats();
        var minStable = _config.LoadSettings().MinStableSeconds;

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            string[] entries;
            try
            {
                entries = Directory.GetFiles(root)
                    .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry);
                if (name.StartsWith('.') || IsSymlink(entry) || LooksInProgress(name))
                {
                    stats.Skipped++;
                    continue;
                }
                if (!IsStable(entry, minStable))
                {
                    stats.Skipped++;
                    continue;
                }
                var category = SortFile(entry, root, dryRun);
                stats.Moved++;
                if (category == "Needs-review")
                {
                    stats.NeedsReview++;
                }
            }
        }
        return stats;
    }

    // -- journal / undo --------------------------------------------------

    private void Journal(string source, string destination)
    {
        lock (_journalLock)
        {
            Directory.CreateDirectory(_config.DataDir);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{source}\t{destination}\n";
            File.AppendAllText(_config.HistoryPath, line);
        }
    }

    public List<MoveRecord> History(int? limit = null)
    {
        lock (_journalLock)
        {
            if (!File.Exists(_config.HistoryPath))
            {
                return new List<MoveRecord>();
            }
            var rows = new List<MoveRecord>();
            foreach (var line in File.ReadAllLines(_config.HistoryPath))
            {
                var parts = line.Split('\t');
                if (parts.Length < 3)
                {
                    continue;
                }
                rows.Add(new MoveRecord
                {
                    Timestamp = parts[0],
                    Source = parts[1],
                    Destination = string.Join("\t", parts[2..]),
                });
            }
            if (limit is int max && max > 0 && rows.Count > max)
            {
                return rows.Skip(rows.Count - max).ToList();
            }
            return rows;
        }
    }

    public int Undo(bool dryRun = false, int? limit = null)
    {
        lock (_journalLock)
        {
            if (!File.Exists(_config.HistoryPath))
            {
                return 0;
            }

            var all = History();
            if (all.Count == 0)
            {
                return 0;
            }

            var count = limit is > 0 ? Math.Min(limit.Value, all.Count) : all.Count;
            var toUndo = all.Skip(all.Count - count).ToList();
            var rest = all.Take(all.Count - count).ToList();
            var restored = 0;

            for (var i = toUndo.Count - 1; i >= 0; i--)
            {
                var row = toUndo[i];
                if (File.Exists(row.Destination) && !File.Exists(row.Source))
                {
                    if (!dryRun)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(row.Source) ?? ".");
                        File.Move(row.Destination, row.Source, overwrite: false);
                    }
                    restored++;
                }
                else
                {
                    rest.Add(row); // not reversible → keep in the journal
                }
            }

            if (!dryRun)
            {
                var ordered = rest.OrderBy(r => r.Timestamp, StringComparer.Ordinal);
                var sb = new StringBuilder();
                foreach (var row in ordered)
                {
                    sb.Append(row.Timestamp).Append('\t')
                      .Append(row.Source).Append('\t')
                      .Append(row.Destination).Append('\n');
                }
                Directory.CreateDirectory(_config.DataDir);
                File.WriteAllText(_config.HistoryPath, sb.ToString());
            }
            return restored;
        }
    }
}
