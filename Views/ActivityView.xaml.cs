using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using GnuSorter.Services;
using GnuSorter.ViewModels;

namespace GnuSorter.Views;

public sealed partial class ActivityView : Page
{
    private readonly AppServices _services = App.Services;
    private readonly ObservableCollection<ActivityItemViewModel> _items = new();

    public ActivityView()
    {
        InitializeComponent();
        ActivityList.ItemsSource = _items;
        Loaded += (_, _) => Reload();
    }

    private void Reload()
    {
        _items.Clear();

        var records = _services.Engine.Records();

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

        EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UndoButton.IsEnabled = _items.Count > 0;
        UndoSweepButton.IsEnabled = records.Count > 0;
        Footnote.Text = $"{_items.Count} move(s) in the journal.";
    }

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
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.Message = ex.Message;
            StatusBar.IsOpen = true;
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();
}
