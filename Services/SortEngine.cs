using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    // Characters Windows forbids in a file/folder name.
    private static readonly char[] WindowsInvalidChars =
        { '<', '>', ':', '"', '|', '?', '*' };

    // Windows device names that cannot be used as (or as the stem of) a folder.
    private static readonly HashSet<string> WindowsReservedNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "con", "prn", "aux", "nul",
            "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
            "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
        };

    private static readonly JsonSerializerOptions JournalJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private sealed class JournalEntry
    {
        [JsonPropertyName("ts")]
        public string Ts { get; set; } = "";

        [JsonPropertyName("src")]
        public string Src { get; set; } = "";

        [JsonPropertyName("dest")]
        public string Dest { get; set; } = "";
    }

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

    /// <summary>
    /// Sanitise a category into a relative destination name, or return null.
    /// Mirrors the Python <c>safe_category_name</c>: rejects rooted paths,
    /// drive/UNC prefixes, NUL bytes, empty/“.”/“..” components, Windows
    /// invalid characters, trailing dots/spaces (which Windows silently
    /// strips) and the reserved device names (CON, NUL, COM1, …).
    /// </summary>
    public static string? SafeCategoryName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        name = name.Trim();
        if (name.Length == 0 || name.Contains('\0'))
        {
            return null;
        }
        try
        {
            if (Path.IsPathRooted(name) || Path.GetPathRoot(name) is { Length: > 0 })
            {
                return null;
            }
        }
        catch (ArgumentException)
        {
            return null;
        }
        foreach (var component in name.Replace('\\', '/').Split('/'))
        {
            if (component.Length == 0 || component == "." || component == "..")
            {
                return null;
            }
            if (component != component.TrimEnd(' ', '.'))
            {
                return null;
            }
            if (component.IndexOfAny(WindowsInvalidChars) >= 0)
            {
                return null;
            }
            var stem = component.Split('.')[0];
            if (WindowsReservedNames.Contains(stem))
            {
                return null;
            }
        }
        return name;
    }

    /// <summary>True if <paramref name="candidateDir"/> resolves inside <paramref name="root"/>.</summary>
    private static bool IsInsideRoot(string root, string candidateDir)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullCandidate = Path.GetFullPath(candidateDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    public string SortFile(string path, string root, bool dryRun = false)
    {
        var category = Categorize(Path.GetFileName(path), EffectiveRules(root));
        var safe = SafeCategoryName(category) ?? "Needs-review";
        if (safe != category)
        {
            Console.Error.WriteLine($"!! unsafe destination '{category}', using '{safe}'");
        }
        var destDir = Path.Combine(root, safe);
        if (!IsInsideRoot(root, destDir))
        {
            safe = "Needs-review";
            destDir = Path.Combine(root, safe);
        }
        var destination = UniquePath(Path.Combine(destDir, Path.GetFileName(path)));

        if (dryRun)
        {
            return safe;
        }

        Directory.CreateDirectory(destDir);
        File.Move(path, destination);
        Journal(path, destination);
        return safe;
    }

    // -- sweeping --------------------------------------------------------

    public SortStats Sweep(IEnumerable<string> roots, bool dryRun = false)
    {
        var stats = new SortStats();
        var settings = _config.LoadSettings();
        var minStable = settings.MinStableSeconds;
        var interval = settings.StabilityInterval;

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

            // Pass 1: collect eligible files, remembering their sizes, so the
            // (slow) settle sleep only happens once per batch instead of once
            // per file.
            var candidates = new List<(string Path, long Size)>();
            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry);
                if (name.StartsWith('.') || IsSymlink(entry) || LooksInProgress(name))
                {
                    stats.Skipped++;
                    continue;
                }
                try
                {
                    var info = new FileInfo(entry);
                    if (!info.Exists ||
                        (minStable > 0 &&
                         (DateTime.UtcNow - info.LastWriteTimeUtc).TotalSeconds < minStable))
                    {
                        stats.Skipped++;
                        continue;
                    }
                    candidates.Add((entry, info.Length));
                }
                catch (Exception)
                {
                    stats.Skipped++;
                }
            }

            if (candidates.Count == 0)
            {
                continue;
            }

            if (!dryRun && interval > 0)
            {
                Thread.Sleep(TimeSpan.FromSeconds(interval));
            }

            // Pass 2: re-check each candidate's size, then move it. One
            // un-sortable file (locked, permissions, …) must not abort the
            // rest of the sweep.
            foreach (var (entry, size1) in candidates)
            {
                if (!File.Exists(entry))
                {
                    stats.Skipped++;
                    continue;
                }
                if (!dryRun)
                {
                    try
                    {
                        if (new FileInfo(entry).Length != size1)
                        {
                            stats.Skipped++;
                            continue;
                        }
                    }
                    catch (Exception)
                    {
                        stats.Skipped++;
                        continue;
                    }
                }
                try
                {
                    var category = SortFile(entry, root, dryRun);
                    stats.Moved++;
                    if (category == "Needs-review")
                    {
                        stats.NeedsReview++;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"!! could not sort {entry}: {ex.Message}");
                    stats.Failed++;
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
            var entry = new JournalEntry
            {
                Ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Src = source,
                Dest = destination,
            };
            var line = JsonSerializer.Serialize(entry, JournalJson) + "\n";
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
            foreach (var raw in File.ReadAllLines(_config.HistoryPath))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }
                if (line.StartsWith('{'))               // current JSON format
                {
                    try
                    {
                        var entry = JsonSerializer.Deserialize<JournalEntry>(line);
                        if (entry is not null)
                        {
                            rows.Add(new MoveRecord
                            {
                                Timestamp = entry.Ts,
                                Source = entry.Src,
                                Destination = entry.Dest,
                            });
                        }
                    }
                    catch (JsonException)
                    {
                        // ignore malformed lines
                    }
                    continue;
                }
                var parts = line.Split('\t');           // legacy tab format
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
                    sb.Append(JsonSerializer.Serialize(
                        new JournalEntry { Ts = row.Timestamp, Src = row.Source, Dest = row.Destination },
                        JournalJson)).Append('\n');
                }
                Directory.CreateDirectory(_config.DataDir);
                File.WriteAllText(_config.HistoryPath, sb.ToString());
            }
            return restored;
        }
    }
}
