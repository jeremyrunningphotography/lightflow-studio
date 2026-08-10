using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class TrimEditorLifecycleTests
{
    [Fact]
    public async Task ApplyResult_SurvivesAsyncReleaseAndCommitsObservableRowState()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lifecycle = new TrimEditorCloseLifecycle(() => new ValueTask(release.Task));
        var closing = lifecycle.CloseAsync(true);
        Assert.False(closing.IsCompleted);

        release.SetResult();
        var dialogResult = await closing;
        Assert.True(dialogResult);

        var option = CreateOption();
        var changed = new List<string>();
        option.PropertyChanged += (_, args) => changed.Add(args.PropertyName!);
        var range = new MediaRange(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(70));
        var history = new RecordingHistory();

        Assert.True(TrimStatePersistence.ApplyDialogResult(dialogResult, option, range, history));
        Assert.Equal(range, option.TrimRange);
        Assert.True(option.HasActiveTrim);
        Assert.Equal("Edit Trim", option.TrimActionText);
        Assert.True(option.TrimIndicatorHasProportions);
        Assert.Equal(.2, option.TrimIndicatorStart, 3);
        Assert.Equal(.5, option.TrimIndicatorWidth, 3);
        Assert.Equal(range, history.SavedRange);
        Assert.Contains(nameof(BatchFileOption.TrimRange), changed);
        Assert.Contains(nameof(BatchFileOption.HasActiveTrim), changed);
        Assert.Contains(nameof(BatchFileOption.TrimActionText), changed);
        Assert.Contains(nameof(BatchFileOption.TrimIndicatorStart), changed);
        Assert.Contains(nameof(BatchFileOption.TrimIndicatorWidth), changed);

        var reopenedDraft = new TrimSelection(TimeSpan.FromSeconds(100), option.TrimRange);
        Assert.Equal(range, reopenedDraft.Cancel());
    }

    [Fact]
    public async Task CancelResult_SurvivesReleaseWithoutChangingExistingTrim()
    {
        var lifecycle = new TrimEditorCloseLifecycle(() => ValueTask.CompletedTask);
        var dialogResult = await lifecycle.CloseAsync(false);
        var option = CreateOption();
        var existing = new MediaRange(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(80));
        option.ApplyTrim(existing);
        var history = new RecordingHistory();

        Assert.False(TrimStatePersistence.ApplyDialogResult(dialogResult, option, null, history));
        Assert.Equal(existing, option.TrimRange);
        Assert.Null(history.SavedRange);
        Assert.False(history.Removed);
    }

    [Fact]
    public async Task ResetApply_RemovesTrimAndObservableIndicatorStateAfterRelease()
    {
        var option = CreateOption();
        var existing = new MediaRange(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(80));
        option.ApplyTrim(existing);
        var selection = new TrimSelection(TimeSpan.FromSeconds(100), existing);
        selection.Reset();
        var lifecycle = new TrimEditorCloseLifecycle(() => ValueTask.CompletedTask);
        var dialogResult = await lifecycle.CloseAsync(true);
        var history = new RecordingHistory();

        Assert.True(TrimStatePersistence.ApplyDialogResult(dialogResult, option, selection.Apply(), history));
        Assert.Null(option.TrimRange);
        Assert.False(option.HasActiveTrim);
        Assert.Equal("Trim", option.TrimActionText);
        Assert.True(history.Removed);
    }

    private static BatchFileOption CreateOption()
    {
        var option = new BatchFileOption(Path.GetFullPath("source.mp4"), "source.mp4", 100);
        option.ApplyMetadata(new MediaMetadata(1920, 1080, 30, 100, 100, "h264", true));
        return option;
    }

    private sealed class RecordingHistory : ITrimHistoryStore
    {
        public MediaRange? SavedRange { get; private set; }
        public bool Removed { get; private set; }
        public MediaRange? Restore(string path) => null;
        public void Save(string path, MediaRange range) => SavedRange = range;
        public void Remove(string path) => Removed = true;
    }
}
