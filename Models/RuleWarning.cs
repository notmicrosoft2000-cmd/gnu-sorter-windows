namespace GnuSorter.Models;

/// <summary>
/// A non-fatal warning about how an ordered ruleset behaves — a rule that can
/// never match, or one shadowed by an earlier rule. Produced by
/// <c>SortEngine.Overlaps</c> and shown as a badge on the rules page.
/// </summary>
public sealed class RuleWarning
{
    /// <summary>Name of the rule the warning is about, if any.</summary>
    public string? Rule { get; init; }

    /// <summary>Friendly, one-sentence explanation.</summary>
    public string Message { get; init; } = "";
}
