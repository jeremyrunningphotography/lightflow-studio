// Research-only protocol probe. No Windows native consumer or Mac production adapter is claimed.
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Lightflow.Application;

static byte[] Read(string path) { if (new FileInfo(path).Length > ClosedCatalogManifestFormat.MaximumMetadataBytes) throw new InvalidDataException("Bound"); return File.ReadAllBytes(path); }
static void Unlinked(string path) { for (string? p=Path.GetFullPath(path);p!=null;p=Path.GetDirectoryName(p)) if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0) throw new IOException("Linked input"); }
if(args[0]=="plan")
{
    var payload=Read(args[1]); var id=Guid.Parse(args[2]);
    var plans=new Dictionary<string,object>();
    foreach(var scenario in new[]{"good","partial","truncated-manifest","missing-ready","hash-mismatch","cancelled","interrupted"})
    {
        var request=new ClosedCatalogTransferRequest(Guid.NewGuid(),0,ClosedCatalogTransferMode.BackupTransport,id,1,0x4C463031);
        var manifest=new ClosedCatalogManifest(1,request.OperationId,0,id,1,request.ApplicationId,DateTimeOffset.Parse("2026-10-10T23:00:00+00:00"),"LF-MAC-RES-013 synthetic local SQLite backup; not production schema",ClosedCatalogPublicationMode.VerifiedContainer,1,[new("catalog.snapshot",ClosedCatalogArtifactRole.CatalogDatabase,payload.Length,Convert.ToHexString(SHA256.HashData(payload)))],[]);
        var ready=new ClosedCatalogReadiness(1,request.OperationId,0,id,ClosedCatalogManifestFormat.Digest(manifest),1);
        var record=new ClosedCatalogJournalRecord(1,request,0,ClosedCatalogTransferState.Preflight,true,"",null,null);
        ClosedCatalogJournalFormat.Validate([record]);
        var json=JsonSerializer.SerializeToUtf8Bytes(record);var frame=new byte[json.Length+36];BinaryPrimitives.WriteInt32LittleEndian(frame,json.Length);json.CopyTo(frame,4);SHA256.HashData(json).CopyTo(frame,json.Length+4);
        plans[scenario]=new{Directory=$"LightflowCatalog-User-v1-20261010T230000Z-{request.OperationId:N}.incomplete",Pending=Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(request)),Manifest=Convert.ToBase64String(ClosedCatalogManifestFormat.Serialize(manifest)),Ready=Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(ready)),Journal=Convert.ToBase64String(frame)};
    }
    Console.WriteLine(JsonSerializer.Serialize(plans));return;
}
var root=Path.GetFullPath(args[1]); Unlinked(root); var results=new List<object>();
foreach(var directory in Directory.EnumerateDirectories(root,"LightflowCatalog-User-*.incomplete"))
{
    bool verified=false;string? error=null;
    try
    {
        Unlinked(directory);
        var m=ClosedCatalogManifestFormat.Deserialize(Read(Path.Combine(directory,"manifest.json")));
        var pendingBytes=Read(Path.Combine(directory,"pending.json"));var pending=JsonSerializer.Deserialize<ClosedCatalogTransferRequest>(pendingBytes)!;
        if(!JsonSerializer.SerializeToUtf8Bytes(pending).AsSpan().SequenceEqual(pendingBytes))throw new InvalidDataException("Noncanonical pending");
        ClosedCatalogManifestFormat.Validate(m,pending);
        var readyBytes=Read(Path.Combine(directory,"ready.json"));var ready=JsonSerializer.Deserialize<ClosedCatalogReadiness>(readyBytes)!;
        if(!JsonSerializer.SerializeToUtf8Bytes(ready).AsSpan().SequenceEqual(readyBytes))throw new InvalidDataException("Noncanonical readiness");
        ClosedCatalogManifestFormat.ValidateReadiness(m,ready);
        var entries=Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName).Where(p=>p is not("pending.json" or "manifest.json" or "ready.json")).Order(StringComparer.Ordinal);
        if(!entries.SequenceEqual(m.Artifacts.Select(a=>a.RelativeName).Order(StringComparer.Ordinal)))throw new InvalidDataException("Artifact set differs");
        foreach(var a in m.Artifacts){var p=Path.Combine(directory,a.RelativeName);Unlinked(p);var bytes=Read(p);if(bytes.LongLength!=a.ByteLength||Convert.ToHexString(SHA256.HashData(bytes))!=a.Sha256)throw new InvalidDataException("Payload mismatch");}
        verified=true;
    }
    catch(Exception e) when(e is IOException or InvalidDataException or JsonException or ArgumentException){error=e.GetType().Name+": "+e.Message;}
    results.Add(new{Container=Path.GetFileName(directory),Verified=verified,Error=error});
}
var journals=new List<object>();
foreach(var path in Directory.EnumerateFiles(root,"*.journal"))
{
    bool recovery=true;string? error=null;
    try{
        using var f=File.OpenRead(path);var records=new List<ClosedCatalogJournalRecord>();
        while(f.Position<f.Length){if(records.Count>=ClosedCatalogJournalFormat.MaximumRecords)throw new InvalidDataException("Count");var size=new byte[4];f.ReadExactly(size);var length=BinaryPrimitives.ReadInt32LittleEndian(size);if(length<1||length>ClosedCatalogJournalFormat.MaximumRecordBytes)throw new InvalidDataException("Length");var bytes=new byte[length];f.ReadExactly(bytes);var hash=new byte[32];f.ReadExactly(hash);var record=JsonSerializer.Deserialize<ClosedCatalogJournalRecord>(bytes)!;if(!JsonSerializer.SerializeToUtf8Bytes(record).AsSpan().SequenceEqual(bytes)||!Convert.FromHexString(ClosedCatalogJournalFormat.Digest(record)).AsSpan().SequenceEqual(hash))throw new InvalidDataException("Digest");records.Add(record);}
        recovery=ClosedCatalogJournalFormat.RequiresRecovery(records);
    }catch(Exception e)when(e is IOException or InvalidDataException or JsonException){error=e.GetType().Name+": "+e.Message;}
    journals.Add(new{Journal=Path.GetFileName(path),RequiresRecovery=recovery,Error=error});
}
Console.WriteLine(JsonSerializer.Serialize(new{Containers=results,Journals=journals,Scope="Shared format validation and mirrored bounded Windows framing, not native Windows consumption or production Catalog validation"}));
