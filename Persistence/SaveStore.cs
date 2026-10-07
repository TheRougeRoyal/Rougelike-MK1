using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Roguelike.Persistence;

/// <summary>Stores the single resumable run save.</summary>
public interface ISaveStore
{
    /// <summary>Reads the save or returns null when absent.</summary>
    string? Read();
    /// <summary>Atomically replaces the save.</summary>
    void Write(string json);
    /// <summary>Deletes the save.</summary>
    void Delete();
}

/// <summary>Atomic file-backed save store.</summary>
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
        using (FileStream stream = new(temp, FileMode.Create, FileAccess.Write, FileShare.None,
            4096, FileOptions.WriteThrough))
        using (StreamWriter writer = new(stream, Encoding.UTF8))
        {
            writer.Write(json);
            writer.Flush();
            stream.Flush(true);
        }
        if (File.Exists(path)) File.Copy(path, backup, true);
        File.Move(temp, path, true);
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
    /// <inheritdoc />
    public string? Read() => Value;
    /// <inheritdoc />
    public void Write(string json) => Value = json;
    /// <inheritdoc />
    public void Delete() => Value = null;
}

/// <summary>Dedicated persistence DTO envelope.</summary>
public sealed class SaveDto
{
    /// <summary>Save schema version.</summary>
    public int SchemaVersion { get; set; } = 2;
    /// <summary>Content hash used to generate the run.</summary>
    public string ContentHash { get; set; } = string.Empty;
    /// <summary>Run seed.</summary>
    public int Seed { get; set; }
    /// <summary>Depth and turn number.</summary>
    public int Depth { get; set; }
    /// <summary>Completed turns.</summary>
    public int TurnNumber { get; set; }
    /// <summary>Serialized RNG states.</summary>
    public Dictionary<string, ulong> RandomStates { get; set; } = new();
    /// <summary>Opaque versioned game payload.</summary>
    public JsonElement Game { get; set; }
    /// <summary>Checksum of all fields except this one.</summary>
    public string Checksum { get; set; } = string.Empty;
}

/// <summary>Validates and serializes save envelopes.</summary>
public static class SaveCodec
{
    /// <summary>Serializes an envelope with a checksum.</summary>
    public static string Encode(SaveDto dto)
    {
        dto.Checksum = string.Empty;
        string unsigned = JsonSerializer.Serialize(dto, Options);
        dto.Checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(unsigned))).ToLowerInvariant();
        return JsonSerializer.Serialize(dto, Options);
    }
    /// <summary>Decodes and verifies a save, including a trivial v1 migration.</summary>
    public static SaveDto Decode(string json)
    {
        SaveDto dto;
        try { dto = JsonSerializer.Deserialize<SaveDto>(json, Options) ?? throw new InvalidDataException("Save is empty."); }
        catch (JsonException exception) { throw new InvalidDataException("Save JSON is corrupt or truncated.", exception); }
        if (dto.SchemaVersion > 2) throw new InvalidDataException($"Save schemaVersion {dto.SchemaVersion} is newer than supported version 2.");
        string expected = dto.Checksum;
        dto.Checksum = string.Empty;
        string unsigned = JsonSerializer.Serialize(dto, Options);
        string actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(unsigned))).ToLowerInvariant();
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Save checksum mismatch.");
        if (dto.SchemaVersion == 1) dto.SchemaVersion = 2;
        dto.Checksum = expected;
        return dto;
    }
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
}
