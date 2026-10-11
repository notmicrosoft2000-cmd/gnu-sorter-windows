namespace GnuSorter.Models;

/// <summary>One journal entry describing a file move (used for undo/history queries).</summary>
public sealed class MoveRecord
{
    public string Id { get; set; } = "";

    public int Version { get; set; } = 1;

    public string Batch { get; set; } = "";

    public string Timestamp { get; set; } = "";

    public string Source { get; set; } = "";

    public string Destination { get; set; } = "";

    public long? Size { get; set; }

    public double? Mtime { get; set; }

    public string Rule { get; set; } = "";

    public string Root { get; set; } = "";

    public string FileName => System.IO.Path.GetFileName(Source);

    public string DestinationFolder =>
        System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(Destination) ?? "");
}
