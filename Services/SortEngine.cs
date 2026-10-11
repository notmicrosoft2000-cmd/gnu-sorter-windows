using System.Globalization;
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
    {
        ".part", ".crdownload", ".download", ".opdownload", ".partial",
        ".tmp", ".filepart", ".ytdl", ".!qb", ".un~",
    };

    // Extra-cautious settling: files this large get a longer settle window.
    private const double LargeFileMb = 100.0;    // MB beyond which size scales the window
    private const double ExtraPer100Mb = 4.0;    // +4 s per additional 100 MB
    private const double MaxExtraSettle = 60.0;  // ... capped at one extra minute
    private const double MtimeTolerance = 0.25;  // s — a rewrite bumps mtime

    // Journal v2 (mirrors the Python engine so the two apps share one format).
    private const int JournalVersion = 2;
    private const long RotateBytes = 1_000_000;   // trim the journal once it grows past ~1 MB
    private const int RotateKeep = 2500;          // ...keeping the most recent entries
    private const double MtimeMatchTolerance = 2.0; // s — coarse filesystems vs. journal mtime

    /// <summary>
    /// How long a file of <paramref name="sizeBytes"/> must keep its size and
    /// mtime still before it is trusted. A 3 GB download settles for
    /// <c>baseSeconds + 60</c>, a 200 MB one for <c>+4</c>; anything under
    /// 100 MB just uses the base window unchanged.
    /// </summary>
    public static double SettleWindowSeconds(long sizeBytes, double baseSeconds)
    {
        if (baseSeconds <= 0)
        {
            return 0;
        }
        var mb = sizeBytes / (1024.0 * 1024.0);
        if (mb <= LargeFileMb)
        {
            return baseSeconds;
        }
        var extra = (mb - LargeFileMb) / 100.0 * ExtraPer100Mb;
        return baseSeconds + Math.Min(extra, MaxExtraSettle);
    }

    /// <summary>
    /// Unix-epoch seconds for a UTC time — the unit the cross-platform journal
    /// stores mtimes in (matching Python's <c>os.stat().st_mtime</c>).
    /// </summary>
    private static double ToUnixSeconds(DateTime utc) =>
        (utc - DateTime.UnixEpoch).TotalSeconds;

    /// <summary>
    /// Windows-only signal: true when another process currently holds the file
    /// open for writing (a sharing violation on our attempt to open it for
    /// write). Downloaders open .part/.crdownload files without granting write
    /// sharing, so this catches in-progress downloads no suffix or size probe
    /// would see. Any other failure (read-only file, permissions) is treated as
    /// "not busy" so it never blocks sorting.
    /// </summary>
    public static bool IsFileBusy(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Write,
                FileShare.ReadWrite);
            return false;
        }
        catch (IOException)
        {
            return true; // sharing violation → another process is writing it
        }
        catch (UnauthorizedAccessException)
        {
            return false; // read-only / ACL denied → not an in-progress write
        }
        catch (Exception)
        {
            return false; // lost the file race → let the stability probes decide
        }
    }

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
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Selector parsing: "30m", "2h", "7d" for durations, and
    // "YYYY-MM-DD" / "YYYY-MM-DD HH:MM:SS" for absolute cut-offs.
    private static readonly Regex DurationRegex = new(
        @"^(\d+)\s*([smhdw])$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] TimestampFormats =
        { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd" };

    private sealed class JournalEntry
    {
        [JsonPropertyName("v")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("batch")]
        public string? Batch { get; set; }

        [JsonPropertyName("ts")]
        public string Ts { get; set; } = "";

        [JsonPropertyName("src")]
        public string Src { get; set; } = "";

        [JsonPropertyName("dest")]
        public string Dest { get; set; } = "";

        [JsonPropertyName("size")]
        public long? Size { get; set; }

        [JsonPropertyName("mtime")]
        public double? Mtime { get; set; }

        [JsonPropertyName("rule")]
        public string? Rule { get; set; }

        [JsonPropertyName("root")]
        public string? Root { get; set; }
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

        /// <summary>False means the rule is skipped entirely.</summary>
        public bool Enabled { get; init; } = true;

        /// <summary>Catch-all: matches every file.</summary>
        public bool MatchAll { get; init; }

        /// <summary>When true every specified matcher must hit (AND mode).</summary>
        public bool MatchModeAll { get; init; }

        public Regex? ExcludeRegex { get; init; }

        public double? MinMb { get; init; }

        public double? MaxMb { get; init; }

        public double? Days { get; init; }

        public bool IsNameMatch => NameRegex is not null;
    }

    public static List<CompiledRule> Compile(IEnumerable<SortRule> rules)
    {
        var compiled = new List<CompiledRule>();
        foreach (var rule in rules)
        {
            var extensions = rule.Extensions
                .Select(e => (e ?? "").Trim().TrimStart('.').ToLowerInvariant())
                .Where(e => e.Length > 0)
                .Select(e => "." + e)
                .OrderByDescending(e => e.Length)
                .ToArray();

            compiled.Add(new CompiledRule
            {
                Name = string.IsNullOrEmpty(rule.Name) ? "Other" : rule.Name,
                NameRegex = TryCompile(rule.MatchNameRegex),
                Extensions = extensions,
                Enabled = rule.Enabled is not false,
                MatchAll = rule.MatchAll,
                MatchModeAll = string.Equals(rule.MatchMode, "all",
                    StringComparison.OrdinalIgnoreCase),
                ExcludeRegex = TryCompile(rule.ExcludeNameRegex),
                MinMb = PositiveOrNull(rule.MinSizeMb),
                MaxMb = PositiveOrNull(rule.MaxSizeMb),
                Days = PositiveOrNull(rule.ModifiedWithinDays),
            });
        }
        return compiled;
    }

    private static Regex? TryCompile(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }
        try
        {
            return new Regex(pattern, RegexOptions.IgnoreCase
                | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static double? PositiveOrNull(double? value) =>
        value is double v && v > 0 ? v : null;

    public List<CompiledRule> EffectiveRules(string rootPath) =>
        Compile(_config.RulesFor(rootPath).Rules);

    // -- classification --------------------------------------------------

    /// <summary>
    /// Destination folder for a file. <paramref name="rules"/> null means the
    /// template; an empty list means "no rules" (everything needs review).
    /// <paramref name="info"/> (when given) powers the optional size/recency
    /// bounds; rules that need it are skipped when it is not available.
    /// </summary>
    public string Categorize(string fileName, IReadOnlyList<CompiledRule>? rules,
                             FileInfo? info = null)
    {
        var effective = rules ?? Compile(_config.LoadRules());
        var name = fileName.ToLowerInvariant();
        double? sizeMb = null;
        double? ageDays = null;
        if (info is not null)
        {
            try
            {
                if (info.Exists)
                {
                    sizeMb = info.Length / (1024.0 * 1024.0);
                    ageDays = (DateTime.UtcNow - info.LastWriteTimeUtc).TotalDays;
                }
            }
            catch (Exception)
            {
                // Metadata is a nice-to-have; a rule that needs it is skipped.
            }
        }

        foreach (var rule in effective)
        {
            if (!rule.Enabled)
            {
                continue;
            }

            var extHit = false;
            foreach (var ext in rule.Extensions)
            {
                if (name.EndsWith(ext, StringComparison.Ordinal))
                {
                    extHit = true;
                    break;
                }
            }
            var reHit = rule.NameRegex is not null && rule.NameRegex.IsMatch(name);
            var hasExt = rule.Extensions.Length > 0;
            var hasRe = rule.NameRegex is not null;
            bool matched;
            if (rule.MatchModeAll)
            {
                // "All": every specified matcher must hit (the catch-all flag
                // counts as always true). A rule with no matchers never matches.
                matched = hasExt || hasRe || rule.MatchAll;
                if (hasExt && !extHit)
                {
                    matched = false;
                }
                if (hasRe && !reHit)
                {
                    matched = false;
                }
            }
            else
            {
                matched = rule.MatchAll || extHit || reHit;
            }
            if (!matched)
            {
                continue;
            }

            if (rule.ExcludeRegex is not null && rule.ExcludeRegex.IsMatch(name))
            {
                continue;
            }
            if (rule.MinMb is not null || rule.MaxMb is not null)
            {
                if (sizeMb is null)
                {
                    continue;
                }
                if (rule.MinMb is double lo && sizeMb < lo)
                {
                    continue;
                }
                if (rule.MaxMb is double hi && sizeMb > hi)
                {
                    continue;
                }
            }
            if (rule.Days is double days && (ageDays is null || ageDays > days))
            {
                continue;
            }
            return rule.Name;
        }
        return "Needs-review";
    }

    /// <summary>
    /// Dry-run preview: map each file name to the destination it would sort
    /// into. Used by the rules-page tester and the wizard; never touches disk.
    /// </summary>
    public Dictionary<string, string> Classify(
        IEnumerable<string> names, IReadOnlyList<CompiledRule>? rules = null)
    {
        var effective = rules ?? Compile(_config.LoadRules());
        var result = new Dictionary<string, string>();
        foreach (var name in names)
        {
            result[name] = Categorize(name, effective);
        }
        return result;
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

    /// <summary>
    /// True only if the file is old enough and both its size AND mtime held
    /// still across <paramref name="minProbes"/> probes spaced
    /// <paramref name="interval"/> apart. A download resuming (size grows) or
    /// a file being rewritten in place (mtime bumps) resets the check. Large
    /// files get a proportionally longer settle window first
    /// (<see cref="SettleWindowSeconds"/>).
    /// </summary>
    public static bool IsStable(string path, double minSeconds, double interval = 0.5,
        int minProbes = 3)
    {
        long size1;
        DateTime mtime1;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return false;
            }
            if (minSeconds > 0)
            {
                var window = SettleWindowSeconds(info.Length, minSeconds);
                if ((DateTime.UtcNow - info.LastWriteTimeUtc).TotalSeconds < window)
                {
                    return false;
                }
            }
            size1 = info.Length;
            mtime1 = info.LastWriteTimeUtc;
        }
        catch (Exception)
        {
            return false;
        }

        for (var probe = 0; probe < Math.Max(minProbes - 1, 0); probe++)
        {
            if (interval > 0)
            {
                Thread.Sleep(TimeSpan.FromSeconds(interval));
            }
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    return false;
                }
                if (info.Length != size1
                    || Math.Abs((info.LastWriteTimeUtc - mtime1).TotalSeconds) > MtimeTolerance)
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
        return true;
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

    public string SortFile(string path, string root, bool dryRun = false, string? batch = null)
    {
        var info = new FileInfo(path);
        var category = Categorize(Path.GetFileName(path), EffectiveRules(root), info);
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

        // Capture the source metadata before the move so restore can verify the
        // file came back unchanged (and detect an already-restored original).
        long? size = null;
        double? mtime = null;
        try
        {
            if (info.Exists)
            {
                size = info.Length;
                mtime = ToUnixSeconds(info.LastWriteTimeUtc);
            }
        }
        catch (Exception)
        {
            // Metadata is a nice-to-have; never block the move over it.
        }

        Directory.CreateDirectory(destDir);
        File.Move(path, destination);
        Journal(path, destination, safe, root,
            batch ?? Guid.NewGuid().ToString("N"), size, mtime);
        return safe;
    }

    // -- sweeping --------------------------------------------------------

    public SortStats Sweep(IEnumerable<string> roots, bool dryRun = false)
    {
        var stats = new SortStats();
        var settings = _config.LoadSettings();
        var minStable = settings.MinStableSeconds;
        var interval = settings.StabilityInterval;
        var cautious = settings.CautiousDownloads;

        // One operation id for every move this sweep makes, so undo can treat
        // the whole sweep as a single, reversible unit.
        var batch = Guid.NewGuid().ToString("N");
        stats.BatchId = batch;

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

            // Pass 1: collect eligible files, remembering their sizes and mtimes, so the
            // (slow) settle sleep only happens once per batch instead of once
            // per file. Anything clearly mid-download (progress suffix) or
            // still inside its settle window is "held" — parked, reported,
            // never sorted.
            var held = new List<string>();
            var candidates = new List<(string Path, long Size, DateTime Mtime)>();
            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry);
                if (name.StartsWith('.'))
                {
                    stats.Skipped++;
                    continue;
                }
                if (IsSymlink(entry))
                {
                    stats.Skipped++;
                    continue;
                }
                if (LooksInProgress(name))
                {
                    held.Add(name);
                    continue;
                }
                try
                {
                    var info = new FileInfo(entry);
                    if (!info.Exists)
                    {
                        stats.Skipped++;
                        continue;
                    }
                    if (minStable > 0)
                    {
                        var window = SettleWindowSeconds(info.Length, minStable);
                        if ((DateTime.UtcNow - info.LastWriteTimeUtc).TotalSeconds < window)
                        {
                            held.Add(name);
                            continue;
                        }
                    }
                    candidates.Add((entry, info.Length, info.LastWriteTimeUtc));
                }
                catch (Exception)
                {
                    stats.Skipped++;
                }
            }

            if (candidates.Count == 0)
            {
                stats.Held += held.Count;
                stats.HeldFiles.AddRange(held);
                continue;
            }

            // Confirmation probes: wait once, twice in extra-cautious mode,
            // and only keep moving files whose size — and, when cautious,
            // mtime too — held still between probes. In cautious mode a still
            // open write handle (a downloader mid-write) also puts the file
            // back on the holding list — a signal Linux cannot see.
            var qualifying = candidates;
            var probes = cautious && !dryRun ? 2 : (!dryRun ? 1 : 0);
            for (var probe = 0; probe < probes; probe++)
            {
                if (interval > 0)
                {
                    Thread.Sleep(TimeSpan.FromSeconds(interval));
                }
                var kept = new List<(string Path, long Size, DateTime Mtime)>();
                foreach (var (entry, size1, mtime1) in qualifying)
                {
                    if (!File.Exists(entry))
                    {
                        stats.Skipped++;
                        continue;
                    }
                    try
                    {
                        var info = new FileInfo(entry);
                        if (info.Length != size1
                            || Math.Abs((info.LastWriteTimeUtc - mtime1).TotalSeconds) > MtimeTolerance)
                        {
                            held.Add(Path.GetFileName(entry));
                            continue;
                        }
                        if (cautious && IsFileBusy(entry))
                        {
                            held.Add(Path.GetFileName(entry));
                            continue;
                        }
                        kept.Add((entry, size1, mtime1));
                    }
                    catch (Exception)
                    {
                        stats.Skipped++;
                    }
                }
                qualifying = kept;
            }

            stats.Held += held.Count;
            stats.HeldFiles.AddRange(held);

            foreach (var (entry, _, _) in qualifying)
            {
                try
                {
                    var category = SortFile(entry, root, dryRun, batch);
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

    // -- journal / undo (v2) ---------------------------------------------

    private void Journal(string source, string destination, string rule, string root,
        string batch, long? size, double? mtime)
    {
        lock (_journalLock)
        {
            Directory.CreateDirectory(_config.DataDir);
            var entry = new JournalEntry
            {
                Version = JournalVersion,
                Id = Guid.NewGuid().ToString("N"),
                Batch = batch,
                Ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Src = source,
                Dest = destination,
                Size = size,
                Mtime = mtime,
                Rule = rule,
                Root = root,
            };
            File.AppendAllText(_config.HistoryPath, Serialize(entry) + "\n");
            try
            {
                if (new FileInfo(_config.HistoryPath).Length > RotateBytes)
                {
                    RotateJournal();
                }
            }
            catch (IOException)
            {
                // A stat race must never fail a completed move.
            }
        }
    }

    private static string Serialize(JournalEntry entry) =>
        JsonSerializer.Serialize(entry, JournalJson);

    /// <summary>Parse one journal line into an entry (any schema version), else null.</summary>
    private static JournalEntry? ParseEntry(string raw)
    {
        var line = raw.Trim();
        if (line.Length == 0)
        {
            return null;
        }
        if (line.StartsWith('{'))                       // v1 / v2 JSON
        {
            try
            {
                var entry = JsonSerializer.Deserialize<JournalEntry>(line, JournalJson);
                if (entry is null || string.IsNullOrEmpty(entry.Src)
                    || string.IsNullOrEmpty(entry.Dest))
                {
                    return null;
                }
                return entry;
            }
            catch (JsonException)
            {
                return null;
            }
        }
        var parts = line.Split('\t');                   // legacy tab-separated (v0)
        if (parts.Length < 3)
        {
            return null;
        }
        return new JournalEntry
        {
            Version = 0,
            Ts = parts[0],
            Src = parts[1],
            Dest = string.Join("\t", parts[2..]),
        };
    }

    /// <summary>Parsed entries in file order; malformed lines are dropped.</summary>
    private List<JournalEntry> ReadJournal()
    {
        var entries = new List<JournalEntry>();
        if (!File.Exists(_config.HistoryPath))
        {
            return entries;
        }
        foreach (var raw in File.ReadAllLines(_config.HistoryPath))
        {
            var entry = ParseEntry(raw);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }
        return entries;
    }

    private void WriteJournal(IEnumerable<JournalEntry> entries)
    {
        Directory.CreateDirectory(_config.DataDir);
        var sb = new StringBuilder();
        foreach (var entry in entries)
        {
            sb.Append(Serialize(entry)).Append('\n');
        }
        File.WriteAllText(_config.HistoryPath, sb.ToString());
    }

    /// <summary>Trim the journal to the most recent entries once it grows too large.</summary>
    private void RotateJournal()
    {
        var loaded = ReadJournal();
        var kept = loaded.Count > RotateKeep
            ? loaded.Skip(loaded.Count - RotateKeep).ToList()
            : loaded;
        WriteJournal(kept);
    }

    /// <summary>Drop malformed lines; returns how many lines were removed.</summary>
    public int RepairJournal()
    {
        lock (_journalLock)
        {
            if (!File.Exists(_config.HistoryPath))
            {
                return 0;
            }
            var kept = new List<JournalEntry>();
            var dropped = 0;
            foreach (var line in File.ReadAllLines(_config.HistoryPath))
            {
                var entry = ParseEntry(line);
                if (entry is null)
                {
                    dropped++;
                }
                else
                {
                    kept.Add(entry);
                }
            }
            if (dropped > 0)
            {
                WriteJournal(kept);
            }
            return dropped;
        }
    }

    public List<MoveRecord> History(int? limit = null)
    {
        lock (_journalLock)
        {
            var rows = ReadJournal().Select(ToRecord).ToList();
            if (limit is int max && max > 0 && rows.Count > max)
            {
                return rows.Skip(rows.Count - max).ToList();
            }
            return rows;
        }
    }

    /// <summary>
    /// Journal entries (chronological) matching the selectors: last <paramref name="limit"/>,
    /// a <paramref name="since"/><paramref name="before"/> time window (<c>30m</c>, <c>2h</c>,
    /// <c>7d</c> or <c>YYYY-MM-DD[ HH:MM:SS]</c>), a destination <paramref name="folder"/>,
    /// a <paramref name="rule"/> name, or an operation <paramref name="batch"/> id.
    /// </summary>
    public List<MoveRecord> Records(int? limit = null, string? since = null,
        string? before = null, string? folder = null, string? rule = null,
        string? batch = null)
    {
        lock (_journalLock)
        {
            return Select(ReadJournal(), limit, since, before, folder, rule, batch)
                .Select(ToRecord).ToList();
        }
    }

    /// <summary>
    /// How many live journal entries each rule name has sorted. One pass over
    /// the journal; powers the "N sorted" badge on the rules page.
    /// </summary>
    public Dictionary<string, int> RuleCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        lock (_journalLock)
        {
            foreach (var entry in ReadJournal())
            {
                var rule = entry.Rule;
                if (string.IsNullOrEmpty(rule))
                {
                    continue;
                }
                counts[rule] = counts.TryGetValue(rule, out var n) ? n + 1 : 1;
            }
        }
        return counts;
    }

    private static MoveRecord ToRecord(JournalEntry entry) => new()
    {
        Id = entry.Id ?? "",
        Version = entry.Version,
        Batch = entry.Batch ?? "",
        Timestamp = entry.Ts,
        Source = entry.Src,
        Destination = entry.Dest,
        Size = entry.Size,
        Mtime = entry.Mtime,
        Rule = entry.Rule ?? "",
        Root = entry.Root ?? "",
    };

    private static List<JournalEntry> Select(List<JournalEntry> loaded, int? limit,
        string? since, string? before, string? folder, string? rule, string? batch)
    {
        if (limit is <= 0)                             // explicit "none"
        {
            return new List<JournalEntry>();
        }
        var rows = loaded;
        if (!string.IsNullOrWhiteSpace(since))
        {
            var cutoff = DateTime.Now - ParseDuration(since);
            rows = rows.Where(x => TryTimestamp(x.Ts, out var dt) && dt >= cutoff).ToList();
        }
        if (!string.IsNullOrWhiteSpace(before))
        {
            var cutoff = ParseBefore(before);
            rows = rows.Where(x => TryTimestamp(x.Ts, out var dt) && dt < cutoff).ToList();
        }
        if (!string.IsNullOrWhiteSpace(folder))
        {
            var wanted = ConfigStore.NormalizeKey(folder);
            rows = rows.Where(x => string.Equals(
                    ConfigStore.NormalizeKey(Path.GetDirectoryName(x.Dest) ?? ""),
                    wanted, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        if (!string.IsNullOrWhiteSpace(rule))
        {
            var wanted = rule.ToLowerInvariant();
            rows = rows.Where(x => (x.Rule ?? "").ToLowerInvariant() == wanted).ToList();
        }
        if (!string.IsNullOrWhiteSpace(batch))
        {
            var wanted = batch.ToLowerInvariant();
            rows = rows.Where(x => (x.Batch ?? "").ToLowerInvariant() == wanted).ToList();
        }
        if (limit is int n && n > 0 && rows.Count > n)
        {
            rows = rows.Skip(rows.Count - n).ToList();
        }
        return rows;
    }

    public int Undo(bool dryRun = false, int? limit = null, string? since = null,
        string? before = null, string? folder = null, string? rule = null,
        string? batch = null)
    {
        lock (_journalLock)
        {
            var loaded = ReadJournal();
            if (loaded.Count == 0)
            {
                return 0;
            }
            var toUndo = Select(loaded, limit, since, before, folder, rule, batch);
            if (toUndo.Count == 0)
            {
                return 0;
            }
            var selected = new HashSet<JournalEntry>(toUndo, ReferenceEqualityComparer.Instance);
            var rest = loaded.Where(x => !selected.Contains(x)).ToList();
            var restored = 0;

            // Reverse order: the most recent move is put back first.
            for (var i = toUndo.Count - 1; i >= 0; i--)
            {
                var entry = toUndo[i];
                if (!File.Exists(entry.Dest))
                {
                    rest.Add(entry);                    // no longer there → keep it
                    continue;
                }
                if (entry.Size is long expected)
                {
                    try
                    {
                        if (new FileInfo(entry.Dest).Length != expected)
                        {
                            Console.Error.WriteLine(
                                $"* {entry.Dest} changed size since sorting; restoring anyway");
                        }
                    }
                    catch (Exception)
                    {
                        // stat race → proceed with the restore
                    }
                }
                if (File.Exists(entry.Src))
                {
                    if (IsSameFile(entry, entry.Src))
                    {
                        continue;                       // already back → drop it
                    }
                    var target = UniquePath(entry.Src); // collision-safe restore
                    if (!dryRun)
                    {
                        try
                        {
                            EnsureParent(target);
                            File.Move(entry.Dest, target);
                        }
                        catch (Exception)
                        {
                            rest.Add(entry);
                            continue;
                        }
                    }
                    restored++;
                    continue;
                }
                if (dryRun)
                {
                    restored++;
                    continue;
                }
                try
                {
                    EnsureParent(entry.Src);
                    File.Move(entry.Dest, entry.Src);
                }
                catch (Exception)
                {
                    rest.Add(entry);
                    continue;
                }
                restored++;
            }

            if (!dryRun)
            {
                WriteJournal(rest);
            }
            return restored;
        }
    }

    /// <summary>
    /// Reverse the most recent operation — a whole sweep, or a single watched
    /// move. This is the entry point a toast notification's "Undo" button calls.
    /// Returns how many files were restored (0 when there is nothing to undo).
    /// </summary>
    public int UndoLastBatch(bool dryRun = false)
    {
        lock (_journalLock)
        {
            var last = ReadJournal().LastOrDefault();
            if (last is null)
            {
                return 0;
            }
            // A pre-v2 single move has no batch id → just reverse that entry.
            return string.IsNullOrEmpty(last.Batch)
                ? Undo(dryRun, limit: 1)
                : Undo(dryRun, batch: last.Batch);
        }
    }

    /// <summary>
    /// True when <paramref name="path"/> looks like the very file the journal
    /// recorded (size and mtime both match) — i.e. it is already back home.
    /// </summary>
    private static bool IsSameFile(JournalEntry entry, string path)
    {
        if (entry.Size is null || entry.Mtime is null)
        {
            return false;
        }
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return false;
            }
            return info.Length == entry.Size.Value
                && Math.Abs(ToUnixSeconds(info.LastWriteTimeUtc) - entry.Mtime.Value)
                    <= MtimeMatchTolerance;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void EnsureParent(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static TimeSpan ParseDuration(string text)
    {
        var match = DurationRegex.Match(text.Trim());
        if (!match.Success)
        {
            throw new FormatException($"bad duration '{text}' (use e.g. 30m, 2h, 7d)");
        }
        var n = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        return match.Groups[2].Value.ToLowerInvariant() switch
        {
            "s" => TimeSpan.FromSeconds(n),
            "m" => TimeSpan.FromMinutes(n),
            "h" => TimeSpan.FromHours(n),
            "d" => TimeSpan.FromDays(n),
            "w" => TimeSpan.FromDays(n * 7),
            _ => throw new FormatException($"bad duration '{text}' (use e.g. 30m, 2h, 7d)"),
        };
    }

    private static DateTime ParseBefore(string text)
    {
        if (DateTime.TryParseExact(text.Trim(), TimestampFormats,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return dt;
        }
        throw new FormatException(
            $"bad timestamp '{text}' (use YYYY-MM-DD or 'YYYY-MM-DD HH:MM:SS')");
    }

    private static bool TryTimestamp(string ts, out DateTime dt) =>
        DateTime.TryParseExact(ts, TimestampFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out dt);
}
