namespace GnuSorter.Models;

/// <summary>Result counters returned by a sweep.</summary>
public sealed class SortStats
{
    public int Moved { get; set; }

    public int NeedsReview { get; set; }

    public int Skipped { get; set; }
}
