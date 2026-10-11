using Microsoft.Win32;

namespace GnuSorter.Services;

/// <summary>Registers Funnel to launch at sign-in via the per-user Run key.</summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Funnel";
    private const string LegacyValueName = "GNU Sorter";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void Enable()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            throw new InvalidOperationException("Cannot determine the executable path.");
        }
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key?.SetValue(ValueName, $"\"{exe}\"");
        // Drop the pre-rename "GNU Sorter" Run-key value so old installs don't
        // end up with two launch-on-sign-in entries.
        key?.DeleteValue(LegacyValueName, throwOnMissingValue: false);
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
