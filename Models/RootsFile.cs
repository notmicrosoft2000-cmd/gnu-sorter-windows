namespace GnuSorter.Models;

/// <summary>Wrapper for roots.json: <c>{ "roots": [ ... ] }</c>.</summary>
public sealed class RootsFile
{
    public List<RootFolder> Roots { get; set; } = new();
}
