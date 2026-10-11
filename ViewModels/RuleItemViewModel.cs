using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
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

    /// <summary>Accent colour for the row, or null when the rule has none.</summary>
    public SolidColorBrush? ColorBrush { get; init; }

    public bool HasColor => ColorBrush is not null;
}
