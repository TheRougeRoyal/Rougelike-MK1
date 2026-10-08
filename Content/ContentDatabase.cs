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

        // Phase 1: Structural Validation using JsonDocument
        ValidateStructure(files, errors);

        // Phase 2: Deserialization
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
            itemValues.ToImmutableDictionary(StringComparer.Ordinal), balance ?? throw new ContentLoadException("balance.json: validation failed"), hash);
    }

    private static void ValidateStructure(IReadOnlyDictionary<string, string> files, List<string> errors)
    {
        ValidateFileStructure(files["monsters.json"], "monsters.json", new[] { "schemaVersion", "monsters" }, errors, (element, path) =>
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                for (int i = 0; i < element.GetArrayLength(); i++)
                {
                    string itemPath = $"{path}[{i}]";
                    ValidateProperties(element[i], new[] { "id", "name", "glyph", "color", "maxHp", "attack", "defense", "xp", "sightRadius", "minDepth", "spawnWeight", "behavior", "params" }, itemPath, errors);

                    if (element[i].ValueKind == JsonValueKind.Object)
                    {
                        var monsterObj = element[i];
                        if (monsterObj.TryGetProperty("behavior", out var behaviorProp) && behaviorProp.ValueKind == JsonValueKind.String)
                        {
                            string behavior = behaviorProp.GetString()!;
                            if (monsterObj.TryGetProperty("params", out var paramsProp) && paramsProp.ValueKind == JsonValueKind.Object)
                            {
                                var allowedParams = behavior.ToLowerInvariant() switch
                                {
                                    "ranged" => new[] { "minRange", "maxRange", "alertTurns" },
                                    "slow" => new[] { "actEveryNTurns", "alertTurns" },
                                    "chase" => new[] { "alertTurns" },
                                    "idle" => new[] { "alertTurns", "alwaysChase" },
                                    _ => Array.Empty<string>()
                                };
                                ValidateProperties(paramsProp.Value, allowedParams, $"{itemPath}.params", errors);
                            }
                        }
                    }
                }
            }
        });

        ValidateFileStructure(files["items.json"], "items.json", new[] { "schemaVersion", "items" }, errors, (element, path) =>
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                for (int i = 0; i < element.GetArrayLength(); i++)
                {
                    string itemPath = $"{path}[{i}]";
                    ValidateProperties(element[i], new[] { "id", "name", "description", "glyph", "color", "type", "minDepth", "weight", "maxStack", "attackBonus", "defenseBonus", "slot", "effects" }, itemPath, errors);

                    if (element[i].ValueKind == JsonValueKind.Object)
                    {
                        var itemObj = element[i];
                        if (itemObj.TryGetProperty("effects", out var effectsProp) && effectsProp.ValueKind == JsonValueKind.Array)
                        {
                            for (int j = 0; j < effectsProp.Value.GetArrayLength(); j++)
                            {
                                string effectPath = $"{itemPath}.effects[{j}]";
                                if (effectsProp.Value[j].ValueKind == JsonValueKind.Object)
                                {
                                    var effectObj = effectsProp.Value[j];
                                    if (effectObj.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
                                    {
                                        string type = typeProp.GetString()!;
                                        var allowedEffectProps = type.ToLowerInvariant() switch
                                        {
                                            "heal" => new[] { "type", "amount" },
                                            "buff" => new[] { "type", "amount", "stat", "turns" },
                                            "teleport" => new[] { "type", "minDistance" },
                                            "reveal_map" => new[] { "type" },
                                            _ => new string[] { "type" }
                                        };
                                        ValidateProperties(effectsProp.Value[j], allowedEffectProps, effectPath, errors);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        });

        ValidateFileStructure(files["balance.json"], "balance.json", new[] { "schemaVersion", "startingHp", "startingAttack", "startingDefense", "spawnBase", "spawnPerDepth", "lootMin", "lootMax", "dropChancePercent", "stairHealPercent", "feedbackDuration", "minSpawnDistance", "xpPerLevel", "levelMaxHpBonus", "levelAttackBonus", "levelDefenseBonus", "levelDefenseEvery", "levelHealAmount", "depthHpScale", "depthAttackScale", "depthDefenseScale", "startingLoadout" }, errors, null);
    }

    private static void ValidateFileStructure(string json, string file, string[] allowedRoot, List<string> errors, Action<JsonElement, string>? childrenValidator)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            ValidateProperties(root, allowedRoot, $"{file}: $", errors);
            if (childrenValidator != null)
            {
                foreach (var prop in root.EnumerateObject())
                {
                    childrenValidator(prop.Value, $"{file}: {prop.Name}");
                }
            }
        }
        catch (JsonException exception)
        {
            errors.Add($"{file}: $: JSON error ({exception.Message})");
        }
    }

    private static void ValidateProperties(JsonElement element, string[] allowed, string path, List<string> errors)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        foreach (var prop in element.EnumerateObject())
        {
            if (!allowed.Contains(prop.Name, StringComparer.OrdinalIgnoreCase))
                errors.Add($"{path}: unknown property '{prop.Name}'");
        }
    }

    private static T? Deserialize<T>(string json, string file, List<string> errors)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException exception)
        {
            errors.Add($"{file}: $: JSON error ({exception.Message})");
            return default;
        }
        catch (Exception exception)
        {
            errors.Add($"{file}: $: critical error ({exception.Message})");
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
            errors.Add("monsters.json: $.monsters is required");
            return;
        }
        if (values.Length == 0) errors.Add("monsters.json: $.monsters must be a non-empty array");

        HashSet<string> ids = new(StringComparer.Ordinal);
        for (int index = 0; index < values.Length; index++)
        {
            MonsterJson item = values[index];
            string prefix = $"monsters.json: monsters[{index}]";
            bool hasError = false;

            if (string.IsNullOrWhiteSpace(item.Id)) { errors.Add($"{prefix}.id is required"); hasError = true; }
            else if (!ids.Add(item.Id)) { errors.Add($"{prefix}.id is duplicated"); hasError = true; }

            if (string.IsNullOrWhiteSpace(item.Name)) { errors.Add($"{prefix}.name is required"); hasError = true; }

            if (item.Glyph == null || item.Glyph.Length != 1 || item.Glyph[0] < 32 || item.Glyph[0] > 126)
                { errors.Add($"{prefix}.glyph must be exactly one printable ASCII char"); hasError = true; }

            if (!TryColor(item.Color, out Color color)) { errors.Add($"{prefix}.color must be #RRGGBB"); hasError = true; }

            if (item.MaxHp == null) { errors.Add($"{prefix}.maxHp is required"); hasError = true; }
            else if (item.MaxHp <= 0) { errors.Add($"{prefix}.maxHp must be > 0"); hasError = true; }

            if (item.Attack == null) { errors.Add($"{prefix}.attack is required"); hasError = true; }
            else if (item.Attack < 0) { errors.Add($"{prefix}.attack must be >= 0"); hasError = true; }

            if (item.Defense == null) { errors.Add($"{prefix}.defense is required"); hasError = true; }
            else if (item.Defense < 0) { errors.Add($"{prefix}.defense must be >= 0"); hasError = true; }

            if (item.Xp == null) { errors.Add($"{prefix}.xp is required"); hasError = true; }
            else if (item.Xp < 0) { errors.Add($"{prefix}.xp must be >= 0"); hasError = true; }

            if (item.SightRadius == null) { errors.Add($"{prefix}.sightRadius is required"); hasError = true; }
            else if (item.SightRadius < 0) { errors.Add($"{prefix}.sightRadius must be >= 0"); hasError = true; }

            if (item.MinDepth == null) { errors.Add($"{prefix}.minDepth is required"); hasError = true; }
            else if (item.MinDepth < 1) { errors.Add($"{prefix}.minDepth must be >= 1"); hasError = true; }

            if (item.SpawnWeight == null) { errors.Add($"{prefix}.spawnWeight is required"); hasError = true; }
            else if (item.SpawnWeight <= 0) { errors.Add($"{prefix}.spawnWeight must be > 0"); hasError = true; }

            MonsterBehavior behavior = default;
            if (string.IsNullOrWhiteSpace(item.Behavior) || !Enum.TryParse(item.Behavior, true, out behavior))
            {
                errors.Add($"{prefix}.behavior is unknown or missing");
                hasError = true;
            }

            if (item.Params is not null && item.Params.Values.Any(value => value < 0))
            {
                errors.Add($"{prefix}.params values must be >= 0");
                hasError = true;
            }

            if (!hasError)
            {
                int beforeParams = errors.Count;
                ValidateMonsterParams(item, behavior, prefix, errors);
                if (errors.Count == beforeParams)
                {
                    result[item.Id!] = new MonsterContent(item.Id!, item.Name!, item.Glyph![0], color, item.MaxHp!.Value,
                        item.Attack!.Value, item.Defense!.Value, item.Xp!.Value, item.SightRadius!.Value, item.MinDepth!.Value,
                        item.SpawnWeight!.Value, behavior, item.Params ?? new Dictionary<string, int>());
                }
            }
            else
            {
                if (item.Behavior != null && Enum.TryParse(item.Behavior, true, out MonsterBehavior b))
                    ValidateMonsterParams(item, b, prefix, errors);
            }
        }
    }

    private static void ValidateItems(ItemJson[]? values,
        Dictionary<string, ItemContent> result, List<string> errors)
    {
        if (values is null)
        {
            errors.Add("items.json: $.items is required");
            return;
        }
        if (values.Length == 0) errors.Add("items.json: $.items must be a non-empty array");

        HashSet<string> ids = new(StringComparer.Ordinal);
        for (int index = 0; index < values.Length; index++)
        {
            ItemJson item = values[index];
            string prefix = $"items.json: items[{index}]";
            bool hasError = false;

            if (string.IsNullOrWhiteSpace(item.Id)) { errors.Add($"{prefix}.id is required"); hasError = true; }
            else if (!ids.Add(item.Id)) { errors.Add($"{prefix}.id is duplicated"); hasError = true; }

            if (string.IsNullOrWhiteSpace(item.Name)) { errors.Add($"{prefix}.name is required"); hasError = true; }
            if (string.IsNullOrWhiteSpace(item.Description)) { errors.Add($"{prefix}.description is required"); hasError = true; }

            if (item.Glyph == null || item.Glyph.Length != 1 || item.Glyph[0] < 32 || item.Glyph[0] > 126)
                { errors.Add($"{prefix}.glyph must be exactly one printable ASCII char"); hasError = true; }

            if (!TryColor(item.Color, out Color color)) { errors.Add($"{prefix}.color must be #RRGGBB"); hasError = true; }

            ItemType type = default;
            if (string.IsNullOrWhiteSpace(item.Type) || !Enum.TryParse(item.Type, true, out type))
            {
                errors.Add($"{prefix}.type is unknown or missing");
                hasError = true;
            }

            if (item.MinDepth == null) { errors.Add($"{prefix}.minDepth is required"); hasError = true; }
            else if (item.MinDepth < 1) { errors.Add($"{prefix}.minDepth must be >= 1"); hasError = true; }

            if (item.Weight == null) { errors.Add($"{prefix}.weight is required"); hasError = true; }
            else if (item.Weight <= 0) { errors.Add($"{prefix}.weight must be > 0"); hasError = true; }

            if (item.MaxStack == null) { errors.Add($"{prefix}.maxStack is required"); hasError = true; }
            else if (item.MaxStack <= 0) { errors.Add($"{prefix}.maxStack must be > 0"); hasError = true; }

            if (type != default)
            {
                if (type is ItemType.Weapon or ItemType.Armor)
                {
                    string requiredSlot = type == ItemType.Weapon ? "weapon" : "armor";
                    if (item.Slot == null || !string.Equals(item.Slot, requiredSlot, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add($"{prefix}.slot must be '{requiredSlot}' for type '{item.Type}'");
                        hasError = true;
                    }
                    if (item.MaxStack != 1) { errors.Add($"{prefix}.maxStack must be 1 for equipment"); hasError = true; }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(item.Slot))
                    {
                        errors.Add($"{prefix}.slot must be empty for consumables");
                        hasError = true;
                    }
                }
            }

            if (item.Effects == null)
            {
                errors.Add($"{prefix}.effects array is required");
                hasError = true;
            }
            else
            {
                foreach (EffectJson effect in item.Effects)
                {
                    if (effect == null) { errors.Add($"{prefix}.effects contains a null entry"); hasError = true; continue; }
                    if (string.IsNullOrWhiteSpace(effect.Type)) { errors.Add($"{prefix}.effects type must not be empty"); hasError = true; continue; }

                    if (!EffectRegistry.Known.Contains(effect.Type))
                    {
                        errors.Add($"{prefix}.effects type '{effect.Type}' is unknown");
                        hasError = true;
                    }
                    else
                    {
                        if (effect.Type.Equals("heal", StringComparison.OrdinalIgnoreCase))
                        {
                            if (effect.Amount == null) { errors.Add($"{prefix}.effects heal amount is required"); hasError = true; }
                            else if (effect.Amount <= 0) { errors.Add($"{prefix}.effects heal amount must be > 0"); hasError = true; }
                        }
                        else if (effect.Type.Equals("buff", StringComparison.OrdinalIgnoreCase))
                        {
                            if (string.IsNullOrWhiteSpace(effect.Stat) || !EffectRegistry.BuffStats.Contains(effect.Stat))
                            {
                                errors.Add($"{prefix}.effects buff requires a known stat");
                                hasError = true;
                            }
                            if (effect.Amount == null || effect.Turns == null || effect.Amount <= 0 || effect.Turns <= 0)
                            {
                                errors.Add($"{prefix}.effects buff requires amount > 0 and turns > 0");
                                hasError = true;
                            }
                        }
                        else if (effect.Type.Equals("teleport", StringComparison.OrdinalIgnoreCase))
                        {
                            if (effect.MinDistance == null) { errors.Add($"{prefix}.effects teleport minDistance is required"); hasError = true; }
                            else if (effect.MinDistance <= 0) { errors.Add($"{prefix}.effects teleport minDistance must be > 0"); hasError = true; }
                        }
                        else if (effect.Type.Equals("reveal_map", StringComparison.OrdinalIgnoreCase))
                        {
                            if (effect.Stat != null || effect.Turns != null || effect.MinDistance != null || effect.Amount != null)
                            {
                                errors.Add($"{prefix}.effects reveal_map takes no parameters");
                                hasError = true;
                            }
                        }
                    }
                }
            }

            if (!hasError)
            {
                result[item.Id!] = new ItemContent(item.Id!, item.Name!, item.Description!, item.Glyph![0], color, type,
                    item.MinDepth!.Value, item.Weight!.Value, item.MaxStack!.Value, item.Effects!, item.AttackBonus ?? 0,
                    item.DefenseBonus ?? 0, item.Slot);
            }
        }
    }

    private static BalanceContent? ValidateBalance(BalanceFile? file,
        Dictionary<string, ItemContent> items, List<string> errors)
    {
        if (file is null)
        {
            errors.Add("balance.json: file is required");
            return null;
        }
        string prefix = "balance.json";
        bool hasError = false;

        if (file.StartingHp == null) { errors.Add($"{prefix}: $.startingHp is required"); hasError = true; }
        else if (file.StartingHp <= 0) { errors.Add($"{prefix}: $.startingHp must be > 0"); hasError = true; }

        if (file.StartingAttack == null) { errors.Add($"{prefix}: $.startingAttack is required"); hasError = true; }
        else if (file.StartingAttack < 0) { errors.Add($"{prefix}: $.startingAttack must be >= 0"); hasError = true; }

        if (file.StartingDefense == null) { errors.Add($"{prefix}: $.startingDefense is required"); hasError = true; }
        else if (file.StartingDefense < 0) { errors.Add($"{prefix}: $.startingDefense must be >= 0"); hasError = true; }

        if (file.SpawnBase == null) { errors.Add($"{prefix}: $.spawnBase is required"); hasError = true; }
        else if (file.SpawnBase <= 0) { errors.Add($"{prefix}: $.spawnBase must be > 0"); hasError = true; }

        if (file.SpawnPerDepth == null) { errors.Add($"{prefix}: $.spawnPerDepth is required"); hasError = true; }
        else if (file.SpawnPerDepth < 0) { errors.Add($"{prefix}: $.spawnPerDepth must be >= 0"); hasError = true; }

        if (file.LootMin == null || file.LootMax == null || file.LootMin < 0 || file.LootMax < file.LootMin)
            { errors.Add($"{prefix}: $.lootMin/lootMax must define a non-negative range"); hasError = true; }

        if (file.DropChancePercent == null) { errors.Add($"{prefix}: $.dropChancePercent is required"); hasError = true; }
        else if (file.DropChancePercent is < 0 or > 100)
            { errors.Add($"{prefix}: $.dropChancePercent must be between 0 and 100"); hasError = true; }

        if (file.StairHealPercent == null) { errors.Add($"{prefix}: $.stairHealPercent is required"); hasError = true; }
        else if (file.StairHealPercent is < 0 or > 100)
            { errors.Add($"{prefix}: $.stairHealPercent must be between 0 and 100"); hasError = true; }

        if (file.FeedbackDuration == null) { errors.Add($"{prefix}: $.feedbackDuration is required"); hasError = true; }
        else if (file.FeedbackDuration < 0) { errors.Add($"{prefix}: $.feedbackDuration must be >= 0"); hasError = true; }

        if (file.MinSpawnDistance == null) { errors.Add($"{prefix}: $.minSpawnDistance is required"); hasError = true; }
        else if (file.MinSpawnDistance < 0) { errors.Add($"{prefix}: $.minSpawnDistance must be >= 0"); hasError = true; }

        if (file.XpPerLevel == null) { errors.Add($"{prefix}: $.xpPerLevel is required"); hasError = true; }
        else if (file.XpPerLevel <= 0) { errors.Add($"{prefix}: $.xpPerLevel must be > 0"); hasError = true; }

        if (file.LevelMaxHpBonus == null) { errors.Add($"{prefix}: $.levelMaxHpBonus is required"); hasError = true; }
        else if (file.LevelMaxHpBonus < 0) { errors.Add($"{prefix}: $.levelMaxHpBonus must be >= 0"); hasError = true; }

        if (file.LevelAttackBonus == null) { errors.Add($"{prefix}: $.levelAttackBonus is required"); hasError = true; }
        else if (file.LevelAttackBonus < 0) { errors.Add($"{prefix}: $.levelAttackBonus must be >= 0"); hasError = true; }

        if (file.LevelDefenseBonus == null) { errors.Add($"{prefix}: $.levelDefenseBonus is required"); hasError = true; }
        else if (file.LevelDefenseBonus < 0) { errors.Add($"{prefix}: $.levelDefenseBonus must be >= 0"); hasError = true; }

        if (file.LevelDefenseEvery == null) { errors.Add($"{prefix}: $.levelDefenseEvery is required"); hasError = true; }
        else if (file.LevelDefenseEvery <= 0) { errors.Add($"{prefix}: $.levelDefenseEvery must be >= 1"); hasError = true; }

        if (file.LevelHealAmount == null) { errors.Add($"{prefix}: $.levelHealAmount is required"); hasError = true; }
        else if (file.LevelHealAmount < 0) { errors.Add($"{prefix}: $.levelHealAmount must be >= 0"); hasError = true; }

        if (file.DepthHpScale == null || file.DepthAttackScale == null || file.DepthDefenseScale == null ||
            file.DepthHpScale < 0 || file.DepthAttackScale < 0 || file.DepthDefenseScale < 0)
            { errors.Add($"{prefix}: depth scaling values must be >= 0"); hasError = true; }

        if (file.StartingLoadout is null || file.StartingLoadout.Length == 0)
        {
            errors.Add($"{prefix}: $.startingLoadout must not be empty");
            hasError = true;
        }
        else
        {
            for (int index = 0; index < file.StartingLoadout.Length; index++)
            {
                string id = file.StartingLoadout[index];
                if (!items.TryGetValue(id, out ItemContent item))
                {
                    errors.Add($"{prefix}: $.startingLoadout[{index}] references unknown item id '{id}'");
                    hasError = true;
                }
                else if (item.Type != ItemType.Consumable && item.Type != ItemType.Weapon && item.Type != ItemType.Armor)
                {
                    errors.Add($"{prefix}: $.startingLoadout[{index}] must be a valid item type");
                    hasError = true;
                }
                else if (item.MinDepth > 1)
                {
                    errors.Add($"{prefix}: $.startingLoadout[{index}] must have minDepth 1");
                    hasError = true;
                }
            }
        }

        if (hasError) return null;

        return new BalanceContent(file.StartingHp!.Value, file.StartingAttack!.Value, file.StartingDefense!.Value,
            file.SpawnBase!.Value, file.SpawnPerDepth!.Value, file.LootMin!.Value, file.LootMax!.Value, file.DropChancePercent!.Value,
            file.XpPerLevel!.Value, file.LevelMaxHpBonus!.Value, file.LevelAttackBonus!.Value, file.LevelDefenseBonus!.Value,
            file.LevelDefenseEvery!.Value, file.LevelHealAmount!.Value, file.DepthHpScale!.Value, file.DepthAttackScale!.Value,
            file.DepthDefenseScale!.Value, file.StairHealPercent!.Value, file.FeedbackDuration!.Value, file.MinSpawnDistance!.Value,
            file.StartingLoadout!);
    }

    private static void ValidateMonsterParams(MonsterJson item, MonsterBehavior behavior,
        string prefix, List<string> errors)
    {
        IReadOnlyDictionary<string, int> parameters = item.Params ?? new Dictionary<string, int>();
        if (behavior is MonsterBehavior.Chase or MonsterBehavior.Ranged or MonsterBehavior.Slow)
            RequireParam(parameters, "alertTurns", prefix, errors);
        if (behavior == MonsterBehavior.Ranged)
        {
            RequireParam(parameters, "minRange", prefix, errors);
            RequireParam(parameters, "maxRange", prefix, errors);
        }
        if (behavior == MonsterBehavior.Slow)
            RequireParam(parameters, "actEveryNTurns", prefix, errors);
        if (parameters.TryGetValue("actEveryNTurns", out int actEvery) && actEvery <= 0)
            errors.Add($"{prefix}.params.actEveryNTurns must be > 0");
        if (parameters.TryGetValue("minRange", out int minRange) &&
            parameters.TryGetValue("maxRange", out int maxRange) && minRange > maxRange)
            errors.Add($"{prefix}.params.minRange must be <= maxRange");
    }

    private static void RequireParam(IReadOnlyDictionary<string, int> parameters, string name,
        string prefix, List<string> errors)
    {
        if (!parameters.ContainsKey(name))
            errors.Add($"{prefix}.params.{name} is required for this behavior");
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

    private static bool TryColor(string? value, out Color color)
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

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };
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
    int DepthHpScale, int DepthAttackScale, int DepthDefenseScale, int StairHealPercent,
    int FeedbackDuration, int MinSpawnDistance, IReadOnlyList<string> StartingLoadout);

public sealed record EffectJson(string? Type, int? Amount = null, string? Stat = null, int? Turns = null,
    int? MinDistance = null);

internal sealed record MonsterFile(int? SchemaVersion, MonsterJson[]? Monsters);
internal sealed record ItemFile(int? SchemaVersion, ItemJson[]? Items);
internal sealed record BalanceFile(int? SchemaVersion, int? StartingHp, int? StartingAttack,
    int? StartingDefense, int? SpawnBase, int? SpawnPerDepth, int? LootMin,
    int? LootMax, int? DropChancePercent, int? XpPerLevel, int? LevelMaxHpBonus,
    int? LevelAttackBonus, int? LevelDefenseBonus, int? LevelDefenseEvery, int? LevelHealAmount,
    int? DepthHpScale, int? DepthAttackScale, int? DepthDefenseScale,
    int? StairHealPercent, int? FeedbackDuration, int? MinSpawnDistance,
    string[]? StartingLoadout);
internal sealed record MonsterJson(string? Id, string? Name, string? Glyph,
    string? Color, int? MaxHp, int? Attack, int? Defense, int? Xp,
    int? SightRadius, int? MinDepth, int? SpawnWeight, string? Behavior,
    Dictionary<string, int>? Params);
internal sealed record ItemJson(string? Id, string? Name, string? Description,
    string? Glyph, string? Color, string? Type, int? MinDepth,
    int? Weight, int? MaxStack, int? AttackBonus, int? DefenseBonus, string? Slot,
    EffectJson[]? Effects);

public static class EffectRegistry
{
    public static IReadOnlySet<string> Known { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "heal", "buff", "teleport", "reveal_map" };
    public static IReadOnlySet<string> BuffStats { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "attack", "defense" };
}
