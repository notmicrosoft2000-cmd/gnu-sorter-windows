using CommunityToolkit.Mvvm.ComponentModel;

namespace GnuSorter.ViewModels;

/// <summary>One watched folder as shown in the Folders list.</summary>
public sealed partial class RootItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _path = "";

    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private string _subtitle = "";

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private bool _isCustom;

    /// <summary>Set while the list is being refreshed so toggles don't re-save.</summary>
    [ObservableProperty]
    private bool _isLoading;
}
