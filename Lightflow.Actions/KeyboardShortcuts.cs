using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lightflow.Actions;

[Flags]
public enum ShortcutModifiers { None = 0, Primary = 1, Control = 2, Alt = 4, Shift = 8, Meta = 16 }
public enum ShortcutPlatform { Windows, MacOS }
[Flags]
public enum ShortcutContext { Browser = 1, Player = 2, Home = Browser | Player, Global = 7 }
public enum NavigationDistance { Item, Row, Page }
public sealed record KeyboardGesture(string Key, ShortcutModifiers Modifiers = ShortcutModifiers.None)
{
    public ShortcutModifiers ResolvedModifiers(ShortcutPlatform platform) =>
        (Modifiers & ~ShortcutModifiers.Primary) | (Modifiers.HasFlag(ShortcutModifiers.Primary)
            ? platform == ShortcutPlatform.Windows ? ShortcutModifiers.Control : ShortcutModifiers.Meta : ShortcutModifiers.None);
    public bool Matches(KeyboardGesture other, ShortcutPlatform platform) => Key == other.Key && ResolvedModifiers(platform) == other.ResolvedModifiers(platform);
    public string Display(ShortcutPlatform platform) => string.Join("+", new[] {
        ResolvedModifiers(platform).HasFlag(ShortcutModifiers.Control) ? "Ctrl" : null,
        ResolvedModifiers(platform).HasFlag(ShortcutModifiers.Meta) ? platform == ShortcutPlatform.MacOS ? "Command" : "Windows" : null,
        Modifiers.HasFlag(ShortcutModifiers.Alt) ? platform == ShortcutPlatform.MacOS ? "Option" : "Alt" : null,
        Modifiers.HasFlag(ShortcutModifiers.Shift) ? "Shift" : null, Key }.Where(x => x is not null));
}

/// <summary>Adapter configuration, not another semantic dispatcher. Arguments are curated typed values.</summary>
public sealed record BindableCommand(string Id, ActionDescriptor Action, string Label, ShortcutContext Context,
    ActionArguments Arguments, KeyboardGesture? WindowsDefault = null, KeyboardGesture? MacDefault = null,
    NavigationDistance Distance = NavigationDistance.Item)
{
    public KeyboardGesture? Default(ShortcutPlatform platform) => platform == ShortcutPlatform.Windows ? WindowsDefault : MacDefault;
}

public static class KeyboardCommandCatalog
{
    public static IReadOnlyList<ActionDescriptor> Actions { get; } = PlayerActions.Descriptors.Concat(BrowserActions.Descriptors)
        .Concat(ReviewPresentationActions.Descriptors).Concat(ReviewShellActions.Descriptors).ToArray();
    public static IReadOnlyList<BindableCommand> Commands { get; } = Create();
    private static IReadOnlyList<BindableCommand> Create()
    {
        var list = new List<BindableCommand>();
        void Add(string id, string action, string label, ShortcutContext context, ActionArguments args,
            string? key = null, ShortcutModifiers modifiers = ShortcutModifiers.None, NavigationDistance distance = NavigationDistance.Item)
        {
            var gesture = key is null ? null : new KeyboardGesture(key, modifiers);
            // No speculative macOS defaults: a native adapter must approve its own convention table.
            list.Add(new(id, Actions.Single(a => a.Id == action && a.Bindable), label, context, args, gesture, Distance: distance));
        }
        var p = ShortcutContext.Player;
        var b = ShortcutContext.Browser;
        Add("player.play-pause", PlayerActions.PlayPause, "Play / Pause", p, NoActionArguments.Instance, "Space");
        Add("player.color-bypass", PlayerActions.ColorBypass, "Compare Original (hold)", p, NoActionArguments.Instance, "C");
        Add("player.set-in", PlayerActions.SetBoundary, "Set In", p, new SetBoundaryArguments(WorkingRangeBoundary.In), "I");
        Add("player.set-out", PlayerActions.SetBoundary, "Set Out", p, new SetBoundaryArguments(WorkingRangeBoundary.Out), "O");
        Add("subclip.create", PlayerActions.CreateSubclip, "Create Subclip", p, NoActionArguments.Instance, "S");
        Add("marker.add", PlayerActions.AddMarker, "Add Marker", p, NoActionArguments.Instance, "M");
        foreach (var direction in new[] { -1, 1 }) {
            var name = direction < 0 ? "previous" : "next";
            var label = direction < 0 ? "Previous" : "Next";
            var arrow = direction < 0 ? "Left" : "Right";
            Add($"player.{name}-frame", PlayerActions.StepFrame, $"{label} Frame", p, new FrameStepArguments(direction), arrow);
            Add($"player.{name}-media", PlayerActions.TraverseReview, $"{label} Media", p, new TraverseArguments((TraversalDirection)direction), arrow, ShortcutModifiers.Control);
            Add($"marker.{name}", PlayerActions.NavigateMarker, $"{label} Marker", p, new TraverseArguments((TraversalDirection)direction), arrow, ShortcutModifiers.Alt);
            Add($"asset.flag-{name}", BrowserActions.StepFlag, direction < 0 ? "Step Flag Down" : "Step Flag Up", ShortcutContext.Home,
                new StepFlagArguments((TraversalDirection)direction), direction < 0 ? "Down" : "Up", ShortcutModifiers.Control);
            Add($"player.volume-{name}", ReviewPresentationActions.Volume, direction < 0 ? "Lower Volume" : "Raise Volume", p, new VolumeArguments(AdjustmentMode.Relative, direction * 5));
            Add($"viewer.zoom-{name}", ReviewPresentationActions.StepZoom, direction < 0 ? "Zoom Out" : "Zoom In", p, new LevelArguments(direction));
            Add($"browser.thumbnails-{name}", ReviewShellActions.ThumbnailSize, direction < 0 ? "Smaller Thumbnails" : "Larger Thumbnails", b, new LevelArguments(direction));
        }
        for (var rating = 0; rating <= 5; rating++) Add($"asset.rating-{rating}", BrowserActions.SetRating, $"Set Rating {rating}", ShortcutContext.Home, new SetRatingArguments(rating), rating.ToString());
        foreach (var flag in Enum.GetValues<ClassificationFlag>()) Add($"asset.flag-{flag.ToString().ToLowerInvariant()}", BrowserActions.SetFlag, $"Set Flag: {flag}", b, new SetFlagArguments(flag));
        foreach (var color in Enum.GetValues<ClassificationColorLabel>()) Add($"asset.color-{color.ToString().ToLowerInvariant()}", BrowserActions.SetColorLabel, $"Color Label: {color}", b, new SetColorLabelArguments(color));
        Add("asset.color-clear", BrowserActions.SetColorLabel, "Clear Color Label", b, new SetColorLabelArguments(null));
        foreach (var key in new[] { "Left", "Right", "Up", "Down", "Home", "End", "PageUp", "PageDown" }) {
            var movement = key == "Home" ? BrowserMovement.First : key == "End" ? BrowserMovement.Last : key is "Left" or "Up" or "PageUp" ? BrowserMovement.Previous : BrowserMovement.Next;
            var distance = key is "Up" or "Down" ? NavigationDistance.Row : key is "PageUp" or "PageDown" ? NavigationDistance.Page : NavigationDistance.Item;
            foreach (var extend in new[] { false, true }) Add($"browser.navigate-{key.ToLowerInvariant()}{(extend ? "-extend" : "")}", BrowserActions.NavigateSelection,
                $"{(extend ? "Extend Selection: " : "Navigate: ")}{key}", b, new NavigateSelectionArguments(movement, extend), key,
                extend ? ShortcutModifiers.Shift : ShortcutModifiers.None, distance);
        }
        Add("browser.open", BrowserActions.OpenCurrent, "Open Current Media", b, NoActionArguments.Instance, "Enter");
        Add("review.toggle-right-panel", ReviewShellActions.TogglePanel, "Toggle Right Panel", ShortcutContext.Home, NoActionArguments.Instance, "I", ShortcutModifiers.Primary);
        foreach (var value in Enum.GetValues<ReviewSpeed>()) Add($"player.speed-{value.ToString().ToLowerInvariant()}", ReviewPresentationActions.Speed, $"Playback Speed: {value}", p, new SpeedArguments(value));
        foreach (var value in Enum.GetValues<ReviewZoom>()) Add($"viewer.zoom-{value.ToString().ToLowerInvariant()}", ReviewPresentationActions.Zoom, $"Viewer Zoom: {value}", p, new ZoomArguments(value));
        foreach (var value in Enum.GetValues<PresentationToggle>()) Add($"player.toggle-{value.ToString().ToLowerInvariant()}", ReviewPresentationActions.Toggle, $"Toggle {value}", p, new PresentationToggleArguments(value));
        foreach (var value in Enum.GetValues<ReviewPanelSurface>()) Add($"review.panel-{value.ToString().ToLowerInvariant()}", ReviewShellActions.ShowPanel, $"Show {value}", ShortcutContext.Home, new PanelSurfaceArguments(value));
        foreach (var value in Enum.GetValues<ExportEntry>()) Add($"export.{value.ToString().ToLowerInvariant()}", ReviewShellActions.Export, $"Export: {value}", value is ExportEntry.BrowserVideos or ExportEntry.BrowserSubclips ? b : p, new ExportEntryArguments(value));
        return list.AsReadOnly();
    }
}

public sealed record ShortcutOverride(string CommandId, string ActionId, JsonElement Arguments, KeyboardGesture? Gesture)
{
    internal static readonly JsonSerializerOptions ArgumentOptions = new() { Converters = { new JsonStringEnumConverter() } };
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; init; }
    public static ShortcutOverride Create(BindableCommand command, KeyboardGesture? gesture) =>
        new(command.Id, command.Action.Id, JsonSerializer.SerializeToElement(command.Arguments, command.Arguments.GetType(), ArgumentOptions), gesture);
}
public sealed record ShortcutProfile
{
    public int Version { get; init; } = 1;
    public List<ShortcutOverride> Overrides { get; init; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; init; }
    public ShortcutProfile Copy() => this with { Overrides = [.. Overrides] };
    public void Reset(string id) => Overrides.RemoveAll(o => o.CommandId == id);
    public void ResetAll() => Overrides.RemoveAll(o => KeyboardCommandCatalog.Commands.Any(c => c.Id == o.CommandId && c.Action.Id == o.ActionId));
    public void Set(BindableCommand command, KeyboardGesture? gesture, ShortcutPlatform platform)
    {
        Reset(command.Id);
        if (gesture != command.Default(platform)) Overrides.Add(ShortcutOverride.Create(command, gesture));
    }
}
public sealed record ShortcutInfo(BindableCommand Command, KeyboardGesture? Current, KeyboardGesture? Default, bool Customized)
{
    public bool Unassigned => Current is null;
    public string Label => Command.Label;
    public string Category => Command.Action.Category;
}

public sealed class KeyboardShortcutResolver(ShortcutProfile profile, ShortcutPlatform platform)
{
    private readonly ShortcutInfo[] _bindings = Build(profile.Copy(), platform);
    public IEnumerable<ShortcutInfo> Query() => _bindings;
    private static ShortcutInfo[] Build(ShortcutProfile profile, ShortcutPlatform platform) => KeyboardCommandCatalog.Commands.Select(c => {
        var item = profile.Overrides.LastOrDefault(o => o.CommandId == c.Id);
        var valid = item is not null && item.ActionId == c.Action.Id &&
            item.Arguments.ValueKind == JsonValueKind.Object && System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(item.Arguments.GetRawText()), JsonSerializer.SerializeToNode(c.Arguments, c.Arguments.GetType(), ShortcutOverride.ArgumentOptions));
        return new ShortcutInfo(c, valid ? item!.Gesture : c.Default(platform), c.Default(platform), valid);
    }).ToArray();
    public BindableCommand? Resolve(KeyboardGesture gesture, ShortcutContext context, bool repeat = false)
    {
        if (Reserved(gesture, platform) is not null) return null;
        var matches = Query().Where(i => (i.Command.Context & context) != 0 && i.Current?.Matches(gesture, platform) == true).ToArray();
        // Invalid/ambiguous files fail closed; never pick a dangerous arbitrary winner.
        return matches.Length == 1 && (!repeat || matches[0].Command.Action.Repeat == ActionRepeatPolicy.BoundedRelative) ? matches[0].Command : null;
    }
    public string? Validate(BindableCommand command, KeyboardGesture gesture)
    {
        if (Reserved(gesture, platform) is { } reserved) return reserved;
        var conflict = Query().FirstOrDefault(i => i.Command.Id != command.Id && (i.Command.Context & command.Context) != 0 && i.Current?.Matches(gesture, platform) == true);
        return conflict is null ? null : $"Conflicts with {conflict.Label} ({conflict.Command.Context}).";
    }
    public static string? Reserved(KeyboardGesture gesture, ShortcutPlatform platform)
    {
        var mods = gesture.ResolvedModifiers(platform);
        if (!LogicalKeys.Contains(gesture.Key)) return "This key is not supported.";
        if ((gesture.Modifiers & ~(ShortcutModifiers.Primary | ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Shift | ShortcutModifiers.Meta)) != 0) return "Unsupported modifiers.";
        if (gesture.Key is "Delete" or "Escape" or "Tab") return "This key belongs to local navigation or contextual commands.";
        if (platform == ShortcutPlatform.Windows && (mods.HasFlag(ShortcutModifiers.Meta) ||
            gesture.Key is "F4" or "Space" && mods.HasFlag(ShortcutModifiers.Alt) || gesture.Key == "Tab" && mods.HasFlag(ShortcutModifiers.Alt) ||
            gesture.Key == "F10")) return "This is a Windows system or menu shortcut.";
        if (mods.HasFlag(ShortcutModifiers.Control) && mods.HasFlag(ShortcutModifiers.Alt)) return "Ctrl+Alt may represent AltGr text input and cannot be assigned.";
        if (gesture.Key == "F" && mods.HasFlag(ShortcutModifiers.Control) || gesture.Key is "A" or "X" or "C" or "V" && mods.HasFlag(ShortcutModifiers.Control)) return "This gesture belongs to search, selection or clipboard commands.";
        if (platform == ShortcutPlatform.MacOS && mods.HasFlag(ShortcutModifiers.Meta) && gesture.Key is "Q" or "H" or "Space" or "Tab") return "This is a macOS system shortcut.";
        return null;
    }
    public static IReadOnlySet<string> LogicalKeys { get; } = new HashSet<string>(
        Enumerable.Range('A', 26).Select(c => ((char)c).ToString()).Concat(Enumerable.Range(0, 10).Select(i => i.ToString()))
        .Concat(Enumerable.Range(1, 24).Select(i => $"F{i}")).Concat(new[] { "Space", "Left", "Right", "Up", "Down", "Home", "End", "PageUp", "PageDown", "Enter", "Escape", "Delete", "Tab" }), StringComparer.Ordinal);
}

public sealed record ShortcutLoadResult(ShortcutProfile Profile, string? Diagnostic, bool CanSave = true);
public static class KeyboardShortcutStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public static ShortcutLoadResult Load(string path)
    {
        try {
            if (!File.Exists(path)) return new(new(), null);
            var profile = JsonSerializer.Deserialize<ShortcutProfile>(File.ReadAllText(path), Options);
            if (profile is null || profile.Overrides is null || profile.Overrides.Any(o => o is null || string.IsNullOrWhiteSpace(o.CommandId))) throw new JsonException("Invalid shortcut profile.");
            if (profile.Version != 1) return new(new(), $"Unsupported shortcut schema {profile.Version}; original file retained.", false);
            var resolver = new KeyboardShortcutResolver(profile, ShortcutPlatform.Windows);
            var unknown = profile.Overrides.Where(o => !resolver.Query().Any(i => i.Command.Id == o.CommandId && i.Customized)).Select(o => o.CommandId).ToArray();
            var invalid = resolver.Query().Where(i => i.Current is { } gesture && resolver.Validate(i.Command, gesture) is not null).Select(i => i.Label).ToArray();
            var messages = new List<string>();
            if (unknown.Length > 0) messages.Add($"Unknown or incompatible shortcut entries retained: {string.Join(", ", unknown)}.");
            if (invalid.Length > 0) messages.Add($"Conflicting or reserved shortcuts are inactive: {string.Join(", ", invalid)}. Clear or reset these entries in Settings.");
            return new(profile, messages.Count == 0 ? null : string.Join(" ", messages));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) {
            return new(new(), $"Shortcut defaults loaded: {e.Message} Original file retained until an explicit save.");
        }
    }
    public static void Save(string path, ShortcutProfile profile)
    {
        if (profile.Version != 1) throw new InvalidOperationException("Unsupported shortcut schema.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + $".{Guid.NewGuid():N}.tmp";
        try {
            File.WriteAllText(temp, JsonSerializer.Serialize(profile, Options));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

