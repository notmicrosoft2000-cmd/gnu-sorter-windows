using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using GnuSorter.Services;
using GnuSorter.ViewModels;

namespace GnuSorter.Views;

public sealed partial class SettingsView : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsView()
    {
        ViewModel = new SettingsViewModel(App.Services);
        InitializeComponent();
        Loaded += (_, _) => ViewModel.Reload();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ViewModel.Save();

            if (ViewModel.StartOnLogin)
            {
                StartupService.Enable();
            }
            else
            {
                StartupService.Disable();
            }

            ShowStatus(InfoBarSeverity.Success, "Settings saved.");
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, ex.Message);
        }
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Reload();
        ShowStatus(InfoBarSeverity.Informational, "Reloaded from disk.");
    }

    private void OpenConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{ViewModel.ConfigPath}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, ex.Message);
        }
    }

    private void ShowStatus(InfoBarSeverity severity, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        UpdateStatus.Text = "Checking…";
        try
        {
            var current = UpdateService.CurrentVersion();
            var result = await App.Services.Updates.CheckAsync();
            if (result is null || string.IsNullOrEmpty(result.Value.DownloadUrl))
            {
                UpdateStatus.Text = "Could not check for updates right now "
                                    + "(offline, or no release available yet).";
                return;
            }

            var (latest, url) = result.Value;
            if (latest <= current)
            {
                UpdateStatus.Text = $"You are up to date (version {current}).";
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "Update available",
                Content = $"GNU Sorter {latest} is available (you have {current}). "
                          + "Download and install it now? The app will restart "
                          + "automatically once the new file is in place.",
                PrimaryButtonText = "Update",
                CloseButtonText = "Later",
                XamlRoot = this.XamlRoot,
            };

            var choice = await dialog.ShowAsync();
            if (choice != ContentDialogResult.Primary)
            {
                return;
            }

            UpdateStatus.Text = "Downloading…";
            var (ok, path, error) = await App.Services.Updates.DownloadAsync(url);
            if (!ok || string.IsNullOrEmpty(path))
            {
                UpdateStatus.Text = $"Download failed: {error?.Message}";
                return;
            }

            var (installed, message) = App.Services.Updates.Install(path);
            UpdateStatus.Text = message;
        }
        catch (Exception ex)
        {
            UpdateStatus.Text = $"Update check failed: {ex.Message}";
        }
        finally
        {
            UpdateButton.IsEnabled = true;
        }
    }
}
