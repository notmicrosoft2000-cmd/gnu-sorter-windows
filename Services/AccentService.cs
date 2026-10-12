using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace GnuSorter.Services;

/// <summary>
/// Applies the accent colour across the whole app by rewriting the accent
/// resources <c>App.xaml</c> declares per theme. Mirrors the Linux picker:
/// "system" follows the desktop, every other key is a Funnel palette colour
/// with a light and a dark variant (kept in step with
/// <c>gnu_sorter/gui.py</c> `ACCENTS`).
///
/// The existing brushes are mutated in place rather than replaced, so a
/// <see cref="SolidColorBrush"/> change notification repaints the UI live —
/// no window reload required.
/// </summary>
public static class AccentService
{
    /// <summary>Follow the Windows accent.</summary>
    public const string SystemKey = "system";

    /// <summary>The Funnel brand colour (the default).</summary>
    public const string DefaultKey = "green";

    /// <summary>Menu order; must match the ComboBoxItems in SettingsView.xaml.</summary>
    public static readonly IReadOnlyList<string> OrderedKeys = new[]
    {
        "system", "green", "lime", "teal", "cyan", "blue", "purple", "pink",
        "red", "orange", "yellow", "slate",
    };

    private readonly record struct Pair(string Bg, string Fg);

    // Same palette as gnu_sorter/gui.py ACCENTS (light/dark background + ink).
    private static readonly Dictionary<string, (Pair Light, Pair Dark)> Palette = new()
    {
        ["green"] = (new Pair("#4d7c0f", "#ffffff"), new Pair("#a3e635", "#0b0d08")),
        ["lime"] = (new Pair("#65a30d", "#ffffff"), new Pair("#bef264", "#0b0d08")),
        ["teal"] = (new Pair("#0f766e", "#ffffff"), new Pair("#2dd4bf", "#04201d")),
        ["cyan"] = (new Pair("#0e7490", "#ffffff"), new Pair("#67e8f9", "#04222b")),
        ["blue"] = (new Pair("#1c71d8", "#ffffff"), new Pair("#78aeff", "#0a1f3d")),
        ["purple"] = (new Pair("#7c3aed", "#ffffff"), new Pair("#c4a0ff", "#23123f")),
        ["pink"] = (new Pair("#d6336c", "#ffffff"), new Pair("#ff8fbe", "#3d0a20")),
        ["red"] = (new Pair("#e01b24", "#ffffff"), new Pair("#ff7b72", "#360c0a")),
        ["orange"] = (new Pair("#c64600", "#ffffff"), new Pair("#ffb45c", "#3a1e00")),
        ["yellow"] = (new Pair("#a1660a", "#ffffff"), new Pair("#f5c211", "#332600")),
        ["slate"] = (new Pair("#5e6b78", "#ffffff"), new Pair("#9fb0c0", "#131a20")),
    };

    /// <summary>Restyle the app with the given accent key (unknown → brand green).</summary>
    public static void Apply(string? accent)
    {
        var key = string.IsNullOrWhiteSpace(accent)
            ? DefaultKey
            : accent.Trim().ToLowerInvariant();

        var (light, dark) = Resolve(key);

        var resources = Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        try
        {
            Patch(resources, "Light", light);
            Patch(resources, "Dark", dark);
        }
        catch (Exception)
        {
            // Re-styling is cosmetic; never let it take down the app. The
            // colours from App.xaml (brand green) remain in place.
        }
    }

    private static void Patch(ResourceDictionary resources, string theme, Pair pair)
    {
        if (resources.ThemeDictionaries[theme] is not ResourceDictionary dict)
        {
            dict = new ResourceDictionary();
            resources.ThemeDictionaries[theme] = dict;
        }

        var bg = Parse(pair.Bg);
        var fg = Parse(pair.Fg);

        dict["SystemAccentColor"] = bg;
        dict["SystemAccentColorLight1"] = Mix(bg, 255, 0.10);
        dict["SystemAccentColorLight2"] = Mix(bg, 255, 0.20);
        dict["SystemAccentColorLight3"] = Mix(bg, 255, 0.30);
        dict["SystemAccentColorDark1"] = Mix(bg, 0, 0.10);
        dict["SystemAccentColorDark2"] = Mix(bg, 0, 0.20);
        dict["SystemAccentColorDark3"] = Mix(bg, 0, 0.30);

        SetBrush(dict, "AccentFillColorDefaultBrush", Alpha(bg, 0xFF));
        SetBrush(dict, "AccentFillColorSecondaryBrush", Alpha(bg, 0xE6));
        SetBrush(dict, "AccentFillColorTertiaryBrush", Alpha(bg, 0xCC));
        SetBrush(dict, "AccentFillColorDisabledBrush", Alpha(bg, 0x66));

        SetBrush(dict, "AccentTextFillColorPrimaryBrush", Alpha(bg, 0xFF));
        SetBrush(dict, "AccentTextFillColorSecondaryBrush", Alpha(bg, 0xE6));
        SetBrush(dict, "AccentTextFillColorTertiaryBrush", Alpha(bg, 0xCC));
        SetBrush(dict, "AccentTextFillColorDisabledBrush", Alpha(bg, 0x66));

        SetBrush(dict, "TextOnAccentFillColorPrimaryBrush", Alpha(fg, 0xFF));
        SetBrush(dict, "TextOnAccentFillColorSecondaryBrush", Alpha(fg, 0xE6));
        SetBrush(dict, "TextOnAccentFillColorDisabledBrush", Alpha(fg, 0x66));
        SetBrush(dict, "TextOnAccentFillColorSelectedTextBrush", Alpha(fg, 0xFF));

        SetBrush(dict, "SystemControlHighlightAccentBrush", Alpha(bg, 0xFF));
        SetBrush(dict, "SystemControlForegroundAccentBrush", Alpha(bg, 0xFF));
        SetBrush(dict, "SystemControlBackgroundAccentBrush", Alpha(bg, 0xFF));
        SetBrush(dict, "SystemControlHighlightListAccentLowBrush", Alpha(bg, 0x66));
        SetBrush(dict, "SystemControlHighlightListAccentMediumBrush", Alpha(bg, 0xB3));
        SetBrush(dict, "SystemControlHighlightListAccentHighBrush", Alpha(bg, 0xFF));

        SetBrush(dict, "NavigationViewSelectionIndicatorForeground", Alpha(bg, 0xFF));
    }

    private static void SetBrush(ResourceDictionary dict, string key, Color color)
    {
        if (dict[key] is SolidColorBrush existing)
        {
            existing.Color = color; // notifies and repaints live
        }
        else
        {
            dict[key] = new SolidColorBrush(color);
        }
    }

    private static (Pair Light, Pair Dark) Resolve(string key)
    {
        if (key == SystemKey && TrySystemAccent(out var system))
        {
            return system;
        }
        if (Palette.TryGetValue(key, out var pair))
        {
            return pair;
        }
        return Palette[DefaultKey];
    }

    private static bool TrySystemAccent(out (Pair Light, Pair Dark) pair)
    {
        pair = default;
        try
        {
            var ui = new UISettings();
            var accent = ui.GetColorValue(UIColorType.Accent);
            var ink = Luminance(accent) > 0.6 ? "#000000" : "#ffffff";
            pair = (new Pair(Hex(accent), ink), new Pair(Hex(accent), ink));
            return true;
        }
        catch
        {
            return false; // fall back to the brand green
        }
    }

    private static Color Parse(string hex)
    {
        var s = hex.TrimStart('#');
        return Color.FromArgb(
            255,
            Convert.ToByte(s.Substring(0, 2), 16),
            Convert.ToByte(s.Substring(2, 2), 16),
            Convert.ToByte(s.Substring(4, 2), 16));
    }

    private static string Hex(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

    private static double Luminance(Color c) =>
        (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    private static Color Alpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    private static Color Mix(Color c, byte toward, double amount)
    {
        byte M(byte v) => (byte)Math.Round(v + (toward - v) * amount);
        return Color.FromArgb(255, M(c.R), M(c.G), M(c.B));
    }
}
