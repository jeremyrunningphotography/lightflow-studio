using Lightflow.Actions;
using Lightflow.Domain;
using Xunit;

namespace Lightflow.Application.Tests;

public sealed class AssetClassificationServiceTests
{
    [Fact]
    public async Task CapturedOrderAndCompletionRemainInsideSingleAdmission()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var replacement = Guid.NewGuid();
        var ids = new[] { second, first };
        var admission = new Admission { Hold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var store = new Store(first, second, replacement);
        var pending = new AssetClassificationService(store, admission).ExecuteAsync(ids, new SetRatingArguments(3), values => {
            Assert.True(admission.Active);
            Assert.Equal(new[] { second, first }, values.Select(v => v.AssetId));
            Assert.All(values, v => Assert.Equal(3, v.Rating));
            return values;
        });
        ids[0] = replacement;
        Assert.Empty(store.Calls);
        admission.Hold.SetResult();
        await pending;
        Assert.Equal(new[] { second, first }, store.Calls);
        Assert.Equal(0, store.Values[replacement].Rating);
        Assert.Equal(1, admission.Count);
        Assert.False(admission.Active);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public async Task RatingAssignmentAndMenuTogglePreserveOtherFreshFields(int rating)
    {
        var id = Guid.NewGuid(); var store = new Store(id); var admission = new Admission();
        store.Values[id] = new(id, rating, AssetFlag.Rejected, AssetColorLabel.Purple, ["keep"], 19);
        var service = new AssetClassificationService(store, admission);
        var assigned = await service.ExecuteAsync([id], new SetRatingArguments(rating), values => values[0]);
        Assert.Equal(rating, assigned.Rating); Assert.Equal(19, assigned.Revision);
        var toggled = await service.ExecuteAsync([id], new SetRatingArguments(rating, true), values => values[0]);
        Assert.Equal(0, toggled.Rating);
        Assert.Equal(AssetFlag.Rejected, toggled.Flag); Assert.Equal(AssetColorLabel.Purple, toggled.ColorLabel);
        Assert.Equal(["keep"], toggled.Keywords);
        Assert.Equal(rating == 0 ? 19 : 20, toggled.Revision);
    }

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(1)]
    public async Task FlagsAssignAndStepWithoutWrapping(int requested)
    {
        var id = Guid.NewGuid(); var store = new Store(id);
        store.Values[id] = new(id, 4, AssetFlag.Unflagged, AssetColorLabel.Blue, ["keep"]);
        var service = new AssetClassificationService(store, new Admission());
        var assigned = await service.ExecuteAsync([id], new SetFlagArguments((ClassificationFlag)requested), v => v[0]);
        Assert.Equal((AssetFlag)requested, assigned.Flag);
        var next = await service.ExecuteAsync([id], new StepFlagArguments(TraversalDirection.Next), v => v[0]);
        Assert.Equal((AssetFlag)Math.Min(1, requested + 1), next.Flag);
        for (var i = 0; i < 3; i++) await service.ExecuteAsync([id], new StepFlagArguments(TraversalDirection.Next), v => v[0]);
        var revisionBeforeClamp = store.Values[id].Revision;
        var clamped = await service.ExecuteAsync([id], new StepFlagArguments(TraversalDirection.Next), v => v[0]);
        Assert.Equal(AssetFlag.Picked, clamped.Flag);
        Assert.Equal(revisionBeforeClamp, clamped.Revision);
        for (var i = 0; i < 3; i++) await service.ExecuteAsync([id], new StepFlagArguments(TraversalDirection.Previous), v => v[0]);
        Assert.Equal(AssetFlag.Rejected, store.Values[id].Flag);
        Assert.Equal(4, clamped.Rating); Assert.Equal(AssetColorLabel.Blue, clamped.ColorLabel); Assert.Equal(["keep"], clamped.Keywords);
    }

    [Theory]
    [InlineData(null)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public async Task LabelsMapAndClearWithoutChangingUnrelatedFields(int? requested)
    {
        var id = Guid.NewGuid(); var store = new Store(id);
        store.Values[id] = new(id, 5, AssetFlag.Picked, AssetColorLabel.Blue, ["keep"]);
        var label = requested is { } number ? (ClassificationColorLabel?)number : null;
        var result = await new AssetClassificationService(store, new Admission()).ExecuteAsync([id], new SetColorLabelArguments(label), v => v[0]);
        Assert.Equal(requested is { } value ? (AssetColorLabel?)value : null, result.ColorLabel);
        Assert.Equal(5, result.Rating); Assert.Equal(AssetFlag.Picked, result.Flag); Assert.Equal(["keep"], result.Keywords);
    }

    [Fact]
    public async Task PartialFailurePreservesEarlierCommitAndDoesNotComplete()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var third = Guid.NewGuid();
        var store = new Store(first, second, third) { Fail = second }; var admission = new Admission();
        var completed = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AssetClassificationService(store, admission)
            .ExecuteAsync([first, second, third], new SetRatingArguments(5), _ => completed = true));
        Assert.Equal(5, store.Values[first].Rating); Assert.Equal(0, store.Values[third].Rating);
        Assert.Equal(new[] { first, second }, store.Calls);
        Assert.False(completed); Assert.False(admission.Active);
    }

    [Fact]
    public async Task CancellationBetweenAssetsDoesNotBecomeSuccessOrUndoCommit()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); using var cancellation = new CancellationTokenSource();
        var store = new Store(first, second) { AfterUpdate = cancellation.Cancel }; var admission = new Admission();
        var completed = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AssetClassificationService(store, admission)
            .ExecuteAsync([first, second], new SetRatingArguments(2), _ => completed = true, cancellation.Token));
        Assert.Equal(2, store.Values[first].Rating); Assert.Equal(0, store.Values[second].Rating);
        Assert.False(completed); Assert.False(admission.Active);
    }

    [Fact]
    public async Task ReadOnlyAdapterNeverFallsBackToReadThenSave()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => new AssetClassificationService(new ReadOnlyStore(), new Admission())
            .ExecuteAsync([Guid.NewGuid()], new SetRatingArguments(1), v => v));
    }

    private sealed class Admission : IClassificationMutationAdmission
    {
        public TaskCompletionSource? Hold;
        public bool Active; public int Count;
        public async Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken token = default)
        {
            Count++;
            if (Hold is not null) await Hold.Task.WaitAsync(token);
            token.ThrowIfCancellationRequested(); Active = true;
            try { return await operation(); }
            finally { Active = false; }
        }
    }
    private sealed class Store(params Guid[] ids) : IAssetClassificationStore
    {
        public Dictionary<Guid, AssetClassification> Values = ids.ToDictionary(id => id, AssetClassification.Empty);
        public List<Guid> Calls = [];
        public Guid? Fail; public Action? AfterUpdate;
        public Task<IReadOnlyDictionary<Guid, AssetClassification>> GetAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Executor must use the fresh-value store operation.");
        public Task SaveAsync(AssetClassification value, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Executor must not provide a read/save fallback.");
        public Task<AssetClassification> UpdateAsync(Guid id, Func<AssetClassification, AssetClassification> mutate, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); Calls.Add(id);
            if (id == Fail) throw new InvalidOperationException("Asset failed.");
            var current = Values[id]; var updated = mutate(current);
            if (updated != current) Values[id] = updated with { Revision = current.Revision + 1 };
            AfterUpdate?.Invoke();
            return Task.FromResult(Values[id]);
        }
    }
    private sealed class ReadOnlyStore : IAssetClassificationStore
    {
        public Task<IReadOnlyDictionary<Guid, AssetClassification>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) => throw new Exception("Unexpected read.");
        public Task SaveAsync(AssetClassification value, CancellationToken cancellationToken = default) => throw new Exception("Unexpected save.");
    }
}
