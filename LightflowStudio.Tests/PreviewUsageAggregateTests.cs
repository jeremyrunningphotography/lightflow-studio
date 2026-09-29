using System.Runtime.InteropServices;
using LightflowStudio;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class PreviewUsageAggregateTests
{
    [Fact]
    public async Task LargeStore_CountUsesOneScalarStatementWithoutProjectingPayloadsOrChangingState()
    {
        var root = Directory.CreateTempSubdirectory("preview-usage-count-").FullName;
        try
        {
            var locations = LightflowStorageLocations.Create(root);
            await using var store = new PreviewStoreService(locations);
            await store.InitializeAsync();
            using var connection = new SqliteConnection($"Data Source={locations.PreviewsDatabasePath};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                WITH RECURSIVE entries(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM entries WHERE n<20000)
                INSERT INTO PreviewRecords(AssetId,FileSizeBytes,LastWriteUtcTicks,FingerprintVersion,
                    SourceFingerprint,SourceAvailability,MetadataState,ThumbnailState,StandardPreviewState,
                    MetadataJson,CreatedUtc,UpdatedUtc,MetadataRetryAfterUtc)
                SELECT printf('%08x-0000-0000-0000-000000000000',n),1,1,1,'fingerprint','available',
                    'failed','missing','missing',hex(zeroblob(512)),
                    'not-a-domain-date','not-a-domain-date','not-a-domain-date' FROM entries;
                """;
            command.ExecuteNonQuery();
            // The census must not deserialize domain dates/payloads. SQLite-valid opaque values
            // make any regression to PreviewRecord projection fail deterministically.
            Assert.Equal(20_000L, await store.CountAsync());
            var statements = new List<string>();
            var rows = 0;
            Trace callback = (kind, _, statement, _) =>
            {
                if (kind == 1) statements.Add(Marshal.PtrToStringUTF8(Sql(statement))!);
                if (kind == 4) rows++;
                return 0;
            };
            Assert.Equal(0, TraceV2(connection.Handle!.DangerousGetHandle(), 1 | 4, callback, 0));
            try { Assert.Equal(20_000L, PreviewStoreService.CountRecords(connection)); }
            finally
            {
                TraceV2(connection.Handle!.DangerousGetHandle(), 0, null, 0);
                GC.KeepAlive(callback);
            }
            Assert.Equal("SELECT COUNT(*) FROM PreviewRecords;", Assert.Single(statements));
            Assert.Equal(1, rows);
            command.CommandText = "SELECT COUNT(*) FROM PreviewRecords WHERE MetadataState='failed' AND MetadataRetryAfterUtc='not-a-domain-date' AND length(MetadataJson)=1024;";
            Assert.Equal(20_000L, (long)command.ExecuteScalar()!);
            command.CommandText = "PRAGMA user_version;";
            Assert.Equal(PreviewStoreService.SchemaVersion, Convert.ToInt32(command.ExecuteScalar()));
            command.CommandText = "PRAGMA quick_check;";
            Assert.Equal("ok", command.ExecuteScalar());
            Assert.False(Directory.Exists(locations.ThumbnailCacheDirectory));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.CountAsync(cancelled.Token));
        }
        finally { Directory.Delete(root, true); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Trace(uint kind, nint context, nint statement, nint data);
    [DllImport("e_sqlite3", EntryPoint = "sqlite3_trace_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int TraceV2(nint connection, uint mask, Trace? trace, nint context);
    [DllImport("e_sqlite3", EntryPoint = "sqlite3_sql", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint Sql(nint statement);
}
