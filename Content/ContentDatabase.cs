using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;

namespace Roguelike.Content;

/// <summary>Immutable, validated data-driven game content.</summary>
public sealed class ContentDatabase
{
    private readonly ImmutableDictionary<string, MonsterContent> monsters;
    private readonly ImmutableDictionary<string, ItemContent> items;

    private ContentDatabase(ImmutableDictionary<string, MonsterContent> monsters,
        ImmutableDictionary<string, ItemContent> items, string contentHash, BalanceContent balance)
    {
        this.monsters = monsters;
        this.items = items;
        ContentHash = contentHash;
        Balance = balance;
    }

    /// <summary>Gets the stable SHA-256 hash of the canonical content.</summary>
    public string ContentHash { get; }
    /// <summary>Gets validated balance values.</summary>
    public BalanceContent Balance { get; }
    /// <summary>Gets all monsters.</summary>
    public IReadOnlyCollection<MonsterContent> Monsters => monsters.Values.ToArray();
    /// <summary>Gets all items.</summary>
    public IReadOnlyCollection<ItemContent> Items => items.Values.ToArray();

    /// <summary>Loads the embedded default content.</summary>
    public static ContentDatabase LoadDefault() =>
        Load(new Dictionary<string, string>
        {
            ["monsters.json"] = ReadResource("monsters.json"),
            ["items.json"] = ReadResource("items.json"),
            ["balance.json"] = ReadResource("balance.json")
        });

    /// <summary>Loads a complete override directory.</summary>
    public static ContentDatabase LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory)) throw new ContentLoadException($"Content directory does not exist: {directory}");
        return Load(new Dictionary<string, string>
        {
            ["monsters.json"] = File.ReadAllText(Path.Combine(directory, "monsters.json")),
            ["items.json"] = File.ReadAllText(Path.Combine(directory, "items.json")),
            ["balance.json"] = File.ReadAllText(Path.Combine(directory, "balance.json"))
        });
    }

    /// <summary>Gets a monster by its JSON id.</summary>
    public MonsterContent GetMonster(string id) => monsters.TryGetValue(id, out MonsterContent? value)
        ? value : throw new KeyNotFoundException($"Unknown monster id: {id}");
    /// <summary>Gets an item by its JSON id.</summary>
    public ItemContent GetItem(ItemId id) => items.TryGetValue(id.Value, out ItemContent? value)
        ? value : throw new KeyNotFoundException($"Unknown item id: {id.Value}");

    private static ContentDatabase Load(IReadOnlyDictionary<string, string> files)
    {
        List<string> errors = new();
        JsonDocument monstersDocument = Parse(files, "monsters.json", errors);
        JsonDocument itemsDocument = Parse(files, "items.json", errors);
        JsonDocument balanceDocument = Parse(files, "balance.json", errors);
        MonsterFile? monsterFile = Deserialize<MonsterFile>(monstersDocument, "monsters.json", errors);
        ItemFile? itemFile = Deserialize<ItemFile>(itemsDocument, "items.json", errors);
        BalanceFile? balanceFile = Deserialize<BalanceFile>(balanceDocument, "balance.json", errors);
        Dictionary<string, MonsterContent> monsters = new(StringComparer.Ordinal);
        Dictionary<string, ItemContent> items = new(StringComparer.Ordinal);
        if (monsterFile is not null)
            foreach (MonsterJson item in monsterFile.Monsters ?? [])
            {
                ValidateMonster(item, monsters, errors);
                if (!errors.Any(error => error.StartsWith($"monsters.json: monsters[{Array.IndexOf(monsterFile.Monsters!, item)}].", StringComparison.Ordinal)))
                {
                    TryColor(item.Color, out Color color);
                    monsters[item.Id] = new MonsterContent(item.Id, item.Name, item.Glyph[0], color, item.MaxHp,
                        item.Attack, item.Defense, item.Xp, item.SightRadius, item.MinDepth, item.SpawnWeight,
                        Enum.Parse<MonsterBehavior>(item.Behavior, true));
                }
            }
        if (itemFile is not null)
            foreach (ItemJson item in itemFile.Items ?? [])
            {
                ValidateItem(item, items, errors);
                if (!errors.Any(error => error.StartsWith($"items.json: items[{Array.IndexOf(itemFile.Items!, item)}].", StringComparison.Ordinal)))
                {
                    TryColor(item.Color, out Color color);
                    items[item.Id] = new ItemContent(new ItemId(item.Id), item.Name, item.Description, item.Glyph[0],
                        color, Enum.Parse<ItemType>(item.Type, true), item.MinDepth, item.Weight, item.MaxStack,
                        item.Effects ?? [], item.AttackBonus, item.DefenseBonus);
                }
            }
        if (balanceFile is null) errors.Add("balance.json: root must be an object");
        if (errors.Count > 0) throw new ContentLoadException(string.Join(Environment.NewLine, errors));
        string canonical = string.Join("\n", files.OrderBy(pair => pair.Key).Select(pair => Canonicalize(pair.Value)));
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new ContentDatabase(monsters.ToImmutableDictionary(StringComparer.Ordinal),
            items.ToImmutableDictionary(StringComparer.Ordinal), hash,
            new BalanceContent(balanceFile!.StartingHp, balanceFile.StartingAttack, balanceFile.StartingDefense,
                balanceFile.SpawnBase, balanceFile.SpawnPerDepth));
    }

    private static JsonDocument Parse(IReadOnlyDictionary<string, string> files, string name, List<string> errors)
    {
        try { return JsonDocument.Parse(files[name]); }
        catch (Exception exception) { errors.Add($"{name}: invalid JSON ({exception.Message})"); return JsonDocument.Parse("{}"); }
    }

    private static T? Deserialize<T>(JsonDocument document, string file, List<string> errors)
    {
        try { return document.RootElement.Deserialize<T>(Options); }
        catch (JsonException exception) { errors.Add($"{file}: {exception.Message}"); return default; }
    }

    private static void ValidateMonster(MonsterJson item, Dictionary<string, MonsterContent> values, List<string> errors)
    {
        int index = values.Count;
        string prefix = $"monsters.json: monsters[{index}]";
        if (string.IsNullOrWhiteSpace(item.Id)) errors.Add($"{prefix}.id must not be empty");
        if (!string.IsNullOrWhiteSpace(item.Id) && values.ContainsKey(item.Id)) errors.Add($"{prefix}.id is duplicated");
        if (item.MaxHp <= 0) errors.Add($"{prefix}.maxHp must be > 0");
        if (item.Attack < 0) errors.Add($"{prefix}.attack must be >= 0");
        if (item.Defense < 0) errors.Add($"{prefix}.defense must be >= 0");
        if (item.MinDepth < 1) errors.Add($"{prefix}.minDepth must be >= 1");
        if (item.SpawnWeight <= 0) errors.Add($"{prefix}.spawnWeight must be > 0");
        if (item.Glyph.Length != 1 || item.Glyph[0] < 32 || item.Glyph[0] > 126)
            errors.Add($"{prefix}.glyph must be exactly one printable ASCII char");
        if (!TryColor(item.Color, out _)) errors.Add($"{prefix}.color must be #RRGGBB");
        if (!Enum.TryParse<MonsterBehavior>(item.Behavior, true, out _))
            errors.Add($"{prefix}.behavior is unknown");
    }

    private static void ValidateItem(ItemJson item, Dictionary<string, ItemContent> values, List<string> errors)
    {
        int index = values.Count;
        string prefix = $"items.json: items[{index}]";
        if (string.IsNullOrWhiteSpace(item.Id)) errors.Add($"{prefix}.id must not be empty");
        if (!string.IsNullOrWhiteSpace(item.Id) && values.ContainsKey(item.Id)) errors.Add($"{prefix}.id is duplicated");
        if (item.MinDepth < 1) errors.Add($"{prefix}.minDepth must be >= 1");
        if (item.Weight <= 0) errors.Add($"{prefix}.weight must be > 0");
        if (item.MaxStack <= 0) errors.Add($"{prefix}.maxStack must be > 0");
        if (!TryColor(item.Color, out _)) errors.Add($"{prefix}.color must be #RRGGBB");
        foreach (EffectJson effect in item.Effects ?? [])
            if (!EffectRegistry.Known.Contains(effect.Type))
                errors.Add($"{prefix}.effects type '{effect.Type}' is unknown");
    }

    private static bool TryColor(string value, out Color color)
    {
        color = Color.White;
        if (value is null || value.Length != 7 || value[0] != '#') return false;
        if (!uint.TryParse(value[1..], System.Globalization.NumberStyles.HexNumber, null, out uint rgb)) return false;
        color = new Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    private static string Canonicalize(string json) =>
        JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement, Options);
    private static string ReadResource(string file) =>
        typeof(ContentDatabase).Assembly.GetManifestResourceStream($"Roguelike.Content.{file}") is Stream stream
            ? new StreamReader(stream).ReadToEnd()
            : throw new ContentLoadException($"Embedded content is missing: {file}");

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
}

/// <summary>Raised when content validation fails.</summary>
public sealed class ContentLoadException : Exception
{
    /// <summary>Creates a content error.</summary>
    public ContentLoadException(string message) : base(message) { }
}

/// <summary>Validated monster content.</summary>
public sealed record MonsterContent(string Id, string Name, char Glyph, Color Color, int MaxHp, int Attack,
    int Defense, int Xp, int SightRadius, int MinDepth, int SpawnWeight, MonsterBehavior Behavior);
/// <summary>Validated item content.</summary>
public sealed record ItemContent(ItemId Id, string Name, string Description, char Glyph, Color Color,
    ItemType Type, int MinDepth, int Weight, int MaxStack, IReadOnlyList<EffectJson> Effects,
    int AttackBonus, int DefenseBonus);
/// <summary>Validated balance content.</summary>
public sealed record BalanceContent(int StartingHp, int StartingAttack, int StartingDefense, int SpawnBase, int SpawnPerDepth);
/// <summary>Declarative item effect.</summary>
public sealed record EffectJson(string Type, int Amount = 0, string? Stat = null, int Turns = 0, int MinDistance = 0);

internal sealed record MonsterFile(int SchemaVersion, MonsterJson[]? Monsters);
internal sealed record ItemFile(int SchemaVersion, ItemJson[]? Items);
internal sealed record BalanceFile(int SchemaVersion, int StartingHp = 30, int StartingAttack = 5,
    int StartingDefense = 1, int SpawnBase = 2, int SpawnPerDepth = 2);
internal sealed record MonsterJson(string Id = "", string Name = "", string Glyph = "?", string Color = "#FFFFFF",
    int MaxHp = 0, int Attack = 0, int Defense = 0, int Xp = 0, int SightRadius = 0, int MinDepth = 1,
    int SpawnWeight = 1, string Behavior = "idle", Dictionary<string, int>? Params = null);
internal sealed record ItemJson(string Id = "", string Name = "", string Description = "", string Glyph = "?",
    string Color = "#FFFFFF", string Type = "consumable", int MinDepth = 1, int Weight = 1, int MaxStack = 1,
    int AttackBonus = 0, int DefenseBonus = 0, string? Slot = null, EffectJson[]? Effects = null);

/// <summary>Known declarative effect handlers.</summary>
public static class EffectRegistry
{
    /// <summary>Effect types accepted by the content loader.</summary>
    public static IReadOnlySet<string> Known { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "heal", "buff", "teleport", "reveal_map" };
}
