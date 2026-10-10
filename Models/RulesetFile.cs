namespace GnuSorter.Models;

/// <summary>Per-folder ruleset file: <c>{ "path": "...", "rules": [ ... ] }</c>.</summary>
public sealed class RulesetFile
{
    public string Path { get; set; } = "";

    public List<SortRule> Rules { get; set; } = new();
}
