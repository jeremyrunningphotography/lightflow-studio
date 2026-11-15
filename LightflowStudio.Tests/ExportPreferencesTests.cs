using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class ExportPreferencesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("export-preferences-").FullName;
    private string Settings => Path.Combine(_root, "settings.json");
    private string Preferences => ExportDefaultsStore.PathFor(Settings);

    [Fact]
    public async Task FreshDefaultsAndRootsAreIndependent()
    {
        var session = await ExportPreferenceSession.OpenAsync(Settings);
        Assert.Equal(ExportDestinationMode.SameFolderAsOriginal, session.Current.DestinationMode);
        Assert.Null(session.Current.SpecificFolder);
        Assert.True(session.Current.CreateSubfolder); Assert.Equal("Exports", session.Current.SubfolderName);
        Assert.Equal(NamePartKind.OriginalName, Assert.Single(session.Current.NameParts).Kind);
        Assert.False(File.Exists(Preferences));
        session.Update(session.Current with { SubfolderName = "Mine" }); await session.FlushAsync();
        var independent = await ExportPreferenceSession.OpenAsync(Path.Combine(_root, "other", "settings.json"));
        Assert.Equal("Exports", independent.Current.SubfolderName);
        Assert.Equal("Mine", (await ExportPreferenceSession.OpenAsync(Settings)).Current.SubfolderName);
    }

    [Fact]
    public async Task EveryReusableFieldRoundTripsAndReturnToDefaultsIsSparse()
    {
        var session = await ExportPreferenceSession.OpenAsync(Settings);
        var options = new EncodingOptions { EncoderPreset = 3, Tune = EncoderTune.LowLatency,
            RateControl = RateControlMode.VariableBitrate, Quality = 21, TargetBitrateMbps = 50,
            MaxBitrateMbps = 90, Multipass = MultipassMode.QuarterResolution, SpatialAq = false,
            TemporalAq = false, AqStrength = 11, PixelFormat = VideoPixelFormat.P010,
            FrameRate = 29.97, Deinterlace = true, AudioMode = AudioEncodingMode.None, FastStart = false };
        var value = session.Current with { DestinationMode = ExportDestinationMode.SpecificFolder,
            SpecificFolder = Path.Combine(_root, "Offline"), CreateSubfolder = false, SubfolderName = "Deliveries",
            NameParts = [new(NamePartKind.CustomText, ""), new(NamePartKind.OriginalName), new(NamePartKind.CustomText, "Custom"), new(NamePartKind.Sequence001)],
            Separator = NamePartSeparator.Space, Container = ExportContainerChoice.Mkv, Codec = ExportCodecChoice.Hevc,
            Resolution = OutputResolution.Hd720, Encoding = options,
            Camera = new(ColorStagePolicyMode.NoLut), Creative = new(ColorStagePolicyMode.Override, Guid.NewGuid(), new string('a',64), "Missing") };
        session.Update(value); await session.FlushAsync();
        var reopened = await ExportPreferenceSession.OpenAsync(Settings);
        Assert.Equal(JsonSerializer.Serialize(value), JsonSerializer.Serialize(reopened.Current));
        Assert.Equal("", reopened.Current.NameParts[0].Text);
        session.Update(session.Defaults); await session.FlushAsync();
        using var stored = JsonDocument.Parse(File.ReadAllText(Preferences));
        Assert.Empty(stored.RootElement.GetProperty("Overrides").EnumerateObject());
        Assert.DoesNotContain("Overwrite", File.ReadAllText(Preferences));
    }

    [Fact]
    public async Task IndividualDefaultReturnDoesNotEraseOtherOverrides()
    {
        var session = await ExportPreferenceSession.OpenAsync(Settings);
        session.Update(session.Current with { CreateSubfolder = false, SubfolderName = "mine" });
        session.Update(session.Current with { CreateSubfolder = true }); await session.FlushAsync();
        var node = JsonNode.Parse(File.ReadAllText(Preferences))!;
        Assert.Null(node["Overrides"]!["CreateSubfolder"]);
        Assert.Equal("mine", node["Overrides"]!["SubfolderName"]!.GetValue<string>());
    }

    [Fact]
    public async Task LegacyRecipeSurvivesMigrationResetAndRestart()
    {
        var legacy = new EncodingOptions { Quality = 14, EncoderPreset = 4, FrameRate = 60, AudioMode = AudioEncodingMode.Aac };
        File.WriteAllText(Preferences, JsonSerializer.Serialize(legacy));
        var session = await ExportPreferenceSession.OpenAsync(Settings);
        Assert.Equal(14, session.Current.Encoding.Quality); Assert.Equal(0, session.Current.Encoding.FrameRate);
        Assert.Equal(AudioEncodingMode.Copy, session.Current.Encoding.AudioMode);
        session.Update(session.Current with { Encoding = session.Current.Encoding with { Quality = 30 } });
        await session.FlushAsync(); session.Reset(); await session.FlushAsync();
        Assert.Equal(14, (await ExportPreferenceSession.OpenAsync(Settings)).Current.Encoding.Quality);
        Assert.Equal(legacy, ExportDefaultsStore.Load(Settings));
        Assert.True(File.Exists(Preferences + ".backup"));
    }

    [Fact]
    public async Task RecentFoldersDeduplicateLimitRetainOfflineAndSurviveReset()
    {
        var session = await ExportPreferenceSession.OpenAsync(Settings);
        for (var i = 0; i < 12; i++) session.CommitFolder(Path.Combine(_root, "offline" + i));
        session.CommitFolder(Path.Combine(_root, "offline5").ToUpperInvariant() + Path.DirectorySeparatorChar);
        session.Update(session.Current with { SpecificFolder = Path.Combine(_root,"offline5") });
        session.Reset(); await session.FlushAsync();
        var reopened = await ExportPreferenceSession.OpenAsync(Settings);
        Assert.Equal(10, reopened.RecentFolders.Count);
        Assert.EndsWith("OFFLINE5", reopened.RecentFolders[0]);
        Assert.Null(reopened.Current.SpecificFolder); Assert.False(reopened.HasOverrides);
        Assert.False(Directory.Exists(reopened.RecentFolders[0]));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"SchemaVersion\":99,\"LegacyEncodingBaseline\":{},\"Overrides\":{},\"RecentFolders\":[]}")]
    [InlineData("{\"SchemaVersion\":1}")]
    [InlineData("{\"Quality\":999}")]
    public async Task InvalidStorageIsPreservedUntilExplicitRecovery(string content)
    {
        File.WriteAllText(Preferences, content);
        await Assert.ThrowsAsync<InvalidDataException>(() => ExportPreferenceSession.OpenAsync(Settings));
        Assert.Equal(content, File.ReadAllText(Preferences));
        var recovered = await ExportPreferenceSession.RecoverAsync(Settings);
        recovered.Reset(); await recovered.FlushAsync();
        Assert.Equal(content, File.ReadAllText(Assert.Single(Directory.GetFiles(_root, "*.recovery-*.json"))));
        Assert.False((await ExportPreferenceSession.OpenAsync(Settings)).HasOverrides);
    }

    [Fact]
    public async Task UnknownFieldsSurviveOrdinaryEditsWithoutBecomingKnownPreferences()
    {
        var session = await ExportPreferenceSession.OpenAsync(Settings); session.Reset(); await session.FlushAsync();
        var node = JsonNode.Parse(File.ReadAllText(Preferences))!;
        node["FutureEnvelope"] = 7; node["Overrides"]!["FutureOption"] = "keep";
        node["Overrides"]!["Encoding"] = new JsonObject { ["FutureEncodingOption"] = true };
        File.WriteAllText(Preferences, node.ToJsonString());
        session = await ExportPreferenceSession.OpenAsync(Settings);
        session.Update(session.Current with { SubfolderName = "changed", Encoding = session.Current.Encoding with { Quality = 25 } });
        await session.FlushAsync(); node = JsonNode.Parse(File.ReadAllText(Preferences))!;
        Assert.Equal(7, node["FutureEnvelope"]!.GetValue<int>());
        Assert.Equal("keep", node["Overrides"]!["FutureOption"]!.GetValue<string>());
        Assert.True(node["Overrides"]!["Encoding"]!["FutureEncodingOption"]!.GetValue<bool>());
    }

    [Fact]
    public async Task LatestRapidEditAndResetCannotBeResurrectedByOlderWrites()
    {
        var session = await ExportPreferenceSession.OpenAsync(Settings);
        for (var i = 0; i < 100; i++) session.Update(session.Current with { SubfolderName = "edit" + i });
        session.Reset(); await session.FlushAsync();
        Assert.Equal("Exports", (await ExportPreferenceSession.OpenAsync(Settings)).Current.SubfolderName);
        Assert.False(Directory.GetFiles(_root, "*.tmp").Any());
    }

    [Fact]
    public async Task ExternalWriteFailsWithoutClobberingIt()
    {
        var session = await ExportPreferenceSession.OpenAsync(Settings);
        session.Update(session.Current with { SubfolderName = "mine" });
        File.WriteAllText(Preferences, "external data"); await session.FlushAsync();
        Assert.NotNull(session.SaveError); Assert.Equal("external data", File.ReadAllText(Preferences));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
