namespace GnuSorter.Models;

/// <summary>A watched folder. Mirrors the Python roots.json entries.</summary>
public sealed class RootFolder
{
    public string Path { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public RootFolder Clone() => new() { Path = Path, Enabled = Enabled };
}
