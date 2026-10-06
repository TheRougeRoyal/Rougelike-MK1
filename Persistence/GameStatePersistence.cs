using System.Text.Json;
using Microsoft.Xna.Framework;

namespace Roguelike.Persistence;

/// <summary>Serializes the deterministic game payload without serializing domain objects.</summary>
public static class GameStatePersistence
{
    /// <summary>Saves a run to a store.</summary>
    public static void Save(GameState state, ISaveStore store, string contentHash)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(store);
        GamePayload payload = GamePayload.From(state);
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        SaveDto dto = new()
        {
            ContentHash = contentHash,
            Seed = state.Seed,
            Depth = state.Depth,
            TurnNumber = state.TurnNumber,
            RandomStates = new Dictionary<string, ulong> { ["gameplay"] = state.GameplayRandomState },
            Game = document.RootElement.Clone()
        };
        store.Write(SaveCodec.Encode(dto));
    }

    /// <summary>Loads a run and verifies the expected content hash.</summary>
    public static GameState Load(ISaveStore store, string contentHash)
    {
        string? json = store.Read() ?? throw new InvalidDataException("Save file is missing.");
        SaveDto dto = SaveCodec.Decode(json);
        if (!string.Equals(dto.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Save content hash mismatch.");
        GamePayload payload = dto.Game.Deserialize<GamePayload>() ?? throw new InvalidDataException("Save game payload is missing.");
        GameState state = new(dto.Seed, payload.Width, payload.Height, dto.Depth);
        state.ConfigureLevelForTesting(state.Dungeon, new Point(payload.Player.X, payload.Player.Y));
        state.Player.Restore(payload.Player.Level, payload.Player.Experience, payload.Player.Hp, payload.Player.MaxHp,
            payload.Player.Attack, payload.Player.Defense);
        state.Player.Inventory.Items.Clear();
        foreach (ItemPayload item in payload.Player.Inventory)
            state.Player.Inventory.TryAdd(new ItemInstance(ItemCatalog.Get(new ItemId(item.Id)), item.Count));
        state.Player.EquippedWeapon = RestoreItem(payload.Player.Weapon);
        state.Player.EquippedArmor = RestoreItem(payload.Player.Armor);
        state.Player.Effects.AddRange(payload.Player.Effects.Select(effect =>
            new StatusEffect(Enum.Parse<StatusEffectType>(effect.Type), effect.Magnitude, effect.RemainingTurns)));
        foreach (MonsterPayload monster in payload.Monsters)
        {
            MonsterDefinition definition = MonsterCatalog.All.First(item =>
                item.Type.ToString().Equals(monster.DefinitionId, StringComparison.OrdinalIgnoreCase));
            MonsterActor actor = new(definition, new Point(monster.X, monster.Y))
            {
                Hp = monster.Hp,
                AlertTurns = monster.AlertTurns,
                LastKnownPlayerPosition = monster.LastKnownX is null ? null : new Point(monster.LastKnownX.Value, monster.LastKnownY!.Value)
            };
            state.AddMonsterForTesting(actor);
        }
        foreach (ItemPayload item in payload.FloorItems)
            state.AddFloorItem(new Point(item.X, item.Y), new ItemInstance(ItemCatalog.Get(new ItemId(item.Id)), item.Count));
        state.TurnNumber = dto.TurnNumber;
        state.RestoreGameplayRandomState(dto.RandomStates["gameplay"]);
        return state;
    }

    private static ItemInstance? RestoreItem(ItemPayload? item) =>
        item is null ? null : new ItemInstance(ItemCatalog.Get(new ItemId(item.Id)), item.Count);

    private sealed record GamePayload(int Width, int Height, PlayerPayload Player,
        List<MonsterPayload> Monsters, List<ItemPayload> FloorItems)
    {
        public static GamePayload From(GameState state) => new(state.Width, state.Height,
            new PlayerPayload(state.Player.Position.X, state.Player.Position.Y, state.Player.Level,
                state.Player.Experience, state.Player.Hp, state.Player.MaxHp, state.Player.Attack, state.Player.Defense,
                state.Player.Inventory.Items.Select(item => new ItemPayload(item.Definition.Id.Value, item.Count, 0, 0)).ToList(),
                Item(state.Player.EquippedWeapon), Item(state.Player.EquippedArmor),
                state.Player.Effects.Select(effect => new EffectPayload(effect.Type.ToString(), effect.Magnitude, effect.RemainingTurns)).ToList()),
            state.Monsters.Select(monster => new MonsterPayload(monster.Definition.Type.ToString(), monster.Position.X,
                monster.Position.Y, monster.Hp, monster.AlertTurns, monster.LastKnownPlayerPosition?.X,
                monster.LastKnownPlayerPosition?.Y)).ToList(),
            state.FloorItems.Select(item => new ItemPayload(item.Item.Definition.Id.Value, item.Item.Count,
                item.Position.X, item.Position.Y)).ToList());

        private static ItemPayload? Item(ItemInstance? item) =>
            item is null ? null : new ItemPayload(item.Definition.Id.Value, item.Count, 0, 0);
    }
    private sealed record PlayerPayload(int X, int Y, int Level, int Experience, int Hp, int MaxHp,
        int Attack, int Defense, List<ItemPayload> Inventory, ItemPayload? Weapon, ItemPayload? Armor,
        List<EffectPayload> Effects);
    private sealed record ItemPayload(string Id, int Count, int X, int Y);
    private sealed record EffectPayload(string Type, int Magnitude, int RemainingTurns);
    private sealed record MonsterPayload(string DefinitionId, int X, int Y, int Hp, int AlertTurns,
        int? LastKnownX, int? LastKnownY);
}
