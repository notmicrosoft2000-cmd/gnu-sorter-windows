using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using GnuSorter.Helpers;
using GnuSorter.Models;
using GnuSorter.Services;
using GnuSorter.ViewModels;

namespace GnuSorter.Views;

public sealed partial class FoldersView : Page
{
    private readonly AppServices _services = App.Services;
    private readonly ObservableCollection<RootItemViewModel> _items = new();
    private bool _loading;

    public FoldersView()
    {
        InitializeComponent();
        FoldersList.ItemsSource = _items;
        Loaded += (_, _) => Reload();
    }

    private void Reload()
    {
        _loading = true;
        _items.Clear();
        foreach (var root in _services.Config.LoadRoots())
        {
            _items.Add(ToItem(root));
        }
        _loading = false;
        EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private RootItemViewModel ToItem(RootFolder root) => new()
    {
        Path = root.Path,
        DisplayName = PathDisplay.NameFor(root.Path),
        Subtitle = root.Path,
        Enabled = root.Enabled,
        IsCustom = _services.Config.IsCustom(root.Path),
    };

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads,
            };
            picker.FileTypeFilter.Add("*");

            if (App.RootWindow is null)
            {
                return;
            }
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.RootWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            var path = folder.Path;
            var roots = _services.Config.LoadRoots();
            if (roots.Any(r => SamePath(r.Path, path)))
            {
                ShowStatus(InfoBarSeverity.Informational, "That folder is already in the list.");
                return;
            }

            roots.Add(new RootFolder { Path = path, Enabled = true });
            _services.Config.SaveRoots(roots);
            ShowStatus(InfoBarSeverity.Success, $"Added {PathDisplay.NameFor(path)}.");
            RestartWatcherIfRunning();
            Reload();
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, $"Could not add the folder: {ex.Message}");
        }
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RootItemViewModel item })
        {
            return;
        }

        var roots = _services.Config.LoadRoots();
        roots.RemoveAll(r => SamePath(r.Path, item.Path));
        _services.Config.SaveRoots(roots);

        // Drop a per-folder ruleset along with the folder.
        var ruleset = _services.Config.RulesetPath(item.Path);
        if (System.IO.File.Exists(ruleset))
        {
            System.IO.File.Delete(ruleset);
        }

        ShowStatus(InfoBarSeverity.Success, $"Removed {item.DisplayName}.");
        RestartWatcherIfRunning();
        Reload();
    }

    /// <summary>Keeps the watcher in sync with the folder list.</summary>
    private void RestartWatcherIfRunning()
    {
        if (_services.Watcher.IsRunning)
        {
            _services.Watcher.Stop();
            var restarted = _services.EnabledRoots();
            if (restarted.Count > 0)
            {
                _services.Watcher.Start(restarted);
            }
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RootItemViewModel item })
        {
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{item.Path}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, ex.Message);
        }
    }

    private void FolderToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not FrameworkElement { DataContext: RootItemViewModel item })
        {
            return;
        }

        var roots = _services.Config.LoadRoots();
        foreach (var root in roots.Where(r => SamePath(r.Path, item.Path)))
        {
            root.Enabled = item.Enabled;
        }
        _services.Config.SaveRoots(roots);

        // Restart the watcher so the change takes effect immediately.
        RestartWatcherIfRunning();
    }

    private void SortNow_Click(object sender, RoutedEventArgs e)
    {
        var roots = _services.EnabledRoots();
        if (roots.Count == 0)
        {
            ShowStatus(InfoBarSeverity.Warning, "Add and enable a folder first.");
            return;
        }

        try
        {
            var dryRun = _services.Config.LoadSettings().DryRunDefault;
            var stats = _services.Engine.Sweep(roots, dryRun);
            var message = $"{stats.Moved} file(s) sorted, {stats.NeedsReview} to Needs-review, "
                + $"{stats.Skipped} skipped";
            if (stats.Failed > 0)
            {
                message += $", {stats.Failed} failed";
            }
            message += dryRun ? " (dry run)." : ".";
            ShowStatus(InfoBarSeverity.Success, message);
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, $"Sorting failed: {ex.Message}");
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(ConfigStore.NormalizeKey(a), ConfigStore.NormalizeKey(b),
            StringComparison.OrdinalIgnoreCase);

    private void ShowStatus(InfoBarSeverity severity, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }
}
