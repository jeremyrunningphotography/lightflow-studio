using System.IO;

namespace LightflowStudio;

internal sealed class EncodingOutputLifecycle
{
    public const string PartialExtension = ".lightflow";

    private readonly IEncodingOutputFileSystem _files;
    private readonly string? _identityCacheDirectory;
    private bool _unfinished = true;

    public EncodingOutputLifecycle(string finalPath, string? sourcePath = null,
        string? identityCacheDirectory = null, IEncodingOutputFileSystem? files = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(finalPath);
        FinalPath = Path.GetFullPath(finalPath);
        PartialPath = PartialPathFor(FinalPath);
        if (sourcePath is not null &&
            (PathsEqual(sourcePath, FinalPath) || PathsEqual(sourcePath, PartialPath)))
            throw new InvalidOperationException("The source, final output, and Lightflow partial output paths must be different.");
        _identityCacheDirectory = identityCacheDirectory;
        _files = files ?? PhysicalEncodingOutputFileSystem.Instance;
    }

    public string FinalPath { get; }
    public string PartialPath { get; }
    public bool ReplacesExistingOutput { get; private set; }
    public bool RemovedStalePartial { get; private set; }

    public void Prepare()
    {
        ReplacesExistingOutput = _files.Exists(FinalPath);
        RemovedStalePartial = false;
        if (!_files.Exists(PartialPath)) return;

        try { _files.Delete(PartialPath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Lightflow could not remove the stale partial output '{PartialPath}'. Close any application using it and try again.", exception);
        }

        if (_files.Exists(PartialPath))
            throw new IOException($"Lightflow could not remove the stale partial output '{PartialPath}'.");
        RemovedStalePartial = true;
    }

    public void FinalizeValidatedOutput()
    {
        if (!_files.Exists(PartialPath))
            throw new FileNotFoundException("The validated Lightflow partial output no longer exists.", PartialPath);

        if (_files.Exists(FinalPath))
            _files.Replace(PartialPath, FinalPath);
        else
            _files.Move(PartialPath, FinalPath);
        _unfinished = false;
    }

    public string? CleanupFailedAttempt()
    {
        if (!_unfinished) return null;
        string? warning = null;
        try
        {
            if (_files.Exists(PartialPath)) _files.Delete(PartialPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warning = $"Could not remove incomplete output '{PartialPath}': {exception.Message}";
        }

        // An identity for a still-present pre-existing final belongs to that valid file.
        if (!ReplacesExistingOutput || !_files.Exists(FinalPath))
            EncodingOutputIdentityStore.Delete(FinalPath, _identityCacheDirectory);
        _unfinished = false;
        return warning;
    }

    public static bool IsOwnedPartialPath(string path) =>
        path.EndsWith(PartialExtension, StringComparison.OrdinalIgnoreCase)
        && Path.GetFileNameWithoutExtension(path).Length > 0;

    public static string PartialPathFor(string finalPath) => Path.GetFullPath(finalPath) + PartialExtension;

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}

internal interface IEncodingOutputFileSystem
{
    bool Exists(string path);
    void Delete(string path);
    void Move(string source, string destination);
    void Replace(string source, string destination);
}

internal sealed class PhysicalEncodingOutputFileSystem : IEncodingOutputFileSystem
{
    public static PhysicalEncodingOutputFileSystem Instance { get; } = new();
    private PhysicalEncodingOutputFileSystem() { }
    public bool Exists(string path) => File.Exists(path);
    public void Delete(string path) => File.Delete(path);
    public void Move(string source, string destination) => File.Move(source, destination);
    public void Replace(string source, string destination) => File.Replace(source, destination, null, true);
}
