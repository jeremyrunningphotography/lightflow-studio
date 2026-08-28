using System.Globalization;
using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal sealed record Subclip(
    Guid SubclipId,
    Guid AssetId,
    string Name,
    int Ordinal,
    TimeSpan In,
    TimeSpan Out,
    TimeSpan SourceDuration,
    long Revision,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

internal static class SubclipCurrentOrder
{
    public static IOrderedEnumerable<Subclip> Apply(IEnumerable<Subclip> subclips) =>
        subclips.OrderBy(item => item.In).ThenBy(item => item.SubclipId);

    public static int Compare(Subclip left, Subclip right)
    {
        var byIn = left.In.CompareTo(right.In);
        return byIn != 0 ? byIn : left.SubclipId.CompareTo(right.SubclipId);
    }
}

internal sealed record SubclipOrder(Guid SubclipId, long ExpectedRevision);
internal sealed record SubclipCreateResult(Subclip Subclip, bool Created);

internal interface ISubclipService
{
    Task<SubclipCreateResult> CreateAsync(Guid assetId, MediaRange workingRange, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Subclip>> ListAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<Subclip> RenameAsync(Guid subclipId, long expectedRevision, string name, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid subclipId, long expectedRevision, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid assetId, IReadOnlyList<SubclipOrder> subclips, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Subclip>> ReorderAsync(Guid assetId, IReadOnlyList<SubclipOrder> order,
        CancellationToken cancellationToken = default);
}

internal sealed class SubclipConcurrencyException(string message) : InvalidOperationException(message);

/// <summary>Transactional Catalog boundary for precious Subclip intent; no path or presentation state enters this model.</summary>
internal sealed class CatalogSubclipService(Func<CatalogDatabaseSession?> session, Func<DateTimeOffset>? utcNow = null)
    : ISubclipService
{
    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    private readonly SemaphoreSlim _mutations = new(1, 1);

    public Task<IReadOnlyList<Subclip>> ListAsync(Guid assetId, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<Subclip>>(() =>
        {
            using var connection = RequireSession().OpenConnection();
            return ReadAsset(connection, null, assetId);
        }, cancellationToken);

    public async Task<SubclipCreateResult> CreateAsync(Guid assetId, MediaRange workingRange, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workingRange);
        ValidateExplicitRange(workingRange);
        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                using var connection = RequireSession().OpenConnection();
                using var transaction = connection.BeginTransaction(deferred: false);
                var existing = ReadAsset(connection, transaction, assetId);
                var matching = existing.FirstOrDefault(item =>
                    item.In == workingRange.In!.Value && item.Out == workingRange.Out!.Value);
                if (matching is not null)
                {
                    transaction.Commit();
                    return new SubclipCreateResult(matching, Created: false);
                }
                var usedNames = existing.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var number = 1;
                while (usedNames.Contains($"Subclip {number}")) number++;
                var now = FormatUtc(_utcNow());
                var created = new Subclip(Guid.NewGuid(), assetId, $"Subclip {number}", existing.Count,
                    workingRange.In!.Value, workingRange.Out!.Value, workingRange.SourceDuration, 1,
                    DateTimeOffset.Parse(now, CultureInfo.InvariantCulture), DateTimeOffset.Parse(now, CultureInfo.InvariantCulture));
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO Subclips
                        (SubclipId,AssetId,Name,Ordinal,InTicks,OutTicks,SourceDurationTicks,Revision,CreatedUtc,UpdatedUtc)
                    VALUES ($id,$asset,$name,$ordinal,$in,$out,$duration,1,$now,$now);
                    """;
                AddIdentity(command, created);
                command.Parameters.AddWithValue("$name", created.Name);
                command.Parameters.AddWithValue("$ordinal", created.Ordinal);
                command.Parameters.AddWithValue("$in", created.In.Ticks);
                command.Parameters.AddWithValue("$out", created.Out.Ticks);
                command.Parameters.AddWithValue("$duration", created.SourceDuration.Ticks);
                command.Parameters.AddWithValue("$now", now);
                command.ExecuteNonQuery();
                transaction.Commit();
                return new SubclipCreateResult(created, Created: true);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _mutations.Release(); }
    }

    public async Task<Subclip> RenameAsync(Guid subclipId, long expectedRevision, string name,
        CancellationToken cancellationToken = default)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0) throw new ArgumentException("A Subclip name is required.", nameof(name));
        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                using var connection = RequireSession().OpenConnection();
                using var transaction = connection.BeginTransaction(deferred: false);
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE Subclips SET Name=$name,Revision=Revision+1,UpdatedUtc=$now
                    WHERE SubclipId=$id AND Revision=$revision;
                    """;
                command.Parameters.AddWithValue("$name", name);
                command.Parameters.AddWithValue("$now", FormatUtc(_utcNow()));
                command.Parameters.AddWithValue("$id", subclipId.ToString("D"));
                command.Parameters.AddWithValue("$revision", expectedRevision);
                if (command.ExecuteNonQuery() != 1) throw new SubclipConcurrencyException("The Subclip changed before the rename was committed.");
                var updated = ReadOne(connection, transaction, subclipId)!;
                transaction.Commit();
                return updated;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _mutations.Release(); }
    }

    public async Task DeleteAsync(Guid subclipId, long expectedRevision, CancellationToken cancellationToken = default)
    {
        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                using var connection = RequireSession().OpenConnection();
                using var transaction = connection.BeginTransaction(deferred: false);
                var existing = ReadOne(connection, transaction, subclipId) ??
                    throw new SubclipConcurrencyException("The Subclip no longer exists.");
                if (existing.Revision != expectedRevision) throw new SubclipConcurrencyException("The Subclip changed before deletion.");
                using var delete = connection.CreateCommand();
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM Subclips WHERE SubclipId=$id AND Revision=$revision;";
                delete.Parameters.AddWithValue("$id", subclipId.ToString("D"));
                delete.Parameters.AddWithValue("$revision", expectedRevision);
                if (delete.ExecuteNonQuery() != 1) throw new SubclipConcurrencyException("The Subclip changed before deletion.");
                NormalizeOrdinals(connection, transaction, existing.AssetId, FormatUtc(_utcNow()));
                transaction.Commit();
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _mutations.Release(); }
    }

    public async Task DeleteAsync(Guid assetId, IReadOnlyList<SubclipOrder> subclips,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subclips);
        if (subclips.Count == 0) return;
        if (subclips.Select(item => item.SubclipId).Distinct().Count() != subclips.Count)
            throw new ArgumentException("Subclips to delete cannot contain duplicate identities.", nameof(subclips));
        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                using var connection = RequireSession().OpenConnection();
                using var transaction = connection.BeginTransaction(deferred: false);
                var current = ReadAsset(connection, transaction, assetId);
                var expected = subclips.ToDictionary(item => item.SubclipId, item => item.ExpectedRevision);
                if (expected.Any(item => current.All(existing =>
                        existing.SubclipId != item.Key || existing.Revision != item.Value)))
                    throw new SubclipConcurrencyException("One or more Subclips changed before deletion.");
                foreach (var item in subclips)
                {
                    using var delete = connection.CreateCommand();
                    delete.Transaction = transaction;
                    delete.CommandText = "DELETE FROM Subclips WHERE SubclipId=$id AND AssetId=$asset AND Revision=$revision;";
                    delete.Parameters.AddWithValue("$id", item.SubclipId.ToString("D"));
                    delete.Parameters.AddWithValue("$asset", assetId.ToString("D"));
                    delete.Parameters.AddWithValue("$revision", item.ExpectedRevision);
                    if (delete.ExecuteNonQuery() != 1)
                        throw new SubclipConcurrencyException("The Subclip collection changed during deletion.");
                }
                NormalizeOrdinals(connection, transaction, assetId, FormatUtc(_utcNow()));
                transaction.Commit();
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _mutations.Release(); }
    }

    public async Task<IReadOnlyList<Subclip>> ReorderAsync(Guid assetId, IReadOnlyList<SubclipOrder> order,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.Select(item => item.SubclipId).Distinct().Count() != order.Count)
            throw new ArgumentException("Subclip order cannot contain duplicate identities.", nameof(order));
        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run<IReadOnlyList<Subclip>>(() =>
            {
                using var connection = RequireSession().OpenConnection();
                using var transaction = connection.BeginTransaction(deferred: false);
                var current = ReadAsset(connection, transaction, assetId);
                var expected = order.ToDictionary(item => item.SubclipId, item => item.ExpectedRevision);
                if (current.Count != order.Count || current.Any(item =>
                        !expected.TryGetValue(item.SubclipId, out var revision) || revision != item.Revision))
                    throw new SubclipConcurrencyException("The Subclip collection changed before reorder.");
                ShiftOrdinals(connection, transaction, assetId);
                var now = FormatUtc(_utcNow());
                for (var ordinal = 0; ordinal < order.Count; ordinal++)
                {
                    using var update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText = "UPDATE Subclips SET Ordinal=$ordinal,Revision=Revision+1,UpdatedUtc=$now WHERE SubclipId=$id AND AssetId=$asset;";
                    update.Parameters.AddWithValue("$ordinal", ordinal);
                    update.Parameters.AddWithValue("$now", now);
                    update.Parameters.AddWithValue("$id", order[ordinal].SubclipId.ToString("D"));
                    update.Parameters.AddWithValue("$asset", assetId.ToString("D"));
                    if (update.ExecuteNonQuery() != 1) throw new SubclipConcurrencyException("The Subclip collection changed during reorder.");
                }
                var reordered = ReadAsset(connection, transaction, assetId);
                transaction.Commit();
                return reordered;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _mutations.Release(); }
    }

    private static void ValidateExplicitRange(MediaRange range)
    {
        if (range.In is null) throw new ArgumentException("Set an In point before creating a Subclip.", nameof(range));
        if (range.Out is null) throw new ArgumentException("Set an Out point before creating a Subclip.", nameof(range));
        if (range.Validate().Count != 0) throw new ArgumentException("The saved In/Out range is invalid.", nameof(range));
    }

    private static List<Subclip> ReadAsset(SqliteConnection connection, SqliteTransaction? transaction, Guid assetId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT SubclipId,AssetId,Name,Ordinal,InTicks,OutTicks,SourceDurationTicks,Revision,CreatedUtc,UpdatedUtc FROM Subclips WHERE AssetId=$asset ORDER BY Ordinal,SubclipId;";
        command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
        return Read(command);
    }

    private static Subclip? ReadOne(SqliteConnection connection, SqliteTransaction transaction, Guid subclipId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT SubclipId,AssetId,Name,Ordinal,InTicks,OutTicks,SourceDurationTicks,Revision,CreatedUtc,UpdatedUtc FROM Subclips WHERE SubclipId=$id;";
        command.Parameters.AddWithValue("$id", subclipId.ToString("D"));
        return Read(command).SingleOrDefault();
    }

    private static List<Subclip> Read(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var result = new List<Subclip>();
        while (reader.Read()) result.Add(new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetInt32(3),
            TimeSpan.FromTicks(reader.GetInt64(4)), TimeSpan.FromTicks(reader.GetInt64(5)), TimeSpan.FromTicks(reader.GetInt64(6)), reader.GetInt64(7),
            DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture), DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture)));
        return result;
    }

    private static void AddIdentity(SqliteCommand command, Subclip subclip)
    {
        command.Parameters.AddWithValue("$id", subclip.SubclipId.ToString("D"));
        command.Parameters.AddWithValue("$asset", subclip.AssetId.ToString("D"));
    }

    private static void ShiftOrdinals(SqliteConnection connection, SqliteTransaction transaction, Guid assetId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE Subclips SET Ordinal=Ordinal+1000000000 WHERE AssetId=$asset;";
        command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
        command.ExecuteNonQuery();
    }

    private static void NormalizeOrdinals(SqliteConnection connection, SqliteTransaction transaction, Guid assetId, string now)
    {
        var ids = ReadAsset(connection, transaction, assetId).Select(item => item.SubclipId).ToArray();
        ShiftOrdinals(connection, transaction, assetId);
        for (var index = 0; index < ids.Length; index++)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE Subclips SET Ordinal=$ordinal,Revision=Revision+1,UpdatedUtc=$now WHERE SubclipId=$id;";
            command.Parameters.AddWithValue("$ordinal", index);
            command.Parameters.AddWithValue("$now", now);
            command.Parameters.AddWithValue("$id", ids[index].ToString("D"));
            command.ExecuteNonQuery();
        }
    }

    private static string FormatUtc(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    private CatalogDatabaseSession RequireSession() => session() ?? throw new InvalidOperationException("The Catalog is unavailable.");
}
