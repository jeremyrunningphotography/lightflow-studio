using System.Runtime.InteropServices;
using System.Text;
using Lightflow.Application;
using Lightflow.Domain;
using LightflowStudio;
using Microsoft.Win32.SafeHandles;

namespace Lightflow.Platform.MacOS;

/// <summary>Darwin file effects; shared Catalog owners retain sequencing and formats.</summary>
public sealed class MacCatalogNativeEffects(string root) : ICatalogEvidenceEffects, ICatalogRecoveryNativeEffects
{
    private readonly Dictionary<string,WeakReference<NativeStream>> _ownership=new(StringComparer.Ordinal);
    private readonly Dictionary<string,NativeStream> _admittedMain=new(StringComparer.Ordinal);
    private readonly object _bindings=new();
    public static bool SamePath(string a, string b) => Path.GetFullPath(a) == Path.GetFullPath(b);
    public static bool Contains(string parent, string child) => SamePath(parent, child) || Path.GetFullPath(child).StartsWith(Path.GetFullPath(parent).TrimEnd('/') + "/", StringComparison.Ordinal);
    public bool PathsOverlap(string a, string b) => Contains(a,b) || Contains(b,a);
    public bool SameDatabase(string a, string b) => SamePath(a,b);
    public void GuardAccess(string path)
    {
        if (!Contains(root,path)) throw new IOException("Path is outside the explicit host data root.");
        RequireUnlinked(path);
    }
    public void RequireIsolatedDestination(ILightflowStorageLocations locations,string destination) => GuardAccess(destination);
    public void RequireUnlinked(string path) => Check(Native.Unlinked(Path.GetFullPath(path)), "Unlinked path required",path);
    public void RequireSingleRegularFile(FileStream file) => Check(Native.Regular(Fd(file.SafeFileHandle)), "Single regular file required",file.Name);
    public void RequireBoundOwnershipJournal(FileStream file,string path)
    {
        if(file is not NativeStream bound) throw new IOException("Native ownership stream required.");
        bound.Revalidate(path);
    }
    public string Fingerprint(FileStream database)
    {
        RequireSingleRegularFile(database);
        var header=new byte[100];database.Position=0;database.ReadExactly(header);
        return Native.Identity(database.SafeFileHandle)+":"+database.Length+":"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(header));
    }
    public double LastOwnershipOpenMilliseconds {get;private set;}
    public FileStream OpenOwnershipJournal(string path)
    {
        var timer=System.Diagnostics.Stopwatch.StartNew();
        var file=Open(path,true,true,2,parentLock:2);
        try {file.TrackCatalog=true;file.Revalidate(path);lock(_bindings)_ownership[Path.GetFullPath(path[..^".startup-state".Length])]=new(file);LastOwnershipOpenMilliseconds=timer.Elapsed.TotalMilliseconds;return file;}
        catch {file.Dispose();throw;}
    }
    internal void RegisterMainGuard(string path,NativeStream stream) {lock(_bindings)_admittedMain[Path.GetFullPath(path)]=stream;}
    internal void RemoveMainGuard(string path,NativeStream stream,bool forReplacement)
    {
        lock(_bindings)
        {
            if(_admittedMain.TryGetValue(Path.GetFullPath(path),out var held)&&ReferenceEquals(held,stream))_admittedMain.Remove(Path.GetFullPath(path));
            if(forReplacement&&_ownership.TryGetValue(Path.GetFullPath(path),out var weak)&&weak.TryGetTarget(out var owner))owner.PrepareOwnedRollback();
        }
    }
    internal void ActivateAdmittedMain(string path)
    {
        lock(_bindings)
        {
            path=Path.GetFullPath(path);
            if(!_admittedMain.TryGetValue(path,out var guard)||!_ownership.TryGetValue(path,out var weak)||!weak.TryGetTarget(out var owner))throw new IOException("Activation requires the existing native ownership journal and an admitted main-file guard.");
            owner.RebindAdmittedMain(guard);
        }
    }
    internal void FinishRestoredBinding()
    {lock(_bindings)foreach(var weak in _ownership.Values)if(weak.TryGetTarget(out var owner))owner.CommitOwnedBinding();}
    public FileStream OpenProfileSession(string path) => Open(path,true,true,2);
    public FileStream OpenJournal(string path)
    {
        GuardAccess(path);
        // Existing operation evidence must never be overwritten.
        return Open(path,true,true,2,exclusive:true);
    }
    public FileStream OpenClosedRead(string path) => Open(path,false,false,1,parentLock:1);
    public ICatalogRecoveryReadGuard OpenRecoveryReadGuard(string path,bool closed) => new ReadGuard(Open(path,false,false,1,closed?1:0));
    public string RecoverySqlitePath(string path) { GuardAccess(path);return Path.GetFullPath(path); }
    public IEnumerable<string> EnumerateOwnedFiles(string directory,string pattern,SearchOption option)
    {
        GuardAccess(directory);
        foreach(var file in Directory.EnumerateFiles(directory,pattern,option)) { RequireUnlinked(file);yield return file; }
    }
    public ICatalogStorageAssessor CreateAssessor(IReadOnlyList<string> protectedPaths) => new Assessor(protectedPaths.Count==0 ? new[]{Path.Combine(root,"Previews"),Path.Combine(root,"Temporary")} : protectedPaths);
    public ICatalogDirectoryLease AcquireDirectoryLease(string directory,bool allowPublication)
    {
        GuardAccess(directory);
        if(allowPublication) throw new IOException("Mac bound-directory no-replace publication is not qualified. The existing transfer contract is preserved; Move is refused.");
        return new DirectoryLease(directory);
    }
    public void RequireDirectoryIdentity(string directory,string identity)
    {
        using var lease=new DirectoryLease(directory);
        if(lease.Identity!=identity)throw new IOException("Directory identity changed.");
    }
    public void CreateExclusiveDirectory(string path)
    {
        GuardAccess(path);
        if(Native.Mkdir(path,448)!=0) throw Error("Exclusive mkdir refused",path);
    }
    public ulong? AvailableSpace(string directory) => null; // Unknown never grants capacity.
    internal static NativeStream Open(string path,bool create,bool writable,int mode,int parentLock=0,bool exclusive=false)
    {
        SafeFileHandle? parent=null;
        try
        {
            var parentPath=Path.GetDirectoryName(Path.GetFullPath(path))!;
            parent=Native.Open(parentPath,false,false,parentLock,true);
            var file=Native.Open(path,create,writable,mode,false,exclusive);
            return new NativeStream(file,writable?FileAccess.ReadWrite:FileAccess.Read,parent,parentPath,Path.GetFullPath(path));
        }
        catch { parent?.Dispose();throw; }
    }
    internal static int Fd(SafeFileHandle file)
    {
        if(file.IsClosed||file.IsInvalid)throw new ObjectDisposedException("Native Catalog handle");
        return checked((int)file.DangerousGetHandle());
    }
    internal static void Check(int result,string message,string path) { if(result!=0)throw new IOException($"{message}: {path}; Darwin errno={result}."); }
    internal static IOException Error(string operation,string path) => new($"{operation}: {path}; Darwin errno={Marshal.GetLastPInvokeError()}.");
    internal sealed class NativeStream(SafeFileHandle handle,FileAccess access,SafeFileHandle parent,string parentPath,string path)
        : FileStream(handle,access,4096,false)
    {
        public override string Name => path;
        internal bool TrackCatalog {get;set;}
        private SafeFileHandle? _catalog;
        private SafeFileHandle? _priorCatalog;
        internal void Revalidate(string candidate)
        {
            if(!SamePath(path,candidate))throw new IOException("Ownership path changed.");
            RevalidateJournal();
            if(TrackCatalog) {
                var database=path[..^".startup-state".Length];
                if(_catalog is null && File.Exists(database)) _catalog=Native.Open(database,false,false,0,false);
                if(_catalog is not null)Check(Native.Bound(Fd(_catalog),database,0),"Catalog file identity changed",database);
            }
        }
        private void RevalidateJournal()
        {
            Check(Native.Bound(Fd(parent),parentPath,1),"Ownership directory changed",parentPath);
            Check(Native.Bound(Fd(SafeFileHandle),path,0),"Ownership file changed",path);
        }
        internal void RebindAdmittedMain(NativeStream guard)
        {
            RevalidateJournal();guard.Revalidate(guard.Name);
            if(_catalog is not null&&Native.Identity(_catalog)==Native.Identity(guard.SafeFileHandle))return;
            var next=Native.Duplicate(guard.SafeFileHandle);
            _priorCatalog?.Dispose();_priorCatalog=_catalog;_catalog=next;
        }
        internal void PrepareOwnedRollback()
        {if(_priorCatalog is not null){_catalog?.Dispose();_catalog=_priorCatalog;_priorCatalog=null;}}
        internal void CommitOwnedBinding(){_priorCatalog?.Dispose();_priorCatalog=null;}
        public override void Flush(bool flushToDisk)
        {
            base.Flush(flushToDisk);
            if(flushToDisk) Check(Native.Flush(Fd(SafeFileHandle),Fd(parent)),"Native durable flush refused",path);
        }
        public override async ValueTask DisposeAsync()
        { try { await base.DisposeAsync(); } finally {_catalog?.Dispose();_priorCatalog?.Dispose();parent.Dispose();} }
        protected override void Dispose(bool disposing) { try { base.Dispose(disposing); } finally { if(disposing){_catalog?.Dispose();_priorCatalog?.Dispose();parent.Dispose();} } }
    }
    private sealed class ReadGuard(NativeStream file) : ICatalogRecoveryReadGuard
    {
        public void RequireSingleRegularFile() => file.Revalidate(file.Name);
        public void Dispose() => file.Dispose();
    }
    private sealed class DirectoryLease : ICatalogDirectoryLease
    {
        private readonly SafeFileHandle _handle;
        public string Identity {get;}
        public DirectoryLease(string path) { _handle=Native.Open(path,false,false,0,true);Identity=Native.Identity(_handle); }
        public void Revalidate(string directory) => Check(Native.Bound(Fd(_handle),directory,1),"Directory binding changed",directory);
        public void PublishNoReplace(string destination) => throw new IOException("Mac handle-bound publication remains unqualified; no path-based rename fallback.");
        public void Dispose()=>_handle.Dispose();
    }
    private sealed class Assessor(IReadOnlyList<string> paths) : ICatalogStorageAssessor
    {
        private readonly MacStorageLocationAssessor _assessor=new(paths);
        public Task<StorageLocationAssessment> AssessAsync(StorageAssessmentRequest request,CancellationToken token=default)=>_assessor.AssessAsync(request,token);
        public void Dispose()=>_assessor.Dispose();
    }
    internal static class Native
    {
        private const string Library="libLightflowStorage.dylib";
        [DllImport(Library,EntryPoint="lf_cat_unlinked")] internal static extern int Unlinked([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
        [DllImport(Library,EntryPoint="lf_cat_open",SetLastError=true)] private static extern int OpenFd([MarshalAs(UnmanagedType.LPUTF8Str)] string path,int create,int write,int mode,int directory);
        [DllImport(Library,EntryPoint="lf_cat_bound")] internal static extern int Bound(int fd,[MarshalAs(UnmanagedType.LPUTF8Str)] string path,int directory);
        [DllImport(Library,EntryPoint="lf_cat_regular")] internal static extern int Regular(int fd);
        [DllImport(Library,EntryPoint="lf_cat_identity")] private static extern int IdentityFd(int fd,StringBuilder result,int size);
        [DllImport(Library,EntryPoint="lf_cat_sync_directory")] internal static extern int SyncDirectory(int fd);
        [DllImport(Library,EntryPoint="lf_cat_flush")] internal static extern int Flush(int fd,int parent);
        [DllImport("libSystem.B.dylib",EntryPoint="mkdir",SetLastError=true)] internal static extern int Mkdir([MarshalAs(UnmanagedType.LPUTF8Str)] string path,int mode);
        [DllImport(Library,EntryPoint="lf_cat_duplicate",SetLastError=true)] private static extern int DuplicateFd(int fd);
        internal static SafeFileHandle Duplicate(SafeFileHandle handle)
        {var fd=DuplicateFd(Fd(handle));if(fd<0)throw Error("Native binding duplication refused","");return new SafeFileHandle(fd,true);}
        internal static SafeFileHandle Open(string path,bool create,bool write,int mode,bool directory,bool exclusive=false)
        {
            if(!OperatingSystem.IsMacOS())throw new PlatformNotSupportedException("Native Mac Catalog effects require Darwin.");
            var fd=OpenFd(path,exclusive?2:create?1:0,write?1:0,mode,directory?1:0);
            if(fd<0)throw Error("Native open/lock refused",path);
            return new SafeFileHandle(fd,true);
        }
        internal static string Identity(SafeFileHandle handle) { var result=new StringBuilder(256);Check(IdentityFd(Fd(handle),result,256),"Identity unavailable","");return result.ToString(); }
    }
}
