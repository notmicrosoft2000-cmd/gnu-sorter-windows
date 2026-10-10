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
}
