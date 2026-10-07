using System.Text.Json;

namespace Roguelike.Runs;

/// <summary>Configuration identifying a run.</summary>
public sealed record RunConfig(int Seed, string ContentHash, string GameVersion);
/// <summary>Completed-run history entry.</summary>
public sealed record RunRecord(string Id, int Seed, DateTime DateUtc, int DepthReached, int Level,
    int Turns, int Kills, string CauseOfDeath, string ContentHash);

/// <summary>Persists completed run records.</summary>
public interface IRunHistoryStore
{
    /// <summary>Gets records newest first.</summary>
    IReadOnlyList<RunRecord> Read();
    /// <summary>Adds a record and keeps at most 100.</summary>
    void Append(RunRecord record);
}

public static class RunRanking
{
    public static IReadOnlyList<RunRecord> Top(IReadOnlyList<RunRecord> records) =>
        records.OrderByDescending(record => record.DepthReached)
            .ThenByDescending(record => record.Level)
            .ThenBy(record => record.Turns)
            .Take(10)
            .ToArray();
}

/// <summary>In-memory history store.</summary>
public sealed class MemoryRunHistoryStore : IRunHistoryStore
{
    private readonly List<RunRecord> records = new();
    /// <inheritdoc />
    public IReadOnlyList<RunRecord> Read() => records;
    /// <inheritdoc />
    public void Append(RunRecord record)
    {
        records.Insert(0, record);
        if (records.Count > 100) records.RemoveRange(100, records.Count - 100);
    }
}

/// <summary>Atomic JSON history store.</summary>
public sealed class FileRunHistoryStore : IRunHistoryStore
{
    private readonly string path;
    /// <summary>Creates a history store in a directory.</summary>
    public FileRunHistoryStore(string directory)
    {
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "history.json");
    }
    /// <inheritdoc />
    public IReadOnlyList<RunRecord> Read()
    {
        if (!File.Exists(path)) return Array.Empty<RunRecord>();
        try { return JsonSerializer.Deserialize<List<RunRecord>>(File.ReadAllText(path)) ?? []; }
        catch
        {
            File.Move(path, path + ".corrupt-" + DateTime.UtcNow.Ticks, true);
            return Array.Empty<RunRecord>();
        }
    }
    /// <inheritdoc />
    public void Append(RunRecord record)
    {
        List<RunRecord> records = Read().ToList();
        records.Insert(0, record);
        if (records.Count > 100) records.RemoveRange(100, records.Count - 100);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
}
