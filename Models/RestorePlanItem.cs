namespace GnuSorter.Models;

/// <summary>
/// One row of a restore preview — exactly what <c>SortEngine.Undo</c> would do
/// to a journal entry, computed without touching the disk. Mirrors the Python
/// <c>restore_plan()</c> so a preview can never drift from the commit.
/// </summary>
public sealed class RestorePlanItem
{
    public MoveRecord Record { get; init; } = new();

    /// <summary>The file's original path.</summary>
    public string Source { get; init; } = "";

    /// <summary>Where the file currently lives (the sorted location).</summary>
    public string Destination { get; init; } = "";

    /// <summary>The intended restore path (the original, or its name inside a target folder).</summary>
    public string Intended { get; init; } = "";

    /// <summary>Where the file would actually land after collision handling.</summary>
    public string Target { get; init; } = "";

    public long? Size { get; init; }

    /// <summary>One of <c>restore</c>, <c>collision</c>, <c>already</c> or <c>missing</c>.</summary>
    public string Status { get; init; } = "restore";

    /// <summary>True when the destination's size drifted from the journal.</summary>
    public bool Changed { get; init; }

    /// <summary>Short human explanation of the status.</summary>
    public string Note { get; init; } = "";

    /// <summary>True when this row would actually move a file back.</summary>
    public bool Actionable => Status is "restore" or "collision";
}
