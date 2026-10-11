using System.Text.RegularExpressions;
using GnuSorter.Models;

namespace GnuSorter.Services;

/// <summary>
/// Turns a rule into plain English. Mirrors the Python <c>describe_rule</c> so
/// the two apps word things the same way in the rules page, the tester, and the
/// (upcoming) wizard review step.
/// </summary>
public static class RuleText
{
    private static readonly Regex LiteralName = new(@"^[A-Za-z0-9 _.\-]+$");

    public static string HumanizeRegex(string? pattern)
    {
        pattern = pattern?.Trim() ?? "";
        if (pattern.Length == 0)
        {
            return "";
        }

        var words = pattern.Split('|').Select(HumanizeToken).ToList();
        if (words.Count > 0 && words.All(w => w is not null))
        {
            if (words.Count == 1)
            {
                return $"the name contains \u201c{words[0]}\u201d";
            }
            var joined = string.Join(", ", words.Select(w => $"\u201c{w}\u201d"));
            return $"the name contains one of: {joined}";
        }
        if (LiteralName.IsMatch(pattern))
        {
            return $"the name contains \u201c{pattern}\u201d";
        }
        return $"the name matches /{pattern}/";
    }

    private static string? HumanizeToken(string token)
    {
        if (token.Length == 0)
        {
            return null;
        }
        var plain = token.Replace("\\-", "-").Replace("\\_", "_")
                         .Replace("\\.", ".").Replace("\\ ", " ");
        if (!LiteralName.IsMatch(plain))
        {
            return null;
        }
        var trimmed = plain.Trim(' ', '.', '-', '_');
        return trimmed.Length > 0 ? trimmed : plain;
    }

    public static string Describe(SortRule rule)
    {
        var clauses = MatchClauses(rule);
        var text = clauses.Count switch
        {
            0 => "nothing yet",
            1 => clauses[0],
            _ => string.Join(" or ", clauses),
        };
        var filters = FilterClauses(rule);
        if (filters.Count > 0)
        {
            text = $"{text}, {string.Join(", ", filters)}";
        }
        return text.Length > 0
            ? char.ToUpperInvariant(text[0]) + text[1..]
            : text;
    }

    private static List<string> MatchClauses(SortRule rule)
    {
        var clauses = new List<string>();
        var exts = rule.Extensions
            .Select(e => (e ?? "").Trim().TrimStart('.'))
            .Where(e => e.Length > 0)
            .ToList();
        if (exts.Count > 0)
        {
            var shown = string.Join(", ", exts.Take(6).Select(e => "." + e));
            if (exts.Count > 6)
            {
                shown += $", and {exts.Count - 6} more";
            }
            clauses.Add($"a file ending in {shown}");
        }
        if (!string.IsNullOrWhiteSpace(rule.MatchNameRegex))
        {
            clauses.Add(HumanizeRegex(rule.MatchNameRegex));
        }
        if (rule.MatchAll)
        {
            clauses.Add("any file");
        }
        return clauses.Where(c => c.Length > 0).ToList();
    }

    private static List<string> FilterClauses(SortRule rule)
    {
        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(rule.ExcludeNameRegex))
        {
            filters.Add($"skipping names matching /{rule.ExcludeNameRegex}/");
        }

        double? lo = rule.MinSizeMb is double mn && mn > 0 ? mn : null;
        double? hi = rule.MaxSizeMb is double mx && mx > 0 ? mx : null;
        if (lo is not null && hi is not null)
        {
            filters.Add($"between {Num(lo.Value)} and {Num(hi.Value)} MB");
        }
        else if (lo is not null)
        {
            filters.Add($"at least {Num(lo.Value)} MB");
        }
        else if (hi is not null)
        {
            filters.Add($"up to {Num(hi.Value)} MB");
        }

        if (rule.ModifiedWithinDays is double days && days > 0)
        {
            filters.Add($"modified in the last {Num(days)} day(s)");
        }
        return filters;
    }

    private static string Num(double value) =>
        value == Math.Floor(value)
            ? ((long)value).ToString()
            : value.ToString("0.##");
}
