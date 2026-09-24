using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ComboBox = System.Windows.Controls.ComboBox;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;
using Orientation = System.Windows.Controls.Orientation;
using Brush = System.Windows.Media.Brush;

namespace LightflowStudio;

/// <summary>One field with explicit alternatives, never an arbitrary Boolean subtree.</summary>
internal sealed class BrowserFilterRowEditor : Grid
{
    private readonly ComboBox _field = new() { DisplayMemberPath = "Name", MinWidth = 135 };
    private readonly ContentControl _operator = new() { Margin = new(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top };
    private readonly ContentControl _value = new() { VerticalAlignment = VerticalAlignment.Top };
    private bool _inputValid = true;
    private Action? _refreshSuggestions;
    private readonly List<BrowserFilterPredicate> _alternatives;
    private IReadOnlyList<BrowserGridTile> _tiles;
    private readonly Func<BrowserFilterField, bool> _fieldAvailable;
    private readonly Action _changed;
    private bool _initializing;
    public BrowserFilterField Field => ((BrowserFilterDescriptor)_field.SelectedItem).Field;
    public IReadOnlyList<BrowserFilterPredicate> Predicates => _alternatives.ToArray();

    public BrowserFilterRowEditor(BrowserFilterField field, IEnumerable<BrowserFilterPredicate> alternatives,
        IReadOnlyList<BrowserGridTile> tiles, Func<BrowserFilterField, bool> fieldAvailable, Action changed, Action remove)
    {
        _tiles = tiles; _alternatives = alternatives.ToList(); _fieldAvailable = fieldAvailable; _changed = changed;
        Margin = new(0, 0, 0, 8);
        ColumnDefinitions.Add(new() { Width = new(150) });
        ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _field.VerticalAlignment = VerticalAlignment.Top;
        _initializing = true;
        _field.ItemsSource = BrowserFilterDescriptors.All.Where(d => d.Field == field || (fieldAvailable(d.Field) && d.CanAuthor(_tiles))).ToArray();
        _field.SelectedItem = BrowserFilterDescriptors.Get(field);
        _initializing = false;
        _field.DropDownOpened += (_, _) => RefreshFields();
        _field.SelectionChanged += (_, _) =>
        {
            if (_initializing || _field.SelectedItem is null) return;
            _alternatives.Clear(); BuildValue(); _changed();
        };
        AutomationProperties.SetName(_field, "Filter field");
        Add(_field, 0); Add(_operator, 1); Add(_value, 2);
        var removeButton = new Button { Content = "×", Style = (Style)FindResource("FilterRowActionStyle"), VerticalAlignment = VerticalAlignment.Top, ToolTip = "Remove filter" };
        AutomationProperties.SetName(removeButton, "Remove filter");
        removeButton.Click += (_, _) => remove(); Add(removeButton, 3);
        BuildValue();
    }
    private void Add(UIElement element, int column) { SetColumn(element, column); Children.Add(element); }
    private void RefreshFields()
    {
        var selected = Field; _initializing = true;
        _field.ItemsSource = BrowserFilterDescriptors.All.Where(d => d.Field == selected || (_fieldAvailable(d.Field) && d.CanAuthor(_tiles))).ToArray();
        _field.SelectedItem = BrowserFilterDescriptors.Get(selected); _initializing = false;
    }
    public void RefreshValues(IReadOnlyList<BrowserGridTile> tiles)
    {
        _tiles = tiles;
        if (BrowserFilterDescriptors.Get(Field).Editor == BrowserFilterEditorKind.Choices) BuildChoices();
        _refreshSuggestions?.Invoke();
    }
    private void LabelOperator(string text) => _operator.Content = new TextBlock { Text = text, Margin = new(0, 7, 0, 0) };
    private void BuildValue()
    {
        _inputValid = true; _refreshSuggestions = null;
        SetColumnSpan(_operator, 1); _value.Visibility = Visibility.Visible;
        switch (BrowserFilterDescriptors.Get(Field).Editor)
        {
            case BrowserFilterEditorKind.Structured: if (BrowserFilterDescriptors.Get(Field).PreferPresets) BuildPresets(); else BuildStructured(); break;
            case BrowserFilterEditorKind.State: BuildState(); break;
            case BrowserFilterEditorKind.Text: BuildText(); break;
            case BrowserFilterEditorKind.Rating: BuildRating(); break;
            case BrowserFilterEditorKind.DateRanges: BuildDates(); break;
            default: BuildChoices(); break;
        }
    }
    private void BuildText()
    {
        LabelOperator("contains");
        var text = new TextBox { Text = _alternatives.FirstOrDefault()?.TextValue ?? "", MinWidth = 120 };
        AutomationProperties.SetName(text, "Text to match");
        TextInputHint.SetText(text, "Text to match");
        void Update()
        {
            _alternatives.Clear();
            if (!string.IsNullOrWhiteSpace(text.Text)) _alternatives.Add(BrowserFilterPredicate.ForText(Field, text.Text.Trim()));
        }
        Update(); text.TextChanged += (_, _) => { Update(); _changed(); }; _value.Content = text;
    }
    private sealed record Comparison(BrowserNumberComparison Value, string Name);
    private void BuildRating()
    {
        var saved = _alternatives.FirstOrDefault();
        var operators = new[] { new Comparison(BrowserNumberComparison.GreaterThanOrEqual, "is at least"),
            new Comparison(BrowserNumberComparison.Equal, "is"), new Comparison(BrowserNumberComparison.LessThanOrEqual, "is at most"),
            new Comparison(BrowserNumberComparison.GreaterThan, "is greater than"), new Comparison(BrowserNumberComparison.LessThan, "is less than") };
        var comparison = new ComboBox { ItemsSource = operators, DisplayMemberPath = "Name", MinWidth = 105 };
        comparison.SelectedItem = operators.Single(o => o.Value == (saved?.Comparison ?? BrowserNumberComparison.GreaterThanOrEqual));
        var rating = new ComboBox { ItemsSource = Enumerable.Range(0, 6).Select(n => n == 0 ? "Unrated" : new string('★', n)).ToArray(), SelectedIndex = saved is null ? -1 : (int)saved.NumberValue!, MinWidth = 105 };
        AutomationProperties.SetName(comparison, "Rating operator"); AutomationProperties.SetName(rating, "Rating");
        void Update()
        {
            _alternatives.Clear();
            if (rating.SelectedIndex >= 0) _alternatives.Add(BrowserFilterPredicate.ForRating(((Comparison)comparison.SelectedItem).Value, rating.SelectedIndex));
            _changed();
        }
        comparison.SelectionChanged += (_, _) => Update(); rating.SelectionChanged += (_, _) => Update();
        _operator.Content = comparison; _value.Content = rating;
    }
    private void BuildChoices()
    {
        var choices = BrowserFilterDescriptors.Get(Field).Values(_tiles).Concat(_alternatives).Distinct().ToArray();
        var button = new ToggleButton { Style = (Style)FindResource("FilterValueToggleStyle") };
        var list = new StackPanel();
        var popup = new Popup { PlacementTarget = button, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, VerticalOffset = 4 };
        popup.Child = new Border { Background = (Brush)FindResource("CardBrush"), BorderBrush = (Brush)FindResource("BorderBrush"), BorderThickness = new(1), CornerRadius = new(6), Padding = new(6),
            Child = new ScrollViewer { Content = list, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinWidth = 200 } };
        void UpdateLabel()
        {
            LabelOperator(_alternatives.Count > 1 ? "is any of" : "is");
            button.Content = new TextBlock { Text = (_alternatives.Count == 0 ? "Select…" : string.Join(", ", _alternatives.Select(BrowserFilterDescriptors.ValueLabel))), TextWrapping = TextWrapping.Wrap };
        }
        foreach (var choice in choices)
        {
            var check = new CheckBox { Content = BrowserFilterDescriptors.ValueLabel(choice), IsChecked = _alternatives.Contains(choice), Margin = new(0, 4, 0, 4) };
            check.Click += (_, _) => { if (check.IsChecked == true) _alternatives.Add(choice); else _alternatives.Remove(choice); UpdateLabel(); _changed(); };
            list.Children.Add(check);
        }
        button.IsEnabled = choices.Length > 0;
        ((FrameworkElement)popup.Child).SetBinding(FrameworkElement.MinWidthProperty, new System.Windows.Data.Binding("ActualWidth") { Source = button });
        System.Windows.Input.KeyboardNavigation.SetTabNavigation(list, System.Windows.Input.KeyboardNavigationMode.Cycle);
        button.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is System.Windows.Input.Key.Down or System.Windows.Input.Key.F4) { button.IsChecked = true; e.Handled = true; }
        };
        list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape) { popup.IsOpen = false; button.Focus(); e.Handled = true; }
        };
        popup.Opened += (_, _) => list.Children.OfType<CheckBox>().FirstOrDefault()?.Focus();
        button.Checked += (_, _) => popup.IsOpen = true; button.Unchecked += (_, _) => popup.IsOpen = false;
        popup.Closed += (_, _) => button.IsChecked = false;
        Unloaded += (_, _) => popup.IsOpen = false;
        AutomationProperties.SetName(button, "Filter values");
        var host = new Grid(); host.Children.Add(button); host.Children.Add(popup); _value.Content = host; UpdateLabel();
    }
    private sealed record StateChoice(string Label, bool Value);
    private void BuildState()
    {
        var descriptor = BrowserFilterDescriptors.Get(Field);
        var choices = new[] { new StateChoice(descriptor.StateOperators[0], true), new StateChoice(descriptor.StateOperators[1], false) };
        var panel = new StackPanel(); var selectors = new List<ComboBox>();
        var saved = _alternatives.Select(p => p.BooleanValue).ToArray();
        if (saved.Length == 0) saved = [null];
        void Update()
        {
            _alternatives.Clear();
            foreach (var selector in selectors)
                if (selector.SelectedItem is StateChoice choice) _alternatives.Add(BrowserFilterPredicate.ForState(Field, choice.Value));
            var position = 0;
            foreach (var line in panel.Children.OfType<StackPanel>())
                foreach (var label in line.Children.OfType<TextBlock>()) label.Visibility = position++ == 0 ? Visibility.Hidden : Visibility.Visible;
            _inputValid = selectors.All(s => s.SelectedItem is StateChoice); _changed();
        }
        foreach (var state in saved)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            var selector = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Label", MinWidth = 170 };
            selector.SelectedItem = state is { } value ? choices[value ? 0 : 1] : null;
            AutomationProperties.SetName(selector, descriptor.Name + " state");
            selector.SelectionChanged += (_, _) => Update(); selectors.Add(selector); line.Children.Add(selector);
            if (saved.Length > 1)
            {
                // Captured Browser alternatives stay explicit, without offering an 'either state' value.
                line.Children.Insert(0, new TextBlock { Text = "or", Margin = new(0, 4, 6, 0), Visibility = panel.Children.Count == 0 ? Visibility.Hidden : Visibility.Visible });
                var remove = new Button { Content = "×", Style = (Style)FindResource("FilterRowActionStyle"), ToolTip = "Remove state alternative" };
                AutomationProperties.SetName(remove, "Remove state alternative"); line.Children.Add(remove);
                remove.Click += (_, _) => { selectors.Remove(selector); panel.Children.Remove(line); Update(); };
            }
            panel.Children.Add(line);
        }
        SetColumnSpan(_operator, 2); _value.Visibility = Visibility.Collapsed; _value.Content = null; _operator.Content = panel;
    }
    private sealed record ValueSuggestion(BrowserFilterPredicate? Predicate, string Label);
    private sealed record PresetChoice(BrowserFilterPredicate? Predicate, string Label, bool Custom = false);
    private sealed class PresetEntry
    {
        public ComboBox Selector { get; } = new() { DisplayMemberPath = "Label" };
        public Grid CustomPanel { get; } = new();
        public TextBox[] Boxes { get; set; } = [];
        public BrowserFilterPredicate? Value { get; set; }
        public bool Custom { get; set; }
        public bool Valid { get; set; }
        public Button Remove { get; set; } = null!;
    }
    private void BuildPresets()
    {
        var descriptor = BrowserFilterDescriptors.Get(Field); var input = descriptor.Input!;
        var panel = new StackPanel(); var entries = new List<PresetEntry>();
        var saved = _alternatives.ToArray(); var synchronizing = false;
        void Update()
        {
            _alternatives.Clear();
            foreach (var entry in entries)
            {
                if (entry.Custom)
                {
                    try { entry.Value = input.Parse(entry.Boxes.Select(b => b.Text).ToArray()); entry.Valid = true; }
                    catch (FormatException) { entry.Valid = false; entry.Value = null; }
                }
                else entry.Valid = entry.Value is not null;
                if (entry.Value is not null) _alternatives.Add(entry.Value);
                entry.Remove.Visibility = entries.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            }
            _inputValid = entries.All(e => e.Valid);
            LabelOperator(entries.Count > 1 ? input.AlternativesOperator : input.Operator); _changed();
        }
        void Refresh()
        {
            synchronizing = true;
            var values = (descriptor.Suggestions?.Invoke(_tiles) ?? descriptor.Values(_tiles))
                .Concat(entries.Where(e => e.Value is not null).Select(e => e.Value!)).Distinct().ToArray();
            foreach (var entry in entries)
            {
                var choices = new[] { new PresetChoice(null, "Select " + descriptor.Name.ToLowerInvariant() + "…") }
                    .Concat(values.Select(p => new PresetChoice(p, BrowserFilterDescriptors.ValueLabel(p))))
                    .Append(new PresetChoice(null, "Custom…", true)).ToArray();
                entry.Selector.ItemsSource = choices;
                entry.Selector.SelectedItem = entry.Custom ? choices[^1] : choices.FirstOrDefault(c => c.Predicate == entry.Value) ?? choices[0];
            }
            synchronizing = false;
        }
        void AddEntry(BrowserFilterPredicate? value)
        {
            var entry = new PresetEntry { Value = value, Valid = value is not null };
            var line = new Grid { Margin = new(0, 0, 0, 4) };
            line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            line.Children.Add(entry.Selector); line.Children.Add(entry.CustomPanel);
            entry.CustomPanel.Visibility = Visibility.Collapsed;
            AutomationProperties.SetName(entry.Selector, descriptor.Name + " value");
            var formatted = value is null ? input.Components.Select(_ => "").ToArray() : input.Format(value);
            entry.Boxes = input.Components.Select((component, i) => new TextBox { Text = formatted[i], MinWidth = 48, ToolTip = input.Help }).ToArray();
            for (var i = 0; i < entry.Boxes.Length; i++)
            {
                if (i > 0)
                {
                    entry.CustomPanel.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                    var separator = new TextBlock { Text = input.Separator, VerticalAlignment = VerticalAlignment.Center, Margin = new(3, 0, 3, 0) };
                    SetColumn(separator, entry.CustomPanel.ColumnDefinitions.Count - 1); entry.CustomPanel.Children.Add(separator);
                }
                entry.CustomPanel.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                var box = entry.Boxes[i]; SetColumn(box, entry.CustomPanel.ColumnDefinitions.Count - 1); entry.CustomPanel.Children.Add(box);
                TextInputHint.SetText(box, input.Components[i]); AutomationProperties.SetName(box, "Custom " + descriptor.Name + " " + input.Components[i]);
                box.TextChanged += (_, _) => { if (!synchronizing && entry.Custom) Update(); };
            }
            entry.CustomPanel.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var unit = new TextBlock { Text = input.Unit, VerticalAlignment = VerticalAlignment.Center, Margin = new(4, 0, 4, 0) };
            SetColumn(unit, entry.CustomPanel.ColumnDefinitions.Count - 1); entry.CustomPanel.Children.Add(unit);
            entry.CustomPanel.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var back = new Button { Content = "Presets…", Style = (Style)FindResource("FilterInlineActionStyle") };
            SetColumn(back, entry.CustomPanel.ColumnDefinitions.Count - 1); entry.CustomPanel.Children.Add(back);
            back.Click += (_, _) => { entry.Custom = false; entry.Selector.Visibility = Visibility.Visible; entry.CustomPanel.Visibility = Visibility.Collapsed; Refresh(); Update(); entry.Selector.Focus(); };
            entry.Selector.SelectionChanged += (_, _) =>
            {
                if (synchronizing || entry.Selector.SelectedItem is not PresetChoice choice) return;
                entry.Custom = choice.Custom;
                if (entry.Custom)
                {
                    if (entry.Value is not null)
                    {
                        synchronizing = true; var current = input.Format(entry.Value);
                        for (var i = 0; i < current.Length; i++) entry.Boxes[i].Text = current[i]; synchronizing = false;
                    }
                    entry.Selector.Visibility = Visibility.Collapsed; entry.CustomPanel.Visibility = Visibility.Visible; entry.Boxes[0].Focus();
                }
                else entry.Value = choice.Predicate;
                Update();
            };
            entry.Remove = new Button { Content = "×", Style = (Style)FindResource("FilterRowActionStyle"), ToolTip = "Remove alternative" };
            AutomationProperties.SetName(entry.Remove, "Remove alternative"); SetColumn(entry.Remove, 1); line.Children.Add(entry.Remove);
            entry.Remove.Click += (_, _) => { entries.Remove(entry); panel.Children.Remove(line); Update(); };
            entries.Add(entry); panel.Children.Insert(Math.Max(0, panel.Children.Count - 1), line); Refresh();
        }
        var add = new Button { Content = "+ Add alternative", Style = (Style)FindResource("FilterInlineActionStyle"), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        panel.Children.Add(add);
        if (saved.Length == 0) AddEntry(null); else foreach (var value in saved) AddEntry(value);
        add.Click += (_, _) => { AddEntry(null); Update(); };
        _refreshSuggestions = Refresh; _value.Content = panel; Update();
    }

    private void BuildStructured()
    {
        var descriptor = BrowserFilterDescriptors.Get(Field); var input = descriptor.Input!;
        var panel = new StackPanel();
        var editors = new List<(TextBox[] Boxes, ComboBox Suggestions, ComboBox? Operator)>();
        var alternativeRemovals = new List<Button>();
        var saved = _alternatives.ToArray();
        var creating = true;
        void Update()
        {
            if (creating) return;
            _alternatives.Clear(); _inputValid = true;
            foreach (var editor in editors)
            {
                try
                {
                    var predicate = input.Parse(editor.Boxes.Select(box => box.Text).ToArray());
                    if (editor.Operator?.SelectedItem is Comparison selected) predicate = predicate with { Comparison = selected.Value };
                    _alternatives.Add(predicate);
                    foreach (var box in editor.Boxes) box.ToolTip = input.Help;
                }
                catch (FormatException ex)
                {
                    _inputValid = false;
                    foreach (var box in editor.Boxes) { box.ToolTip = ex.Message; }
                }
            }
            foreach (var button in alternativeRemovals) button.Visibility = editors.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (descriptor.DurationComparisons) _operator.Content = null;
            else LabelOperator(editors.Count > 1 ? input.AlternativesOperator : input.Operator); _changed();
        }
        void RefreshSuggestions()
        {
            var values = (descriptor.Suggestions?.Invoke(_tiles) ?? descriptor.Values(_tiles)).Concat(_alternatives).Distinct().ToArray();
            foreach (var editor in editors)
            {
                editor.Suggestions.ItemsSource = new[] { new ValueSuggestion(null, descriptor.SuggestionsLabel) }
                    .Concat(values.Select(p => new ValueSuggestion(p, BrowserFilterDescriptors.ValueLabel(p)))).ToArray();
                editor.Suggestions.SelectedIndex = 0;
                editor.Suggestions.Visibility = values.Length == 0 || !descriptor.OfferSuggestions ? Visibility.Collapsed : Visibility.Visible;
            }
        }
        void AddAlternative(BrowserFilterPredicate? predicate)
        {
            var group = new StackPanel { Margin = new(0, 0, 0, 5) };
            var line = new Grid();
            ComboBox? comparison = null;
            if (descriptor.DurationComparisons)
            {
                var operators = new[] { new Comparison(BrowserNumberComparison.GreaterThanOrEqual, "is at least"), new Comparison(BrowserNumberComparison.LessThanOrEqual, "is at most") };
                comparison = new ComboBox { ItemsSource = operators, DisplayMemberPath = "Name", MinWidth = 105, VerticalAlignment = VerticalAlignment.Center };
                comparison.SelectedItem = operators.FirstOrDefault(o => o.Value == predicate?.Comparison) ?? operators[0];
                AutomationProperties.SetName(comparison, "Duration operator");
                line.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); line.Children.Add(comparison);
            }
            var boxes = new TextBox[input.Components.Length];
            var formatted = predicate is null ? input.Components.Select(_ => "").ToArray() : input.Format(predicate);
            for (var i = 0; i < boxes.Length; i++)
            {
                if (i > 0)
                {
                    line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                    var separator = new TextBlock { Text = input.Separator, VerticalAlignment = VerticalAlignment.Center, Margin = new(3, 0, 3, 0) };
                    SetColumn(separator, line.ColumnDefinitions.Count - 1); line.Children.Add(separator);
                }
                line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                var box = boxes[i] = new TextBox { Text = formatted[i], MinWidth = 54, ToolTip = input.Help };
                TextInputHint.SetText(box, input.Components[i]); AutomationProperties.SetName(box, descriptor.Name + " " + input.Components[i]);
                SetColumn(box, line.ColumnDefinitions.Count - 1); line.Children.Add(box); box.TextChanged += (_, _) => Update();
            }
            line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var unit = new TextBlock { Text = input.Unit, VerticalAlignment = VerticalAlignment.Center, Margin = new(4, 0, 0, 0) };
            SetColumn(unit, line.ColumnDefinitions.Count - 1); line.Children.Add(unit);
            line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var suggestionsColumn = line.ColumnDefinitions.Count - 1;
            line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var remove = new Button { Content = "×", Style = (Style)FindResource("FilterRowActionStyle"), ToolTip = "Remove alternative" };
            AutomationProperties.SetName(remove, "Remove alternative"); SetColumn(remove, line.ColumnDefinitions.Count - 1); line.Children.Add(remove);
            alternativeRemovals.Add(remove);
            var suggestions = new ComboBox { DisplayMemberPath = "Label", Width = 78, VerticalAlignment = VerticalAlignment.Center, ToolTip = descriptor.Name + " suggestions" };
            AutomationProperties.SetName(suggestions, descriptor.Name + " suggestions");
            suggestions.SelectionChanged += (_, _) =>
            {
                if (suggestions.SelectedItem is not ValueSuggestion { Predicate: { } selected }) return;
                var values = input.Format(selected);
                for (var i = 0; i < boxes.Length; i++) boxes[i].Text = values[i];
                suggestions.SelectedIndex = 0;
            };
            editors.Add((boxes, suggestions, comparison));
            if (comparison is not null) comparison.SelectionChanged += (_, _) => Update(); group.Children.Add(line); SetColumn(suggestions, suggestionsColumn); line.Children.Add(suggestions);
            panel.Children.Insert(Math.Max(0, panel.Children.Count - 1), group);
            remove.Click += (_, _) => { editors.Remove((boxes, suggestions, comparison)); alternativeRemovals.Remove(remove); panel.Children.Remove(group); Update(); };
            RefreshSuggestions();
        }
        var add = new Button { Content = "+ Add alternative", Style = (Style)FindResource("FilterInlineActionStyle"), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        panel.Children.Add(add);
        if (saved.Length == 0) AddAlternative(null); else foreach (var predicate in saved) AddAlternative(predicate);
        creating = false;
        add.Click += (_, _) => { AddAlternative(null); Update(); };
        _refreshSuggestions = RefreshSuggestions;
        Update();
        _value.Content = panel;
    }

    private void BuildDates()
    {
        LabelOperator(_alternatives.Count > 1 ? "in any range" : "in range");
        var panel = new StackPanel();
        var ranges = _alternatives.ToArray();
        if (ranges.Length == 0) ranges = [BrowserFilterPredicate.ForDateRange(null, null)];
        var editors = new List<(DatePicker From, DatePicker To)>();
        void Update()
        {
            _alternatives.Clear();
            foreach (var pair in editors)
                if (pair.From.SelectedDate is not null || pair.To.SelectedDate is not null)
                    _alternatives.Add(BrowserFilterPredicate.ForDateRange(pair.From.SelectedDate, pair.To.SelectedDate));
            LabelOperator(editors.Count > 1 ? "in any range" : "in range"); _changed();
        }
        void AddRange(BrowserFilterPredicate range)
        {
            var line = new StackPanel { Margin = new(0, 0, 0, 6) };
            var from = new DatePicker { SelectedDate = range.DateFrom, Style = (Style)FindResource("BrowserFilterDatePickerStyle") }; var to = new DatePicker { SelectedDate = range.DateTo, Style = (Style)FindResource("BrowserFilterDatePickerStyle") };
            AutomationProperties.SetName(from, "From date"); AutomationProperties.SetName(to, "Through date");
            line.Children.Add(from); line.Children.Add(new TextBlock { Text = "through", Margin = new(0, 2, 0, 2) }); line.Children.Add(to);
            var remove = new Button { Content = "Remove range", HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Padding = new(4, 2, 4, 2) };
            line.Children.Add(remove); editors.Add((from, to));
            from.SelectedDateChanged += (_, _) => Update(); to.SelectedDateChanged += (_, _) => Update();
            remove.Click += (_, _) => { editors.Remove((from, to)); panel.Children.Remove(line); Update(); };
            panel.Children.Insert(Math.Max(0, panel.Children.Count - 1), line);
        }
        var add = new Button { Content = "+ Add date range", HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        panel.Children.Add(add); foreach (var range in ranges) AddRange(range);
        add.Click += (_, _) => { AddRange(BrowserFilterPredicate.ForDateRange(null, null)); LabelOperator("in any range"); };
        _value.Content = panel;
    }
    public bool IsValid => _inputValid && _alternatives.Count > 0 && _alternatives.All(p => p.DateFrom is null || p.DateTo is null || p.DateFrom <= p.DateTo);
}
