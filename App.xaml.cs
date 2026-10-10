using Microsoft.UI.Xaml;
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
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Keep the app alive; the exception is surfaced in the debugger output.
        System.Diagnostics.Debug.WriteLine(e.Exception);
    }
}
