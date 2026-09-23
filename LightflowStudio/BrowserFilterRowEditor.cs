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
    private readonly ContentControl _value = new();
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
        _field.ItemsSource = BrowserFilterDescriptors.All.Where(d => d.Field == field || fieldAvailable(d.Field)).ToArray();
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
        var removeButton = new Button { Content = "×", Width = 30, Margin = new(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top, ToolTip = "Remove filter" };
        AutomationProperties.SetName(removeButton, "Remove filter");
        removeButton.Click += (_, _) => remove(); Add(removeButton, 3);
        BuildValue();
    }
    private void Add(UIElement element, int column) { SetColumn(element, column); Children.Add(element); }
    private void RefreshFields()
    {
        var selected = Field; _initializing = true;
        _field.ItemsSource = BrowserFilterDescriptors.All.Where(d => d.Field == selected || _fieldAvailable(d.Field)).ToArray();
        _field.SelectedItem = BrowserFilterDescriptors.Get(selected); _initializing = false;
    }
    public void RefreshValues(IReadOnlyList<BrowserGridTile> tiles)
    {
        _tiles = tiles;
        if (BrowserFilterDescriptors.Get(Field).Editor == BrowserFilterEditorKind.Choices) BuildChoices();
    }
    private void LabelOperator(string text) => _operator.Content = new TextBlock { Text = text, Margin = new(0, 7, 0, 0) };
    private void BuildValue()
    {
        switch (BrowserFilterDescriptors.Get(Field).Editor)
        {
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
        var host = new Grid(); host.Children.Add(text);
        var placeholder = new TextBlock { Text = "Text to match", IsHitTestVisible = false, Margin = new(8, 7, 0, 0), Foreground = (Brush)FindResource("MutedTextBrush") };
        host.Children.Add(placeholder);
        void Update()
        {
            placeholder.Visibility = text.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _alternatives.Clear();
            if (!string.IsNullOrWhiteSpace(text.Text)) _alternatives.Add(BrowserFilterPredicate.ForText(Field, text.Text.Trim()));
        }
        Update(); text.TextChanged += (_, _) => { Update(); _changed(); }; _value.Content = host;
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
        var button = new ToggleButton { HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left, MinHeight = 30, Padding = new(8, 4, 8, 4) };
        var list = new StackPanel();
        var popup = new Popup { PlacementTarget = button, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        popup.Child = new Border { Background = (Brush)FindResource("CardBrush"), BorderBrush = (Brush)FindResource("BorderBrush"), BorderThickness = new(1), Padding = new(10),
            Child = new ScrollViewer { Content = list, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinWidth = 200 } };
        void UpdateLabel()
        {
            LabelOperator(Field == BrowserFilterField.Duration ? (_alternatives.Count > 1 ? "meets any" : "meets") : _alternatives.Count > 1 ? "is any of" : "is");
            button.Content = new TextBlock { Text = (_alternatives.Count == 0 ? "Choose values…" : string.Join(", ", _alternatives.Select(BrowserFilterDescriptors.ValueLabel))) + " ▾", TextWrapping = TextWrapping.Wrap };
        }
        foreach (var choice in choices)
        {
            var check = new CheckBox { Content = BrowserFilterDescriptors.ValueLabel(choice), IsChecked = _alternatives.Contains(choice), Margin = new(0, 4, 0, 4) };
            check.Click += (_, _) => { if (check.IsChecked == true) _alternatives.Add(choice); else _alternatives.Remove(choice); UpdateLabel(); _changed(); };
            list.Children.Add(check);
        }
        if (choices.Length == 0) list.Children.Add(new TextBlock { Text = "No known values in this Source", Foreground = (Brush)FindResource("MutedTextBrush") });
        button.Checked += (_, _) => popup.IsOpen = true; button.Unchecked += (_, _) => popup.IsOpen = false;
        popup.Closed += (_, _) => button.IsChecked = false;
        Unloaded += (_, _) => popup.IsOpen = false;
        AutomationProperties.SetName(button, "Filter values");
        var host = new Grid(); host.Children.Add(button); host.Children.Add(popup); _value.Content = host; UpdateLabel();
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
    public bool IsValid => _alternatives.Count > 0 && _alternatives.All(p => p.DateFrom is null || p.DateTo is null || p.DateFrom <= p.DateTo);
}
