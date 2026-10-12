namespace Lightflow.Application;

/// <summary>Native evidence effects only. The Catalog owner decides acquisition, validation and completion ordering.</summary>
public interface ICatalogEvidenceEffects
{
    FileStream OpenOwnershipJournal(string path);
    FileStream OpenProfileSession(string path);
    void RequireBoundOwnershipJournal(FileStream file, string path);
    void RequireSingleRegularFile(FileStream file);
    void RequireUnlinked(string path);
    bool SameDatabase(string boundDatabase, string candidateDatabase);
    string Fingerprint(FileStream database);
}
