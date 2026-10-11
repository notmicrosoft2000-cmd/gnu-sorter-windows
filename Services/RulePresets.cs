using GnuSorter.Models;

namespace GnuSorter.Services;

/// <summary>
/// Built-in rule sets, mirroring the Python defaults and GUI presets.
/// </summary>
public static class RulePresets
{
    // "screenshot"-style names, but only when they are actually image files.
    // A rule's extensions and regex are OR-ed, so binding the extension into
    // the regex keeps `screen_capture.mp4` out of the Screenshots folder.
    private const string ScreenshotRegex =
        @"(screenshot|ibis|scree.?shot|capture).*\.(jpg|jpeg|png|gif|webp|heic|bmp|tiff|avif|dng|svg)$";

    public static List<SortRule> GnomeDefaults() => new()
    {
        // Name-based rules come first: the first matching rule wins, so the
        // Wallpapers rule must precede the broad Photos extension rule.
        SortRule.NameRule("Photos/Screenshots", ScreenshotRegex),
        SortRule.NameRule("Wallpapers", "wallpaper|white.?sur"),
        SortRule.ExtensionsRule("Photos", "jpg", "jpeg", "png", "gif", "webp", "heic",
            "bmp", "tiff", "avif", "svg", "ico", "raw", "psd", "xcf"),
        SortRule.ExtensionsRule("Videos", "mp4", "mkv", "webm", "mov", "avi", "flv",
            "m4v", "wmv", "mpeg", "mpg", "3gp", "ts", "m2ts"),
        SortRule.ExtensionsRule("Music", "mp3", "flac", "wav", "ogg", "m4a", "aac",
            "opus", "wma", "mid", "midi"),
        SortRule.ExtensionsRule("Documents", "pdf", "doc", "docx", "odt", "txt", "md",
            "rtf", "xls", "xlsx", "csv", "ppt", "pptx", "odp", "epub"),
        SortRule.ExtensionsRule("Archives", "tar.gz", "tar.xz", "tar.bz2", "tar.zst",
            "tgz", "zip", "tar", "gz", "xz", "bz2", "7z", "rar", "zst", "lz4"),
        SortRule.ExtensionsRule("Installers", "appimage", "deb", "rpm", "apk", "exe",
            "msi", "dmg", "pkg", "run", "flatpak", "flatpakref"),
        SortRule.ExtensionsRule("ISOs", "iso", "img"),
        SortRule.ExtensionsRule("Code", "py", "js", "ts", "jsx", "tsx", "html", "css",
            "go", "rs", "c", "cpp", "h", "hpp", "java", "json", "sh", "fish", "yaml",
            "yml", "toml", "xml", "sql", "lua", "rb", "kt", "swift", "php", "vue", "svelte"),
        SortRule.ExtensionsRule("Fonts", "ttf", "otf", "woff", "woff2", "eot"),
        SortRule.ExtensionsRule("Torrents", "torrent"),
    };

    public static List<SortRule> Photography() => new()
    {
        SortRule.NameRule("Photos/Screenshots", ScreenshotRegex),
        SortRule.NameRule("Wallpapers", "wallpaper|white.?sur"),
        SortRule.ExtensionsRule("Photos", "jpg", "jpeg", "png", "gif", "webp", "heic",
            "bmp", "tiff", "avif", "raw", "psd", "xcf", "dng"),
        SortRule.ExtensionsRule("Videos", "mp4", "mkv", "webm", "mov", "avi", "m4v", "3gp"),
    };

    public static List<SortRule> CodeProjects() => new()
    {
        SortRule.ExtensionsRule("Code", "py", "js", "ts", "jsx", "tsx", "html", "css",
            "go", "rs", "c", "cpp", "h", "hpp", "java", "json", "sh", "yaml", "yml",
            "toml", "xml", "sql", "lua", "rb", "kt", "swift", "php", "vue", "svelte"),
        SortRule.ExtensionsRule("Documents", "md", "txt", "pdf", "odt", "rtf"),
        SortRule.ExtensionsRule("Archives", "zip", "tar", "gz", "xz", "7z", "zst"),
    };

    public static readonly IReadOnlyList<(string Label, Func<List<SortRule>> Rules)> All =
        new (string, Func<List<SortRule>>)[]
        {
            ("GNOME defaults", () => GnomeDefaults()),
            ("Photography", () => Photography()),
            ("Code projects", () => CodeProjects()),
            ("Empty", () => new List<SortRule>()),
        };
}
