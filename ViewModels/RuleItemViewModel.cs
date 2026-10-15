using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using GnuSorter.Models;

namespace GnuSorter.ViewModels;

/// <summary>One row in the rules page — a single rule in priority order.</summary>
public sealed partial class RuleItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _subtitle = "";

    /// <summary>1-based priority shown at the start of the row.</summary>
    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    private bool _isCustom;

    [ObservableProperty]
    private bool _isNameMatch;

    /// <summary>Whether the rule currently participates in matching.</summary>
    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>Overlap warning for this rule, or null when it is fine.</summary>
    [ObservableProperty]
    private string? _warning;

    public SortRule Rule { get; init; } = new();

    /// <summary>Accent colour for the row, or null when the rule has none.</summary>
    public SolidColorBrush? ColorBrush { get; init; }

    public bool HasColor => ColorBrush is not null;

    partial void OnWarningChanged(string? value) => OnPropertyChanged(nameof(HasWarning));

    public bool HasWarning => !string.IsNullOrEmpty(Warning);
}
