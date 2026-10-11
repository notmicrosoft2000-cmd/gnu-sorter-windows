namespace GnuSorter.Models;

/// <summary>
/// One pass over the move journal, then fast lookups. Mirrors the Python
/// <c>JournalIndex</c>: entries are grouped by rule, destination folder and
/// operation (batch) id so the rules page ("N sorted" badges) and the Activity
/// page can answer queries without re-reading history.log.
/// </summary>
public sealed class JournalIndex
{
    public JournalIndex(IEnumerable<MoveRecord> entries)
    {
        Entries = entries.ToList();

        var byRule = new Dictionary<string, List<MoveRecord>>(StringComparer.OrdinalIgnoreCase);
        var byFolder = new Dictionary<string, List<MoveRecord>>(StringComparer.OrdinalIgnoreCase);
        var byBatch = new Dictionary<string, List<MoveRecord>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in Entries)
        {
            if (!string.IsNullOrEmpty(entry.Rule))
            {
                Bucket(byRule, entry.Rule).Add(entry);
            }
            var folder = System.IO.Path.GetDirectoryName(entry.Destination);
            if (!string.IsNullOrEmpty(folder))
            {
                Bucket(byFolder, folder).Add(entry);
            }
            if (!string.IsNullOrEmpty(entry.Batch))
            {
                Bucket(byBatch, entry.Batch).Add(entry);
            }
        }

        ByRule = byRule;
        ByFolder = byFolder;
        ByBatch = byBatch;
    }

    public IReadOnlyList<MoveRecord> Entries { get; }

    /// <summary>Entries grouped by the rule that caught them.</summary>
    public IReadOnlyDictionary<string, List<MoveRecord>> ByRule { get; }

    /// <summary>Entries grouped by destination folder.</summary>
    public IReadOnlyDictionary<string, List<MoveRecord>> ByFolder { get; }

    /// <summary>Entries grouped by sweep/operation id.</summary>
    public IReadOnlyDictionary<string, List<MoveRecord>> ByBatch { get; }

    public int Count => Entries.Count;

    /// <summary>Number of live journal entries per rule name.</summary>
    public Dictionary<string, int> Counts()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rule, rows) in ByRule)
        {
            counts[rule] = rows.Count;
        }
        return counts;
    }

    private static List<MoveRecord> Bucket(
        Dictionary<string, List<MoveRecord>> map, string key)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<MoveRecord>();
            map[key] = list;
        }
        return list;
    }
}
