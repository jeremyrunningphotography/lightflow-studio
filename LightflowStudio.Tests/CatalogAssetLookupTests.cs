using System.Collections.Concurrent;
using System.Diagnostics;
using LightflowStudio;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class CatalogAssetLookupTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(13, 1)]
    [InlineData(250, 1)]
    [InlineData(1201, 3)]
    public async Task Lookup_MaterializesOnlyDistinctRequestedRowsInBoundedBatches(int count, int expectedQueries)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "asset-lookup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var session = (await new CatalogDatabaseService(LightflowStorageLocations.Create(directory)).CreateNewAsync()).Session!;
            var ids = Enumerable.Range(0, 10000).Select(_ => Guid.NewGuid()).ToArray();
            using (var connection = session.OpenConnection())
            {
                using var transaction = connection.BeginTransaction();
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                var root = Guid.NewGuid().ToString("D");
                command.CommandText = "INSERT INTO MediaRoots (RootId,DisplayName,SourceStatus,CreatedUtc,UpdatedUtc) VALUES ($root,'Test','online',$now,$now);";
                command.Parameters.AddWithValue("$root", root);
                command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
                command.ExecuteNonQuery();
                command.CommandText = """
                    INSERT INTO MediaAssets (AssetId,RootId,RelativePath,RelativePathKey,MediaType,
                        FileSizeBytes,LastWriteUtcTicks,SourceStatus,CreatedUtc,UpdatedUtc)
                    VALUES ($id,$root,$path,$path,'image',10,1,'available',$now,$now);
                    """;
                var idParameter = command.Parameters.Add("$id", SqliteType.Text);
                var pathParameter = command.Parameters.Add("$path", SqliteType.Text);
                foreach (var id in ids)
                {
                    idParameter.Value = id.ToString("D");
                    pathParameter.Value = $"{id:D}.jpg";
                    command.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            using var parent = new Activity("lookup-test").Start();
            var batches = new ConcurrentBag<Activity>();
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "LightflowStudio.Browser",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity =>
                {
                    if (activity.TraceId == parent.TraceId && activity.OperationName == "catalog.assets_by_id.batch")
                        batches.Add(activity);
                }
            };
            ActivitySource.AddActivityListener(listener);
            // Duplicates span batch boundaries; unknown IDs must be omitted rather than fabricated.
            var requested = ids.Take(count).Concat(ids.Take(count).Reverse()).ToList();
            if (count > 0) requested.Add(Guid.NewGuid());
            var repository = new CatalogMediaAssetRepository(() => session);
            var result = await repository.GetManyAsync(requested);
            Assert.Equal(ids.Take(count).Order(), result.Keys.Order());
            Assert.Equal(expectedQueries, batches.Count);
            Assert.Equal(count, batches.Sum(batch => (int)batch.GetTagItem("materialized_rows")!));
            Assert.All(batches, batch => Assert.InRange((int)batch.GetTagItem("asset_ids")!, 1, 500));
            Assert.Equal(count == 0 ? 0 : count + 1, batches.Sum(batch => (int)batch.GetTagItem("asset_ids")!));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task EmptyLookupNeedsNoCatalogAndCancellationIsHonored()
    {
        var repository = new CatalogMediaAssetRepository(() => throw new InvalidOperationException("No Catalog needed"));
        Assert.Empty(await repository.GetManyAsync([]));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.GetManyAsync([], new CancellationToken(true)));
    }
}
