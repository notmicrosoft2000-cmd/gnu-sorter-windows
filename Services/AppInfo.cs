using System.Reflection;
using GnuSorter.Models;

namespace GnuSorter.Services;

/// <summary>Single-source app identity: product name, version and channel.</summary>
public static class AppInfo
{
    public const string ProductName = "Funnel";

    /// <summary>Codename/slug kept for paths, packages and the exe.</summary>
    public const string Codename = "gnu-sorter";

    /// <summary>SemVer-ish version from the assembly, e.g. "3.0.0-dev.0".</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>The release channel this build belongs to.</summary>
    public static string Channel => VersionInfo.ChannelOf(Version);

    /// <summary>"Version 3.0.0-dev.0 · by notmicrosoft2000-cmd" for the About pane.</summary>
    public static string VersionDisplay =>
        $"Version {Version} · by notmicrosoft2000-cmd";

    private static string ReadVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            // SourceLink / SDK may append "+<commit>"; keep just the version.
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }

        var v = asm.GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
