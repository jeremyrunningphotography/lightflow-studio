using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

internal sealed class TestDescriptionStore : IAssetDescriptionStore
{
    internal Dictionary<Guid, AssetDescription> Values = [];
    internal IReadOnlyDictionary<Guid, long>? AppliedTargets;
    internal AssetDescriptionPatch? AppliedPatch;
    internal TaskCompletionSource? SaveStarted, SaveRelease;
    internal Exception? Failure;
    internal Func<IReadOnlyCollection<Guid>, Task<IReadOnlyDictionary<Guid, AssetDescription>>>? Read;
    public Task<IReadOnlyDictionary<Guid, AssetDescription>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        Read?.Invoke(ids) ?? Task.FromResult<IReadOnlyDictionary<Guid, AssetDescription>>(
            ids.ToDictionary(id => id, id => Values.GetValueOrDefault(id) ?? new(id)));
    public async Task ApplyAsync(IReadOnlyDictionary<Guid, long> revisions, AssetDescriptionPatch patch, CancellationToken cancellationToken = default)
    {
        patch.Validate(); AppliedTargets = revisions; AppliedPatch = patch;
        SaveStarted?.TrySetResult();
        if (SaveRelease is not null) await SaveRelease.Task;
        if (Failure is not null) throw Failure;
    }
}

public sealed class InspectorDescriptionEditorTests
{
    [Fact]
    public async Task MixedAndCommonValues_RequireExplicitSetOrClear_UntouchedFieldsAreOmitted()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var store = new TestDescriptionStore { Values = new() { [a] = new(a, "One", Notes: "same"), [b] = new(b, "Two", Notes: "same") } };
        using var editor = new InspectorDescriptionEditor(store);
        await editor.SetContextAsync([a, b], false);
        var title = editor.Fields.Single(f => f.Field == AssetDescriptionField.Title);
        var notes = editor.Fields.Single(f => f.Field == AssetDescriptionField.Notes);
        Assert.True(title.IsMixed); Assert.Equal("", title.Text); Assert.True(title.IsReadOnly);
        Assert.Equal("Common value", notes.ValueState);
        Assert.Equal("Not set", editor.Fields.Single(f => f.Field == AssetDescriptionField.CreatorOverride).ValueState);
        Assert.False(editor.CanApply);
        await editor.ApplyAsync(); Assert.Null(store.AppliedPatch);
        title.Operation = DescriptionEditOperation.Set; title.Text = " 新しい 🎬 ";
        notes.Operation = DescriptionEditOperation.Clear;
        await editor.SetContextAsync([b, a], false);
        Assert.Same(title, editor.Fields.Single(f => f.Field == AssetDescriptionField.Title));
        Assert.True(editor.HasDraft);
        await editor.ApplyAsync();
        Assert.Equal(2, store.AppliedPatch!.Values.Count);
        Assert.Equal(" 新しい 🎬 ", store.AppliedPatch.Values[AssetDescriptionField.Title]);
        Assert.Null(store.AppliedPatch.Values[AssetDescriptionField.Notes]);
        Assert.Equal(2, store.AppliedTargets!.Count);
        Assert.False(editor.HasDraft);
        Assert.Contains("Saved", editor.Status);
    }

    [Fact]
    public async Task SameContextPreservesDraft_ChangedSelectionAndPlayerTransitionDiscardIt()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        using var editor = new InspectorDescriptionEditor(new TestDescriptionStore());
        await editor.SetContextAsync([a], false);
        editor.Fields[0].Operation = DescriptionEditOperation.Set;
        editor.Fields[0].Text = "draft";
        await editor.SetContextAsync([a], false);
        Assert.True(editor.HasDraft); Assert.Equal("draft", editor.Fields[0].Text);
        await editor.SetContextAsync([b], false);
        Assert.False(editor.HasDraft); Assert.Contains("discarded", editor.Status);
        editor.Fields[0].Operation = DescriptionEditOperation.Clear;
        await editor.SetContextAsync([b], true);
        Assert.False(editor.HasDraft); Assert.Contains("discarded", editor.Status);
        await editor.SetContextAsync([b, null], false);
        Assert.False(editor.CanApply); Assert.False(editor.CanEdit);
    }

    [Fact]
    public async Task InFlightSaveKeepsCapturedTargets_AndDoesNotReplaceNewContextFields()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var store = new TestDescriptionStore { SaveStarted = new(), SaveRelease = new(), Values = new() { [b] = new(b, "B") } };
        using var editor = new InspectorDescriptionEditor(store);
        await editor.SetContextAsync([a], false);
        editor.Fields[0].Operation = DescriptionEditOperation.Set; editor.Fields[0].Text = "A";
        var saving = editor.ApplyAsync(); await store.SaveStarted.Task;
        Assert.False(editor.CanApply);
        await editor.SetContextAsync([b], false);
        Assert.False(editor.CanEdit);
        store.SaveRelease.SetResult(); await saving;
        Assert.Equal(a, Assert.Single(store.AppliedTargets!).Key);
        Assert.Equal("B", editor.Fields[0].Text);
        Assert.True(editor.CanEdit); Assert.Contains("previous", editor.Status);
    }

    [Fact]
    public async Task LateReadCannotOverwriteNewContext_EvenWhenStoreIgnoresCancellation()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var delayed = new TaskCompletionSource<IReadOnlyDictionary<Guid, AssetDescription>>();
        var store = new TestDescriptionStore { Read = ids => ids.Contains(a) ? delayed.Task :
            Task.FromResult<IReadOnlyDictionary<Guid, AssetDescription>>(new Dictionary<Guid, AssetDescription> { [b] = new(b, "B") }) };
        using var editor = new InspectorDescriptionEditor(store);
        var old = editor.SetContextAsync([a], false);
        await editor.SetContextAsync([b], false);
        delayed.SetResult(new Dictionary<Guid, AssetDescription> { [a] = new(a, "A") }); await old;
        Assert.Equal("B", editor.Fields[0].Text); Assert.True(editor.CanEdit);
    }

    [Fact]
    public async Task ConflictPreservesDraft_BlocksRetryUntilExplicitReload()
    {
        var store = new TestDescriptionStore { Failure = new AssetDescriptionConflictException() };
        using var editor = new InspectorDescriptionEditor(store);
        await editor.SetContextAsync([Guid.NewGuid()], false);
        editor.Fields[0].Operation = DescriptionEditOperation.Set; editor.Fields[0].Text = "draft";
        await editor.ApplyAsync();
        Assert.True(editor.HasDraft); Assert.False(editor.CanApply); Assert.Contains("not saved", editor.Status);
        Assert.Equal("draft", editor.Fields[0].Text);
        await editor.ReloadAsync(); Assert.False(editor.HasDraft); Assert.True(editor.CanEdit);
    }

    [Fact]
    public async Task EmptySetIsNotAnImplicitClear_AndFailureKeepsDraftEditable()
    {
        var store = new TestDescriptionStore();
        using var editor = new InspectorDescriptionEditor(store);
        await editor.SetContextAsync([Guid.NewGuid()], false);
        editor.Fields[0].Operation = DescriptionEditOperation.Set;
        await editor.ApplyAsync();
        Assert.Null(store.AppliedPatch); Assert.True(editor.HasDraft); Assert.True(editor.CanApply);
        Assert.Contains("choose Clear", editor.Status);
        editor.Fields[0].Text = "valid"; store.Failure = new IOException("disk full");
        await editor.ApplyAsync(); Assert.Contains("not saved", editor.Status); Assert.True(editor.HasDraft);
    }
}
