using System.IO;
namespace LightflowStudio;

public sealed class MachineIdentityProvider(string path) : IMachineIdentityProvider
{
    private static readonly object Gate = new();
    private string? _value;

    public string GetMachineId()
    {
        lock (Gate)
        {
            if (_value is not null) return _value;
            if (File.Exists(path)) return _value = ReadEstablishedIdentity();

            var created = Guid.NewGuid().ToString("D");
            var temporary = path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(temporary, created);
                File.Move(temporary, path, overwrite: false);
                return _value = created;
            }
            catch (IOException exception) when (File.Exists(path))
            {
                return _value = ReadEstablishedIdentity(exception);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new MachineIdentityException(MachineIdentityFailure.Unavailable,
                    "Lightflow could not establish this installation's Media Root identity.", exception);
            }
            finally { try { File.Delete(temporary); } catch { } }
        }
    }

    private string ReadEstablishedIdentity(Exception? raceException = null)
    {
        string contents;
        try { contents = File.ReadAllText(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new MachineIdentityException(MachineIdentityFailure.Unavailable,
                "Lightflow could not read this installation's established Media Root identity.", exception);
        }
        if (!Guid.TryParse(contents.Trim(), out var identity))
            throw new MachineIdentityException(MachineIdentityFailure.Malformed,
                "This installation's Media Root identity is malformed. Lightflow preserved it for diagnosis.", raceException);
        return identity.ToString("D");
    }
}

