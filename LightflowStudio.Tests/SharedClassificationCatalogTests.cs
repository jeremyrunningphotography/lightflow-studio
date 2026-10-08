using Lightflow.Actions;
using Lightflow.Application;
using Lightflow.Domain;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class SharedClassificationCatalogTests
{
    [Fact]
    public async Task SharedExecutorKeepsWholeBatchAndCompletionAdmittedWhileCatalogDrains()
    {
        await WithCatalog(async (session, ids, store) => {
            var blocking = new BlockingStore(store);
            var service = new AssetClassificationService(blocking, session.Mutations);
            Task<CatalogMutationLifecycle.Quiescence>? drain = null;
            var pending = service.ExecuteAsync(ids, new SetRatingArguments(4), values => {
                Assert.NotNull(drain); Assert.False(drain.IsCompleted);
                Assert.All(values, v => { Assert.Equal(4, v.Rating); Assert.Equal(["keep"], v.Keywords); });
                return values;
            });
            await blocking.FirstCommitted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            drain = session.Mutations.QuiesceAsync();
            Assert.False(drain.IsCompleted);
            blocking.Continue.SetResult();
            var values = await pending.WaitAsync(TimeSpan.FromSeconds(10));
            using var quiet = await drain.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(ids, values.Select(v => v.AssetId));
            Assert.All((await store.GetAsync(ids)).Values, v => Assert.Equal(4, v.Rating));
        });
    }

    [Fact]
    public async Task RealCatalogPartialFailureRetainsFirstCommitAndDispatcherReportsFailed()
    {
        await WithCatalog(async (session, ids, store) => {
            var missing = Guid.NewGuid();
            var port = new Port(new AssetClassificationService(store, session.Mutations), [ids[0], missing, ids[1]]);
            var result = await new BrowserActions(port).InvokeAsync(new(BrowserActions.SetRating,
                new SetRatingArguments(5), new("r1.controller", ActionInputKind.Controller), Guid.NewGuid(), port.Context.Target));
            Assert.Equal(ActionOutcome.Failed, result.Outcome); Assert.False(port.Completed);
            var values = await store.GetAsync(ids);
            Assert.Equal(5, values[ids[0]].Rating); Assert.Equal(2, values[ids[0]].Revision);
            Assert.Equal(0, values[ids[1]].Rating); Assert.Equal(1, values[ids[1]].Revision);
            using var quiet = await session.Mutations.QuiesceAsync().WaitAsync(TimeSpan.FromSeconds(10));
        });
    }

    [Fact]
    public async Task RealCatalogReadbackAndNoOpsPreserveRevisionAndFreshConcurrentFields()
    {
        await WithCatalog(async (session, ids, store) => {
            var service = new AssetClassificationService(store, session.Mutations);
            var second = new CatalogAssetClassificationStore(() => session);
            await Task.WhenAll(service.ExecuteAsync(ids, new SetRatingArguments(3), v => v),
                service.ExecuteAsync(ids, new SetFlagArguments(ClassificationFlag.Picked), v => v),
                second.UpdateAsync(ids[0], v => v with { Keywords = [.. v.Keywords, "fresh"] }));
            var before = await store.GetAsync(ids);
            var noOp = await service.ExecuteAsync(ids, new SetRatingArguments(3), v => v);
            foreach (var value in noOp) {
                Assert.Equal(before[value.AssetId].Revision, value.Revision);
                Assert.Equal(AssetFlag.Picked, value.Flag);
                Assert.Equal(value.AssetId == ids[0] ? new[] { "keep", "fresh" } : new[] { "keep" }, value.Keywords);
            }
        });
    }

    private static async Task WithCatalog(Func<CatalogDatabaseSession, Guid[], CatalogAssetClassificationStore, Task> action)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "shared-classification-tests", Guid.NewGuid().ToString("N"));
        var session = (await new CatalogDatabaseService(LightflowStorageLocations.Create(directory)).CreateNewAsync()).Session!;
        try {
            var root = Guid.NewGuid(); var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
            using (var connection = session.OpenConnection())
            using (var command = connection.CreateCommand()) {
                command.CommandText = "INSERT INTO MediaRoots (RootId,DisplayName,SourceStatus,CreatedUtc,UpdatedUtc) VALUES ($root,'Media','online',$now,$now);";
                command.Parameters.AddWithValue("$root", root.ToString("D"));
                command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O")); command.ExecuteNonQuery();
                command.CommandText = "INSERT INTO MediaAssets (AssetId,RootId,RelativePath,RelativePathKey,MediaType,FileSizeBytes,LastWriteUtcTicks,SourceStatus,CreatedUtc,UpdatedUtc) VALUES ($asset,$root,$path,$path,'video',1,1,'available',$now,$now);";
                command.Parameters.Add("$asset", Microsoft.Data.Sqlite.SqliteType.Text);
                command.Parameters.Add("$path", Microsoft.Data.Sqlite.SqliteType.Text);
                for (var i = 0; i < ids.Length; i++) {
                    command.Parameters["$asset"].Value = ids[i].ToString("D"); command.Parameters["$path"].Value = $"clip-{i}.mp4";
                    command.ExecuteNonQuery();
                }
            }
            var store = new CatalogAssetClassificationStore(() => session);
            foreach (var id in ids) await store.SaveAsync(new(id, 0, AssetFlag.Unflagged, null, ["keep"]));
            await action(session, ids, store);
        }
        finally { await session.DisposeAsync(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class BlockingStore(IAssetClassificationStore store) : IAssetClassificationStore
    {
        public TaskCompletionSource FirstCommitted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        public Task<IReadOnlyDictionary<Guid, AssetClassification>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) => store.GetAsync(ids, cancellationToken);
        public Task SaveAsync(AssetClassification value, CancellationToken cancellationToken = default) => store.SaveAsync(value, cancellationToken);
        public async Task<AssetClassification> UpdateAsync(Guid id, Func<AssetClassification, AssetClassification> mutate, CancellationToken token = default)
        {
            var value = await store.UpdateAsync(id, mutate, token);
            if (++_calls == 1) { FirstCommitted.SetResult(); await Continue.Task.WaitAsync(token); }
            return value;
        }
    }
    private sealed class Port(AssetClassificationService service, IReadOnlyList<Guid> ids) : IBrowserActionPort
    {
        public bool Completed;
        public BrowserActionContext Context { get; } = new(new(Guid.NewGuid(), 1,
            new(BrowserScopeKind.Collection, Guid.NewGuid()), 1), true, true, ids, ids[0], true);
        public Task<ActionResult> ClassifyAsync(BrowserActionTarget target, IReadOnlyList<Guid> capturedAssetIds, ActionArguments arguments, CancellationToken token) =>
            service.ExecuteAsync(capturedAssetIds, arguments, _ => { Completed = true; return new ActionResult(ActionOutcome.Completed); }, token);
        public Task<ActionResult> NavigateAsync(BrowserActionTarget target, NavigateSelectionArguments arguments, CancellationToken token) => throw new NotSupportedException();
        public Task<ActionResult> OpenAsync(BrowserActionTarget target, OpenBrowserArguments? arguments, CancellationToken token) => throw new NotSupportedException();
    }
}
