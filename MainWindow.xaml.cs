using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GnuSorter;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ContentFrame.Navigate(typeof(Views.FoldersView));
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            Type? pageType = tag switch
            {
                "FoldersView" => typeof(Views.FoldersView),
                "RulesView" => typeof(Views.RulesView),
                "ActivityView" => typeof(Views.ActivityView),
                _ => null
            };

            if (pageType != null)
            {
                ContentFrame.Navigate(pageType);
            }
        }
    }
}
