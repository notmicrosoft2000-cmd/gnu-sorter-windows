using System.Text.Json.Serialization;

namespace GnuSorter.Models;

/// <summary>
/// One sorting rule: send files whose extension (or name regex) matches into a
/// destination folder named <see cref="Name"/>. Mirrors the Python schema
/// (rules.json): name / extensions / match_name_regex.
/// </summary>
public sealed class SortRule
{
    public string Name { get; set; } = "";

    public List<string> Extensions { get; set; } = new();

    [JsonPropertyName("match_name_regex")]
    public string? MatchNameRegex { get; set; }

    [JsonIgnore]
    public bool IsNameMatch => !string.IsNullOrWhiteSpace(MatchNameRegex);

    public SortRule Clone() => new()
    {
        Name = Name,
        Extensions = new List<string>(Extensions),
        MatchNameRegex = MatchNameRegex,
    };

    public static SortRule ExtensionsRule(string name, params string[] extensions) =>
        new() { Name = name, Extensions = extensions.ToList() };

    public static SortRule NameRule(string name, string regex) =>
        new() { Name = name, MatchNameRegex = regex };
}
