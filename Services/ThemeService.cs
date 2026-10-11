using Microsoft.UI.Xaml;

namespace GnuSorter.Services;

/// <summary>Applies the user's appearance choice to the whole window.</summary>
public static class ThemeService
{
    public const string Default = "default";
    public const string Light = "light";
    public const string Dark = "dark";

    /// <summary>
    /// Set the window root's requested theme ("default" follows Windows).
    /// Safe to call before the window exists (it simply does nothing).
    /// </summary>
    public static void Apply(string? scheme)
    {
        if (App.RootWindow?.Content is FrameworkElement root)
        {
            root.RequestedTheme = ToElementTheme(scheme);
        }
    }

    public static ElementTheme ToElementTheme(string? scheme) => scheme switch
    {
        Light => ElementTheme.Light,
        Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };
}
