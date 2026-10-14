using System.IO;
using System.Text.Json;
using Lightflow.Application;
namespace LightflowStudio;

public sealed class CatalogTransferFiles(ICatalogTransferNativeEffects effects)
{
    private readonly ICatalogTransferNativeEffects _effects = effects;
    public FileStream OpenClosedRead(string path) => _effects.OpenClosedRead(path);
    public void VerifyMetadata(string directory, ClosedCatalogManifest expected, ClosedCatalogReadiness readiness)
    {
        var pendingBytes = ReadBounded(Path.Combine(directory, "pending.json"));
        var pending = JsonSerializer.Deserialize<ClosedCatalogTransferRequest>(pendingBytes) ?? throw new InvalidDataException("Missing operation claim.");
        if (!JsonSerializer.SerializeToUtf8Bytes(pending).AsSpan().SequenceEqual(pendingBytes) ||
            pending.OperationId != expected.OperationId || pending.Generation != expected.Generation || pending.CatalogId != expected.CatalogId ||
            pending.SchemaVersion != expected.SchemaVersion || pending.ApplicationId != expected.ApplicationId || !Enum.IsDefined(pending.Mode))
            throw new InvalidDataException("Operation claim does not match the closed artifact manifest.");
        var actual = ClosedCatalogManifestFormat.Deserialize(ReadBounded(Path.Combine(directory, "manifest.json")));
        if (ClosedCatalogManifestFormat.Digest(actual) != ClosedCatalogManifestFormat.Digest(expected)) throw new InvalidDataException("Published manifest changed.");
        var bytes = ReadBounded(Path.Combine(directory, "ready.json"));
        var actualReady = JsonSerializer.Deserialize<ClosedCatalogReadiness>(bytes) ?? throw new InvalidDataException("Missing readiness receipt.");
        if (!JsonSerializer.SerializeToUtf8Bytes(actualReady).AsSpan().SequenceEqual(bytes) || actualReady != readiness)
            throw new InvalidDataException("Readiness receipt changed or is incomplete.");
        ClosedCatalogManifestFormat.ValidateReadiness(actual, actualReady);
    }
    public IEnumerable<string> EnumerateRegularFiles(string directory, string? ownedJournal = null)
    {
        _effects.RequireUnlinked(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("A transfer artifact is a link or junction.");
            if ((attributes & FileAttributes.Directory) != 0)
                foreach (var file in EnumerateRegularFiles(entry)) yield return file;
            else if (!string.Equals(entry, ownedJournal, StringComparison.OrdinalIgnoreCase))
            { using var guard = OpenClosedRead(entry); yield return entry; }
        }
    }
    public string Child(string directory, string relative)
    {
        if (!PortablePathIdentityValidator.ValidateRelativePath(relative).IsSafe) throw new InvalidDataException("Unsafe transfer artifact path.");
        return Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
    }
    public void ValidateDirectorySet(string directory, ClosedCatalogManifest manifest)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in manifest.Artifacts)
            for (var parent = Path.GetDirectoryName(artifact.RelativeName.Replace('/', Path.DirectorySeparatorChar)); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
                allowed.Add(parent.Replace('\\', '/'));
        ValidateChildren(directory);
        void ValidateChildren(string parent)
        {
            foreach (var child in Directory.EnumerateDirectories(parent))
            {
                _effects.RequireUnlinked(child);
                if (!allowed.Contains(Path.GetRelativePath(directory, child).Replace('\\', '/')))
                    throw new InvalidDataException("Unexpected transport directory.");
                ValidateChildren(child);
            }
        }
    }
    public byte[] ReadBounded(string path)
    {
        using var file = OpenClosedRead(path);
        if (file.Length > ClosedCatalogManifestFormat.MaximumMetadataBytes) throw new InvalidDataException("Transfer metadata exceeds its format bound.");
        var bytes = new byte[checked((int)file.Length)]; file.ReadExactly(bytes); return bytes;
    }
    public void WriteExclusive(string path, byte[] bytes)
    {
        _effects.RequireUnlinked(path);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        file.Write(bytes); file.Flush(true);
    }
}
