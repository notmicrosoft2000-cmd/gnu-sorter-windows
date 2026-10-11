using System.Collections.ObjectModel;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using GnuSorter.Services;
using GnuSorter.ViewModels;

namespace GnuSorter.Views;

public sealed partial class ActivityView : Page
{
    private readonly AppServices _services = App.Services;
    private readonly ObservableCollection<ActivityItemViewModel> _items = new();
    private bool _loadingFilter;

    public ActivityView()
    {
        InitializeComponent();
        ActivityList.ItemsSource = _items;
        Loaded += (_, _) => Reload();
    }

    /// <summary>File-name substring/glob typed in the search box, or null.</summary>
    private string? Query =>
        string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text.Trim();

    /// <summary>Selected rule filter, or null for "All rules".</summary>
    private string? SelectedRule =>
        RuleFilter.SelectedItem is ComboBoxItem { Tag: string tag } && tag.Length > 0
            ? tag
            : null;

    /// <summary>Source-extension filter (comma list), or null.</summary>
    private string? ExtFilter =>
        string.IsNullOrWhiteSpace(ExtFilterBox.Text) ? null : ExtFilterBox.Text.Trim();

    /// <summary>Recorded-size window in bytes; either bound may be null.</summary>
    private long? MinSize => SortEngine.ParseSize(MinSizeBox.Text);

    private long? MaxSize => SortEngine.ParseSize(MaxSizeBox.Text);

    /// <summary>Restore conflict strategy: rename (default), overwrite or skip.</summary>
    private string Conflict =>
        ConflictChoice.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : "rename";

    /// <summary>True when any filter is active (so a restore has a target set).</summary>
    private bool HasFilter =>
        Query is not null || SelectedRule is not null || ExtFilter is not null
        || MinSize is not null || MaxSize is not null;

    private void Reload()
    {
        var all = _services.Engine.Records();
        var records = _services.Engine.Records(name: Query, rule: SelectedRule,
            ext: ExtFilter, minSize: MinSize, maxSize: MaxSize);

        _items.Clear();
        // Newest first.
        foreach (var record in records.AsEnumerable().Reverse())
        {
            _items.Add(new ActivityItemViewModel
            {
                FileName = record.FileName,
                Detail = $"{record.Source}  →  {record.DestinationFolder}/",
                Time = record.Timestamp,
                Rule = string.IsNullOrEmpty(record.Rule) ? record.DestinationFolder : record.Rule,
                Batch = record.Batch,
            });
        }

        RebuildRuleFilter(all);

        EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UndoButton.IsEnabled = all.Count > 0;
        UndoSweepButton.IsEnabled = all.Count > 0;
        RestoreButton.IsEnabled = HasFilter && _items.Count > 0;
        BackupButton.IsEnabled = all.Count > 0;

        Footnote.Text = records.Count == all.Count
            ? $"{all.Count} move(s) in the journal."
            : $"{records.Count} of {all.Count} move(s).";
    }

    /// <summary>
    /// Keep the rule filter in step with the journal without dropping the
    /// current selection (and without re-entering <see cref="Reload"/>).
    /// </summary>
    private void RebuildRuleFilter(List<Models.MoveRecord> all)
    {
        var current = SelectedRule;
        var names = all.Select(r => r.Rule)
            .Where(r => !string.IsNullOrEmpty(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _loadingFilter = true;
        try
        {
            RuleFilter.Items.Clear();
            RuleFilter.Items.Add(new ComboBoxItem { Content = "All rules", Tag = "" });
            foreach (var name in names)
            {
                RuleFilter.Items.Add(new ComboBoxItem { Content = name, Tag = name });
            }
            var index = 0;
            for (var i = 0; i < RuleFilter.Items.Count; i++)
            {
                if (RuleFilter.Items[i] is ComboBoxItem { Tag: string tag }
                    && string.Equals(tag, current, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }
            RuleFilter.SelectedIndex = index;
        }
        finally
        {
            _loadingFilter = false;
        }
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => Reload();

    private void RuleFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingFilter)
        {
            Reload();
        }
    }

    // -- undo ------------------------------------------------------------

    private void Undo_Click(object sender, RoutedEventArgs e) =>
        Undo(() => _services.Engine.Undo(dryRun: false, limit: 1), "move");

    /// <summary>
    /// Reverses the most recent sweep — the same call a toast notification's
    /// "Undo" button would make (<see cref="SortEngine.UndoLastBatch"/>).
    /// </summary>
    private void UndoSweep_Click(object sender, RoutedEventArgs e) =>
        Undo(() => _services.Engine.UndoLastBatch(), "sweep");

    private void Undo(Func<int> restore, string what)
    {
        try
        {
            var count = restore();
            if (count > 0)
            {
                StatusBar.Severity = InfoBarSeverity.Success;
                StatusBar.Message = $"Restored {count} file(s) from the last {what}.";
            }
            else
            {
                StatusBar.Severity = InfoBarSeverity.Informational;
                StatusBar.Message =
                    $"Nothing restored from the last {what} (already back, or the file changed).";
            }
            StatusBar.IsOpen = true;
            Reload();
        }
        catch (Exception ex)
        {
            Report(InfoBarSeverity.Error, ex.Message);
        }
    }

    // -- restore preview -------------------------------------------------

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var conflict = Conflict;
        var plan = _services.Engine.RestorePlan(name: Query, rule: SelectedRule,
            ext: ExtFilter, minSize: MinSize, maxSize: MaxSize, onConflict: conflict);
        var actionable = plan.Count(p => p.Actionable);
        if (actionable == 0)
        {
            Report(InfoBarSeverity.Informational, "Nothing to restore for this filter.");
            return;
        }

        var list = new StackPanel { Spacing = 4, MinWidth = 460 };
        foreach (var item in plan)
        {
            var detail = item.Status switch
            {
                "missing" => "no longer there",
                "already" => "already back",
                "skip" => "occupied — left in place",
                _ => $"from {item.Record.DestinationFolder}/"
                     + (item.Status is "collision" or "overwrite" ? $"  ·  {item.Note}" : "")
                     + (item.Changed ? "  ·  size changed" : ""),
            };
            list.Children.Add(new TextBlock
            {
                Text = $"{item.Record.FileName}  —  {detail}",
                TextWrapping = TextWrapping.Wrap,
                Opacity = item.Actionable ? 1.0 : 0.6,
            });
        }

        var dialog = new ContentDialog
        {
            Title = "Restore preview",
            Content = new ScrollViewer { Content = list, MaxHeight = 420 },
            PrimaryButtonText = $"Restore {actionable} file(s)",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            var count = _services.Engine.Undo(name: Query, rule: SelectedRule,
                ext: ExtFilter, minSize: MinSize, maxSize: MaxSize,
                onConflict: conflict);
            Report(InfoBarSeverity.Success, $"Restored {count} file(s).");
        }
        catch (Exception ex)
        {
            Report(InfoBarSeverity.Error, ex.Message);
        }
        Reload();
    }

    // -- backup / export / import ---------------------------------------

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _services.Engine.BackupJournal();
            if (path is null)
            {
                Report(InfoBarSeverity.Informational, "There is no journal to back up.");
            }
            else
            {
                Report(InfoBarSeverity.Success, $"Journal backed up to {path}.");
            }
        }
        catch (Exception ex)
        {
            Report(InfoBarSeverity.Error, ex.Message);
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (App.RootWindow is null)
        {
            return;
        }
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedStartLocation =
                    Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
                SuggestedFileName = "funnel-journal",
            };
            picker.FileTypeChoices.Add("Journal log", new List<string> { ".log" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.RootWindow));
            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }
            var count = _services.Engine.ExportJournal(file.Path);
            Report(InfoBarSeverity.Success, $"Exported {count} journal entr(ies).");
        }
        catch (Exception ex)
        {
            Report(InfoBarSeverity.Error, ex.Message);
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (App.RootWindow is null)
        {
            return;
        }
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                SuggestedStartLocation =
                    Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
            };
            picker.FileTypeFilter.Add(".log");
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.RootWindow));
            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }
            var added = _services.Engine.ImportJournal(file.Path);
            Report(InfoBarSeverity.Success, $"Imported {added} journal entr(ies).");
            Reload();
        }
        catch (Exception ex)
        {
            Report(InfoBarSeverity.Error, ex.Message);
        }
    }

    private void Report(InfoBarSeverity severity, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();
}
