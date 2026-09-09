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
    public async Task MixedAndCommonValues_DirectEditsAndClear_OmitUntouchedFields()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var store = new TestDescriptionStore { Values = new() { [a] = new(a, "One", Notes: "same"), [b] = new(b, "Two", Notes: "same") } };
        using var editor = new InspectorDescriptionEditor(store, _ => true);
        await editor.SetContextAsync([a, b], false);
        var title = editor.Fields.Single(f => f.Field == AssetDescriptionField.Title);
        var notes = editor.Fields.Single(f => f.Field == AssetDescriptionField.Notes);
        Assert.True(title.IsMixed); Assert.Equal("", title.Text); Assert.False(title.IsDirty);
        Assert.Equal("", notes.ValueState);
        Assert.Equal("", editor.Fields.Single(f => f.Field == AssetDescriptionField.CreatorOverride).ValueState);
        Assert.False(editor.CanApply);
        await editor.ApplyAsync(); Assert.Null(store.AppliedPatch);
        title.Text = " 新しい 🎬 ";
        notes.Text = "";
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
    public async Task SameContextPreservesDraft_UnapprovedContextChangeCannotDiscardIt()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        using var editor = new InspectorDescriptionEditor(new TestDescriptionStore(), _ => true);
        await editor.SetContextAsync([a], false);

        editor.Fields[0].Text = "draft";
        await editor.SetContextAsync([a], false);
        Assert.True(editor.HasDraft); Assert.Equal("draft", editor.Fields[0].Text);
        await editor.SetContextAsync([b], false);
        Assert.True(editor.HasDraft); Assert.Equal("draft", editor.Fields[0].Text);
        Assert.True(await editor.ResolveTransitionAsync(DescriptionTransitionChoice.Discard));
        await editor.SetContextAsync([b], false);
        Assert.False(editor.HasDraft); Assert.Contains("discarded", editor.Status);
        await editor.SetContextAsync([b], true);
        Assert.Contains("discarded", editor.Status); // Selection then Viewer transition must not erase feedback.
        editor.Fields[0].Text = "another draft";
        Assert.DoesNotContain("discarded", editor.Status);
        Assert.True(await editor.ResolveTransitionAsync(DescriptionTransitionChoice.Discard));
        await editor.SetContextAsync([b], false);
        Assert.False(editor.HasDraft); Assert.Contains("discarded", editor.Status);
        await editor.SetContextAsync([b, null], false);
        Assert.False(editor.CanApply); Assert.False(editor.CanEdit);
    }

    [Fact]
    public async Task InFlightSaveKeepsCapturedTargets_AndBlocksContextReplacement()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var store = new TestDescriptionStore { SaveStarted = new(), SaveRelease = new(), Values = new() { [b] = new(b, "B") } };
        using var editor = new InspectorDescriptionEditor(store, _ => true);
        await editor.SetContextAsync([a], false);
        editor.Fields[0].Text = "A";
        var saving = editor.ApplyAsync(); await store.SaveStarted.Task;
        Assert.False(editor.CanApply);
        await editor.SetContextAsync([b], false);
        Assert.False(editor.CanEdit);
        Assert.Equal("A", editor.Fields[0].Text);
        Assert.False(await editor.ResolveTransitionAsync(DescriptionTransitionChoice.Discard));
        store.SaveRelease.SetResult(); await saving;
        Assert.Equal(a, Assert.Single(store.AppliedTargets!).Key);
        await editor.SetContextAsync([b], false);
        Assert.Equal("B", editor.Fields[0].Text);
        Assert.True(editor.CanEdit);
    }

    [Fact]
    public async Task LateReadCannotOverwriteNewContext_EvenWhenStoreIgnoresCancellation()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var delayed = new TaskCompletionSource<IReadOnlyDictionary<Guid, AssetDescription>>();
        var store = new TestDescriptionStore { Read = ids => ids.Contains(a) ? delayed.Task :
            Task.FromResult<IReadOnlyDictionary<Guid, AssetDescription>>(new Dictionary<Guid, AssetDescription> { [b] = new(b, "B") }) };
        using var editor = new InspectorDescriptionEditor(store, _ => true);
        var old = editor.SetContextAsync([a], false);
        await editor.SetContextAsync([b], false);
        delayed.SetResult(new Dictionary<Guid, AssetDescription> { [a] = new(a, "A") }); await old;
        Assert.Equal("B", editor.Fields[0].Text); Assert.True(editor.CanEdit);
    }

    [Fact]
    public async Task ConflictPreservesDraft_BlocksRetryUntilExplicitReload()
    {
        var store = new TestDescriptionStore { Failure = new AssetDescriptionConflictException() };
        using var editor = new InspectorDescriptionEditor(store, _ => true);
        await editor.SetContextAsync([Guid.NewGuid()], false);
        editor.Fields[0].Text = "draft";
        await editor.ApplyAsync();
        Assert.True(editor.HasDraft); Assert.False(editor.CanApply); Assert.Contains("not saved", editor.Status);
        Assert.Equal("draft", editor.Fields[0].Text);
        await editor.ReloadAsync(); Assert.False(editor.HasDraft); Assert.True(editor.CanEdit);
    }

    [Fact]
    public async Task InitiallyEmptyIsNoOp_AndFailureKeepsDraftEditable()
    {
        var store = new TestDescriptionStore();
        using var editor = new InspectorDescriptionEditor(store, _ => true);
        await editor.SetContextAsync([Guid.NewGuid()], false);

        await editor.ApplyAsync();
        Assert.Null(store.AppliedPatch); Assert.False(editor.HasDraft); Assert.False(editor.CanApply);
        editor.Fields[0].Text = "valid"; store.Failure = new IOException("disk full");
        await editor.ApplyAsync(); Assert.Contains("not saved", editor.Status); Assert.True(editor.HasDraft);
    }

    [Fact]
    public async Task MixedRenderEchoIsNoOp_TypingThenDeletingIsIntentionalClear_OnlyEditedFieldIsPatched()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var store = new TestDescriptionStore { Values = new() { [a] = new(a, "A", Notes: "one"), [b] = new(b, "B", Notes: "two") } };
        var confirmations = 0;
        using var editor = new InspectorDescriptionEditor(store, _ => { confirmations++; return true; });
        await editor.SetContextAsync([a, b], false);
        editor.Fields[0].Text = ""; // Binding initialization/focus is not an edit.
        await editor.ApplyAsync();
        Assert.Equal(0, confirmations); Assert.Null(store.AppliedPatch);
        editor.Fields[0].Text = "typed"; editor.Fields[0].Text = "";
        await editor.ApplyAsync();
        Assert.Equal(1, confirmations);
        var changed = Assert.Single(store.AppliedPatch!.Values);
        Assert.Equal(AssetDescriptionField.Title, changed.Key); Assert.Null(changed.Value);
    }

    [Fact]
    public async Task ApplyConfirmsOnlyActualChanges_CancelAndRevertDoNotWrite()
    {
        var id = Guid.NewGuid();
        var store = new TestDescriptionStore { Values = new() { [id] = new(id, "original", Notes: "line\nline") } };
        var requests = new List<DescriptionConfirmation>(); var accept = false;
        using var editor = new InspectorDescriptionEditor(store, request => { requests.Add(request); return accept; });
        await editor.SetContextAsync([id], false);
        editor.Fields[0].Text = "changed"; editor.Fields[0].Text = "original";
        editor.Fields[2].Text = "line\r\nline"; // Presentation line endings do not dirty an untouched value.
        await editor.ApplyAsync(); Assert.Empty(requests); Assert.Null(store.AppliedPatch);
        editor.Fields[0].Text = "changed";
        await editor.ApplyAsync();
        Assert.Empty(requests); Assert.False(editor.HasDraft);
        Assert.Equal("changed", Assert.Single(store.AppliedPatch!.Values).Value);
        await editor.SetContextAsync([id, Guid.NewGuid()], false);
        store.AppliedPatch = null;
        editor.Fields[0].Text = "bulk";
        await editor.ApplyAsync();
        Assert.True(Assert.Single(requests).IsApply); Assert.True(editor.HasDraft); Assert.Null(store.AppliedPatch);
        accept = true; await editor.ApplyAsync();
        Assert.Equal(2, requests.Count); Assert.Equal(2, requests[1].AssetCount); Assert.Equal(["Title"], requests[1].Fields);
    }

    [Fact]
    public async Task ReloadConfirmsOnlyDirtyEdits_CancelKeepsDraft_EmptySelectionKeepsDiscardFeedback()
    {
        var requests = new List<DescriptionConfirmation>(); var accept = false;
        using var editor = new InspectorDescriptionEditor(new TestDescriptionStore(), request => { requests.Add(request); return accept; });
        await editor.SetContextAsync([Guid.NewGuid()], false);
        await editor.ReloadAsync(); Assert.Empty(requests);
        editor.Fields[0].Text = "draft";
        await editor.ReloadAsync();
        Assert.False(Assert.Single(requests).IsApply); Assert.Equal("draft", editor.Fields[0].Text);
        accept = true; await editor.ReloadAsync(); Assert.False(editor.HasDraft);
        await editor.ReloadAsync(); Assert.Equal(2, requests.Count);
        editor.Fields[0].Text = "draft";
        await editor.ResolveTransitionAsync(DescriptionTransitionChoice.Discard);
        await editor.SetContextAsync([], false); Assert.Contains("discarded", editor.Status);
    }

    [Fact]
    public async Task ConfirmationContextChangeCannotRetargetWrite_AndInvalidTextDoesNotConfirm()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var store = new TestDescriptionStore(); var calls = 0;
        InspectorDescriptionEditor? editor = null;
        using (editor = new InspectorDescriptionEditor(store, request =>
        {
            calls++;
            Assert.False(editor!.CanApply); Assert.False(editor.CanReload);
            _ = editor.SetContextAsync([b], false);
            return true;
        }))
        {
            await editor.SetContextAsync([a, Guid.NewGuid()], false);
            editor.Fields[0].Text = "invalid\nTitle";
            await editor.ApplyAsync(); Assert.Equal(0, calls);
            editor.Fields[0].Text = "valid";
            await editor.ApplyAsync(); Assert.Equal(1, calls); Assert.Equal(2, store.AppliedTargets!.Count);
            Assert.Contains(a, store.AppliedTargets.Keys); Assert.DoesNotContain(b, store.AppliedTargets.Keys);
            Assert.False(editor.HasDraft);
        }
    }
}
