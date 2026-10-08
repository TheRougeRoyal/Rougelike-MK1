using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Roguelike.Content;

namespace Roguelike.Persistence;

/// <summary>Versioned, DTO-only persistence for a headless run.</summary>
public static class GameStatePersistence
{
    public const int CurrentSchemaVersion = 2;

    public static void Save(GameState state, ISaveStore store, string contentHash)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(store);
        SaveFileDto dto = SaveFileDto.From(state, contentHash);
        store.Write(SaveCodec.Encode(dto));
    }

    /// <summary>Loads a save without throwing for malformed or incompatible input.</summary>
    public static LoadResult Load(ISaveStore store, string contentHash, ContentDatabase content)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(content);
        string? primary = null;
        try { primary = store.Read(); }
        catch (Exception) { primary = null; }

        if (primary is not null)
        {
            LoadResult result = TryDecode(primary, contentHash, content, false);
            if (result.IsSuccess) return result;
            string? backup = ReadBackup(store);
            if (backup is not null)
            {
                LoadResult backupResult = TryDecode(backup, contentHash, content, true);
                if (backupResult.IsSuccess) return backupResult;
            }
            return result;
        }

        string? fallback = ReadBackup(store);
        if (fallback is not null)
        {
            LoadResult result = TryDecode(fallback, contentHash, content, true);
            if (result.IsSuccess) return result;
            return result;
        }
        return LoadResult.Failure("Save file is missing.");
    }

    private static string? ReadBackup(ISaveStore store)
    {
        try { return store.ReadBackup(); }
        catch { return null; }
    }

    private static LoadResult TryDecode(string json, string contentHash, ContentDatabase content, bool backup)
    {
        try
        {
            SaveFileDto dto = SaveCodec.Decode(json);
            if (!string.Equals(dto.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase))
                return LoadResult.Failure("Save content hash mismatch; delete the save and start a new run.");
            GameState state = dto.ToState(content);
            string source = backup ? "Loaded backup save from save.json.bak." : "Loaded save from save.json.";
            return LoadResult.Success(state, source, backup);
        }
        catch (KeyNotFoundException exception)
        {
            return LoadResult.Failure($"Save references an unknown item or monster id: {exception.Message}");
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or
            ArgumentException or FormatException or InvalidOperationException)
        {
            return LoadResult.Failure(exception.Message);
        }
        catch (Exception exception)
        {
            return LoadResult.Failure($"Save could not be loaded: {exception.Message}");
        }
    }
}

/// <summary>Result of a non-throwing save load.</summary>
public sealed record LoadResult(GameState? State, string? Reason, bool UsedBackup)
{
    public bool IsSuccess => State is not null;
    public static LoadResult Success(GameState state, string message, bool usedBackup = false) =>
        new(state, message, usedBackup);
    public static LoadResult Failure(string reason) => new(null, reason, false);

    public static implicit operator GameState(LoadResult result) =>
        result.State ?? throw new InvalidDataException(result.Reason ?? "Save could not be loaded.");
}

/// <summary>Top-level version-2 save DTO.</summary>
public sealed class SaveFileDto
{
    public int SchemaVersion { get; set; } = GameStatePersistence.CurrentSchemaVersion;
    public string ContentHash { get; set; } = string.Empty;
    public int RunSeed { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Depth { get; set; }
    public int TurnNumber { get; set; }
    public GameStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public RandomStreamsDto RandomStreams { get; set; } = new();
    public PlayerDto Player { get; set; } = new();
    public List<MonsterDto> Monsters { get; set; } = new();
    public List<FloorItemDto> FloorItems { get; set; } = new();
    public string ExploredTiles { get; set; } = string.Empty;
    public RunStatsDto RunStats { get; set; } = new();
    public List<LogEntryDto> MessageLog { get; set; } = new();
    public string Checksum { get; set; } = string.Empty;

    public static SaveFileDto From(GameState state, string contentHash) => new()
    {
        ContentHash = contentHash,
        RunSeed = state.Seed,
        Width = state.Width,
        Height = state.Height,
        Depth = state.Depth,
        TurnNumber = state.TurnNumber,
        Status = state.Status,
        Message = state.Message,
        RandomStreams = new RandomStreamsDto
        {
            Level = state.LevelRandomState,
            Monster = state.MonsterRandomState,
            Loot = state.LootRandomState,
            Gameplay = state.GameplayRandomState
        },
        Player = PlayerDto.From(state.Player),
        Monsters = state.Monsters.Select(MonsterDto.From).ToList(),
        FloorItems = state.FloorItems.Select(FloorItemDto.From).ToList(),
        ExploredTiles = state.Dungeon.ExportExplored(),
        RunStats = RunStatsDto.From(state.RunStats),
        MessageLog = state.MessageLog.Entries.TakeLast(100).Select(LogEntryDto.From).ToList()
    };

    public GameState ToState(ContentDatabase content)
    {
        if (Width < 12 || Height < 10 || Depth < 1) throw new InvalidDataException("Save dimensions or depth are invalid.");
        List<MonsterActor> monsters = Monsters.Select(item => item.ToDomain(content)).ToList();
        List<FloorItem> floorItems = FloorItems.Select(item => item.ToDomain(content)).ToList();
        List<MessageLogEntry> messages = MessageLog.Select(item => item.ToDomain()).ToList();
        GameState.PlayerSnapshot player = Player.ToSnapshot(content);
        GameState state = GameState.Restore(new GameState.GameStateSnapshot(RunSeed, Width, Height, Depth, TurnNumber, Status,
            new Point(Player.X, Player.Y), player, ExploredTiles, RunStats.ToSnapshot(), messages,
            monsters, floorItems, RandomStreams.Gameplay), content);
        state.Message = Message;
        state.FeedbackTurns = 0;
        state.FeedbackActor = null;
        return state;
    }
}

public sealed class RandomStreamsDto
{
    public ulong Level { get; set; }
    public ulong Monster { get; set; }
    public ulong Loot { get; set; }
    public ulong Gameplay { get; set; }
}

public sealed class PlayerDto
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Level { get; set; }
    public int Experience { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }
    public List<ItemDto> Inventory { get; set; } = new();
    public ItemDto? EquippedWeapon { get; set; }
    public ItemDto? EquippedArmor { get; set; }
    public List<EffectDto> Effects { get; set; } = new();

    public static PlayerDto From(PlayerActor player) => new()
    {
        X = player.Position.X, Y = player.Position.Y, Level = player.Level,
        Experience = player.Experience, Hp = player.Hp, MaxHp = player.MaxHp,
        Attack = player.Attack, Defense = player.Defense,
        Inventory = player.Inventory.Items.Select(ItemDto.From).ToList(),
        EquippedWeapon = player.EquippedWeapon is null ? null : ItemDto.From(player.EquippedWeapon),
        EquippedArmor = player.EquippedArmor is null ? null : ItemDto.From(player.EquippedArmor),
        Effects = player.Effects.Select(EffectDto.From).ToList()
    };

    public GameState.PlayerSnapshot ToSnapshot(ContentDatabase content) =>
        new(Level, Experience, Hp, MaxHp, Attack, Defense,
            Inventory.Select(item => item.ToDomain(content)).ToList(),
            EquippedWeapon?.ToDomain(content), EquippedArmor?.ToDomain(content),
            Effects.Select(item => item.ToDomain()).ToList());
}

public sealed class MonsterDto
{
    public string DefinitionId { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Hp { get; set; }
    public int AlertTurns { get; set; }
    public int? LastKnownX { get; set; }
    public int? LastKnownY { get; set; }

    public static MonsterDto From(MonsterActor monster) => new()
    {
        DefinitionId = monster.Definition.Id, X = monster.Position.X, Y = monster.Position.Y,
        Hp = monster.Hp, AlertTurns = monster.AlertTurns,
        LastKnownX = monster.LastKnownPlayerPosition?.X,
        LastKnownY = monster.LastKnownPlayerPosition?.Y
    };

    public MonsterActor ToDomain(ContentDatabase content)
    {
        MonsterContent value = content.GetMonster(DefinitionId);
        MonsterDefinition definition = new(value.Id, value.Name, value.Glyph, value.MaxHp, value.Attack,
            value.Defense, value.SightRadius, value.Behavior, value.Color, value.MinDepth, value.Xp, value.Params);
        return new MonsterActor(definition, new Point(X, Y))
        {
            Hp = Hp,
            AlertTurns = AlertTurns,
            LastKnownPlayerPosition = LastKnownX is null ? null : new Point(LastKnownX.Value, LastKnownY ??
                throw new InvalidDataException("Monster last-known position is incomplete."))
        };
    }
}

public sealed class FloorItemDto
{
    public string Id { get; set; } = string.Empty;
    public int Count { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public static FloorItemDto From(FloorItem item) => new()
    {
        Id = item.Item.Definition.Id, Count = item.Item.Count, X = item.Position.X, Y = item.Position.Y
    };
    public FloorItem ToDomain(ContentDatabase content) =>
        new(new Point(X, Y), new ItemInstance(content.CreateDefinition(Id), Count));
}

public sealed class ItemDto
{
    public string Id { get; set; } = string.Empty;
    public int Count { get; set; }
    public static ItemDto From(ItemInstance item) => new() { Id = item.Definition.Id, Count = item.Count };
    public ItemInstance ToDomain(ContentDatabase content) =>
        new(content.CreateDefinition(Id), Count);
}

public sealed class EffectDto
{
    public StatusEffectType Type { get; set; }
    public int Magnitude { get; set; }
    public int RemainingTurns { get; set; }
    public static EffectDto From(StatusEffect effect) => new()
    {
        Type = effect.Type, Magnitude = effect.Magnitude, RemainingTurns = effect.RemainingTurns
    };
    public StatusEffect ToDomain() => new(Type, Magnitude, RemainingTurns);
}

public sealed class RunStatsDto
{
    public int TurnsSurvived { get; set; }
    public int MonstersSlain { get; set; }
    public int ItemsPickedUp { get; set; }
    public int DamageDealt { get; set; }
    public int DamageTaken { get; set; }
    public int MaxDepth { get; set; }
    public string? CauseOfDeath { get; set; }
    public static RunStatsDto From(RunStats stats) => new()
    {
        TurnsSurvived = stats.TurnsSurvived, MonstersSlain = stats.MonstersSlain,
        ItemsPickedUp = stats.ItemsPickedUp, DamageDealt = stats.DamageDealt,
        DamageTaken = stats.DamageTaken, MaxDepth = stats.MaxDepth, CauseOfDeath = stats.CauseOfDeath
    };
    public GameState.RunStatsSnapshot ToSnapshot() =>
        new(TurnsSurvived, MonstersSlain, ItemsPickedUp, DamageDealt, DamageTaken, MaxDepth, CauseOfDeath);
}

public sealed class LogEntryDto
{
    public string Text { get; set; } = string.Empty;
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
    public int TurnNumber { get; set; }
    public int Count { get; set; } = 1;
    public static LogEntryDto From(MessageLogEntry entry) => new()
    {
        Text = entry.Text, R = entry.Color.R, G = entry.Color.G, B = entry.Color.B,
        TurnNumber = entry.TurnNumber, Count = entry.Count
    };
    public MessageLogEntry ToDomain() => new(Text, new Color(R, G, B), TurnNumber, Count);
}

/// <summary>Checksum and migration codec. Checksums are verified before any migration.</summary>
public static class SaveCodec
{
    public static string Encode(SaveFileDto dto)
    {
        dto.SchemaVersion = GameStatePersistence.CurrentSchemaVersion;
        dto.Checksum = string.Empty;
        string unsigned = JsonSerializer.Serialize(dto, Options);
        dto.Checksum = Hash(unsigned);
        return JsonSerializer.Serialize(dto, Options);
    }

    public static SaveFileDto Decode(string json)
    {
        SaveFileDto dto;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement versionElement = document.RootElement.EnumerateObject()
                .FirstOrDefault(property => property.NameEquals("schemaVersion") ||
                    property.NameEquals("SchemaVersion")).Value;
            int version = versionElement.ValueKind != JsonValueKind.Undefined
                ? versionElement.GetInt32() : 0;
            if (version > GameStatePersistence.CurrentSchemaVersion)
                throw new InvalidDataException($"Save schemaVersion {version} is newer than supported version {GameStatePersistence.CurrentSchemaVersion}; update the game.");
            if (version == 1) return MigrateV1(json);
            if (version != GameStatePersistence.CurrentSchemaVersion)
                throw new InvalidDataException($"Unsupported save schemaVersion {version}.");
            dto = JsonSerializer.Deserialize<SaveFileDto>(json, Options) ??
                throw new InvalidDataException("Save is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Save JSON is corrupt or truncated.", exception);
        }
        Verify(dto);
        return dto;
    }

    private static SaveFileDto MigrateV1(string json)
    {
        LegacySaveDto legacy = JsonSerializer.Deserialize<LegacySaveDto>(json, LegacyOptions) ??
            throw new InvalidDataException("Save is empty.");
        VerifyLegacy(legacy);
        LegacyGamePayload game = legacy.Game.Deserialize<LegacyGamePayload>(LegacyOptions) ??
            throw new InvalidDataException("Save game payload is missing.");
        SaveFileDto migrated = new()
        {
            SchemaVersion = GameStatePersistence.CurrentSchemaVersion,
            ContentHash = legacy.ContentHash,
            RunSeed = legacy.Seed,
            Width = game.Width,
            Height = game.Height,
            Depth = legacy.Depth,
            TurnNumber = legacy.TurnNumber,
            Status = game.Status,
            RandomStreams = new RandomStreamsDto
            {
                Gameplay = legacy.RandomStates.GetValueOrDefault("gameplay"),
                Level = RandomStreams.Create(legacy.Seed, legacy.Depth, 0x4C455645UL).State,
                Monster = RandomStreams.Create(legacy.Seed, legacy.Depth, 0x4D4F4E53UL).State,
                Loot = RandomStreams.Create(legacy.Seed, legacy.Depth, 0x4C4F4F54UL).State
            },
            Player = game.Player.ToV2(),
            Monsters = game.Monsters.Select(item => item.ToV2()).ToList(),
            FloorItems = game.FloorItems.Select(item => item.ToFloorV2()).ToList(),
            ExploredTiles = game.Explored,
            RunStats = game.RunStats.ToV2(),
            MessageLog = game.Messages.Select(item => item.ToV2()).ToList()
        };
        return migrated;
    }

    private static void Verify(SaveFileDto dto)
    {
        string expected = dto.Checksum;
        dto.Checksum = string.Empty;
        if (!string.Equals(expected, Hash(JsonSerializer.Serialize(dto, Options)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Save checksum mismatch.");
        dto.Checksum = expected;
    }

    private static void VerifyLegacy(LegacySaveDto dto)
    {
        string expected = dto.Checksum;
        dto.Checksum = string.Empty;
        if (!string.Equals(expected, Hash(JsonSerializer.Serialize(dto, LegacyOptions)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Save checksum mismatch.");
        dto.Checksum = expected;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private static readonly JsonSerializerOptions LegacyOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private sealed class LegacySaveDto
    {
        public int SchemaVersion { get; set; }
        public string ContentHash { get; set; } = string.Empty;
        public int Seed { get; set; }
        public int Depth { get; set; }
        public int TurnNumber { get; set; }
        public Dictionary<string, ulong> RandomStates { get; set; } = new();
        public JsonElement Game { get; set; }
        public string Checksum { get; set; } = string.Empty;
    }
    private sealed record LegacyGamePayload(int Width, int Height, GameStatus Status, string Explored,
        LegacyStats RunStats, List<LegacyMessage> Messages, LegacyPlayer Player,
        List<LegacyMonster> Monsters, List<LegacyItem> FloorItems);
    private sealed record LegacyStats(int TurnsSurvived, int MonstersSlain, int ItemsPickedUp,
        int DamageDealt, int DamageTaken, int MaxDepth, string? CauseOfDeath)
    {
        public RunStatsDto ToV2() => new()
        {
            TurnsSurvived = TurnsSurvived, MonstersSlain = MonstersSlain, ItemsPickedUp = ItemsPickedUp,
            DamageDealt = DamageDealt, DamageTaken = DamageTaken, MaxDepth = MaxDepth, CauseOfDeath = CauseOfDeath
        };
    }
    private sealed record LegacyMessage(string Text, byte R, byte G, byte B, int TurnNumber, int Count)
    {
        public LogEntryDto ToV2() => new() { Text = Text, R = R, G = G, B = B, TurnNumber = TurnNumber, Count = Count };
    }
    private sealed record LegacyPlayer(int X, int Y, int Level, int Experience, int Hp, int MaxHp,
        int Attack, int Defense, List<LegacyItem> Inventory, LegacyItem? Weapon, LegacyItem? Armor,
        List<LegacyEffect> Effects)
    {
        public PlayerDto ToV2() => new()
        {
            X = X, Y = Y, Level = Level, Experience = Experience, Hp = Hp, MaxHp = MaxHp,
            Attack = Attack, Defense = Defense, Inventory = Inventory.Select(item => item.ToV2()).ToList(),
            EquippedWeapon = Weapon?.ToV2(), EquippedArmor = Armor?.ToV2(),
            Effects = Effects.Select(item => item.ToV2()).ToList()
        };
    }
    private sealed record LegacyItem(string Id, int Count, int X, int Y)
    {
        public ItemDto ToV2() => new() { Id = Id, Count = Count };
        public FloorItemDto ToFloorV2() => new() { Id = Id, Count = Count, X = X, Y = Y };
    }
    private sealed record LegacyEffect(string Type, int Magnitude, int RemainingTurns)
    {
        public EffectDto ToV2() => new()
        {
            Type = Enum.Parse<StatusEffectType>(Type), Magnitude = Magnitude, RemainingTurns = RemainingTurns
        };
    }
    private sealed record LegacyMonster(string DefinitionId, int X, int Y, int Hp, int AlertTurns,
        int? LastKnownX, int? LastKnownY)
    {
        public MonsterDto ToV2() => new()
        {
            DefinitionId = DefinitionId, X = X, Y = Y, Hp = Hp, AlertTurns = AlertTurns,
            LastKnownX = LastKnownX, LastKnownY = LastKnownY
        };
    }
}
