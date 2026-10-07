using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework;

namespace Roguelike.Content;

/// <summary>Immutable, validated game content loaded from JSON.</summary>
public sealed class ContentDatabase
{
    private const int SchemaVersion = 2;
    private readonly ImmutableDictionary<string, MonsterContent> monsters;
    private readonly ImmutableDictionary<string, ItemContent> items;

    private ContentDatabase(
        ImmutableDictionary<string, MonsterContent> monsters,
        ImmutableDictionary<string, ItemContent> items,
        BalanceContent balance,
        string contentHash)
    {
        this.monsters = monsters;
        this.items = items;
        Balance = balance;
        ContentHash = contentHash;
    }

    public string ContentHash { get; }
    public BalanceContent Balance { get; }
    public IReadOnlyCollection<MonsterContent> Monsters => monsters.Values.ToArray();
    public IReadOnlyCollection<ItemContent> Items => items.Values.ToArray();

    public static ContentDatabase LoadDefault() => Load(new Dictionary<string, string>
    {
        ["monsters.json"] = ReadResource("monsters.json"),
        ["items.json"] = ReadResource("items.json"),
        ["balance.json"] = ReadResource("balance.json")
    });

    public static ContentDatabase LoadDirectory(string directory)
    {
        Dictionary<string, string> files = new(StringComparer.Ordinal);
        List<string> errors = new();
        foreach (string name in new[] { "monsters.json", "items.json", "balance.json" })
        {
            string path = Path.Combine(directory, name);
            try { files[name] = File.ReadAllText(path); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{name}: {path}: unable to read file ({exception.Message})");
                files[name] = "{}";
            }
        }

        try { return Load(files, errors); }
        catch (ContentLoadException) { throw; }
        catch (Exception exception) { throw new ContentLoadException($"content: root: {exception.Message}", exception); }
    }

    public MonsterContent GetMonster(string id) =>
        monsters.TryGetValue(id, out MonsterContent? value)
            ? value
            : throw new KeyNotFoundException($"Unknown monster id: {id}");

    public ItemContent GetItem(string id) =>
        items.TryGetValue(id, out ItemContent? value)
            ? value
            : throw new KeyNotFoundException($"Unknown item id: {id}");

    public ItemDefinition CreateDefinition(string id)
    {
        ItemContent item = GetItem(id);
        return new ItemDefinition(item.Id, item.Name, item.Description, item.Type, item.Color,
            item.MinDepth, item.Weight, item.MaxStack, item.AttackBonus, item.DefenseBonus,
            item.Glyph, item.Effects, item.Slot);
    }

    private static ContentDatabase Load(IReadOnlyDictionary<string, string> files, List<string>? initialErrors = null)
    {
        List<string> errors = initialErrors ?? new();
        MonsterFile? monsterFile = Deserialize<MonsterFile>(files["monsters.json"], "monsters.json", errors);
        ItemFile? itemFile = Deserialize<ItemFile>(files["items.json"], "items.json", errors);
        BalanceFile? balanceFile = Deserialize<BalanceFile>(files["balance.json"], "balance.json", errors);
        Dictionary<string, MonsterContent> monsterValues = new(StringComparer.Ordinal);
        Dictionary<string, ItemContent> itemValues = new(StringComparer.Ordinal);

        ValidateSchema(monsterFile?.SchemaVersion, "monsters.json", errors);
        ValidateSchema(itemFile?.SchemaVersion, "items.json", errors);
        ValidateSchema(balanceFile?.SchemaVersion, "balance.json", errors);
        ValidateMonsters(monsterFile?.Monsters, monsterValues, errors);
        ValidateItems(itemFile?.Items, itemValues, errors);
        BalanceContent? balance = ValidateBalance(balanceFile, itemValues, errors);
        ValidateDepthCoverage(monsterValues.Values, itemValues.Values, errors);

        if (errors.Count > 0)
            throw new ContentLoadException(string.Join(Environment.NewLine, errors));

        string canonical = string.Join("\n", files.OrderBy(pair => pair.Key)
            .Select(pair => Canonicalize(pair.Value)));
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
        return new ContentDatabase(monsterValues.ToImmutableDictionary(StringComparer.Ordinal),
            itemValues.ToImmutableDictionary(StringComparer.Ordinal), balance!, hash);
    }

    private static T? Deserialize<T>(string json, string file, List<string> errors)
    {
        try { return JsonSerializer.Deserialize<T>(json, Options); }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            errors.Add($"{file}: $: invalid JSON ({exception.Message})");
            return default;
        }
    }

    private static void ValidateSchema(int? version, string file, List<string> errors)
    {
        if (version is null)
        {
            errors.Add($"{file}: $.schemaVersion is required");
            return;
        }
        if (version != SchemaVersion)
            errors.Add($"{file}: $.schemaVersion must be {SchemaVersion}");
    }

    private static void ValidateMonsters(MonsterJson[]? values,
        Dictionary<string, MonsterContent> result, List<string> errors)
    {
        if (values is null)
        {
            errors.Add("monsters.json: $.monsters must be a non-empty array");
            return;
        }
        if (values.Length == 0) errors.Add("monsters.json: $.monsters must be a non-empty array");
        HashSet<string> ids = new(StringComparer.Ordinal);
        for (int index = 0; index < values.Length; index++)
        {
            MonsterJson item = values[index];
            string prefix = $"monsters.json: monsters[{index}]";
            List<string> before = errors.ToList();
            if (string.IsNullOrWhiteSpace(item.Id)) errors.Add($"{prefix}.id must not be empty");
            else if (!ids.Add(item.Id)) errors.Add($"{prefix}.id is duplicated");
            if (string.IsNullOrWhiteSpace(item.Name)) errors.Add($"{prefix}.name must not be empty");
            if (item.Glyph.Length != 1 || item.Glyph[0] < 32 || item.Glyph[0] > 126)
                errors.Add($"{prefix}.glyph must be exactly one printable ASCII char");
            if (!TryColor(item.Color, out _)) errors.Add($"{prefix}.color must be #RRGGBB");
            if (item.MaxHp <= 0) errors.Add($"{prefix}.maxHp must be > 0");
            if (item.Attack < 0) errors.Add($"{prefix}.attack must be >= 0");
            if (item.Defense < 0) errors.Add($"{prefix}.defense must be >= 0");
            if (item.Xp < 0) errors.Add($"{prefix}.xp must be >= 0");
            if (item.SightRadius < 0) errors.Add($"{prefix}.sightRadius must be >= 0");
            if (item.MinDepth < 1) errors.Add($"{prefix}.minDepth must be >= 1");
            if (item.SpawnWeight <= 0) errors.Add($"{prefix}.spawnWeight must be > 0");
            if (!Enum.TryParse(item.Behavior, true, out MonsterBehavior behavior))
                errors.Add($"{prefix}.behavior is unknown");
            if (item.Params is not null && item.Params.Values.Any(value => value < 0))
                errors.Add($"{prefix}.params values must be >= 0");
            if (errors.Count != before.Count) continue;
            TryColor(item.Color, out Color color);
            result[item.Id] = new MonsterContent(item.Id, item.Name, item.Glyph[0], color, item.MaxHp,
                item.Attack, item.Defense, item.Xp, item.SightRadius, item.MinDepth,
                item.SpawnWeight, behavior, item.Params ?? new Dictionary<string, int>());
        }
    }

    private static void ValidateItems(ItemJson[]? values,
        Dictionary<string, ItemContent> result, List<string> errors)
    {
        if (values is null)
        {
            errors.Add("items.json: $.items must be a non-empty array");
            return;
        }
        if (values.Length == 0) errors.Add("items.json: $.items must be a non-empty array");
        HashSet<string> ids = new(StringComparer.Ordinal);
        for (int index = 0; index < values.Length; index++)
        {
            ItemJson item = values[index];
            string prefix = $"items.json: items[{index}]";
            int errorCount = errors.Count;
            if (string.IsNullOrWhiteSpace(item.Id)) errors.Add($"{prefix}.id must not be empty");
            else if (!ids.Add(item.Id)) errors.Add($"{prefix}.id is duplicated");
            if (string.IsNullOrWhiteSpace(item.Name)) errors.Add($"{prefix}.name must not be empty");
            if (item.Glyph.Length != 1 || item.Glyph[0] < 32 || item.Glyph[0] > 126)
                errors.Add($"{prefix}.glyph must be exactly one printable ASCII char");
            if (!TryColor(item.Color, out Color color)) errors.Add($"{prefix}.color must be #RRGGBB");
            if (!Enum.TryParse(item.Type, true, out ItemType type))
                errors.Add($"{prefix}.type is unknown");
            if (item.MinDepth < 1) errors.Add($"{prefix}.minDepth must be >= 1");
            if (item.Weight <= 0) errors.Add($"{prefix}.weight must be > 0");
            if (item.MaxStack <= 0) errors.Add($"{prefix}.maxStack must be > 0");
            if (type is ItemType.Weapon or ItemType.Armor)
            {
                string requiredSlot = type == ItemType.Weapon ? "weapon" : "armor";
                if (!string.Equals(item.Slot, requiredSlot, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"{prefix}.slot must be '{requiredSlot}' for type '{item.Type}'");
            }
            else if (!string.IsNullOrWhiteSpace(item.Slot))
                errors.Add($"{prefix}.slot must be empty for consumables");
            foreach (EffectJson effect in item.Effects ?? [])
            {
                if (!EffectRegistry.Known.Contains(effect.Type))
                    errors.Add($"{prefix}.effects type '{effect.Type}' is unknown");
                else if (effect.Type.Equals("heal", StringComparison.OrdinalIgnoreCase) && effect.Amount <= 0)
                    errors.Add($"{prefix}.effects heal amount must be > 0");
                else if (effect.Type.Equals("buff", StringComparison.OrdinalIgnoreCase) &&
                    (!EffectRegistry.BuffStats.Contains(effect.Stat ?? string.Empty) ||
                     effect.Amount <= 0 || effect.Turns <= 0))
                    errors.Add($"{prefix}.effects buff requires a known stat, amount > 0, and turns > 0");
                else if (effect.Type.Equals("teleport", StringComparison.OrdinalIgnoreCase) &&
                    effect.MinDistance <= 0)
                    errors.Add($"{prefix}.effects teleport minDistance must be > 0");
            }
            if (errors.Count != errorCount) continue;
            result[item.Id] = new ItemContent(item.Id, item.Name, item.Description, item.Glyph[0], color, type,
                item.MinDepth, item.Weight, item.MaxStack, item.Effects ?? [], item.AttackBonus,
                item.DefenseBonus, item.Slot);
        }
    }

    private static BalanceContent? ValidateBalance(BalanceFile? file,
        Dictionary<string, ItemContent> items, List<string> errors)
    {
        if (file is null) return null;
        string prefix = "balance.json";
        if (file.StartingHp <= 0) errors.Add($"{prefix}: $.startingHp must be > 0");
        if (file.StartingAttack < 0) errors.Add($"{prefix}: $.startingAttack must be >= 0");
        if (file.StartingDefense < 0) errors.Add($"{prefix}: $.startingDefense must be >= 0");
        if (file.SpawnBase <= 0) errors.Add($"{prefix}: $.spawnBase must be > 0");
        if (file.SpawnPerDepth < 0) errors.Add($"{prefix}: $.spawnPerDepth must be >= 0");
        if (file.LootMin < 0 || file.LootMax < file.LootMin)
            errors.Add($"{prefix}: $.lootMin/lootMax must define a non-negative range");
        if (file.DropChancePercent is < 0 or > 100)
            errors.Add($"{prefix}: $.dropChancePercent must be between 0 and 100");
        if (file.XpPerLevel <= 0 ||         file.LevelMaxHpBonus < 0 || file.LevelAttackBonus < 0 || file.LevelDefenseEvery <= 0 ||
            file.LevelDefenseBonus < 0 || file.LevelHealAmount < 0)
            errors.Add($"{prefix}: level-up and XP values are invalid");
        if (file.DepthHpScale < 0 || file.DepthAttackScale < 0 || file.DepthDefenseScale < 0)
            errors.Add($"{prefix}: depth scaling values must be >= 0");
        if (file.StartingLoadout is null || file.StartingLoadout.Length == 0)
            errors.Add($"{prefix}: $.startingLoadout must not be empty");
        else
            for (int index = 0; index < file.StartingLoadout.Length; index++)
                if (!items.ContainsKey(file.StartingLoadout[index]))
                    errors.Add($"{prefix}: $.startingLoadout[{index}] references unknown item id '{file.StartingLoadout[index]}'");
        return new BalanceContent(file.StartingHp, file.StartingAttack, file.StartingDefense,
            file.SpawnBase, file.SpawnPerDepth, file.LootMin, file.LootMax, file.DropChancePercent,
            file.XpPerLevel, file.LevelMaxHpBonus, file.LevelAttackBonus, file.LevelDefenseBonus,
            file.LevelDefenseEvery, file.LevelHealAmount, file.DepthHpScale, file.DepthAttackScale,
            file.DepthDefenseScale,
            file.StartingLoadout ?? []);
    }

    private static void ValidateDepthCoverage(IEnumerable<MonsterContent> monsters,
        IEnumerable<ItemContent> items, List<string> errors)
    {
        for (int depth = 1; depth <= 10; depth++)
        {
            if (!monsters.Any(monster => monster.MinDepth <= depth))
                errors.Add($"content: depth {depth} must have at least one monster");
            if (!items.Any(item => item.MinDepth <= depth))
                errors.Add($"content: depth {depth} must have at least one item");
        }
    }

    private static bool TryColor(string value, out Color color)
    {
        color = Color.White;
        if (value is null || value.Length != 7 || value[0] != '#') return false;
        if (!uint.TryParse(value[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            return false;
        color = new Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    private static string Canonicalize(string json) =>
        JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement, Options);

    private static string ReadResource(string file) =>
        typeof(ContentDatabase).Assembly.GetManifestResourceStream($"Roguelike.Content.{file}") is Stream stream
            ? new StreamReader(stream).ReadToEnd()
            : throw new ContentLoadException($"content: {file}: embedded content is missing");

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
}

public sealed class ContentLoadException : Exception
{
    public ContentLoadException(string message, Exception? innerException = null) : base(message, innerException) { }
}

public sealed record MonsterContent(string Id, string Name, char Glyph, Color Color, int MaxHp, int Attack,
    int Defense, int Xp, int SightRadius, int MinDepth, int SpawnWeight, MonsterBehavior Behavior,
    IReadOnlyDictionary<string, int> Params);

public sealed record ItemContent(string Id, string Name, string Description, char Glyph, Color Color,
    ItemType Type, int MinDepth, int Weight, int MaxStack, IReadOnlyList<EffectJson> Effects,
    int AttackBonus, int DefenseBonus, string? Slot);

public sealed record BalanceContent(int StartingHp, int StartingAttack, int StartingDefense, int SpawnBase,
    int SpawnPerDepth, int LootMin, int LootMax, int DropChancePercent, int XpPerLevel,
    int LevelMaxHpBonus, int LevelAttackBonus, int LevelDefenseBonus, int LevelDefenseEvery, int LevelHealAmount,
    int DepthHpScale, int DepthAttackScale, int DepthDefenseScale, IReadOnlyList<string> StartingLoadout);

public sealed record EffectJson(string Type, int Amount = 0, string? Stat = null, int Turns = 0,
    int MinDistance = 0);

internal sealed record MonsterFile(int SchemaVersion, MonsterJson[]? Monsters);
internal sealed record ItemFile(int SchemaVersion, ItemJson[]? Items);
internal sealed record BalanceFile(int SchemaVersion, int StartingHp = 30, int StartingAttack = 5,
    int StartingDefense = 1, int SpawnBase = 2, int SpawnPerDepth = 2, int LootMin = 2,
    int LootMax = 4, int DropChancePercent = 20, int XpPerLevel = 20, int LevelMaxHpBonus = 5,
    int LevelAttackBonus = 1, int LevelDefenseBonus = 1, int LevelDefenseEvery = 3, int LevelHealAmount = 10,
    int DepthHpScale = 2, int DepthAttackScale = 0, int DepthDefenseScale = 0,
    string[]? StartingLoadout = null);
internal sealed record MonsterJson(string Id = "", string Name = "", string Glyph = "?",
    string Color = "#FFFFFF", int MaxHp = 0, int Attack = 0, int Defense = 0, int Xp = 0,
    int SightRadius = 0, int MinDepth = 1, int SpawnWeight = 1, string Behavior = "idle",
    Dictionary<string, int>? Params = null);
internal sealed record ItemJson(string Id = "", string Name = "", string Description = "",
    string Glyph = "?", string Color = "#FFFFFF", string Type = "consumable", int MinDepth = 1,
    int Weight = 1, int MaxStack = 1, int AttackBonus = 0, int DefenseBonus = 0, string? Slot = null,
    EffectJson[]? Effects = null);

public static class EffectRegistry
{
    public static IReadOnlySet<string> Known { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "heal", "buff", "teleport", "reveal_map" };
    public static IReadOnlySet<string> BuffStats { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "attack", "defense" };
}
