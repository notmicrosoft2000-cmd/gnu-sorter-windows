using CommunityToolkit.Mvvm.ComponentModel;

namespace GnuSorter.ViewModels;

/// <summary>One journal row shown on the Activity page.</summary>
public sealed partial class ActivityItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _fileName = "";

    [ObservableProperty]
    private string _detail = "";

    [ObservableProperty]
    private string _time = "";
}
