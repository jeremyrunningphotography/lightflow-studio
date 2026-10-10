using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using Lightflow.Application;
using Lightflow.Domain;
using LightflowStudio;
using Microsoft.Data.Sqlite;
using Microsoft.Win32.SafeHandles;
using Xunit;
using Xunit.Abstractions;

namespace LightflowStudio.Tests;

public sealed class CatalogClosedAdmissionTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lf005-closed-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RealMigrations_CloseBeforeAssessment_RecreateCompanions_PreserveMainObjectAndCommittedValues()
    {
        var locations = LightflowStorageLocations.Create(_root);
        using var admission = new CatalogLocationAdmission(locations, StorageOperation.Create);
        await admission.ValidateAsync(default);
        var bound = admission.LocationsForUse();
        var closedSteps = 0;
        var sqlSteps = 0;
        var migrations = CatalogMigrations.All.Select(m => m with
        {
            Apply = (connection, transaction, context) =>
            {
                Assert.True(File.Exists(bound.CatalogDatabasePath + "-shm"));
                Assert.True(File.Exists(bound.CatalogDatabasePath + "-wal"));
                using (var metadata = CreateFileW(bound.CatalogDatabasePath + "-shm", 0, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero))
                {
                    Assert.False(metadata.IsInvalid);
                    Assert.True(GetFileInformationByHandle(metadata, out var info));
                    Assert.Equal(1u, info.Links);
                    if (m.Version == 1) output.WriteLine($"activeShmMetadataApi=GetFileInformationByHandle; links={info.Links}; attributes=0x{info.Attributes:X}");
                }
                m.Apply(connection, transaction, context);
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = m.Version == 1
                    ? "CREATE TABLE ClosedBoundaryProbe(Value INTEGER); INSERT INTO ClosedBoundaryProbe VALUES(1);"
                    : $"INSERT INTO ClosedBoundaryProbe VALUES({m.Version});";
                command.ExecuteNonQuery();
                sqlSteps++;
            }
        }).ToArray();
        var timer = Stopwatch.StartNew();
        var created = await new CatalogDatabaseService(locations, null, migrations)
        {
            ResolvedDatabasePath = bound.CatalogDatabasePath,
            ValidateStorageAccess = (token, boundary) =>
            {
                if (boundary == CatalogStorageBoundary.MigrationStep_AfterInitialSQLiteUse)
                {
                    Assert.False(File.Exists(bound.CatalogDatabasePath + "-shm"));
                    Assert.False(File.Exists(bound.CatalogDatabasePath + "-wal"));
                    Assert.False(DeleteFileW(bound.CatalogDatabasePath));
                    Assert.Equal(32, Marshal.GetLastWin32Error());
                    Assert.Throws<IOException>(() => File.Move(bound.CatalogDatabasePath, bound.CatalogDatabasePath + ".substitute"));
                    closedSteps++;
                }
                admission.ValidateClosedDatabaseBoundary(token, boundary);
            }
        }.CreateNewAsync();
        Assert.True(created.IsSuccess, created.Diagnostic);
        output.WriteLine($"createMs={timer.Elapsed.TotalMilliseconds:F3}; migrations={sqlSteps}; closedSteps={closedSteps}");
        Assert.Equal(migrations.Length, sqlSteps);
        Assert.Equal(migrations.Length - 1, closedSteps);
        created.Session!.CloseBeforeActivation();
        await admission.ValidateAsync(default);
        using (var connection = created.Session.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            Assert.True(File.Exists(bound.CatalogDatabasePath + "-shm"));
            command.CommandText = "SELECT count(*) FROM ClosedBoundaryProbe;";
            Assert.Equal((long)migrations.Length, command.ExecuteScalar());
            output.WriteLine($"sqliteVersion={connection.ServerVersion}; journal={created.Session.RuntimePolicy.JournalMode}; synchronous={created.Session.RuntimePolicy.SynchronousLevel}");
        }
        var id = created.Session.Identity.CatalogId;
        await created.Session.DisposeAsync();
        admission.Dispose();
        // Main guard released on success; no retained sidecar/main handles.
        File.Move(bound.CatalogDatabasePath, bound.CatalogDatabasePath + ".released");
        File.Move(bound.CatalogDatabasePath + ".released", bound.CatalogDatabasePath);
        var reopened = await new CatalogDatabaseService(locations).OpenExistingAsync();
        Assert.True(reopened.IsSuccess, reopened.Diagnostic);
        Assert.Equal(id, reopened.Session!.Identity.CatalogId);
        await reopened.Session.DisposeAsync();
    }

    [Theory]
    [InlineData("-wal")]
    [InlineData("-shm")]
    [InlineData("-journal")]
    public async Task PostCloseUnsafeCompanion_RefusesBeforeNextSql_PreservesPartialCatalog(string suffix)
    {
        var locations = LightflowStorageLocations.Create(_root);
        using var admission = new CatalogLocationAdmission(locations, StorageOperation.Create);
        await admission.ValidateAsync(default);
        var bound = admission.LocationsForUse();
        var seeded = Path.Combine(_root, "precious.bin");
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(seeded, [1, 4, 9, 16]);
        var digest = SHA256.HashData(File.ReadAllBytes(seeded));
        var nextSqlRan = false;
        var migrations = new[] { CatalogMigrations.All[0], new CatalogMigration(2, "Must not run", (_, _, _) => nextSqlRan = true) };
        var service = new CatalogDatabaseService(locations, null, migrations)
        {
            ResolvedDatabasePath = bound.CatalogDatabasePath,
            ValidateStorageAccess = (token, boundary) =>
            {
                if (boundary == CatalogStorageBoundary.MigrationStep_AfterInitialSQLiteUse)
                {
                    Assert.False(File.Exists(bound.CatalogDatabasePath + suffix));
                    Assert.True(CreateHardLinkW(bound.CatalogDatabasePath + suffix, seeded, IntPtr.Zero), new Win32Exception(Marshal.GetLastWin32Error()).Message);
                }
                admission.ValidateClosedDatabaseBoundary(token, boundary);
            }
        };
        var refusal = await Assert.ThrowsAsync<CatalogLocationAdmissionException>(() => service.CreateNewAsync());
        Assert.Contains("predicate=LeafLinkCount", refusal.Message);
        Assert.Contains("links=2", refusal.Message);
        Assert.False(nextSqlRan);
        Assert.Equal(digest, SHA256.HashData(File.ReadAllBytes(seeded)));
        Assert.True(File.Exists(bound.CatalogDatabasePath));
        admission.Dispose();
        File.Delete(bound.CatalogDatabasePath + suffix);
        using var check = ReadOnly(bound.CatalogDatabasePath);
        using var command = check.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        Assert.Equal(1L, command.ExecuteScalar());
        command.CommandText = "SELECT count(*) FROM SchemaMigrations;";
        Assert.Equal(1L, command.ExecuteScalar());
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("-wal", false)]
    [InlineData("-shm", false)]
    [InlineData("-journal", false)]
    [InlineData("", true)]
    [InlineData("-wal", true)]
    [InlineData("-shm", true)]
    [InlineData("-journal", true)]
    public async Task PreOpenUnsafeLeaves_RefuseWithoutChangingSeededBytes(string suffix, bool directory)
    {
        var locations = LightflowStorageLocations.Create(_root);
        Directory.CreateDirectory(locations.CatalogDirectory);
        var seeded = Path.Combine(_root, "seeded.bin");
        File.WriteAllBytes(seeded, [7, 6, 5, 4]);
        var leaf = locations.CatalogDatabasePath + suffix;
        if (directory) Directory.CreateDirectory(leaf);
        else Assert.True(CreateHardLinkW(leaf, seeded, IntPtr.Zero));
        var digest = SHA256.HashData(File.ReadAllBytes(seeded));
        using var admission = new CatalogLocationAdmission(locations, StorageOperation.Open);
        var refusal = await Assert.ThrowsAsync<CatalogLocationAdmissionException>(() => admission.ValidateAsync(default));
        Assert.Contains(directory ? "predicate=LeafDirectory" : "predicate=LeafLinkCount", refusal.Message);
        Assert.Equal(digest, SHA256.HashData(File.ReadAllBytes(seeded)));
    }

    [Fact]
    public async Task ExistingMigrationFailure_PreservesCommittedStepIdentityAndVerifiedBackup()
    {
        var locations = LightflowStorageLocations.Create(_root);
        var seed = await new CatalogDatabaseService(locations, null, CatalogMigrations.All.Take(1).ToArray()).CreateNewAsync();
        var id = seed.Session!.Identity.CatalogId;
        await seed.Session.DisposeAsync();
        using var admission = new CatalogLocationAdmission(locations, StorageOperation.Open);
        await admission.ValidateAsync(default);
        var bound = admission.LocationsForUse();
        var recovery = new SqliteCatalogRecoveryService(bound);
        var migrations = new[]
        {
            CatalogMigrations.All[0],
            new CatalogMigration(2, "Committed probe", (connection, transaction, _) =>
            {
                using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = "CREATE TABLE CommittedProbe(Value TEXT); INSERT INTO CommittedProbe VALUES('authored');";
                command.ExecuteNonQuery();
            }),
            new CatalogMigration(3, "Rollback probe", (connection, transaction, _) =>
            {
                using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = "CREATE TABLE MustRollBack(Value TEXT);"; command.ExecuteNonQuery();
                throw new InvalidOperationException("deliberate rollback");
            })
        };
        var failed = await new CatalogDatabaseService(locations, recovery, migrations)
        { ResolvedDatabasePath = bound.CatalogDatabasePath, ValidateStorageAccess = admission.ValidateClosedDatabaseBoundary }.OpenExistingAsync();
        Assert.Equal(CatalogOpenStatus.MigrationFailed, failed.Status);
        Assert.Null(failed.Session);
        using (var connection = ReadOnly(bound.CatalogDatabasePath))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA user_version;"; Assert.Equal(2L, command.ExecuteScalar());
            command.CommandText = "SELECT Value FROM CommittedProbe;"; Assert.Equal("authored", command.ExecuteScalar());
            command.CommandText = "SELECT count(*) FROM sqlite_schema WHERE name='MustRollBack';"; Assert.Equal(0L, command.ExecuteScalar());
            command.CommandText = "SELECT CatalogId FROM CatalogInfo;"; Assert.Equal(id.ToString("D"), command.ExecuteScalar());
        }
        var backup = Assert.Single(recovery.ListBackups(), item => item.Kind == CatalogBackupKind.Migration);
        var valid = await recovery.CheckIntegrityAsync(backup.Path);
        Assert.True(valid.IsValid, valid.Diagnostic); Assert.Equal(id, valid.CatalogId); Assert.Equal(1, valid.SchemaVersion);
        admission.Dispose();
        File.Move(bound.CatalogDatabasePath, bound.CatalogDatabasePath + ".recoverable");
    }

    [Fact]
    public async Task CancellationAfterCommittedCreation_ClosesOwnedPoolAndReleasesGuard()
    {
        var locations = LightflowStorageLocations.Create(_root);
        using var admission = new CatalogLocationAdmission(locations, StorageOperation.Create);
        await admission.ValidateAsync(default);
        using var cancellation = new CancellationTokenSource();
        var initial = CatalogMigrations.All[0];
        var migrations = new[] { initial with { Apply = (connection, transaction, context) =>
        { initial.Apply(connection, transaction, context); cancellation.Cancel(); } }, CatalogMigrations.All[1] };
        var service = new CatalogDatabaseService(locations, null, migrations)
        { ResolvedDatabasePath = admission.LocationsForUse().CatalogDatabasePath, ValidateStorageAccess = admission.ValidateClosedDatabaseBoundary };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateNewAsync(cancellation.Token));
        admission.Dispose();
        Assert.False(File.Exists(locations.CatalogDatabasePath + "-shm"));
        Assert.True(File.Exists(locations.CatalogDatabasePath));
        File.Move(locations.CatalogDatabasePath, locations.CatalogDatabasePath + ".recoverable");
    }

    [Fact]
    public async Task MainChangedBetweenQualificationAndGuard_RefusesAndReleasesFailedPin()
    {
        var locations = LightflowStorageLocations.Create(_root);
        var seeded = await new CatalogDatabaseService(locations).CreateNewAsync();
        await seeded.Session!.DisposeAsync();
        using var assessor = new WindowsStorageLocationAssessor([locations.PreviewsDirectory, locations.TemporaryDirectory]);
        var request = new StorageAssessmentRequest(Guid.NewGuid(), 0, StorageRole.ActiveCatalog, StorageOperation.Open, locations.CatalogDirectory);
        var facts = await assessor.AssessAsync(request);
        Assert.Equal(StorageLocationDecision.Eligible, StorageLocationPolicy.Evaluate(request, facts, DateTimeOffset.UtcNow).Decision);
        var saved = locations.CatalogDatabasePath + ".original";
        File.Move(locations.CatalogDatabasePath, saved);
        File.Copy(saved, locations.CatalogDatabasePath);
        Assert.Throws<IOException>(() => assessor.GuardMainDatabase(locations.CatalogDatabasePath));
        File.Move(locations.CatalogDatabasePath, locations.CatalogDatabasePath + ".released");
        Assert.True(File.Exists(saved));
    }

    [Fact]
    public async Task AtomicCreationRefusal_PreservesClaimWithoutPublishingOrReplacement()
    {
        var locations = LightflowStorageLocations.Create(_root);
        using var admission = new CatalogLocationAdmission(locations, StorageOperation.Create);
        await admission.ValidateAsync(default);
        var claimed = false;
        var service = new CatalogDatabaseService(locations)
        {
            ResolvedDatabasePath = admission.LocationsForUse().CatalogDatabasePath,
            ValidateStorageAccess = (token, boundary) =>
            {
                if (boundary == CatalogStorageBoundary.AfterAtomicCreation_BeforeInitialSQLiteUse)
                {
                    claimed = true;
                    Assert.Equal(0, new FileInfo(locations.CatalogDatabasePath).Length);
                    throw new CatalogLocationAdmissionException("Test revocation after authorized claim", StorageLocationReason.VolumeChanged);
                }
                admission.ValidateClosedDatabaseBoundary(token, boundary);
            }
        };
        await Assert.ThrowsAsync<CatalogLocationAdmissionException>(() => service.CreateNewAsync());
        Assert.True(claimed);
        Assert.Equal(0, new FileInfo(locations.CatalogDatabasePath).Length);
        admission.Dispose();
        File.Move(locations.CatalogDatabasePath, locations.CatalogDatabasePath + ".recoverable");
    }

    [Fact]
    public async Task NativeDeniedMainAccessAfterClosure_RemainsFailClosed()
    {
        var locations = LightflowStorageLocations.Create(_root);
        using var admission = new CatalogLocationAdmission(locations, StorageOperation.Create);
        await admission.ValidateAsync(default);
        var bound = admission.LocationsForUse();
        FileSecurity? original = null;
        var leaf = new FileInfo(bound.CatalogDatabasePath);
        try
        {
            var service = new CatalogDatabaseService(locations, null, CatalogMigrations.All.Take(2).ToArray())
            {
                ResolvedDatabasePath = bound.CatalogDatabasePath,
                ValidateStorageAccess = (token, boundary) =>
                {
                    if (boundary == CatalogStorageBoundary.MigrationStep_AfterInitialSQLiteUse)
                    {
                        original = leaf.GetAccessControl();
                        var denied = leaf.GetAccessControl();
                        denied.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ReadData, AccessControlType.Deny));
                        leaf.SetAccessControl(denied);
                    }
                    admission.ValidateClosedDatabaseBoundary(token, boundary);
                }
            };
            var failure = await Assert.ThrowsAsync<CatalogLocationAdmissionException>(() => service.CreateNewAsync());
            Assert.Equal(StorageLocationReason.ReadUnavailable, failure.Reason);
            Assert.Contains("win32=5", failure.Message);
            Assert.True(File.Exists(bound.CatalogDatabasePath));
        }
        finally { if (original is not null) leaf.SetAccessControl(original); }
    }

    [NativeFileSymlinkTheory]
    [InlineData("")]
    [InlineData("-wal")]
    [InlineData("-shm")]
    [InlineData("-journal")]
    public async Task NativeFileSymlinkLeaves_RefuseWithoutTargetWrites(string suffix)
    {
        var locations = LightflowStorageLocations.Create(_root);
        Directory.CreateDirectory(locations.CatalogDirectory);
        var target = Path.Combine(_root, "authored.bin"); File.WriteAllBytes(target, [2, 7, 1, 8]);
        var digest = SHA256.HashData(File.ReadAllBytes(target));
        var leaf = locations.CatalogDatabasePath + suffix;
        // The attribute reports only a verified missing Windows symlink privilege as a gap.
        // Once available, every creation and safety assertion must pass.
        File.CreateSymbolicLink(leaf, target);
        using var admission = new CatalogLocationAdmission(locations, StorageOperation.Open);
        var refusal = await Assert.ThrowsAsync<CatalogLocationAdmissionException>(() => admission.ValidateAsync(default));
        Assert.Contains("predicate=LeafReparse", refusal.Message);
        Assert.Equal(digest, SHA256.HashData(File.ReadAllBytes(target)));
        admission.Dispose(); File.Delete(leaf);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-wal")]
    [InlineData("-shm")]
    [InlineData("-journal")]
    public async Task NativeReparseLeaf_ReportsBothContributingPredicates(string suffix)
    {
        var locations = LightflowStorageLocations.Create(_root);
        Directory.CreateDirectory(locations.CatalogDirectory);
        var target = Path.Combine(_root, "outside"); Directory.CreateDirectory(target);
        var leaf = locations.CatalogDatabasePath + suffix;
        using var process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/c mklink /J \"{leaf}\" \"{target}\"", UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        })!;
        await process.WaitForExitAsync(); Assert.Equal(0, process.ExitCode);
        using var admission = new CatalogLocationAdmission(locations, StorageOperation.Open);
        var refusal = await Assert.ThrowsAsync<CatalogLocationAdmissionException>(() => admission.ValidateAsync(default));
        Assert.Contains("predicate=LeafReparse", refusal.Message);
        Assert.Contains("predicate=LeafDirectory", refusal.Message);
        Assert.Contains("attributes=0x", refusal.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        admission.Dispose(); Directory.Delete(leaf);
    }

    [Fact]
    public async Task ProtectedRootOverlap_DiagnosticIncludesPredicateAndNoOwnerPath()
    {
        Directory.CreateDirectory(_root);
        var catalog = Path.Combine(_root, "catalog");
        Directory.CreateDirectory(catalog);
        var request = new StorageAssessmentRequest(Guid.NewGuid(), 6, StorageRole.ActiveCatalog, StorageOperation.Create, catalog);
        using var assessor = new WindowsStorageLocationAssessor([_root]);
        var facts = await assessor.AssessAsync(request);
        Assert.Equal(StorageLocationReason.AmbiguousContainment, StorageLocationPolicy.Evaluate(request, facts, DateTimeOffset.UtcNow).Reason);
        Assert.Contains("predicate=ProtectedRootOverlap", facts.ProviderDiagnostic);
        Assert.Contains($"assessmentId={facts.AssessmentId:N}", facts.ProviderDiagnostic);
        Assert.DoesNotContain(_root, facts.ProviderDiagnostic);
    }

    [Fact]
    public async Task FactoryClosure_RequiresOwnedScopesClosed_LeavesUnrelatedFactoryUsable()
    {
        var a = LightflowStorageLocations.Create(Path.Combine(_root, "a"));
        var b = LightflowStorageLocations.Create(Path.Combine(_root, "b"));
        var first = await new CatalogDatabaseService(a).CreateNewAsync();
        var second = await new CatalogDatabaseService(b).CreateNewAsync();
        await first.Session!.DisposeAsync(); await second.Session!.DisposeAsync();
        var owner = new CatalogSqliteConnectionFactory(a.CatalogDatabasePath);
        var unrelated = new CatalogSqliteConnectionFactory(b.CatalogDatabasePath);
        using var other = unrelated.OpenConnection();
        using (var checkedOut = owner.OpenConnection())
        {
            using var readerCommand = checkedOut.CreateCommand();
            readerCommand.CommandText = "SELECT CatalogId FROM CatalogInfo;";
            using var reader = readerCommand.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Throws<CatalogLocationAdmissionException>(() => owner.CloseUnpublishedPool(CatalogStorageBoundary.AfterCatalogValidation_SQLiteClosed));
        }
        owner.CloseUnpublishedPool(CatalogStorageBoundary.AfterCatalogValidation_SQLiteClosed);
        using var command = other.CreateCommand(); command.CommandText = "SELECT CatalogId FROM CatalogInfo;";
        Assert.Equal(second.Session.Identity.CatalogId.ToString("D"), command.ExecuteScalar());
        owner.Publish();
        Assert.Throws<CatalogLocationAdmissionException>(() => owner.CloseUnpublishedPool(CatalogStorageBoundary.AfterCatalogValidation_SQLiteClosed));
        other.Dispose(); unrelated.ClearPool(); owner.ClearPool();
    }

    private static SqliteConnection ReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open(); return connection;
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string name, string existing, IntPtr security);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteFileW(string name);
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh,
            Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
}

public sealed class NativeFileSymlinkTheoryAttribute : TheoryAttribute
{
    private static readonly Lazy<bool> MissingPrivilege = new(() =>
    {
        var root = Path.Combine(Path.GetTempPath(), "lf005-symlink-prerequisite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.CreateSymbolicLink(Path.Combine(root, "link"), Path.Combine(root, "target"));
            File.Delete(Path.Combine(root, "link"));
            return false;
        }
        catch (IOException error) when ((error.HResult & 0xFFFF) == 1314) { return true; }
        finally { Directory.Delete(root, true); }
    });

    public NativeFileSymlinkTheoryAttribute()
    {
        if (MissingPrivilege.Value) Skip = "Native coverage gap: Windows file-symlink privilege/developer mode is unavailable (Win32 1314).";
    }
}
