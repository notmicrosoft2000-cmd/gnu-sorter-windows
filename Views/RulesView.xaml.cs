using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using GnuSorter.Helpers;
using GnuSorter.Models;
using GnuSorter.Services;
using GnuSorter.ViewModels;

namespace GnuSorter.Views;

public sealed partial class RulesView : Page
{
    private readonly AppServices _services = App.Services;
    private readonly ObservableCollection<RootItemViewModel> _folders = new();
    private readonly ObservableCollection<RuleItemViewModel> _extItems = new();
    private readonly ObservableCollection<RuleItemViewModel> _nameItems = new();

    private List<SortRule> _templateRules = new();
    private List<SortRule> _currentRules = new();
    private string? _currentRoot;
    private bool _custom;
    private bool _loadingToggle;
    private bool _ready;

    public RulesView()
    {
        InitializeComponent();
        FoldersPane.ItemsSource = _folders;
        ExtRules.ItemsSource = _extItems;
        NameRules.ItemsSource = _nameItems;
        BuildPresetMenu();
        Loaded += (_, _) => ReloadFolders();
    }

    // -- folder list -----------------------------------------------------

    private void ReloadFolders()
    {
        _templateRules = _services.Config.LoadRules();

        _ready = false;
        _folders.Clear();
        foreach (var root in _services.Config.LoadRoots())
        {
            _folders.Add(new RootItemViewModel
            {
                Path = root.Path,
                DisplayName = PathDisplay.NameFor(root.Path),
                Subtitle = root.Path,
                Enabled = root.Enabled,
                IsCustom = _services.Config.IsCustom(root.Path),
            });
        }
        _ready = true;

        if (_folders.Count > 0)
        {
            FoldersPane.SelectedIndex = 0;
        }
        else
        {
            _currentRoot = null;
            LoadSelection();
        }
    }

    private void FoldersPane_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || FoldersPane.SelectedItem is not RootItemViewModel item)
        {
            return;
        }
        _currentRoot = item.Path;
        LoadSelection();
    }

    private void LoadSelection()
    {
        if (_currentRoot is null)
        {
            _custom = false;
            _currentRules = _templateRules.Select(r => r.Clone()).ToList();
            InheritToggle.IsEnabled = false;
            SetEditingEnabled(true);
            HeaderText.Text = "Default rules (all folders)";
            RulesHint.Text = "No folders yet. These rules apply to every folder you add.";
        }
        else
        {
            var (rules, custom) = _services.Config.RulesFor(_currentRoot);
            _custom = custom;
            _currentRules = rules.Select(r => r.Clone()).ToList();
            InheritToggle.IsEnabled = true;
            _loadingToggle = true;
            InheritToggle.IsOn = !custom;
            _loadingToggle = false;
            SetEditingEnabled(!custom);

            var name = PathDisplay.NameFor(_currentRoot);
            HeaderText.Text = custom ? $"Rules for {name}" : $"Rules for {name} — using defaults";
            RulesHint.Text = custom
                ? "These rules apply only to this folder."
                : "This folder uses the default rules. Turn off \u201cUse default rules\u201d to customize.";
        }

        RefreshRules();
    }

    private void SetEditingEnabled(bool enabled)
    {
        AddRuleButton.IsEnabled = enabled;
        AdvancedButton.IsEnabled = enabled;
        PresetButton.IsEnabled = enabled;
        ExtRules.IsEnabled = enabled;
        NameRules.IsEnabled = enabled;
    }

    // -- rule list -------------------------------------------------------

    private void RefreshRules()
    {
        var query = SearchBox.Text?.Trim() ?? "";
        _extItems.Clear();
        _nameItems.Clear();

        foreach (var rule in _currentRules)
        {
            var subtitle = DescribeRule(rule);
            if (query.Length > 0
                && !rule.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !subtitle.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var vm = new RuleItemViewModel
            {
                Name = rule.Name,
                Subtitle = subtitle,
                IsCustom = _custom,
                IsNameMatch = rule.IsNameMatch,
                Rule = rule,
            };

            if (rule.IsNameMatch)
            {
                _nameItems.Add(vm);
            }
            else
            {
                _extItems.Add(vm);
            }
        }
    }

    private static string DescribeRule(SortRule rule)
    {
        var parts = new List<string>();
        if (rule.Extensions.Count > 0)
        {
            parts.Add(string.Join(", ", rule.Extensions.Select(e => "." + e.TrimStart('.'))));
        }
        if (rule.IsNameMatch)
        {
            parts.Add($"matches /{rule.MatchNameRegex}/");
        }
        return parts.Count == 0 ? "no matchers" : string.Join("  ·  ", parts);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshRules();

    // -- rule editing ----------------------------------------------------

    private async void AddRule_Click(object sender, RoutedEventArgs e)
    {
        if (!AddRuleButton.IsEnabled)
        {
            return;
        }

        var rule = await ShowRuleDialogAsync(null);
        if (rule is null)
        {
            return;
        }

        _currentRules.Add(rule);
        Persist();
        RefreshRules();
    }

    private async void EditRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RuleItemViewModel item })
        {
            return;
        }

        var edited = await ShowRuleDialogAsync(item.Rule);
        if (edited is null)
        {
            return;
        }

        item.Rule.Name = edited.Name;
        item.Rule.Extensions = edited.Extensions;
        item.Rule.MatchNameRegex = edited.MatchNameRegex;
        Persist();
        RefreshRules();
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RuleItemViewModel item })
        {
            return;
        }

        _currentRules.RemoveAll(r => ReferenceEquals(r, item.Rule));
        Persist();
        RefreshRules();
    }

    private async Task<SortRule?> ShowRuleDialogAsync(SortRule? existing)
    {
        var nameBox = new TextBox
        {
            Header = "Destination folder",
            Text = existing?.Name ?? "",
            PlaceholderText = "Photos",
        };
        var extBox = new TextBox
        {
            Header = "File extensions",
            Text = existing is null ? "" : string.Join(", ", existing.Extensions),
            PlaceholderText = "jpg, png, gif",
        };
        var regexBox = new TextBox
        {
            Header = "Name matches (regex, optional)",
            Text = existing?.MatchNameRegex ?? "",
            PlaceholderText = "screenshot|capture",
        };
        var error = new TextBlock
        {
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap,
        };

        var panel = new StackPanel { Spacing = 12, MinWidth = 380 };
        panel.Children.Add(nameBox);
        panel.Children.Add(extBox);
        panel.Children.Add(regexBox);
        panel.Children.Add(error);

        var dialog = new ContentDialog
        {
            Title = existing is null ? "Add rule" : "Edit rule",
            Content = panel,
            PrimaryButtonText = existing is null ? "Add" : "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        dialog.PrimaryButtonClick += (sender, args) =>
        {
            var name = nameBox.Text.Trim();
            var extensions = ParseExtensions(extBox.Text);
            var regex = regexBox.Text.Trim();

            if (name.Length == 0)
            {
                Fail(error, "Enter a destination folder name.", args);
                return;
            }
            if (extensions.Count == 0 && regex.Length == 0)
            {
                Fail(error, "Add at least one extension or a name pattern.", args);
                return;
            }
            if (regex.Length > 0)
            {
                try
                {
                    _ = new Regex(regex);
                }
                catch (ArgumentException)
                {
                    Fail(error, "That regular expression is not valid.", args);
                }
            }
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        return new SortRule
        {
            Name = nameBox.Text.Trim(),
            Extensions = ParseExtensions(extBox.Text),
            MatchNameRegex = string.IsNullOrWhiteSpace(regexBox.Text) ? null : regexBox.Text.Trim(),
        };
    }

    private static void Fail(TextBlock error, string message, ContentDialogButtonClickEventArgs args)
    {
        error.Text = message;
        error.Visibility = Visibility.Visible;
        args.Cancel = true;
    }

    private static List<string> ParseExtensions(string text) =>
        text.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.TrimStart('.'))
            .Where(e => e.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    // -- persistence -----------------------------------------------------

    private void Persist()
    {
        if (_currentRoot is null)
        {
            _services.Config.SaveRules(_currentRules);
            _templateRules = _currentRules.Select(r => r.Clone()).ToList();
        }
        else if (_custom)
        {
            _services.Config.SaveRulesFor(_currentRoot, _currentRules);
        }

        if (_currentRoot is not null)
        {
            foreach (var folder in _folders.Where(f => SamePath(f.Path, _currentRoot)))
            {
                folder.IsCustom = _services.Config.IsCustom(_currentRoot);
            }
        }
    }

    // -- inherit / presets / advanced ------------------------------------

    private async void InheritToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingToggle || !_ready || _currentRoot is null)
        {
            return;
        }

        if (InheritToggle.IsOn)
        {
            if (!_services.Config.IsCustom(_currentRoot))
            {
                LoadSelection();
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "Use default rules?",
                Content = "This discards this folder's custom rules and follows the default rules instead.",
                PrimaryButtonText = "Use defaults",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                _loadingToggle = true;
                InheritToggle.IsOn = false;
                _loadingToggle = false;
                return;
            }

            _services.Config.AttachRules(_currentRoot);
            LoadSelection();
            return;
        }

        // Turning customization on.
        if (_services.Config.IsCustom(_currentRoot))
        {
            LoadSelection();
            return;
        }

        _services.Config.DetachRules(_currentRoot);
        LoadSelection();
    }

    private void BuildPresetMenu()
    {
        foreach (var (label, factory) in RulePresets.All)
        {
            var item = new MenuFlyoutItem { Text = label };
            item.Click += (_, _) => ApplyPreset(factory());
            PresetFlyout.Items.Add(item);
        }
    }

    private void ApplyPreset(List<SortRule> rules)
    {
        if (_currentRoot is not null && !_custom)
        {
            _services.Config.DetachRules(_currentRoot);
            _custom = true;
            SetEditingEnabled(true);
            _loadingToggle = true;
            InheritToggle.IsOn = false;
            _loadingToggle = false;
        }

        _currentRules = rules.Select(r => r.Clone()).ToList();
        Persist();
        RefreshRules();

        StatusBar.Severity = InfoBarSeverity.Success;
        StatusBar.Message = $"Applied preset: {_currentRules.Count} rule(s).";
        StatusBar.IsOpen = true;
    }

    private async void Advanced_Click(object sender, RoutedEventArgs e)
    {
        if (!AdvancedButton.IsEnabled)
        {
            return;
        }

        var box = new TextBox
        {
            Text = JsonSerializer.Serialize(_currentRules, ConfigStore.SerializerOptions),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"),
            Height = 360,
            MinWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var error = new TextBlock
        {
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap,
        };

        var panel = new StackPanel { Spacing = 12, MinWidth = 600 };
        panel.Children.Add(new TextBlock
        {
            Text = "Edit the rules as JSON. Each rule has a name, an extensions list, "
                + "and an optional match_name_regex.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
        });
        panel.Children.Add(box);
        panel.Children.Add(error);

        var dialog = new ContentDialog
        {
            Title = "Advanced rule editor",
            Content = panel,
            PrimaryButtonText = "Apply",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        List<SortRule>? parsed = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                parsed = JsonSerializer.Deserialize<List<SortRule>>(box.Text, ConfigStore.SerializerOptions)
                    ?? throw new JsonException("Expected a JSON array of rules.");
            }
            catch (JsonException ex)
            {
                error.Text = ex.Message;
                error.Visibility = Visibility.Visible;
                args.Cancel = true;
            }
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var rules = parsed;
        if (rules is null)
        {
            return;
        }

        _currentRules = rules.Select(r => r.Clone()).ToList();
        Persist();
        RefreshRules();
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(ConfigStore.NormalizeKey(a), ConfigStore.NormalizeKey(b),
            StringComparison.OrdinalIgnoreCase);
}
