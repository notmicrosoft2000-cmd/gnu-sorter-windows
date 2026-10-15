using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
    private readonly ObservableCollection<RuleItemViewModel> _items = new();

    private List<SortRule> _templateRules = new();

    /// <summary>The rules for the selected folder, in true priority order.</summary>
    private List<SortRule> _currentRules = new();

    private string? _currentRoot;
    private bool _custom;
    private bool _ready;

    /// <summary>The rule a row menu is acting on (set when the menu opens).</summary>
    private RuleItemViewModel? _menuItem;

    public RulesView()
    {
        InitializeComponent();
        FoldersPane.ItemsSource = _folders;
        RulesList.ItemsSource = _items;
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
            HeaderText.Text = "Default rules (all folders)";
            RulesHint.Text = "No folders yet. These rules apply to every folder you add.";
        }
        else
        {
            var (rules, custom) = _services.Config.RulesFor(_currentRoot);
            _custom = custom;
            _currentRules = rules.Select(r => r.Clone()).ToList();

            var name = PathDisplay.NameFor(_currentRoot);
            HeaderText.Text = custom ? $"Rules for {name}" : $"Rules for {name} — using defaults";
            RulesHint.Text = "Rules are checked top to bottom — the first match wins.";
        }

        UpdateInheritUi();
        RefreshRules();
    }

    private void UpdateInheritUi()
    {
        if (_currentRoot is null)
        {
            InheritStatus.Text = "No folder selected.";
            CustomizeButton.Visibility = Visibility.Collapsed;
        }
        else if (_custom)
        {
            InheritStatus.Text = "This folder has its own rules.";
            CustomizeButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            InheritStatus.Text = "Using the default rules.";
            CustomizeButton.Visibility = Visibility.Visible;
        }

        SetEditingEnabled(_currentRoot is null || _custom);
        ResetRulesItem.IsEnabled = _currentRoot is not null && _custom;
    }

    private void SetEditingEnabled(bool enabled)
    {
        AddRuleButton.IsEnabled = enabled;
        RulesList.IsEnabled = enabled;
    }

    // -- rule list -------------------------------------------------------

    private void RefreshRules()
    {
        var query = SearchBox.Text?.Trim() ?? "";
        var counts = SafeRuleCounts();
        var byRule = WarningsByRule(_currentRules);

        _items.Clear();
        for (var i = 0; i < _currentRules.Count; i++)
        {
            var rule = _currentRules[i];
            var subtitle = RuleText.Describe(rule);
            if (counts.TryGetValue(rule.Name, out var count) && count > 0)
            {
                subtitle += $"  \u00b7  {count} sorted";
            }
            if (rule.Enabled is false)
            {
                subtitle += "  \u00b7  paused";
            }
            if (query.Length > 0
                && !rule.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !subtitle.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            byRule.TryGetValue(rule.Name, out var warning);
            _items.Add(new RuleItemViewModel
            {
                Index = i + 1,
                Name = rule.Name,
                Subtitle = subtitle,
                IsCustom = _custom,
                IsNameMatch = rule.IsNameMatch,
                IsEnabled = rule.Enabled is not false,
                Rule = rule,
                Warning = warning,
                ColorBrush = ParseColor(rule.Color),
            });
        }

        // Reordering only makes sense when the whole list is visible.
        RulesList.CanReorderItems = query.Length == 0;
        UpdateWarningsBar(_currentRules);
    }

    private void UpdateWarningsBar(List<SortRule> rules)
    {
        var warnings = SafeOverlaps(rules);
        if (warnings.Count == 0)
        {
            WarningsBar.IsOpen = false;
            return;
        }
        var first = warnings[0].Message;
        WarningsBar.Message = warnings.Count == 1
            ? first
            : $"{warnings.Count} rules need a look: {first}";
        WarningsBar.IsOpen = true;
    }

    private static Dictionary<string, string> WarningsByRule(List<SortRule> rules)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var warning in SafeOverlaps(rules))
        {
            if (string.IsNullOrEmpty(warning.Rule))
            {
                continue;
            }
            result[warning.Rule] = result.TryGetValue(warning.Rule, out var existing)
                ? $"{existing} {warning.Message}"
                : warning.Message;
        }
        return result;
    }

    private static List<RuleWarning> SafeOverlaps(List<SortRule> rules)
    {
        try
        {
            return SortEngine.Overlaps(rules);
        }
        catch (Exception)
        {
            return new List<RuleWarning>();
        }
    }

    private Dictionary<string, int> SafeRuleCounts()
    {
        try
        {
            return _services.Engine.RuleCounts();
        }
        catch (Exception)
        {
            return new Dictionary<string, int>();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshRules();

    // -- rule interactions ----------------------------------------------

    private void RuleMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RuleItemViewModel item } element)
        {
            return;
        }
        _menuItem = item;
        if (Resources["RuleMenuFlyout"] is MenuFlyout flyout)
        {
            flyout.ShowAt(element);
        }
    }

    private void RuleToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_ready
            || sender is not ToggleSwitch { DataContext: RuleItemViewModel item } toggle)
        {
            return;
        }
        // Ignore the initial binding push; only react to a real user change.
        if (toggle.IsOn == item.IsEnabled)
        {
            return;
        }
        item.IsEnabled = toggle.IsOn;
        item.Rule.Enabled = toggle.IsOn ? null : false;
        Persist();
        // Rebuild after the toggle finishes so the row's subtitle updates.
        DispatcherQueue.TryEnqueue(RefreshRules);
    }

    private void RulesList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is ListView { SelectedItem: RuleItemViewModel item })
        {
            _ = EditRuleAsync(item);
        }
    }

    private void RulesList_DragItemsCompleted(ListViewBase sender,
        DragItemsCompletedEventArgs args)
    {
        if (!_ready)
        {
            return;
        }
        // The ListView reordered the bound collection; mirror that order onto
        // the rule list (this is the priority order) and persist it.
        _currentRules = _items.Select(vm => vm.Rule).ToList();
        for (var i = 0; i < _items.Count; i++)
        {
            _items[i].Index = i + 1;
        }
        Persist();
    }

    private async void AddRule_Click(SplitButton sender, SplitButtonClickEventArgs args)
    {
        if (!AddRuleButton.IsEnabled)
        {
            return;
        }

        var wizard = new RuleWizardDialog(_services, _currentRoot, _currentRules)
        {
            XamlRoot = XamlRoot,
        };
        await wizard.ShowAsync();

        var rule = wizard.CreatedRule;
        if (rule is null)
        {
            return;
        }

        // Replace a same-named rule, then place the new one at the chosen
        // priority (first match wins).
        _currentRules.RemoveAll(r =>
            string.Equals(r.Name, rule.Name, StringComparison.OrdinalIgnoreCase));
        var index = Math.Clamp(wizard.InsertIndex, 0, _currentRules.Count);
        _currentRules.Insert(index, rule);
        Persist();
        LoadSelection();

        StatusBar.Severity = InfoBarSeverity.Success;
        StatusBar.Message = $"Added rule: {rule.Name}/";
        StatusBar.IsOpen = true;
    }

    private void EditRule_Click(object sender, RoutedEventArgs e)
    {
        if (_menuItem is not null)
        {
            _ = EditRuleAsync(_menuItem);
        }
    }

    private async Task EditRuleAsync(RuleItemViewModel item)
    {
        var edited = await ShowRuleDialogAsync(item.Rule);
        if (edited is null)
        {
            return;
        }

        item.Rule.Name = edited.Name;
        item.Rule.Extensions = edited.Extensions;
        item.Rule.MatchNameRegex = edited.MatchNameRegex;
        item.Rule.ExcludeNameRegex = edited.ExcludeNameRegex;
        item.Rule.MinSizeMb = edited.MinSizeMb;
        item.Rule.MaxSizeMb = edited.MaxSizeMb;
        item.Rule.ModifiedWithinDays = edited.ModifiedWithinDays;
        item.Rule.Enabled = edited.Enabled;
        item.Rule.MatchAll = edited.MatchAll;
        item.Rule.Icon = edited.Icon;
        item.Rule.Color = edited.Color;
        item.Rule.MatchMode = edited.MatchMode;
        Persist();
        RefreshRules();
    }

    private void DuplicateRule_Click(object sender, RoutedEventArgs e)
    {
        if (_menuItem is not { } item)
        {
            return;
        }

        var copy = item.Rule.Clone();
        copy.Name = UniqueName(copy.Name);
        var index = _currentRules.IndexOf(item.Rule);
        if (index >= 0)
        {
            _currentRules.Insert(index + 1, copy);
        }
        else
        {
            _currentRules.Add(copy);
        }
        Persist();
        RefreshRules();
    }

    private void MoveRuleUp_Click(object sender, RoutedEventArgs e) => MoveRule(-1);

    private void MoveRuleDown_Click(object sender, RoutedEventArgs e) => MoveRule(+1);

    private void MoveRule(int delta)
    {
        if (_menuItem is not { } item)
        {
            return;
        }
        var index = _currentRules.IndexOf(item.Rule);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _currentRules.Count)
        {
            return;
        }
        (_currentRules[index], _currentRules[target]) =
            (_currentRules[target], _currentRules[index]);
        Persist();
        RefreshRules();
    }

    private string UniqueName(string baseName)
    {
        var candidate = $"{baseName} copy";
        var n = 2;
        while (_currentRules.Any(r =>
                   string.Equals(r.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName} copy {n++}";
        }
        return candidate;
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (_menuItem is not { } item)
        {
            return;
        }

        _currentRules.RemoveAll(r => ReferenceEquals(r, item.Rule));
        Persist();
        RefreshRules();
    }

    private void Customize_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRoot is null || _custom)
        {
            return;
        }
        _services.Config.DetachRules(_currentRoot);
        LoadSelection();
    }

    // -- tester / preview ------------------------------------------------

    private void TesterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var name = TesterBox.Text?.Trim() ?? "";
        if (name.Length == 0)
        {
            TesterResult.Text = "Type a name to preview";
            return;
        }

        string dest;
        try
        {
            dest = _services.Engine.Categorize(name, SortEngine.Compile(_currentRules));
        }
        catch (Exception)
        {
            TesterResult.Text = "Could not test that name.";
            return;
        }

        if (dest == "Needs-review")
        {
            TesterResult.Text = $"{name} \u2192 Needs-review \u2014 no enabled rule matches";
            return;
        }

        var index = _currentRules.FindIndex(r =>
            string.Equals(r.Name, dest, StringComparison.OrdinalIgnoreCase));
        var pos = index >= 0 ? $"  (rule #{index + 1})" : "";
        TesterResult.Text = $"{name} \u2192 {dest}/{pos}";
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRoot is null)
        {
            PreviewResult.Text = "Select a folder first.";
            return;
        }

        Dictionary<string, int> plan;
        try
        {
            plan = _services.Engine.PreviewPlan(
                new[] { _currentRoot }, SortEngine.Compile(_currentRules));
        }
        catch (Exception)
        {
            PreviewResult.Text = "Could not preview this folder.";
            return;
        }

        if (plan.Count == 0)
        {
            PreviewResult.Text = "Nothing to sort right now.";
            return;
        }

        var parts = plan.OrderByDescending(kv => kv.Value)
            .Take(6)
            .Select(kv => $"{kv.Value} \u2192 {kv.Key}");
        PreviewResult.Text = string.Join("  \u00b7  ", parts);
    }

    // -- import / export -------------------------------------------------

    private async void ExportRules_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedFileName = "funnel-rules",
        };
        picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
        if (!InitializePicker(picker))
        {
            return;
        }

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(_currentRules, ConfigStore.SerializerOptions);
            await Windows.Storage.FileIO.WriteTextAsync(file, json);
            StatusBar.Severity = InfoBarSeverity.Success;
            StatusBar.Message = $"Exported {_currentRules.Count} rule(s).";
            StatusBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.Message = $"Export failed: {ex.Message}";
            StatusBar.IsOpen = true;
        }
    }

    private async void ImportRules_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        if (!InitializePicker(picker))
        {
            return;
        }

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        List<SortRule>? imported;
        try
        {
            var text = await Windows.Storage.FileIO.ReadTextAsync(file);
            imported = ParseRulePack(text);
        }
        catch (Exception ex)
        {
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.Message = $"Could not read that file: {ex.Message}";
            StatusBar.IsOpen = true;
            return;
        }

        if (imported is null || imported.Count == 0)
        {
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.Message = "No rules found in that file.";
            StatusBar.IsOpen = true;
            return;
        }

        var choice = await ShowImportChoiceAsync(imported.Count);
        if (choice == ContentDialogResult.None)
        {
            return;
        }

        if (choice == ContentDialogResult.Primary)
        {
            _currentRules = imported.Select(r => r.Clone()).ToList();
        }
        else
        {
            foreach (var rule in imported)
            {
                _currentRules.RemoveAll(r => string.Equals(
                    r.Name, rule.Name, StringComparison.OrdinalIgnoreCase));
                _currentRules.Add(rule.Clone());
            }
        }

        if (_currentRoot is not null && !_custom)
        {
            _services.Config.DetachRules(_currentRoot);
            _custom = true;
            UpdateInheritUi();
        }

        Persist();
        RefreshRules();
        StatusBar.Severity = InfoBarSeverity.Success;
        StatusBar.Message = $"Imported {imported.Count} rule(s).";
        StatusBar.IsOpen = true;
    }

    private static List<SortRule>? ParseRulePack(string text)
    {
        using var doc = JsonDocument.Parse(text);
        JsonElement array;
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            array = doc.RootElement;
        }
        else if (doc.RootElement.ValueKind == JsonValueKind.Object
                 && doc.RootElement.TryGetProperty("rules", out var wrapped)
                 && wrapped.ValueKind == JsonValueKind.Array)
        {
            array = wrapped;
        }
        else
        {
            return null;
        }
        return JsonSerializer.Deserialize<List<SortRule>>(
            array.GetRawText(), ConfigStore.SerializerOptions);
    }

    private async Task<ContentDialogResult> ShowImportChoiceAsync(int count)
    {
        var dialog = new ContentDialog
        {
            Title = "Import rules",
            Content = $"{count} rule(s) found. Replace this folder's rules, or "
                      + "append them (same-named rules are overwritten)?",
            PrimaryButtonText = "Replace",
            SecondaryButtonText = "Append",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Secondary,
            XamlRoot = XamlRoot,
        };
        return await dialog.ShowAsync();
    }

    private void ResetRules_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRoot is null)
        {
            return;
        }
        if (!_custom)
        {
            StatusBar.Severity = InfoBarSeverity.Informational;
            StatusBar.Message = "Already using the default rules.";
            StatusBar.IsOpen = true;
            return;
        }
        _services.Config.AttachRules(_currentRoot);
        LoadSelection();
        StatusBar.Severity = InfoBarSeverity.Success;
        StatusBar.Message = "Reverted to the default rules.";
        StatusBar.IsOpen = true;
    }

    private static bool InitializePicker(object picker)
    {
        if (App.RootWindow is null)
        {
            return false;
        }
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.RootWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        return true;
    }

    // -- edit dialog -----------------------------------------------------

    private async Task<SortRule?> ShowRuleDialogAsync(SortRule? existing)
    {
        var nameBox = new TextBox
        {
            Header = "Destination folder",
            Text = existing?.Name ?? "",
            PlaceholderText = "Photos",
        };
        var browseButton = new Button
        {
            Content = "Choose\u2026",
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        var nameRow = new Grid { ColumnSpacing = 8 };
        nameRow.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(nameBox, 0);
        Grid.SetColumn(browseButton, 1);
        nameRow.Children.Add(nameBox);
        nameRow.Children.Add(browseButton);
        browseButton.Click += async (_, _) =>
        {
            var picked = await PickFolderAsync();
            if (picked is not null)
            {
                nameBox.Text = RelativeDestination(picked);
            }
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
        var excludeBox = new TextBox
        {
            Header = "Never match when the name matches (regex, optional)",
            Text = existing?.ExcludeNameRegex ?? "",
            PlaceholderText = "keep|draft",
        };
        var minBox = new TextBox
        {
            Header = "Minimum size in MB (optional)",
            Text = existing?.MinSizeMb?.ToString(CultureInfo.InvariantCulture) ?? "",
        };
        var maxBox = new TextBox
        {
            Header = "Maximum size in MB (optional)",
            Text = existing?.MaxSizeMb?.ToString(CultureInfo.InvariantCulture) ?? "",
        };
        var daysBox = new TextBox
        {
            Header = "Only files modified in the last N days (optional)",
            Text = existing?.ModifiedWithinDays?.ToString(CultureInfo.InvariantCulture) ?? "",
        };
        var enabledSwitch = new ToggleSwitch
        {
            Header = "Enabled",
            OnContent = "On",
            OffContent = "Paused",
            IsOn = existing?.Enabled is not false,
        };
        var matchAllSwitch = new ToggleSwitch
        {
            Header = "Require every condition",
            OnContent = "All (AND)",
            OffContent = "Any (OR)",
            IsOn = string.Equals(existing?.MatchMode, "all",
                StringComparison.OrdinalIgnoreCase),
        };
        var colorBox = new TextBox
        {
            Header = "Accent colour for this rule (hex, optional)",
            Text = existing?.Color ?? "",
            PlaceholderText = "#3b82f6",
        };
        var error = new TextBlock
        {
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap,
        };

        var panel = new StackPanel { Spacing = 12, MinWidth = 380 };
        panel.Children.Add(nameRow);
        panel.Children.Add(extBox);
        panel.Children.Add(regexBox);
        panel.Children.Add(excludeBox);
        panel.Children.Add(minBox);
        panel.Children.Add(maxBox);
        panel.Children.Add(daysBox);
        panel.Children.Add(enabledSwitch);
        panel.Children.Add(matchAllSwitch);
        panel.Children.Add(colorBox);
        panel.Children.Add(error);

        var dialog = new ContentDialog
        {
            Title = existing is null ? "Add rule" : "Edit rule",
            Content = new ScrollViewer { Content = panel, MaxHeight = 520 },
            PrimaryButtonText = existing is null ? "Add" : "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        double? minMb = null;
        double? maxMb = null;
        double? days = null;

        dialog.PrimaryButtonClick += (sender, args) =>
        {
            var name = nameBox.Text.Trim();
            var extensions = ParseExtensions(extBox.Text);
            var regex = regexBox.Text.Trim();
            var exclude = excludeBox.Text.Trim();

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
            if (!ValidRegex(regex, error, args, "name pattern")
                || !ValidRegex(exclude, error, args, "exclude pattern"))
            {
                return;
            }
            if (!TryParseOptional(minBox.Text, out minMb))
            {
                Fail(error, "Minimum size must be a positive number.", args);
                return;
            }
            if (!TryParseOptional(maxBox.Text, out maxMb))
            {
                Fail(error, "Maximum size must be a positive number.", args);
                return;
            }
            if (!TryParseOptional(daysBox.Text, out days))
            {
                Fail(error, "Days must be a positive number.", args);
                return;
            }
            if (minMb is not null && maxMb is not null && minMb > maxMb)
            {
                Fail(error, "Minimum size cannot be larger than the maximum.", args);
                return;
            }
            if (colorBox.Text.Trim().Length > 0
                && ParseColor(colorBox.Text) is null)
            {
                Fail(error, "Accent colour must be a hex value like #3b82f6.", args);
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
            ExcludeNameRegex = string.IsNullOrWhiteSpace(excludeBox.Text) ? null : excludeBox.Text.Trim(),
            MinSizeMb = minMb,
            MaxSizeMb = maxMb,
            ModifiedWithinDays = days,
            Enabled = enabledSwitch.IsOn ? (bool?)null : false,
            MatchAll = existing?.MatchAll ?? false,
            Icon = existing?.Icon,
            Color = string.IsNullOrWhiteSpace(colorBox.Text)
                ? null
                : colorBox.Text.Trim(),
            MatchMode = matchAllSwitch.IsOn ? "all" : null,
        };
    }

    private static bool ValidRegex(string pattern, TextBlock error,
        ContentDialogButtonClickEventArgs args, string label)
    {
        if (pattern.Length == 0)
        {
            return true;
        }
        try
        {
            _ = new Regex(pattern);
            return true;
        }
        catch (ArgumentException)
        {
            Fail(error, $"That {label} is not a valid regular expression.", args);
            return false;
        }
    }

    private static bool TryParseOptional(string text, out double? value)
    {
        value = null;
        text = text.Trim();
        if (text.Length == 0)
        {
            return true;
        }
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture,
                out var parsed) && parsed > 0)
        {
            value = parsed;
            return true;
        }
        return false;
    }

    /// <summary>Parse "#rgb" / "#rrggbb" into a brush; null when empty or invalid.</summary>
    private static SolidColorBrush? ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var text = value.Trim().TrimStart('#');
        try
        {
            byte r, g, b;
            if (text.Length == 3)
            {
                r = Convert.ToByte(new string(text[0], 2), 16);
                g = Convert.ToByte(new string(text[1], 2), 16);
                b = Convert.ToByte(new string(text[2], 2), 16);
            }
            else if (text.Length >= 6)
            {
                r = Convert.ToByte(text.Substring(0, 2), 16);
                g = Convert.ToByte(text.Substring(2, 2), 16);
                b = Convert.ToByte(text.Substring(4, 2), 16);
            }
            else
            {
                return null;
            }
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<string?> PickFolderAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads,
            };
            picker.FileTypeFilter.Add("*");
            if (!InitializePicker(picker))
            {
                return null;
            }
            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string RelativeDestination(string pickedPath)
    {
        var pickedFull = Path.GetFullPath(pickedPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (_currentRoot is not null)
        {
            var rootFull = Path.GetFullPath(_currentRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (pickedFull.StartsWith(rootFull + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetRelativePath(_currentRoot, pickedFull)
                    .Replace('\\', '/');
            }
        }
        return Path.GetFileName(pickedFull);
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

    // -- presets / advanced ---------------------------------------------

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
            UpdateInheritUi();
        }

        if (rules.Count == 0)
        {
            // "Empty" preset keeps the folder custom but clears its rules.
            _currentRules = new List<SortRule>();
        }
        else
        {
            _currentRules = rules.Select(r => r.Clone()).ToList();
        }
        Persist();
        RefreshRules();

        StatusBar.Severity = InfoBarSeverity.Success;
        StatusBar.Message = $"Applied preset: {_currentRules.Count} rule(s).";
        StatusBar.IsOpen = true;
    }

    private async void Advanced_Click(object sender, RoutedEventArgs e)
    {
        if (!RulesList.IsEnabled)
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
                + "and optional match_name_regex, exclude_name_regex, min_size_mb, "
                + "max_size_mb, modified_within_days, enabled and match_all fields. "
                + "UI-only icon and color hints are preserved.",
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
