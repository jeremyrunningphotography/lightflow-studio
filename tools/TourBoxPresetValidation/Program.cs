using System.Text.Json;
using System.Text.Json.Serialization;
using Lightflow.Actions;

// Developer documentation/compatibility tool. Never writes a Lightflow profile or a Console preset.
var json = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
try
{
    var root = FindRoot();
    var manifestPath = Path.Combine(root, "docs", "tourbox", "elite-plus-windows.mapping.json");
    string? profilePath = null;
    var selfTest = false;
    var writeGuides = false;
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--manifest" when i + 1 < args.Length: manifestPath = args[++i]; break;
            case "--profile" when i + 1 < args.Length: profilePath = args[++i]; break;
            case "--self-test": selfTest = true; break;
            case "--write-guides": writeGuides = true; break;
            default: throw new ArgumentException("Usage: --self-test | --write-guides | --profile <keyboard-shortcuts.json> | --manifest <mapping.json>");
        }
    }
    var preset = JsonSerializer.Deserialize<Preset>(File.ReadAllText(manifestPath), json)
        ?? throw new InvalidDataException("Empty mapping specification.");
    var errors = Validate(preset);
    if (errors.Count != 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
    var bindings = Bindings(preset);
    Console.WriteLine($"Catalog: {KeyboardCommandCatalog.Actions.Count} descriptors / {KeyboardCommandCatalog.Commands.Count} variants.");
    Console.WriteLine($"Proposal valid: {preset.Mappings.Length} physical inputs, {bindings.Count} semantic bindings, {Additional(preset).Count()} explicit additions; no overlapping-context conflicts.");
    Console.WriteLine("Console emission, association, haptics, reconnect and Color hold remain physical acceptance gates.");
    if (selfTest) RunSelfTests(preset, json);
    if (writeGuides)
    {
        WriteGuides(preset, Path.GetDirectoryName(Path.GetFullPath(manifestPath))!, json);
        Console.WriteLine("Wrote CONTROL-MAP.md and COMMAND-INVENTORY.md from the compiled catalog.");
    }
    if (profilePath is not null)
    {
        if (!File.Exists(profilePath)) throw new FileNotFoundException("Profile does not exist. Omit --profile to validate the proposal against code defaults.", profilePath);
        var loaded = KeyboardShortcutStore.Load(profilePath);
        var differences = Compatibility(preset, loaded.Profile);
        if (loaded.Diagnostic is not null || !loaded.CanSave)
            differences.Insert(0, loaded.Diagnostic ?? "Profile schema is not supported.");
        if (differences.Count != 0)
        {
            Console.Error.WriteLine("Current profile is NOT compatible with this exact proposed Console layout:");
            foreach (var difference in differences) Console.Error.WriteLine("- " + difference);
            Console.Error.WriteLine("Adapt Console to your saved gestures or explicitly stage individual changes in Settings. No file was changed.");
            return 2;
        }
        Console.WriteLine("Current profile matches the proposed emissions (including the conditional C2 probe). No file was changed.");
    }
    else Console.WriteLine("No user profile was inspected or modified. Proposal validation does not mean customized installations are compatible.");
    return 0;
}
catch (Exception error) when (error is IOException or JsonException or ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

static string FindRoot()
{
    for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "Lightflow.Actions", "Lightflow.Actions.csproj"))) return directory.FullName;
    throw new InvalidOperationException("Run from the Lightflow repository or one of its subdirectories.");
}

static Dictionary<string, KeyboardGesture> Bindings(Preset preset)
{
    var bindings = new Dictionary<string, KeyboardGesture>(StringComparer.Ordinal);
    foreach (var mapping in preset.Mappings)
        foreach (var id in mapping.Commands) bindings.TryAdd(id, mapping.Gesture);
    return bindings;
}

static IEnumerable<BindableCommand> Additional(Preset preset) => preset.Mappings
    .Where(m => m.Binding == "Additional").SelectMany(m => m.Commands).Distinct()
    .Select(id => KeyboardCommandCatalog.Commands.Single(c => c.Id == id));

static ShortcutProfile ProposedProfile(Preset preset)
{
    var profile = new ShortcutProfile();
    var bindings = Bindings(preset);
    foreach (var command in Additional(preset)) profile.Set(command, bindings[command.Id], ShortcutPlatform.Windows);
    return profile;
}

static List<string> Validate(Preset preset)
{
    var errors = new List<string>();
    if (preset.Version != 1 || preset.Platform != ShortcutPlatform.Windows || preset.Model != "TourBox Elite Plus")
        errors.Add("This specification supports schema 1 / Elite Plus / Windows only.");
    if (KeyboardCommandCatalog.Actions.Count != 23 || KeyboardCommandCatalog.Commands.Count != 77)
        errors.Add("Catalog inventory changed; review the documented baseline before updating the preset.");
    if (preset.Mappings is null || preset.Mappings.Length == 0) return ["No physical mappings."];
    var seenInputs = new HashSet<string>();
    var seenCommands = new Dictionary<string, KeyboardGesture>();
    foreach (var mapping in preset.Mappings)
    {
        if (mapping is null) { errors.Add("Null physical mapping."); continue; }
        var input = $"{mapping.Control} / {mapping.Operation}";
        if (!seenInputs.Add(input)) errors.Add($"Duplicate physical input: {input}");
        if (mapping.Commands is null || mapping.Gesture is null) { errors.Add($"Incomplete input: {input}"); continue; }
        if (mapping.Kind == "Local")
        {
            if (mapping.Binding != "Local" || mapping.Commands.Length != 0 || mapping.Gesture != new KeyboardGesture("Escape") || mapping.ConsoleMode != "Standard")
                errors.Add("Only plain local Escape/Back is documented outside the semantic catalog.");
            continue;
        }
        if (mapping.Kind is not ("Relative" or "Invoke" or "Hold") || mapping.Binding is not ("Default" or "Additional"))
            errors.Add($"Unknown input policy: {input}");
        if (mapping.ConsoleMode != "Standard") errors.Add($"UP/REP/AB/macros are not accepted for this layout: {input}");
        if (mapping.Kind == "Hold" ? mapping.Gate != "PhysicalHoldProbe" : mapping.Gate is not null)
            errors.Add($"Hold must remain an explicit physical probe: {input}");
        if (mapping.Commands.Length == 0) errors.Add($"No semantic commands: {input}");
        if (KeyboardShortcutResolver.Reserved(mapping.Gesture, ShortcutPlatform.Windows) is { } reservation)
            errors.Add($"{input}: {reservation}");
        foreach (var id in mapping.Commands)
        {
            var command = KeyboardCommandCatalog.Commands.SingleOrDefault(c => c.Id == id);
            if (command is null) { errors.Add($"Unknown bindable command: {id}"); continue; }
            if (seenCommands.TryGetValue(id, out var earlier) && !earlier.Matches(mapping.Gesture, ShortcutPlatform.Windows))
                errors.Add($"A command cannot have two different profile gestures: {id}");
            seenCommands[id] = mapping.Gesture;
            if (mapping.Binding == "Default" && command.WindowsDefault?.Matches(mapping.Gesture, ShortcutPlatform.Windows) != true)
                errors.Add($"Documented default drifted: {id}");
            if (mapping.Binding == "Additional" && command.WindowsDefault is not null)
                errors.Add($"Additional binding would replace an existing default: {id}");
            if (mapping.Kind == "Relative" && (command.Action.Repeat != ActionRepeatPolicy.BoundedRelative || !RelativeSafe(command)))
                errors.Add($"Rotary must target a curated relative operation: {id}");
            if (mapping.Kind == "Relative")
            {
                var direction = mapping.Operation switch { "Counterclockwise" or "Down" => -1, "Clockwise" or "Up" => 1, _ => 0 };
                if (direction == 0 || Direction(command.Arguments) != direction) errors.Add($"Rotary direction/argument mismatch: {id}");
            }
            if (mapping.Kind == "Invoke" && (!command.Action.Phases.Contains(ActionPhase.Invoke) || command.Action.Repeat != ActionRepeatPolicy.Suppress))
                errors.Add($"Button must be a discrete Invoke operation: {id}");
            if (mapping.Kind == "Hold" && (command.Action.Repeat != ActionRepeatPolicy.Session ||
                !new[] { ActionPhase.Begin, ActionPhase.End, ActionPhase.Cancel }.All(command.Action.Phases.Contains)))
                errors.Add($"Hold must target a Begin/End/Cancel session: {id}");
        }
    }
    if (errors.Count != 0) return errors;
    var resolver = new KeyboardShortcutResolver(ProposedProfile(preset), ShortcutPlatform.Windows);
    foreach (var info in resolver.Query())
        if (info.Current is { } gesture && resolver.Validate(info.Command, gesture) is { } conflict) errors.Add($"{info.Command.Id}: {conflict}");
    foreach (var mapping in preset.Mappings.Where(m => m.Kind != "Local"))
        foreach (var context in new[] { ShortcutContext.Browser, ShortcutContext.Player })
        {
            var expected = mapping.Commands.Where(id => (KeyboardCommandCatalog.Commands.Single(c => c.Id == id).Context & context) != 0).ToArray();
            var actual = resolver.Resolve(mapping.Gesture, context)?.Id;
            if (expected.Length > 1 || actual != expected.SingleOrDefault())
                errors.Add($"{mapping.Control}/{mapping.Operation} resolves incorrectly in {context} ({actual ?? "unassigned"}).");
            if (mapping.Kind == "Relative" && resolver.Resolve(mapping.Gesture, context, repeat: true)?.Id != actual)
                errors.Add($"Relative repeat suppressed: {mapping.Control}/{mapping.Operation} in {context}");
        }
    return errors;
}

static bool RelativeSafe(BindableCommand command) => command.Arguments switch
{
    FrameStepArguments { Direction: -1 or 1 } => true,
    TraverseArguments t => Enum.IsDefined(t.Direction),
    StepFlagArguments f => Enum.IsDefined(f.Direction),
    NavigateSelectionArguments { Movement: BrowserMovement.Previous or BrowserMovement.Next, Extend: false, Distance: 1 } => command.Distance == NavigationDistance.Item,
    VolumeArguments { Mode: AdjustmentMode.Relative, Percent: -5 or 5 } => true,
    LevelArguments { Direction: -1 or 1 } => true,
    _ => false
};

static int Direction(ActionArguments arguments) => arguments switch
{
    FrameStepArguments f => f.Direction,
    TraverseArguments t => (int)t.Direction,
    StepFlagArguments f => (int)f.Direction,
    NavigateSelectionArguments n => n.Movement == BrowserMovement.Previous ? -1 : 1,
    VolumeArguments v => Math.Sign(v.Percent),
    LevelArguments l => l.Direction,
    _ => 0
};

static List<string> Compatibility(Preset preset, ShortcutProfile profile)
{
    var resolver = new KeyboardShortcutResolver(profile, ShortcutPlatform.Windows);
    var differences = new List<string>();
    foreach (var (id, expected) in Bindings(preset))
    {
        var info = resolver.Query().Single(i => i.Command.Id == id);
        if (info.Current?.Matches(expected, ShortcutPlatform.Windows) != true)
            differences.Add($"{id}: expected {expected.Display(ShortcutPlatform.Windows)}; saved {info.Current?.Display(ShortcutPlatform.Windows) ?? "unassigned"}.");
        else if (resolver.Validate(info.Command, expected) is { } conflict) differences.Add($"{id}: {conflict}");
    }
    return differences;
}

static void RunSelfTests(Preset valid, JsonSerializerOptions json)
{
    var tests = 0;
    void Reject(string name, Func<Preset, Preset> change)
    {
        if (Validate(change(valid)).Count == 0) throw new InvalidOperationException($"Self-test failed: {name}");
        tests++;
    }
    Preset Change(int index, Func<Mapping, Mapping> change) => valid with
    { Mappings = valid.Mappings.Select((m, i) => i == index ? change(m) : m).ToArray() };
    var rotary = Array.FindIndex(valid.Mappings, m => m.Kind == "Relative");
    var hold = Array.FindIndex(valid.Mappings, m => m.Kind == "Hold");
    var fullscreen = Array.FindIndex(valid.Mappings, m => m.Commands.Contains("player.toggle-fullscreen"));
    var subclip = Array.FindIndex(valid.Mappings, m => m.Commands.Contains("subclip.create"));
    Reject("stale ID", _ => Change(rotary, m => m with { Commands = ["future.missing"] }));
    Reject("AltGr reservation", _ => Change(fullscreen, m => m with { Gesture = new("E", ShortcutModifiers.Control | ShortcutModifiers.Alt) }));
    Reject("Home overlap", _ => Change(fullscreen, m => m with { Gesture = new("I", ShortcutModifiers.Control) }));
    Reject("Player overlap", _ => Change(fullscreen, m => m with { Gesture = new("Right", ShortcutModifiers.Control | ShortcutModifiers.Shift) }));
    Reject("rotary toggle", _ => Change(rotary, m => m with { Commands = ["player.play-pause"], Gesture = new("Space") }));
    Reject("reversed rotary", _ => Change(rotary, m => m with { Operation = "Clockwise" }));
    Reject("non-session hold", _ => Change(hold, m => m with { Commands = ["marker.add"], Gesture = new("M") }));
    Reject("unqualified hold", _ => Change(hold, m => m with { Gate = null }));
    Reject("REP creation", _ => Change(subclip, m => m with { ConsoleMode = "REP" }));
    Reject("duplicate physical input", p => p with { Mappings = [.. p.Mappings, p.Mappings[0]] });
    var profile = ProposedProfile(valid);
    if (Compatibility(valid, profile).Count != 0) throw new InvalidOperationException("Recommended profile must be compatible.");
    tests++;
    profile.Set(KeyboardCommandCatalog.Commands.Single(c => c.Id == "player.next-frame"), new("J"), ShortcutPlatform.Windows);
    var before = JsonSerializer.Serialize(profile, json);
    if (!Compatibility(valid, profile).Any(e => e.Contains("player.next-frame"))) throw new InvalidOperationException("Customized frame shortcut went undetected.");
    if (before != JsonSerializer.Serialize(profile, json)) throw new InvalidOperationException("Compatibility check modified a profile.");
    tests++;
    Console.WriteLine($"Self-tests: {tests} passed (negative mappings, context conflicts, hold/relative policy and read-only customized-profile detection).");
}

static void WriteGuides(Preset preset, string directory, JsonSerializerOptions json)
{
    var map = new List<string> { "# Elite Plus proposed Windows control map", "", "Generated from `elite-plus-windows.mapping.json` and the compiled neutral catalog. Regenerate with", "`dotnet run --project tools/TourBoxPresetValidation -c Release -- --write-guides`.", "", "**Proposed, not physically accepted. C2 is a hold probe only; leave it unassigned in a distributed preset until it passes.**", "", "Side alone, all double-clicks and all unlisted combinations are unassigned. Hold Side first for combinations; it emits no standalone modifier/key. All mapped buttons use Standard mode with UP/REP/AB off. Rotaries emit discrete shortcuts, not macros or mouse-wheel events.", "", "| Physical input | Console keyboard emission | Player meaning | Browser meaning | Binding policy |", "| --- | --- | --- | --- | --- |" };
    string Meaning(Mapping mapping, ShortcutContext context) => mapping.Kind == "Local" ? "Local Escape / Back; normal ownership applies" :
        string.Join("<br>", mapping.Commands.Select(id => KeyboardCommandCatalog.Commands.Single(c => c.Id == id))
            .Where(c => (c.Context & context) != 0).Select(c => $"{c.Label} (`{c.Id}`)")) is { Length: > 0 } meaning ? meaning : "No semantic mapping";
    foreach (var m in preset.Mappings)
        map.Add($"| {m.Control}: {m.Operation}{(m.Gate is null ? "" : " **TEST ONLY**")} | {m.Gesture.Display(ShortcutPlatform.Windows)} | {Meaning(m, ShortcutContext.Player)} | {Meaning(m, ShortcutContext.Browser)} | {m.Binding} |");
    map.AddRange(["", "## Explicit additional Lightflow bindings", "", "These commands are unassigned in accepted Windows defaults. Set them individually through Settings → Keyboard Shortcuts, then Save Settings. Do not replace a customized profile file. Browser/Player reuse is deliberate; Home overlaps both.", "", "| Search command ID | Settings row | Context | Recommended gesture |", "| --- | --- | --- | --- |"]);
    var bindings = Bindings(preset);
    foreach (var c in Additional(preset)) map.Add($"| `{c.Id}` | {c.Label} | {c.Context} | {bindings[c.Id].Display(ShortcutPlatform.Windows)} |");
    map.AddRange(["", "Existing bindings used by the other rows must also be checked when the user's profile is customized. See [setup and recovery](README.md) and [physical acceptance](ACCEPTANCE.md)."]);
    File.WriteAllLines(Path.Combine(directory, "CONTROL-MAP.md"), map);
    var inventory = new List<string> { "# Merged semantic action and shortcut inventory", "", $"Baseline: `{preset.Baseline}`; {KeyboardCommandCatalog.Actions.Count} descriptors and {KeyboardCommandCatalog.Commands.Count} curated variants. This generated inventory is a reference, not a request to map every command.", "", "## Semantic descriptors", "", "| ID | Label | Argument shape | Phases | Repeat | Execution |", "| --- | --- | --- | --- | --- | --- |" };
    foreach (var a in KeyboardCommandCatalog.Actions) inventory.Add($"| `{a.Id}` | {a.Label} | {a.Arguments} | {string.Join(", ", a.Phases)} | {a.Repeat} | {a.Execution} |");
    inventory.AddRange(["", "## Bindable variants", "", "macOS defaults remain unassigned; this Windows plan does not approve a macOS mapping table.", "", "| Variant ID | Action ID | Context / distance | Typed arguments | Windows default | macOS default |", "| --- | --- | --- | --- | --- | --- |"]);
    foreach (var c in KeyboardCommandCatalog.Commands)
        inventory.Add($"| `{c.Id}` | `{c.Action.Id}` | {c.Context} / {c.Distance} | `{JsonSerializer.Serialize(c.Arguments, c.Arguments.GetType(), new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } })}` | {c.WindowsDefault?.Display(ShortcutPlatform.Windows) ?? "Unassigned"} | {c.MacDefault?.Display(ShortcutPlatform.MacOS) ?? "Unassigned"} |");
    File.WriteAllLines(Path.Combine(directory, "COMMAND-INVENTORY.md"), inventory);
}

sealed record Preset(int Version, string Model, ShortcutPlatform Platform, string Status, string Baseline, Mapping[] Mappings);
sealed record Mapping(string Control, string Operation, string Kind, string ConsoleMode, KeyboardGesture Gesture,
    string[] Commands, string Binding, string? Gate = null);
