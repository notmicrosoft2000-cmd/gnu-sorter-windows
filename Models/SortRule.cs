using System.Text.Json.Serialization;

namespace GnuSorter.Models;

/// <summary>
/// One sorting rule: send files that match into a destination folder named
/// <see cref="Name"/>. Mirrors the Python schema (rules.json):
/// name / extensions / match_name_regex / enabled / match_all /
/// exclude_name_regex / min_size_mb / max_size_mb / modified_within_days,
/// plus UI-only icon / color hints.
/// </summary>
public sealed class SortRule
{
    public string Name { get; set; } = "";

    public List<string> Extensions { get; set; } = new();

    [JsonPropertyName("match_name_regex")]
    public string? MatchNameRegex { get; set; }

    /// <summary>false → the rule is skipped; null or true → enabled.</summary>
    public bool? Enabled { get; set; }

    /// <summary>When true the rule matches every file (catch-all fallback).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool MatchAll { get; set; }

    [JsonPropertyName("exclude_name_regex")]
    public string? ExcludeNameRegex { get; set; }

    public double? MinSizeMb { get; set; }

    public double? MaxSizeMb { get; set; }

    public double? ModifiedWithinDays { get; set; }

    /// <summary>UI-only presentation metadata; never affects matching.</summary>
    public string? Icon { get; set; }

    public string? Color { get; set; }

    /// <summary>"any" (default) or "all"; reserved for the wizard decision.</summary>
    public string? MatchMode { get; set; }

    [JsonIgnore]
    public bool IsNameMatch => !string.IsNullOrWhiteSpace(MatchNameRegex);

    [JsonIgnore]
    public bool IsEnabled => Enabled is not false;

    public SortRule Clone() => new()
    {
        Name = Name,
        Extensions = new List<string>(Extensions),
        MatchNameRegex = MatchNameRegex,
        Enabled = Enabled,
        MatchAll = MatchAll,
        ExcludeNameRegex = ExcludeNameRegex,
        MinSizeMb = MinSizeMb,
        MaxSizeMb = MaxSizeMb,
        ModifiedWithinDays = ModifiedWithinDays,
        Icon = Icon,
        Color = Color,
        MatchMode = MatchMode,
    };

    public static SortRule ExtensionsRule(string name, params string[] extensions) =>
        new() { Name = name, Extensions = extensions.ToList() };

    public static SortRule NameRule(string name, string regex) =>
        new() { Name = name, MatchNameRegex = regex };
}
