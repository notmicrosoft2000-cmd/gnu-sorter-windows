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

        // Newest first.
        foreach (var record in _services.Engine.History().AsEnumerable().Reverse())
        {
            _items.Add(new ActivityItemViewModel
            {
                FileName = record.FileName,
                Detail = $"{record.Source}  →  {record.Destination}",
                Time = record.Timestamp,
            });
        }

        EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UndoButton.IsEnabled = _items.Count > 0;
        Footnote.Text = $"{_items.Count} move(s) in the journal.";
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var count = _services.Engine.Undo(dryRun: false, limit: 1);
            StatusBar.Severity = count > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Informational;
            StatusBar.Message = count > 0
                ? "Restored 1 file."
                : "The last move could not be reversed (the file changed).";
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
