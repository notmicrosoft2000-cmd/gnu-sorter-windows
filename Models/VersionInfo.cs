using System.Text.RegularExpressions;

namespace GnuSorter.Models;

/// <summary>
/// Channel-aware version parsing shared by the app and the updater. This mirrors
/// the Linux app's PEP 440 handling so both platforms agree on ordering within a
/// channel: dev &lt; alpha &lt; beta &lt; rc &lt; final. Accepts both spellings
/// ("3.0.0-dev.0", "v3.0.0-indev.5", "3.0.0a1", "3.0.0-rc.1", "3.0.0").
/// </summary>
public static class VersionInfo
{
    public const string Indev = "indev";
    public const string Alpha = "alpha";
    public const string Beta = "beta";
    public const string Rc = "rc";
    public const string Release = "release";

    private static readonly Dictionary<string, int> PhaseRank = new()
    {
        ["dev"] = 0,
        ["a"] = 1,
        ["b"] = 2,
        ["rc"] = 3,
        ["final"] = 4,
    };

    /// <summary>Fold a tag into one PEP-440-ish spelling ("v3.0.0-rc.1" → "3.0.0rc1").</summary>
    public static string Normalize(string? version)
    {
        var s = (version ?? "").Trim().TrimStart('v', 'V').ToLowerInvariant();
        s = s.Replace("alpha", "a").Replace("beta", "b").Replace("indev", "dev");
        return Regex.Replace(s, @"[-._]?(a|b|rc|dev)\.?(\d*)", "$1$2");
    }

    /// <summary>Splits a version into (major, minor, patch), phase and phase number.</summary>
    public static ((int Major, int Minor, int Patch) Release, string Phase, int Num)
        Parse(string? version)
    {
        var n = Normalize(version);
        var release = (Major: 0, Minor: 0, Patch: 0);
        var rest = n;

        var m = Regex.Match(n, @"^(\d+(?:\.\d+)*)(.*)$");
        if (m.Success)
        {
            var nums = m.Groups[1].Value
                .Split('.')
                .Select(x => int.TryParse(x, out var v) ? v : 0)
                .ToArray();
            release = (nums.Length > 0 ? nums[0] : 0,
                       nums.Length > 1 ? nums[1] : 0,
                       nums.Length > 2 ? nums[2] : 0);
            rest = m.Groups[2].Value;
        }

        var dev = Regex.Match(rest, @"^\.?dev(\d*)");
        if (dev.Success)
        {
            return (release, "dev", ParseNum(dev.Groups[1].Value));
        }

        var pre = Regex.Match(rest, @"^\.?(a|b|rc)(\d*)");
        if (pre.Success)
        {
            return (release, pre.Groups[1].Value, ParseNum(pre.Groups[2].Value));
        }

        return (release, "final", 0);
    }

    private static int ParseNum(string digits) =>
        digits.Length > 0 && int.TryParse(digits, out var n) ? n : 0;

    /// <summary>A comparable key across channels (same ordering as the Python app).</summary>
    public static (int Major, int Minor, int Patch, int Phase, int Num) Key(string? version)
    {
        var (release, phase, num) = Parse(version);
        return (release.Major, release.Minor, release.Patch, PhaseRank[phase], num);
    }

    public static int Compare(string? a, string? b) => Key(a).CompareTo(Key(b));

    public static bool IsNewer(string? candidate, string? current) =>
        Compare(candidate, current) > 0;

    /// <summary>"indev" | "alpha" | "beta" | "rc" | "release" for a version or tag.</summary>
    public static string ChannelOf(string? version) => Parse(version).Phase switch
    {
        "dev" => Indev,
        "a" => Alpha,
        "b" => Beta,
        "rc" => Rc,
        _ => Release,
    };

    /// <summary>Human display for a tag/version (drops a leading "v").</summary>
    public static string Display(string? version) =>
        (version ?? "").Trim().TrimStart('v', 'V');
}
