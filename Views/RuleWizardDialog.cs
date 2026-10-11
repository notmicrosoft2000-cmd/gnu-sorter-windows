using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using GnuSorter.Models;
using GnuSorter.Services;

namespace GnuSorter.Views;

/// <summary>
/// Six friendly steps, from "what are we tidying" to a proven rule. Mirrors the
/// Linux <c>RuleWizard</c> so the two apps feel the same:
///
///   1 what to tidy · 2 what to catch · 3 where it goes · 4 fine-tune ·
///   5 priority &amp; scope · 6 review with proof.
///
/// The last step shows the rule in plain English and animates the folder's real
/// files flowing into the destination (or dimming when they don't match). The
/// dialog is built in code; <see cref="CreatedRule"/> is null when the user
/// cancels.
/// </summary>
public sealed class RuleWizardDialog : ContentDialog
{
    private static readonly string[] StepTitles =
    {
        "What are we tidying?",
        "Show me what to catch",
        "Where should they go?",
        "Fine-tune (optional)",
        "Priority & scope",
        "Review with proof",
    };

    private static readonly string[] NameModeKeys =
        { "none", "contains", "starts", "ends", "regex" };

    private static readonly (string Key, string Title, string Sub, string Glyph, string[] Exts)[] Recipes =
    {
        ("images", "Images", "Photos, art and pictures", "\uE91B",
            new[] { "jpg", "jpeg", "png", "gif", "webp", "heic", "bmp", "tiff", "svg", "raw" }),
        ("video", "Video", "Movies and clips", "\uE714",
            new[] { "mp4", "mkv", "mov", "avi", "webm", "wmv", "flv", "m4v", "mpg" }),
        ("audio", "Audio", "Music and sound", "\uE8D6",
            new[] { "mp3", "wav", "flac", "ogg", "m4a", "aac", "wma", "opus" }),
        ("documents", "Documents", "Docs, PDFs and notes", "\uE8A5",
            new[] { "pdf", "doc", "docx", "txt", "rtf", "odt", "md", "tex",
                    "xls", "xlsx", "csv", "ppt", "pptx" }),
        ("archives", "Archives", "Zips and bundles", "\uE8B7",
            new[] { "zip", "tar", "gz", "bz2", "xz", "7z", "rar", "tgz" }),
        ("code", "Code", "Source and scripts", "\uE943",
            new[] { "py", "js", "ts", "java", "c", "cpp", "h", "cs", "go", "rs",
                    "rb", "sh", "ps1", "html", "css", "json", "yaml", "yml", "sql" }),
    };

    private readonly AppServices _services;
    private readonly string? _root;
    private readonly List<SortRule> _existing;

    private readonly List<string> _exts = new();
    private readonly List<Panel> _panels = new();
    private int _step;
    private string _recipe = "";
    private string _nameMode = "none";
    private string _nameText = "";
    private string _folder = "";
    private List<string>? _files;
    private int _insertIndex;

    // Persistent controls referenced across steps.
    private TextBlock _stepLabel = new();
    private TextBlock _error = new();
    private Border _bodyHost = new();
    private ScrollViewer _scroller = new();
    private Button _backButton = new();
    private Button _nextButton = new();

    private TextBlock _countLabel = new();
    private AutoSuggestBox _extBox = new();
    private Grid _chipHost = new();
    private Grid _filesHost = new();
    private RadioButtons _nameModes = new();
    private TextBox _nameBox = new();

    private TextBox _folderBox = new();
    private TextBlock _folderHint = new();
    private TextBlock _treeLabel = new();

    private TextBox _minBox = new();
    private TextBox _maxBox = new();
    private TextBox _daysBox = new();
    private TextBox _excludeBox = new();
    private ToggleSwitch _enabledSwitch = new();
    private ToggleSwitch _matchAllSwitch = new();

    private StackPanel _priorityHost = new();
    private TextBlock _priorityWarn = new();

    private TextBlock _reviewName = new();
    private TextBlock _reviewSentence = new();
    private TextBlock _reviewPosition = new();
    private StackPanel _animHost = new();

    /// <summary>The finished rule, or null when the wizard was cancelled.</summary>
    public SortRule? CreatedRule { get; private set; }

    /// <summary>Where the new rule should sit among the folder's rules.</summary>
    public int InsertIndex { get; private set; }

    public RuleWizardDialog(AppServices services, string? root, IReadOnlyList<SortRule> existing)
    {
        _services = services;
        _root = root;
        _existing = existing.Select(r => r.Clone()).ToList();
        _insertIndex = _existing.Count;

        Title = "Add a rule";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.None;

        // The default ContentDialog is narrow; widen it for the wizard.
        Resources["ContentDialogMaxWidth"] = 900.0;
        Resources["ContentDialogMinWidth"] = 560.0;
        Resources["ContentDialogMaxHeight"] = 760.0;

        BuildChrome();
        BuildPanels();
        ShowStep(1);
    }

    // -- chrome ----------------------------------------------------------

    private void BuildChrome()
    {
        var root = new Grid { MinWidth = 540, RowSpacing = 12 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _stepLabel = new TextBlock { Opacity = 0.7, FontSize = 12 };
        Grid.SetRow(_stepLabel, 0);

        _bodyHost = new Border();
        _scroller = new ScrollViewer
        {
            Content = _bodyHost,
            MaxHeight = 500,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(_scroller, 1);

        _error = new TextBlock
        {
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(_error, 2);

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _backButton = new Button { Content = "Back" };
        _backButton.Click += (_, _) => ShowStep(_step - 1);
        Grid.SetColumn(_backButton, 0);

        _nextButton = new Button { Content = "Next" };
        if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var accent)
            && accent is Style style)
        {
            _nextButton.Style = style;
        }
        _nextButton.Click += Next_Click;
        Grid.SetColumn(_nextButton, 2);

        footer.Children.Add(_backButton);
        footer.Children.Add(_nextButton);
        Grid.SetRow(footer, 3);

        root.Children.Add(_stepLabel);
        root.Children.Add(_scroller);
        root.Children.Add(_error);
        root.Children.Add(footer);
        Content = root;
    }

    private void BuildPanels()
    {
        _panels.Add(BuildStep1());
        _panels.Add(BuildStep2());
        _panels.Add(BuildStep3());
        _panels.Add(BuildStep4());
        _panels.Add(BuildStep5());
        _panels.Add(BuildStep6());
    }

    private void ShowStep(int n)
    {
        _step = Math.Clamp(n, 1, 6);
        Title = StepTitles[_step - 1];
        _stepLabel.Text = $"Step {_step} of 6";
        _error.Visibility = Visibility.Collapsed;
        _bodyHost.Child = _panels[_step - 1];
        _backButton.IsEnabled = _step > 1;
        _nextButton.Content = _step == 6 ? "Create rule" : "Next";
        OnEnterStep(_step);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step >= 6)
        {
            TryCreate();
            return;
        }
        if (_step == 1 && _recipe.Length == 0)
        {
            _recipe = "custom";
        }
        ShowStep(_step + 1);
    }

    private void OnEnterStep(int step)
    {
        switch (step)
        {
            case 2:
                RefreshChips();
                RefreshFiles();
                RefreshCount();
                break;
            case 3:
                FolderChanged();
                break;
            case 5:
                RefreshPriority();
                break;
            case 6:
                RefreshReview();
                break;
        }
    }

    private void TryCreate()
    {
        var safe = SortEngine.SafeCategoryName(_folder);
        if (string.IsNullOrEmpty(safe))
        {
            ShowError("Give the rule a folder name (letters, numbers and dashes).");
            return;
        }
        if (!HasMatcher())
        {
            ShowError("Add at least one extension or a name match.");
            return;
        }
        if (!RegexOk())
        {
            ShowError("That name pattern isn't a valid regular expression.");
            return;
        }
        var rule = BuildRule();
        rule.Name = safe;
        CreatedRule = rule;
        InsertIndex = Math.Clamp(_insertIndex, 0, _existing.Count);
        Hide();
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
    }

    // -- step 1: what are we tidying -------------------------------------

    private Panel BuildStep1()
    {
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(Heading(StepTitles[0]));
        panel.Children.Add(Dim($"Starting in {RootName()}. Pick a starting point — "
            + "you can change every detail next."));

        var cards = new Grid();
        var items = new List<UIElement>();
        foreach (var recipe in Recipes)
        {
            var button = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12),
                MinHeight = 88,
            };
            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(new FontIcon { Glyph = recipe.Glyph, FontSize = 22, Opacity = 0.85 });
            stack.Children.Add(new TextBlock { Text = recipe.Title });
            stack.Children.Add(new TextBlock
            {
                Text = recipe.Sub,
                Opacity = 0.7,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            });
            button.Content = stack;
            var key = recipe.Key;
            var title = recipe.Title;
            var exts = recipe.Exts;
            button.Click += (_, _) => StartRecipe(key, title, exts);
            items.Add(button);
        }
        var custom = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12),
            MinHeight = 88,
        };
        var customStack = new StackPanel { Spacing = 2 };
        customStack.Children.Add(new FontIcon { Glyph = "\uE713", FontSize = 22, Opacity = 0.85 });
        customStack.Children.Add(new TextBlock { Text = "Custom" });
        customStack.Children.Add(new TextBlock
        {
            Text = "Start from scratch",
            Opacity = 0.7,
            FontSize = 12,
        });
        custom.Content = customStack;
        custom.Click += (_, _) => StartRecipe("custom", "Custom", Array.Empty<string>());
        items.Add(custom);

        WrapInto(cards, items, 3, 8, 8);
        panel.Children.Add(cards);
        return panel;
    }

    private void StartRecipe(string key, string title, string[] exts)
    {
        _recipe = key;
        _exts.Clear();
        if (key != "custom")
        {
            foreach (var ext in exts)
            {
                _exts.Add(ext);
            }
        }
        if (string.IsNullOrEmpty(_folder))
        {
            _folder = SuggestFolder(title, exts);
            _folderBox.Text = _folder;
        }
        ShowStep(2);
    }

    private string SuggestFolder(string title, string[] exts)
    {
        if (exts.Length > 0)
        {
            foreach (var rule in _existing)
            {
                if (rule.Extensions.Any(e =>
                        string.Equals(e.TrimStart('.'), exts[0], StringComparison.OrdinalIgnoreCase)))
                {
                    return rule.Name;
                }
            }
            return title;
        }
        var text = _nameText.Trim();
        if (Regex.IsMatch(text, "screenshot|capture|scree", RegexOptions.IgnoreCase))
        {
            return "Screenshots";
        }
        return text.Length > 0 ? char.ToUpperInvariant(text[0]) + text[1..] : "";
    }

    // -- step 2: what to catch -------------------------------------------

    private Panel BuildStep2()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Heading(StepTitles[1]));
        _countLabel = Dim("");
        panel.Children.Add(_countLabel);

        _extBox = new AutoSuggestBox
        {
            PlaceholderText = "Add extension (type to search)",
            QueryIcon = new SymbolIcon(Symbol.Find),
        };
        _extBox.ItemsSource = KnownExts();
        _extBox.TextChanged += ExtBox_TextChanged;
        _extBox.QuerySubmitted += ExtBox_QuerySubmitted;
        _extBox.SuggestionChosen += ExtBox_SuggestionChosen;
        panel.Children.Add(_extBox);

        _chipHost = new Grid();
        panel.Children.Add(_chipHost);

        panel.Children.Add(new TextBlock { Text = "Files already here", FontSize = 13 });
        panel.Children.Add(Dim("Tap a file to catch everything with that extension."));
        _filesHost = new Grid();
        panel.Children.Add(_filesHost);

        panel.Children.Add(new TextBlock { Text = "…or match the file name", FontSize = 13 });
        _nameModes = new RadioButtons { MaxColumns = 5 };
        _nameModes.Items.Add("doesn't matter");
        _nameModes.Items.Add("contains");
        _nameModes.Items.Add("starts with");
        _nameModes.Items.Add("ends with");
        _nameModes.Items.Add("regex");
        _nameModes.SelectedIndex = 0;
        _nameModes.SelectionChanged += (_, _) =>
        {
            var index = Math.Clamp(_nameModes.SelectedIndex, 0, NameModeKeys.Length - 1);
            _nameMode = NameModeKeys[index];
            RefreshCount();
        };
        panel.Children.Add(_nameModes);

        _nameBox = new TextBox { PlaceholderText = "screenshot" };
        _nameBox.TextChanged += (_, _) =>
        {
            _nameText = _nameBox.Text;
            RefreshCount();
        };
        panel.Children.Add(_nameBox);
        return panel;
    }

    private void ExtBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }
        var query = (sender.Text ?? "").Trim().TrimStart('.').ToLowerInvariant();
        sender.ItemsSource = KnownExts()
            .Where(e => e.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void ExtBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        AddExtension(args.QueryText);

    private void ExtBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string ext)
        {
            AddExtension(ext);
        }
    }

    private void AddExtension(string text)
    {
        foreach (var raw in text.Split(
                     new[] { ',', ';', ' ', '\t', '\r', '\n' },
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var ext = raw.TrimStart('.').ToLowerInvariant();
            if (ext.Length > 0 && !_exts.Any(x => string.Equals(x, ext, StringComparison.OrdinalIgnoreCase)))
            {
                _exts.Add(ext);
            }
        }
        _extBox.Text = "";
        _extBox.ItemsSource = KnownExts();
        RefreshChips();
        RefreshCount();
    }

    private void RefreshChips()
    {
        if (_chipHost is null)
        {
            return;
        }
        var items = new List<UIElement>();
        if (_exts.Count == 0)
        {
            items.Add(Dim("No extensions yet — add some above."));
        }
        else
        {
            foreach (var ext in _exts.ToList())
            {
                var captured = ext;
                var chip = new Button { Content = "." + captured, Padding = new Thickness(10, 3, 10, 3) };
                ToolTipService.SetToolTip(chip, "Remove ." + captured);
                chip.Click += (_, _) =>
                {
                    _exts.RemoveAll(x => string.Equals(x, captured, StringComparison.OrdinalIgnoreCase));
                    RefreshChips();
                    RefreshCount();
                };
                items.Add(chip);
            }
        }
        WrapInto(_chipHost, items, 5, 6, 6);
    }

    private void RefreshFiles()
    {
        if (_filesHost is null)
        {
            return;
        }
        var names = Scan();
        var items = new List<UIElement>();
        if (names.Count == 0)
        {
            items.Add(Dim("This folder is empty — add extensions above."));
        }
        else
        {
            var freq = ExtFrequency();
            var ranked = names
                .OrderByDescending(n => freq.TryGetValue(ExtensionOf(n), out var c) ? c : 0)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Take(48);
            foreach (var name in ranked)
            {
                var ext = ExtensionOf(name);
                var toggle = new ToggleButton
                {
                    Content = name,
                    IsChecked = ext.Length > 0
                        && _exts.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                };
                ToolTipService.SetToolTip(toggle, ext.Length > 0 ? "catch ." + ext : "no extension");
                toggle.Click += (_, _) =>
                {
                    if (ext.Length == 0)
                    {
                        return;
                    }
                    if (toggle.IsChecked == true)
                    {
                        if (!_exts.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)))
                        {
                            _exts.Add(ext);
                        }
                    }
                    else
                    {
                        _exts.RemoveAll(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
                    }
                    RefreshChips();
                    RefreshCount();
                };
                items.Add(toggle);
            }
        }
        WrapInto(_filesHost, items, 2, 6, 4);
    }

    private void RefreshCount()
    {
        if (_countLabel is null)
        {
            return;
        }
        if (!RegexOk())
        {
            _countLabel.Text = "That regex isn't valid yet — check the name pattern.";
            return;
        }
        if (!HasMatcher())
        {
            _countLabel.Text = "Nothing selected yet — pick extensions or a name match.";
            return;
        }
        var (caught, total) = Counts();
        _countLabel.Text = total == 0
            ? "No files here to preview, but this rule is ready."
            : $"This catches {caught} of {total} files in this folder.";
    }

    private (int Caught, int Total) Counts()
    {
        var names = Scan();
        if (names.Count == 0)
        {
            return (0, 0);
        }
        var rule = BuildRule();
        var folder = SortEngine.SafeCategoryName(rule.Name) ?? "Wizard";
        rule.Name = folder;
        try
        {
            var map = _services.Engine.Classify(names, SortEngine.Compile(new[] { rule }));
            return (names.Count(n => map.TryGetValue(n, out var dest) && dest == folder), names.Count);
        }
        catch (Exception)
        {
            return (0, names.Count);
        }
    }

    // -- step 3: where should they go ------------------------------------

    private Panel BuildStep3()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Heading(StepTitles[2]));
        panel.Children.Add(Dim("A new folder is created inside the watched folder the "
            + "first time something matches."));

        var destinations = _existing
            .Select(r => r.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        if (destinations.Count > 0)
        {
            var host = new Grid();
            var items = new List<UIElement>();
            foreach (var name in destinations)
            {
                var captured = name;
                var button = new Button { Content = captured + "/", Padding = new Thickness(10, 3, 10, 3) };
                button.Click += (_, _) => _folderBox.Text = captured;
                items.Add(button);
            }
            WrapInto(host, items, 4, 6, 6);
            panel.Children.Add(host);
        }

        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _folderBox = new TextBox
        {
            Header = "Destination folder",
            PlaceholderText = "Photos",
            Text = _folder,
        };
        _folderBox.TextChanged += (_, _) => FolderChanged();
        var browse = new Button { Content = "Browse…", VerticalAlignment = VerticalAlignment.Bottom };
        browse.Click += Browse_Click;
        Grid.SetColumn(_folderBox, 0);
        Grid.SetColumn(browse, 1);
        row.Children.Add(_folderBox);
        row.Children.Add(browse);
        panel.Children.Add(row);

        _folderHint = Dim("");
        panel.Children.Add(_folderHint);

        panel.Children.Add(new TextBlock { Text = "Where things live", FontSize = 13 });
        _treeLabel = Dim("");
        panel.Children.Add(_treeLabel);
        return panel;
    }

    private void FolderChanged()
    {
        _folder = _folderBox.Text.Trim();
        var safe = SortEngine.SafeCategoryName(_folder);
        if (_folder.Length == 0)
        {
            _folderHint.Text = "Type a name, pick a suggestion, or Browse…";
        }
        else if (safe is null)
        {
            _folderHint.Text = "That name can't be used for a folder — try letters, numbers and dashes.";
        }
        else
        {
            _folderHint.Text = $"Files will go to {RootName()}/{safe}/";
        }
        RefreshTree();
    }

    private void RefreshTree()
    {
        if (_treeLabel is null)
        {
            return;
        }
        var lines = new List<string> { RootName() + "/" };
        var shown = false;
        foreach (var name in _existing
                     .Select(r => r.Name)
                     .Where(n => !string.IsNullOrWhiteSpace(n))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var isNew = string.Equals(name, _folder, StringComparison.OrdinalIgnoreCase);
            shown |= isNew;
            lines.Add("    " + name + "/" + (isNew ? "  \u2190 new" : ""));
        }
        if (_folder.Length > 0 && !shown)
        {
            lines.Add("    " + _folder + "/  \u2190 new");
        }
        _treeLabel.Text = string.Join("\n", lines);
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (App.RootWindow is null)
            {
                return;
            }
            var picker = new Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads,
            };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.RootWindow));
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                _folderBox.Text = RelativeDestination(folder.Path);
            }
        }
        catch (Exception)
        {
            // The picker is a nicety; typing the name still works.
        }
    }

    private string RelativeDestination(string picked)
    {
        try
        {
            var full = Path.GetFullPath(picked)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.IsNullOrEmpty(_root))
            {
                var rootFull = Path.GetFullPath(_root)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (full.StartsWith(rootFull + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetRelativePath(_root, full).Replace('\\', '/');
                }
            }
            var name = Path.GetFileName(full);
            return SortEngine.SafeCategoryName(name) ?? name;
        }
        catch (Exception)
        {
            return Path.GetFileName(picked);
        }
    }

    // -- step 4: fine-tune -----------------------------------------------

    private Panel BuildStep4()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Heading(StepTitles[3]));
        panel.Children.Add(Dim("All optional — skip anything you don't need."));

        _minBox = new TextBox { Header = "At least this many MB" };
        _maxBox = new TextBox { Header = "At most this many MB" };
        _daysBox = new TextBox { Header = "Only files modified in the last N days" };
        _excludeBox = new TextBox
        {
            Header = "Never match when the name matches (regex, optional)",
            PlaceholderText = "keep|draft",
        };
        _enabledSwitch = new ToggleSwitch
        {
            Header = "Rule enabled",
            OnContent = "On",
            OffContent = "Paused",
            IsOn = true,
        };
        _matchAllSwitch = new ToggleSwitch
        {
            Header = "Require every condition",
            OnContent = "All (AND)",
            OffContent = "Any (OR)",
            IsOn = false,
        };

        foreach (var box in new[] { _minBox, _maxBox, _daysBox })
        {
            box.TextChanged += (_, _) => RefreshCount();
        }

        panel.Children.Add(_minBox);
        panel.Children.Add(_maxBox);
        panel.Children.Add(_daysBox);
        panel.Children.Add(_excludeBox);
        panel.Children.Add(_enabledSwitch);
        panel.Children.Add(_matchAllSwitch);
        return panel;
    }

    // -- step 5: priority & scope ----------------------------------------

    private Panel BuildStep5()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Heading(StepTitles[4]));
        panel.Children.Add(Dim("Rules are tried top to bottom; the first one that matches "
            + "wins. Move your new rule up to give it priority."));

        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var up = new Button { Content = "Move up" };
        up.Click += (_, _) =>
        {
            _insertIndex = Math.Max(0, _insertIndex - 1);
            RefreshPriority();
        };
        var down = new Button { Content = "Move down" };
        down.Click += (_, _) =>
        {
            _insertIndex = Math.Min(_existing.Count, _insertIndex + 1);
            RefreshPriority();
        };
        nav.Children.Add(up);
        nav.Children.Add(down);
        panel.Children.Add(nav);

        _priorityHost = new StackPanel { Spacing = 2 };
        panel.Children.Add(_priorityHost);
        _priorityWarn = Dim("");
        panel.Children.Add(_priorityWarn);
        return panel;
    }

    private void RefreshPriority()
    {
        if (_priorityHost is null)
        {
            return;
        }
        var newRule = BuildRule();
        _insertIndex = Math.Clamp(_insertIndex, 0, _existing.Count);

        _priorityHost.Children.Clear();
        var position = 1;
        for (var i = 0; i <= _existing.Count; i++)
        {
            if (i == _insertIndex)
            {
                _priorityHost.Children.Add(PriorityRow(position++, newRule, isNew: true));
            }
            if (i < _existing.Count)
            {
                _priorityHost.Children.Add(PriorityRow(position++, _existing[i], isNew: false));
            }
        }
        UpdatePriorityWarning(newRule);
    }

    private static Grid PriorityRow(int position, SortRule rule, bool isNew)
    {
        var row = new Grid { ColumnSpacing = 8, Padding = new Thickness(4, 6, 4, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var stack = new StackPanel();
        var title = string.IsNullOrWhiteSpace(rule.Name) ? "(new rule)" : rule.Name;
        stack.Children.Add(new TextBlock { Text = $"{position}.  {title}" });
        var described = RuleText.Describe(rule);
        stack.Children.Add(new TextBlock
        {
            Text = described.Length > 0 ? described : "no matchers",
            Opacity = 0.7,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(stack, 0);

        var tag = new TextBlock
        {
            Text = isNew ? "NEW" : "existing",
            Opacity = 0.6,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(tag, 1);

        row.Children.Add(stack);
        row.Children.Add(tag);
        return row;
    }

    private void UpdatePriorityWarning(SortRule newRule)
    {
        var names = Scan().Take(200).ToList();
        if (names.Count == 0)
        {
            _priorityWarn.Text = "No files to check against yet.";
            return;
        }
        var folder = SortEngine.SafeCategoryName(newRule.Name) ?? "Wizard";
        newRule.Name = folder;

        var raw = new List<SortRule>();
        for (var i = 0; i <= _existing.Count; i++)
        {
            if (i == _insertIndex)
            {
                raw.Add(newRule);
            }
            if (i < _existing.Count)
            {
                raw.Add(_existing[i]);
            }
        }
        try
        {
            var alone = _services.Engine.Classify(names, SortEngine.Compile(new[] { newRule }));
            var together = _services.Engine.Classify(names, SortEngine.Compile(raw));
            var mine = names.Where(n => alone.TryGetValue(n, out var d) && d == folder).ToList();
            var stolen = mine.Where(n => !(together.TryGetValue(n, out var d) && d == folder)).ToList();
            if (mine.Count == 0)
            {
                _priorityWarn.Text = "No files in this folder match yet "
                    + "(fine if it's for files yet to come).";
            }
            else if (stolen.Count > 0)
            {
                _priorityWarn.Text = $"Heads up: {stolen.Count} of {mine.Count} matching "
                    + "files would be caught by an earlier rule first.";
            }
            else
            {
                _priorityWarn.Text = $"Good — all {mine.Count} matching files reach this rule.";
            }
        }
        catch (Exception)
        {
            _priorityWarn.Text = "";
        }
    }

    // -- step 6: review with proof ---------------------------------------

    private Panel BuildStep6()
    {
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(Heading(StepTitles[5]));

        var card = new StackPanel { Spacing = 8 };
        _reviewName = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _reviewSentence = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _reviewPosition = new TextBlock { TextWrapping = TextWrapping.Wrap };
        card.Children.Add(Field("Destination", _reviewName));
        card.Children.Add(Field("Reads as", _reviewSentence));
        card.Children.Add(Field("Priority", _reviewPosition));
        panel.Children.Add(card);

        _animHost = new StackPanel { Spacing = 6 };
        panel.Children.Add(_animHost);
        return panel;
    }

    private void RefreshReview()
    {
        var rule = BuildRule();
        var folder = SortEngine.SafeCategoryName(rule.Name) ?? "(choose a folder)";
        _reviewName.Text = folder + "/";
        var described = RuleText.Describe(rule);
        _reviewSentence.Text = described.Length > 0 ? described : "\u2014";
        _insertIndex = Math.Clamp(_insertIndex, 0, _existing.Count);
        _reviewPosition.Text = $"position {_insertIndex + 1} of {_existing.Count + 1} "
            + "(first match wins)";

        _animHost.Children.Clear();
        _animHost.Children.Add(BuildPreview(folder));
    }

    /// <summary>
    /// The custom "files flow into the folder" animation. Real file names are
    /// drawn on the left; the ones this rule catches travel into the destination
    /// while the rest dim. Honours the Windows "show animations" setting by
    /// jumping straight to the settled frame.
    /// </summary>
    private FrameworkElement BuildPreview(string folder)
    {
        var names = Scan();
        var rule = BuildRule();
        rule.Name = folder;
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (names.Count > 0 && folder != "(choose a folder)")
        {
            try
            {
                var map = _services.Engine.Classify(names, SortEngine.Compile(new[] { rule }));
                foreach (var name in names)
                {
                    if (map.TryGetValue(name, out var dest) && dest == folder)
                    {
                        matched.Add(name);
                    }
                }
            }
            catch (Exception)
            {
                // An invalid half-built rule simply shows nothing matched.
            }
        }

        var shown = matched.Take(6).ToList();
        foreach (var name in names)
        {
            if (shown.Count >= 8)
            {
                break;
            }
            if (!shown.Contains(name))
            {
                shown.Add(name);
            }
        }

        const double width = 500;
        const double height = 190;
        const double chipW = 190;
        const double chipH = 22;
        const double gap = 4;
        const double destW = 140;
        const double destH = 78;
        var top = Math.Max(6, (height - shown.Count * (chipH + gap)) / 2);
        var destX = width - destW - 12;
        var destY = (height - destH) / 2;

        var canvas = new Canvas { Width = width, Height = height };

        var folderBox = new Border
        {
            Width = destW,
            Height = destH,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 250, 201, 71)),
        };
        Canvas.SetLeft(folderBox, destX);
        Canvas.SetTop(folderBox, destY);
        canvas.Children.Add(folderBox);

        var folderLabel = new TextBlock
        {
            Text = folder + "/",
            Width = destW,
            Margin = new Thickness(8),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
        };
        Canvas.SetLeft(folderLabel, destX);
        Canvas.SetTop(folderLabel, destY + 10);
        canvas.Children.Add(folderLabel);

        var storyboard = new Storyboard();
        var moves = new List<(TranslateTransform Transform, double Dx, double Dy)>();
        for (var i = 0; i < shown.Count; i++)
        {
            var name = shown[i];
            var isHit = matched.Contains(name);
            var transform = new TranslateTransform();
            var chip = new Border
            {
                Width = chipW,
                Height = chipH,
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(28, 77, 140, 242)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(217, 77, 140, 242)),
                BorderThickness = new Thickness(1),
                RenderTransform = transform,
                Child = new TextBlock
                {
                    Text = name,
                    FontSize = 11,
                    Margin = new Thickness(6, 2, 6, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };
            var y0 = top + i * (chipH + gap);
            Canvas.SetLeft(chip, 8);
            Canvas.SetTop(chip, y0);
            canvas.Children.Add(chip);

            if (!isHit)
            {
                chip.Opacity = 0.35;
                continue;
            }

            var dx = destX + 28 - 8;
            var dy = destY + destH / 2 - chipH / 2 - y0;
            moves.Add((transform, dx, dy));
            storyboard.Children.Add(Animation(transform, "X", dx, 0, 1.2));
            storyboard.Children.Add(Animation(transform, "Y", dy, 0, 1.2));
            storyboard.Children.Add(Animation(chip, "Opacity", 0.0, 1.15, 0.25));
        }

        var caption = string.IsNullOrEmpty(_root)
            ? "Pick a folder with files to see a preview."
            : names.Count == 0
                ? "No files here yet — but this rule is ready."
                : $"{matched.Count} of {names.Count} files in this folder would sort here.";

        var wrap = new StackPanel { Spacing = 6 };
        wrap.Children.Add(canvas);
        wrap.Children.Add(new TextBlock
        {
            Text = caption,
            Opacity = 0.7,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });

        var animate = true;
        try
        {
            animate = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        }
        catch (Exception)
        {
            // Assume animations are fine if the setting can't be read.
        }

        canvas.Loaded += (_, _) =>
        {
            if (animate)
            {
                try
                {
                    storyboard.Begin();
                    return;
                }
                catch (Exception)
                {
                    // Fall through to the settled frame.
                }
            }
            foreach (var (transform, dx, dy) in moves)
            {
                transform.X = dx;
                transform.Y = dy;
            }
        };
        return wrap;
    }

    private static DoubleAnimation Animation(
        DependencyObject target, string property, double to, double beginSeconds, double seconds)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(TimeSpan.FromSeconds(seconds)),
            BeginTime = TimeSpan.FromSeconds(beginSeconds),
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }

    // -- rule building ---------------------------------------------------

    private SortRule BuildRule() => new()
    {
        Name = _folder.Trim(),
        Extensions = _exts
            .Select(e => e.Trim().TrimStart('.').ToLowerInvariant())
            .Where(e => e.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList(),
        MatchNameRegex = NamePattern() is { Length: > 0 } pattern ? pattern : null,
        ExcludeNameRegex = string.IsNullOrWhiteSpace(_excludeBox.Text) ? null : _excludeBox.Text.Trim(),
        MinSizeMb = ParsePositive(_minBox.Text),
        MaxSizeMb = ParsePositive(_maxBox.Text),
        ModifiedWithinDays = ParsePositive(_daysBox.Text),
        Enabled = _enabledSwitch.IsOn ? null : false,
        MatchMode = _matchAllSwitch.IsOn ? "all" : null,
    };

    private string NamePattern()
    {
        var text = _nameText.Trim();
        if (_nameMode == "none" || text.Length == 0)
        {
            return "";
        }
        return _nameMode switch
        {
            "contains" => Regex.Escape(text),
            "starts" => "^" + Regex.Escape(text),
            "ends" => Regex.Escape(text) + "$",
            "regex" => text,
            _ => "",
        };
    }

    private bool HasMatcher() => _exts.Count > 0 || NamePattern().Length > 0;

    private bool RegexOk()
    {
        if (_nameMode != "regex")
        {
            return true;
        }
        var pattern = _nameText.Trim();
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
            return false;
        }
    }

    private static double? ParsePositive(string? text)
    {
        if (double.TryParse((text ?? "").Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            return value;
        }
        return null;
    }

    // -- folder scanning -------------------------------------------------

    private List<string> Scan()
    {
        if (_files is not null)
        {
            return _files;
        }
        var files = new List<string>();
        if (!string.IsNullOrEmpty(_root) && Directory.Exists(_root))
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(_root))
                {
                    var name = Path.GetFileName(path);
                    if (!name.StartsWith('.'))
                    {
                        files.Add(name);
                    }
                }
            }
            catch (Exception)
            {
                // An unreadable folder just yields an empty preview.
            }
        }
        files.Sort(StringComparer.OrdinalIgnoreCase);
        _files = files;
        return files;
    }

    private Dictionary<string, int> ExtFrequency()
    {
        var freq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Scan())
        {
            var ext = ExtensionOf(name);
            if (ext.Length == 0)
            {
                continue;
            }
            freq[ext] = freq.TryGetValue(ext, out var count) ? count + 1 : 1;
        }
        return freq;
    }

    private List<string> KnownExts()
    {
        var known = new HashSet<string>(ExtFrequency().Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var recipe in Recipes)
        {
            foreach (var ext in recipe.Exts)
            {
                known.Add(ext);
            }
        }
        return known.OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ExtensionOf(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot >= 0 && dot < name.Length - 1 ? name[(dot + 1)..].ToLowerInvariant() : "";
    }

    private string RootName()
    {
        if (string.IsNullOrEmpty(_root))
        {
            return "this folder";
        }
        var name = Path.GetFileName(
            _root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? _root : name;
    }

    // -- small layout helpers --------------------------------------------

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = 20,
        TextWrapping = TextWrapping.Wrap,
    };

    private static TextBlock Dim(string text) => new()
    {
        Text = text,
        Opacity = 0.7,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
    };

    private static StackPanel Field(string label, UIElement control)
    {
        var box = new StackPanel { Spacing = 2 };
        box.Children.Add(new TextBlock { Text = label, Opacity = 0.6, FontSize = 12 });
        box.Children.Add(control);
        return box;
    }

    private static void WrapInto(Grid host, IReadOnlyList<UIElement> items, int columns,
        double hgap, double vgap)
    {
        host.Children.Clear();
        host.ColumnDefinitions.Clear();
        host.RowDefinitions.Clear();
        host.RowSpacing = vgap;
        host.ColumnSpacing = hgap;
        for (var c = 0; c < columns; c++)
        {
            host.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
            });
        }
        var rows = (items.Count + columns - 1) / columns;
        for (var r = 0; r < rows; r++)
        {
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (var i = 0; i < items.Count; i++)
        {
            var element = (FrameworkElement)items[i];
            Grid.SetRow(element, i / columns);
            Grid.SetColumn(element, i % columns);
            host.Children.Add(element);
        }
    }
}
