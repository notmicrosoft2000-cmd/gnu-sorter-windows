namespace GnuSorter.Models;

/// <summary>Result counters returned by a sweep.</summary>
public sealed class SortStats
{
    public int Moved { get; set; }

    public int NeedsReview { get; set; }

    public int Skipped { get; set; }

    /// <summary>Files matched but could not be moved (locked, …).</summary>
    public int Failed { get; set; }

    /// <summary>
    /// Files parked because they are still downloading (progress suffix or an
    /// open write handle) or still inside their settle window.
    /// </summary>
    public int Held { get; set; }

    /// <summary>File names parked by this sweep (already counted in <see cref="Held"/>).</summary>
    public List<string> HeldFiles { get; set; } = new();
}