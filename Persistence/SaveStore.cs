using System.Text;

namespace Roguelike.Persistence;

/// <summary>Stores the single resumable run save.</summary>
public interface ISaveStore
{
    /// <summary>Reads the save or returns null when absent.</summary>
    string? Read();
    /// <summary>Reads the previous atomic-save backup, if available.</summary>
    string? ReadBackup() => null;
    /// <summary>Atomically replaces the save.</summary>
    void Write(string json);
    /// <summary>Deletes the save.</summary>
    void Delete();
}

/// <summary>
/// Atomic file-backed save store. Multiple instances sharing a directory use
/// last-writer-wins semantics; a failed write leaves the prior save untouched.
/// </summary>
public sealed class FileSaveStore : ISaveStore
{
    private readonly string path;
    /// <summary>Creates a store in a directory.</summary>
    public FileSaveStore(string directory)
    {
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "save.json");
    }
    /// <inheritdoc />
    public string? Read() => File.Exists(path) ? File.ReadAllText(path) : null;
    public string? ReadBackup() => File.Exists(path + ".bak") ? File.ReadAllText(path + ".bak") : null;
    /// <inheritdoc />
    public void Write(string json)
    {
        string temp = path + ".tmp";
        string backup = path + ".bak";
        try
        {
            using (FileStream stream = new(temp, FileMode.Create, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough))
            using (StreamWriter writer = new(stream, new UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Copy(path, backup, true);
            File.Move(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); }
                catch { /* Preserve the previous save if cleanup is denied. */ }
            }
        }
    }
    /// <inheritdoc />
    public void Delete()
    {
        if (File.Exists(path)) File.Delete(path);
    }
}

/// <summary>In-memory save store for tests.</summary>
public sealed class MemorySaveStore : ISaveStore
{
    /// <summary>Gets the current stored JSON.</summary>
    public string? Value { get; private set; }
    public string? Backup { get; private set; }
    /// <inheritdoc />
    public string? Read() => Value;
    public string? ReadBackup() => Backup;
    /// <summary>Replaces the current raw value for deterministic failure tests.</summary>
    public void Replace(string? json) => Value = json;
    /// <inheritdoc />
    public void Write(string json)
    {
        Backup = Value;
        Value = json;
    }
    /// <inheritdoc />
    public void Delete()
    {
        Backup = Value;
        Value = null;
    }
}
