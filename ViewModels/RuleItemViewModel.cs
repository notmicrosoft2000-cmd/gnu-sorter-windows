using CommunityToolkit.Mvvm.ComponentModel;
using GnuSorter.Models;

namespace GnuSorter.ViewModels;

/// <summary>One rule row (either an extension rule or a name-match rule).</summary>
public sealed partial class RuleItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _subtitle = "";

    [ObservableProperty]
    private bool _isCustom;

    [ObservableProperty]
    private bool _isNameMatch;

    [ObservableProperty]
    private bool _isVisible = true;

    public SortRule Rule { get; init; } = new();
}
