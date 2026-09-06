using System.IO;

namespace LightflowStudio;

internal enum ExportDestinationMode
{
    SpecificFolder,
    SameFolderAsOriginal
}

/// <summary>Immutable destination intent captured with an Export job definition.</summary>
internal sealed record ExportDestination(
    ExportDestinationMode Mode,
    string? SpecificFolder,
    string? Subfolder)
{
    public ExportDestination Normalize()
    {
        if (!Enum.IsDefined(Mode)) throw new ArgumentOutOfRangeException(nameof(Mode));
        var suppliedFolder = Mode == ExportDestinationMode.SpecificFolder && !string.IsNullOrWhiteSpace(SpecificFolder)
            ? SpecificFolder.Trim() : null;
        if (Mode == ExportDestinationMode.SpecificFolder &&
            (suppliedFolder is null || !Path.IsPathFullyQualified(suppliedFolder)))
            throw new ArgumentException("Choose a valid absolute output folder.", nameof(SpecificFolder));
        var folder = suppliedFolder is null ? null : Path.GetFullPath(suppliedFolder);
        var subfolder = string.IsNullOrWhiteSpace(Subfolder) ? null : Subfolder.Trim();
        if (subfolder is not null && (Path.IsPathRooted(subfolder)
            || subfolder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || subfolder.Contains(Path.DirectorySeparatorChar)
            || subfolder.Contains(Path.AltDirectorySeparatorChar)
            || subfolder is "." or ".."))
            throw new ArgumentException("The output subfolder must be a single valid folder name.", nameof(Subfolder));
        return this with { SpecificFolder = folder, Subfolder = subfolder };
    }

    public string ResolveDirectory(string sourcePath)
    {
        var normalized = Normalize();
        var baseDirectory = normalized.Mode switch
        {
            ExportDestinationMode.SpecificFolder => normalized.SpecificFolder!,
            ExportDestinationMode.SameFolderAsOriginal => Path.GetDirectoryName(Path.GetFullPath(sourcePath))
                ?? throw new ArgumentException("The source does not have a containing folder.", nameof(sourcePath)),
            _ => throw new ArgumentOutOfRangeException(nameof(Mode))
        };
        return normalized.Subfolder is null ? baseDirectory : Path.Combine(baseDirectory, normalized.Subfolder);
    }
}
