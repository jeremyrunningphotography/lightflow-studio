using Lightflow.Actions;
using System.Text.Json;
using Xunit;

namespace LightflowStudio.Tests;

/// <summary>Pure semantic/configuration tests: no WPF objects, keyboard, mouse or application startup.</summary>
public class KeyboardShortcutTests
{
    private static BindableCommand Command(string id) => KeyboardCommandCatalog.Commands.Single(c => c.Id == id);
    private static KeyboardShortcutResolver Resolver(ShortcutProfile? profile = null, ShortcutPlatform platform = ShortcutPlatform.Windows) => new(profile ?? new(), platform);
    public static IEnumerable<object[]> Defaults => new[] {
        ("player.play-pause", "Space", ShortcutModifiers.None), ("player.color-bypass", "C", ShortcutModifiers.None),
        ("player.previous-frame", "Left", ShortcutModifiers.None), ("player.next-frame", "Right", ShortcutModifiers.None),
        ("player.set-in", "I", ShortcutModifiers.None), ("player.set-out", "O", ShortcutModifiers.None),
        ("subclip.create", "S", ShortcutModifiers.None), ("marker.add", "M", ShortcutModifiers.None),
        ("player.previous-media", "Left", ShortcutModifiers.Control), ("player.next-media", "Right", ShortcutModifiers.Control),
        ("marker.previous", "Left", ShortcutModifiers.Alt), ("marker.next", "Right", ShortcutModifiers.Alt),
        ("asset.flag-next", "Up", ShortcutModifiers.Control), ("asset.flag-previous", "Down", ShortcutModifiers.Control),
        ("review.toggle-right-panel", "I", ShortcutModifiers.Primary), ("browser.open", "Enter", ShortcutModifiers.None)
    }.Select(v => new object[] { v.Item1, v.Item2, v.Item3 })
        .Concat(Enumerable.Range(0, 6).Select(i => new object[] { $"asset.rating-{i}", i.ToString(), ShortcutModifiers.None }))
        .Concat(new[] { "Left", "Right", "Up", "Down", "Home", "End", "PageUp", "PageDown" }.SelectMany(k => new[] {
            new object[] { $"browser.navigate-{k.ToLowerInvariant()}", k, ShortcutModifiers.None },
            new object[] { $"browser.navigate-{k.ToLowerInvariant()}-extend", k, ShortcutModifiers.Shift } }));
    [Theory, MemberData(nameof(Defaults))]
    public void DefaultsResolveExactCuratedVariant(string id, string key, ShortcutModifiers mods)
    {
        var command = Command(id);
        Assert.Equal(new KeyboardGesture(key, mods), command.WindowsDefault);
        Assert.Equal(command, Resolver().Resolve(new(key, mods), command.Context == ShortcutContext.Home ? ShortcutContext.Player : command.Context));
        Assert.Null(Resolver().Validate(command, command.WindowsDefault!));
    }
    [Fact]
    public void CatalogUsesAllAcceptedDescriptorsAndExactTypedArguments()
    {
        Assert.Equal(23, KeyboardCommandCatalog.Actions.Count);
        Assert.Equal(23, KeyboardCommandCatalog.Commands.Select(c => c.Action.Id).Distinct().Count());
        Assert.Equal(KeyboardCommandCatalog.Commands.Count, KeyboardCommandCatalog.Commands.Select(c => c.Id).Distinct().Count());
        Assert.Equal(new FrameStepArguments(1), Command("player.next-frame").Arguments);
        Assert.Equal(new SetBoundaryArguments(WorkingRangeBoundary.In), Command("player.set-in").Arguments);
        Assert.Equal(new TraverseArguments(TraversalDirection.Previous), Command("marker.previous").Arguments);
        Assert.Equal(new SetRatingArguments(5), Command("asset.rating-5").Arguments);
        Assert.Equal(new NavigateSelectionArguments(BrowserMovement.Previous, true), Command("browser.navigate-up-extend").Arguments);
        Assert.Equal(NavigationDistance.Row, Command("browser.navigate-up-extend").Distance);
        Assert.Equal(ActionRepeatPolicy.Session, Command("player.color-bypass").Action.Repeat);
        Assert.Null(Command("player.volume-next").WindowsDefault);
        Assert.Null(Command("export.playervideo").WindowsDefault);
    }
    [Fact]
    public void OverrideUnassignResetRestartAndNewDefaults()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lightflow-shortcuts-" + Guid.NewGuid());
        var path = Path.Combine(directory, "keyboard-shortcuts.json");
        try {
            var profile = new ShortcutProfile();
            var play = Command("player.play-pause");
            profile.Set(play, new("P"), ShortcutPlatform.Windows);
            profile.Set(Command("player.next-frame"), null, ShortcutPlatform.Windows);
            KeyboardShortcutStore.Save(path, profile);
            var loaded = KeyboardShortcutStore.Load(path);
            Assert.Null(loaded.Diagnostic);
            Assert.Equal(play, Resolver(loaded.Profile).Resolve(new("P"), ShortcutContext.Player));
            Assert.Null(Resolver(loaded.Profile).Resolve(new("Space"), ShortcutContext.Player));
            Assert.Null(Resolver(loaded.Profile).Resolve(new("Right"), ShortcutContext.Player));
            Assert.Equal(Command("player.set-in"), Resolver(loaded.Profile).Resolve(new("I"), ShortcutContext.Player));
            loaded.Profile.Reset(play.Id); KeyboardShortcutStore.Save(path, loaded.Profile);
            Assert.Equal(play, Resolver(KeyboardShortcutStore.Load(path).Profile).Resolve(new("Space"), ShortcutContext.Player));
            loaded.Profile.ResetAll(); KeyboardShortcutStore.Save(path, loaded.Profile);
            Assert.Empty(KeyboardShortcutStore.Load(path).Profile.Overrides);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            Assert.DoesNotContain("System.Windows", File.ReadAllText(path));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"Version\":1,\"Overrides\":null}")]
    [InlineData("{\"Version\":1,\"Overrides\":[null]}")]
    public void MalformedFilesRecoverWithoutOverwriting(string json)
    {
        var path = Path.GetTempFileName();
        try {
            File.WriteAllText(path, json);
            var loaded = KeyboardShortcutStore.Load(path);
            Assert.NotNull(loaded.Diagnostic); Assert.Empty(loaded.Profile.Overrides);
            Assert.Equal(json, File.ReadAllText(path));
            Assert.NotNull(Resolver(loaded.Profile).Resolve(new("Space"), ShortcutContext.Player));
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void UnknownFutureEntriesAndFieldsSurviveSaveAndReset()
    {
        var path = Path.GetTempFileName();
        try {
            File.WriteAllText(path, """{"Version":1,"futureSetting":{"value":42},"Overrides":[{"CommandId":"future.variant","ActionId":"future.action","Arguments":{"Level":7},"Gesture":{"Key":"F9","Modifiers":"Primary"},"futureField":true}]}""");
            var loaded = KeyboardShortcutStore.Load(path);
            Assert.Contains("future.variant", loaded.Diagnostic);
            loaded.Profile.ResetAll();
            loaded.Profile.Set(Command("player.play-pause"), new("P"), ShortcutPlatform.Windows);
            KeyboardShortcutStore.Save(path, loaded.Profile);
            var roundtrip = KeyboardShortcutStore.Load(path).Profile;
            Assert.Equal(2, roundtrip.Overrides.Count);
            Assert.Equal(42, roundtrip.Extra!["futureSetting"].GetProperty("value").GetInt32());
            Assert.True(roundtrip.Overrides[0].Extra!["futureField"].GetBoolean());
            File.WriteAllText(path, "{\"Version\":2,\"Overrides\":[]}");
            Assert.False(KeyboardShortcutStore.Load(path).CanSave);
            Assert.Throws<InvalidOperationException>(() => KeyboardShortcutStore.Save(path, new() { Version = 2 }));
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void ConflictsUseContextOverlapAndExactResolvedModifiers()
    {
        var profile = new ShortcutProfile(); var play = Command("player.play-pause");
        Assert.Contains("Next Frame", Resolver(profile).Validate(play, new("Right")));
        Assert.Contains("Toggle Right Panel", Resolver(profile).Validate(play, new("I", ShortcutModifiers.Control)));
        Assert.Null(Resolver(profile).Validate(play, new("I", ShortcutModifiers.Control | ShortcutModifiers.Shift)));
        Assert.Null(Resolver(profile).Validate(play, new("Enter"))); // Browser and Player are exclusive.
        profile.Set(play, new("Enter"), ShortcutPlatform.Windows);
        Assert.Equal(play, Resolver(profile).Resolve(new("Enter"), ShortcutContext.Player));
        Assert.Equal(Command("browser.open"), Resolver(profile).Resolve(new("Enter"), ShortcutContext.Browser));
        Assert.True((ShortcutContext.Global & ShortcutContext.Player) != 0);
        profile.Set(Command("player.next-frame"), new("Enter"), ShortcutPlatform.Windows);
        Assert.Null(Resolver(profile).Resolve(new("Enter"), ShortcutContext.Player)); // Corrupt conflicting profile fails closed.
    }
    [Theory]
    [InlineData("F4", ShortcutModifiers.Alt)]
    [InlineData("F9", ShortcutModifiers.Meta)]
    [InlineData("Delete", ShortcutModifiers.None)]
    [InlineData("Escape", ShortcutModifiers.None)]
    [InlineData("Tab", ShortcutModifiers.None)]
    [InlineData("Q", ShortcutModifiers.Control | ShortcutModifiers.Alt)]
    [InlineData("F", ShortcutModifiers.Control)]
    [InlineData("C", ShortcutModifiers.Control)]
    [InlineData("Unknown", ShortcutModifiers.None)]
    public void ReservedGesturesCannotResolveOrBeAssigned(string key, ShortcutModifiers mods)
    {
        var gesture = new KeyboardGesture(key, mods);
        Assert.NotNull(Resolver().Validate(Command("player.play-pause"), gesture));
        Assert.Null(Resolver().Resolve(gesture, ShortcutContext.Player));
    }
    [Fact]
    public void PrimaryAndDefaultsCanDivergeByPlatform()
    {
        var primary = new KeyboardGesture("I", ShortcutModifiers.Primary);
        Assert.True(primary.Matches(new("I", ShortcutModifiers.Control), ShortcutPlatform.Windows));
        Assert.True(primary.Matches(new("I", ShortcutModifiers.Meta), ShortcutPlatform.MacOS));
        Assert.False(primary.Matches(new("I", ShortcutModifiers.Control), ShortcutPlatform.MacOS));
        Assert.Equal("Ctrl+I", primary.Display(ShortcutPlatform.Windows));
        Assert.Equal("Command+I", primary.Display(ShortcutPlatform.MacOS));
        var custom = Command("player.play-pause") with { MacDefault = new("P", ShortcutModifiers.Primary) };
        Assert.Equal(new("P", ShortcutModifiers.Primary), custom.Default(ShortcutPlatform.MacOS));
        Assert.Equal(new("Space"), custom.Default(ShortcutPlatform.Windows));
    }
    [Fact]
    public void RepeatPolicyDoesNotRearmHoldsOrRepeatToggles()
    {
        Assert.Null(Resolver().Resolve(new("Space"), ShortcutContext.Player, true));
        Assert.Null(Resolver().Resolve(new("C"), ShortcutContext.Player, true));
        Assert.Equal(Command("player.next-frame"), Resolver().Resolve(new("Right"), ShortcutContext.Player, true));
        Assert.Equal(Command("asset.flag-next"), Resolver().Resolve(new("Up", ShortcutModifiers.Control), ShortcutContext.Browser, true));
    }
}
