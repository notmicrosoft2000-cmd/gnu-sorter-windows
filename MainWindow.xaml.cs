using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using GnuSorter.Services;
using GnuSorter.Views;

namespace GnuSorter;

public sealed partial class MainWindow : Window
{
    private readonly AppServices _services = App.Services;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Funnel";
        var settings = _services.Config.LoadSettings();
        // App.RootWindow is assigned only after this constructor returns, so
        // ThemeService.Apply (which targets App.RootWindow) would be a no-op
        // here. Apply the saved scheme to this window's own content instead.
        if (Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = ThemeService.ToElementTheme(settings.ColorScheme);
        }
        AccentService.Apply(settings.Accent);

        try
        {
            var icon = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Icon.ico");
            AppWindow.SetIcon(icon);
        }
        catch
        {
            // The taskbar icon is cosmetic; ignore failures.
        }

        _services.Watcher.Sorted += OnSorted;
        ContentFrame.Navigate(typeof(FoldersView));
        UpdateWatchUi();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsView));
            return;
        }

        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            Type? page = tag switch
            {
                "FoldersView" => typeof(FoldersView),
                "RulesView" => typeof(RulesView),
                "ActivityView" => typeof(ActivityView),
                _ => null,
            };

            if (page is not null)
            {
                ContentFrame.Navigate(page);
            }
        }
    }

    private void WatchToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_services.Watcher.IsRunning)
        {
            _services.Watcher.Stop();
            StatusText.Text = "Watching stopped.";
            UpdateWatchUi();
            return;
        }

        var roots = _services.EnabledRoots();
        if (roots.Count == 0)
        {
            StatusText.Text = "Add and enable a folder before watching.";
            UpdateWatchUi();
            return;
        }

        _services.Watcher.Start(roots);
        StatusText.Text = $"Watching {roots.Count} folder(s).";
        UpdateWatchUi();
    }

    private void OnSorted(string path, string category)
    {
        DispatcherQueue.TryEnqueue(() =>
            StatusText.Text = $"Sorted {System.IO.Path.GetFileName(path)} → {category}");
    }

    private void UpdateWatchUi()
    {
        var running = _services.Watcher.IsRunning;
        WatchToggle.IsChecked = running;
        WatchText.Text = running ? "Watching" : "Watch folders";
        WatchLabel.Text = running ? "Live" : "";
    }
}
