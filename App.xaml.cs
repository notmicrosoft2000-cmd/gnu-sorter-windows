using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using GnuSorter.Services;

namespace GnuSorter;

public partial class App : Application
{
    /// <summary>Process-wide services (config, engine, watcher).</summary>
    public static AppServices Services { get; private set; } = null!;

    /// <summary>The top-level window; needed to parent file pickers in WinUI 3.</summary>
    public static Window? RootWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        Services = new AppServices();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        RootWindow = new MainWindow();
        RootWindow.Activate();
        _ = CheckForUpdatesSilentlyAsync();
    }

    private async Task CheckForUpdatesSilentlyAsync()
    {
        try
        {
            var result = await Services.Updates.CheckAsync();
            if (result is null ||
                result.Value.Latest <= UpdateService.CurrentVersion() ||
                string.IsNullOrEmpty(result.Value.DownloadUrl))
            {
                return;
            }

            var (latest, url) = result.Value;
            var dialog = new ContentDialog
            {
                Title = "Update available",
                Content = $"Funnel {latest} is available (you have "
                          + $"{UpdateService.CurrentVersion()}). Install it now? "
                          + "The app will restart automatically.",
                PrimaryButtonText = "Update",
                CloseButtonText = "Later",
                XamlRoot = RootWindow?.Content?.XamlRoot,
            };

            var choice = await dialog.ShowAsync();
            if (choice != ContentDialogResult.Primary)
            {
                return;
            }

            var (ok, path, error) = await Services.Updates.DownloadAsync(url);
            if (!ok || string.IsNullOrEmpty(path))
            {
                return; // updates never crash the app; the Settings page can retry
            }
            Services.Updates.Install(path);
        }
        catch (Exception)
        {
            // Ignored: a startup check must never take the app down.
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Keep the app alive; the exception is surfaced in the debugger output.
        System.Diagnostics.Debug.WriteLine(e.Exception);
    }
}
