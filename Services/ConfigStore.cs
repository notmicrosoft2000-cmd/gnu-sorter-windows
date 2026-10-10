using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GnuSorter.Models;

namespace GnuSorter.Services;

/// <summary>
/// Reads and writes the on-disk configuration. Layout matches the Python app so
/// an existing config directory can be reused:
///   %APPDATA%\gnu-sorter\rules.json       default ruleset ("template")
///   %APPDATA%\gnu-sorter\roots.json       watched folders
///   %APPDATA%\gnu-sorter\roots.d\*.json   per-folder rulesets
///   %APPDATA%\gnu-sorter\settings.json    preferences
///   %LOCALAPPDATA%\gnu-sorter\history.log move journal
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Shared serializer options (snake_case keys, matching the Python app).</summary>
    public static JsonSerializerOptions SerializerOptions => JsonOptions;

    public string ConfigDir { get; }

    public string DataDir { get; }

    public string RulesPath => System.IO.Path.Combine(ConfigDir, "rules.json");

    public string RootsPath => System.IO.Path.Combine(ConfigDir, "roots.json");

    public string SettingsPath => System.IO.Path.Combine(ConfigDir, "settings.json");

    public string RootsDir => System.IO.Path.Combine(ConfigDir, "roots.d");

    public string HistoryPath => System.IO.Path.Combine(DataDir, "history.log");

    public ConfigStore(string? configDir = null, string? dataDir = null)
    {
        ConfigDir = configDir ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "gnu-sorter");
        DataDir = dataDir ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "gnu-sorter");
    }

    // -- lifecycle -------------------------------------------------------

    public void EnsureDefaults()
    {
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(RootsDir);
        if (!File.Exists(RulesPath))
        {
            SaveRules(RulePresets.GnomeDefaults());
        }
        if (!File.Exists(RootsPath))
        {
            SaveRoots(new List<RootFolder> { DefaultRoot() });
        }
        if (!File.Exists(SettingsPath))
        {
            SaveSettings(new AppSettings());
        }
    }

    private static RootFolder DefaultRoot() => new()
    {
        Path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        Enabled = true,
    };

    // -- rules -----------------------------------------------------------

    public List<SortRule> LoadRules()
    {
        var rules = ReadJson<List<SortRule>>(RulesPath);
        // Legacy download-sorter schema: { "categories": [...] }
        if (rules is null)
        {
            var legacy = ReadJson<Dictionary<string, JsonElement>>(RulesPath);
            if (legacy is not null && legacy.TryGetValue("categories", out var categories)
                && categories.ValueKind == JsonValueKind.Array)
            {
                return categories.Deserialize<List<SortRule>>(JsonOptions) ?? new List<SortRule>();
            }
            return RulePresets.GnomeDefaults();
        }
        return rules.Where(r => r is not null).ToList();
    }

    public void SaveRules(IEnumerable<SortRule> rules) =>
        WriteJson(RulesPath, rules.ToList());

    // -- per-folder rulesets ---------------------------------------------

    public static string NormalizeKey(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (expanded.StartsWith('~'))
        {
            expanded = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                expanded.TrimStart('~', '/', '\\'));
        }
        try
        {
            expanded = System.IO.Path.GetFullPath(expanded);
        }
        catch (Exception)
        {
            // keep the expanded value as-is
        }
        if (expanded.Length > 3)
        {
            expanded = expanded.TrimEnd(System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar);
        }
        return expanded;
    }

    public string RulesetPath(string rootPath)
    {
        var key = NormalizeKey(rootPath);
        var digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        return System.IO.Path.Combine(RootsDir, digest[..40] + ".json");
    }

    public bool IsCustom(string rootPath) => File.Exists(RulesetPath(rootPath));

    public (List<SortRule> Rules, bool Custom) RulesFor(string rootPath)
    {
        var path = RulesetPath(rootPath);
        if (!File.Exists(path))
        {
            return (LoadRules(), false);
        }
        var data = ReadJson<RulesetFile>(path);
        if (data is not null && data.Rules is not null)
        {
            return (data.Rules.Where(r => r is not null).ToList(), true);
        }
        return (LoadRules(), false);
    }

    public void SaveRulesFor(string rootPath, IEnumerable<SortRule> rules) =>
        WriteJson(RulesetPath(rootPath), new RulesetFile
        {
            Path = NormalizeKey(rootPath),
            Rules = rules.ToList(),
        });

    /// <summary>Give a folder its own copy of the template. False if already custom.</summary>
    public bool DetachRules(string rootPath)
    {
        if (IsCustom(rootPath))
        {
            return false;
        }
        SaveRulesFor(rootPath, LoadRules().Select(r => r.Clone()));
        return true;
    }

    /// <summary>Revert a folder to the template. False if it was already inheriting.</summary>
    public bool AttachRules(string rootPath)
    {
        var path = RulesetPath(rootPath);
        if (!File.Exists(path))
        {
            return false;
        }
        File.Delete(path);
        return true;
    }

    // -- roots -----------------------------------------------------------

    public List<RootFolder> LoadRoots()
    {
        var file = ReadJson<RootsFile>(RootsPath);
        if (file?.Roots is null)
        {
            return new List<RootFolder> { DefaultRoot() };
        }
        return file.Roots
            .Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Path))
            .Select(r => new RootFolder { Path = r.Path, Enabled = r.Enabled })
            .ToList();
    }

    public void SaveRoots(IEnumerable<RootFolder> roots) =>
        WriteJson(RootsPath, new RootsFile { Roots = roots.ToList() });

    // -- settings --------------------------------------------------------

    public AppSettings LoadSettings()
    {
        var settings = ReadJson<AppSettings>(SettingsPath);
        return settings ?? new AppSettings();
    }

    public void SaveSettings(AppSettings settings) => WriteJson(SettingsPath, settings);

    // -- helpers ---------------------------------------------------------

    private static T? ReadJson<T>(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return default;
            }
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return default;
            }
            return JsonSerializer.Deserialize<T>(text, JsonOptions);
        }
        catch (Exception)
        {
            // A broken config must never crash the app; fall back to defaults.
            return default;
        }
    }

    private static void WriteJson<T>(string path, T value)
    {
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, JsonOptions) + "\n");
        File.Move(tmp, path, overwrite: true);
    }
}
