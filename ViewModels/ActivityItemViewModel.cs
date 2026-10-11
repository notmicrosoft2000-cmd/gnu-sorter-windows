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

    /// <summary>Rule/category that caught the file (journal v2), if known.</summary>
    [ObservableProperty]
    private string _rule = "";

    /// <summary>Operation id shared by every move of the same sweep.</summary>
    [ObservableProperty]
    private string _batch = "";
}
