namespace GnuSorter.Models;

/// <summary>One journal entry describing a file move (used for undo).</summary>
public sealed class MoveRecord
{
    public string Timestamp { get; set; } = "";

    public string Source { get; set; } = "";

    public string Destination { get; set; } = "";

    public string FileName => System.IO.Path.GetFileName(Source);

    public string DestinationFolder =>
        System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(Destination) ?? "");
}
