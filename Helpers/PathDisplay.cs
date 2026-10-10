using GnuSorter.Services;

namespace GnuSorter.Helpers;

/// <summary>Presentation helpers that turn filesystem paths into short labels.</summary>
public static class PathDisplay
{
    /// <summary>The last path segment, falling back to the full path for drives.</summary>
    public static string NameFor(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }
        try
        {
            var normalized = ConfigStore.NormalizeKey(path);
            var name = System.IO.Path.GetFileName(normalized);
            return string.IsNullOrEmpty(name) ? normalized : name;
        }
        catch
        {
            return path;
        }
    }
}
