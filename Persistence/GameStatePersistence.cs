using System.Text.Json;
using Microsoft.Xna.Framework;
using Roguelike.Content;

namespace Roguelike.Persistence;

public static class GameStatePersistence
{
    public static void Save(GameState state, ISaveStore store, string contentHash)
    {
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

    public static GameState Load(ISaveStore store, string contentHash, ContentDatabase content)
    {
        string? json = store.Read() ?? throw new InvalidDataException("Save file is missing.");
        SaveDto dto;
        try { dto = SaveCodec.Decode(json); }
        catch (InvalidDataException primaryException) when (store is FileSaveStore fileStore &&
            fileStore.ReadBackup() is string backup)
        {
            try { dto = SaveCodec.Decode(backup); }
            catch (InvalidDataException) { throw primaryException; }
        }
        if (!string.Equals(dto.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Save content hash mismatch.");
        GamePayload payload = dto.Game.Deserialize<GamePayload>() ??
            throw new InvalidDataException("Save game payload is missing.");
        GameState state = new(dto.Seed, payload.Width, payload.Height, dto.Depth, content);
        state.RestoreSnapshot(state.Dungeon, new Point(payload.Player.X, payload.Player.Y), dto.TurnNumber,
            payload.Status, payload.Explored,
            new GameState.RunStatsSnapshot(payload.RunStats.TurnsSurvived, payload.RunStats.MonstersSlain,
                payload.RunStats.ItemsPickedUp, payload.RunStats.DamageDealt, payload.RunStats.DamageTaken,
                payload.RunStats.MaxDepth, payload.RunStats.CauseOfDeath),
            payload.Messages.Select(message => new MessageLogEntry(message.Text,
                new Color(message.R, message.G, message.B), message.TurnNumber, message.Count)));
        state.Player.Restore(payload.Player.Level, payload.Player.Experience, payload.Player.Hp,
            payload.Player.MaxHp, payload.Player.Attack, payload.Player.Defense);
        state.Player.Inventory.Items.Clear();
        foreach (ItemPayload item in payload.Player.Inventory)
            state.Player.Inventory.TryAdd(new ItemInstance(content.CreateDefinition(item.Id), item.Count));
        state.Player.EquippedWeapon = RestoreItem(payload.Player.Weapon, content);
        state.Player.EquippedArmor = RestoreItem(payload.Player.Armor, content);
        state.Player.Effects.AddRange(payload.Player.Effects.Select(effect =>
            new StatusEffect(Enum.Parse<StatusEffectType>(effect.Type), effect.Magnitude, effect.RemainingTurns)));
        List<MonsterActor> restoredMonsters = new();
        foreach (MonsterPayload monster in payload.Monsters)
        {
            MonsterContent monsterContent = content.GetMonster(monster.DefinitionId);
            MonsterDefinition definition = new(monsterContent.Id, monsterContent.Name, monsterContent.Glyph,
                monsterContent.MaxHp, monsterContent.Attack, monsterContent.Defense, monsterContent.SightRadius,
                monsterContent.Behavior, monsterContent.Color, monsterContent.MinDepth, monsterContent.Xp,
                monsterContent.Params);
            restoredMonsters.Add(new MonsterActor(definition, new Point(monster.X, monster.Y))
            {
                Hp = monster.Hp,
                AlertTurns = monster.AlertTurns,
                LastKnownPlayerPosition = monster.LastKnownX is null
                    ? null : new Point(monster.LastKnownX.Value, monster.LastKnownY!.Value)
            });
        }
        List<FloorItem> restoredItems = payload.FloorItems.Select(item =>
            new FloorItem(new Point(item.X, item.Y), new ItemInstance(content.CreateDefinition(item.Id), item.Count))).ToList();
        state.RestoreActors(restoredMonsters, restoredItems);
        state.RestoreGameplayRandomState(dto.RandomStates["gameplay"]);
        return state;
    }

    private static ItemInstance? RestoreItem(ItemPayload? item, ContentDatabase content) =>
        item is null ? null : new ItemInstance(content.CreateDefinition(item.Id), item.Count);

    private sealed record GamePayload(int Width, int Height, GameStatus Status, string Explored,
        StatsPayload RunStats, List<MessagePayload> Messages, PlayerPayload Player,
        List<MonsterPayload> Monsters, List<ItemPayload> FloorItems)
    {
        public static GamePayload From(GameState state) => new(state.Width, state.Height, state.Status,
            state.Dungeon.ExportExplored(),
            new StatsPayload(state.RunStats.TurnsSurvived, state.RunStats.MonstersSlain,
                state.RunStats.ItemsPickedUp, state.RunStats.DamageDealt, state.RunStats.DamageTaken,
                state.RunStats.MaxDepth, state.RunStats.CauseOfDeath),
            state.MessageLog.Entries.TakeLast(100).Select(message => new MessagePayload(message.Text,
                message.Color.R, message.Color.G, message.Color.B, message.TurnNumber, message.Count)).ToList(),
            new PlayerPayload(state.Player.Position.X, state.Player.Position.Y, state.Player.Level,
                state.Player.Experience, state.Player.Hp, state.Player.MaxHp, state.Player.Attack,
                state.Player.Defense, state.Player.Inventory.Items.Select(item =>
                    new ItemPayload(item.Definition.Id, item.Count, 0, 0)).ToList(),
                Item(state.Player.EquippedWeapon), Item(state.Player.EquippedArmor),
                state.Player.Effects.Select(effect => new EffectPayload(effect.Type.ToString(),
                    effect.Magnitude, effect.RemainingTurns)).ToList()),
            state.Monsters.Select(monster => new MonsterPayload(monster.Definition.Id, monster.Position.X,
                monster.Position.Y, monster.Hp, monster.AlertTurns, monster.LastKnownPlayerPosition?.X,
                monster.LastKnownPlayerPosition?.Y)).ToList(),
            state.FloorItems.Select(item => new ItemPayload(item.Item.Definition.Id, item.Item.Count,
                item.Position.X, item.Position.Y)).ToList());

        private static ItemPayload? Item(ItemInstance? item) =>
            item is null ? null : new ItemPayload(item.Definition.Id, item.Count, 0, 0);
    }

    private sealed record StatsPayload(int TurnsSurvived, int MonstersSlain, int ItemsPickedUp,
        int DamageDealt, int DamageTaken, int MaxDepth, string? CauseOfDeath);
    private sealed record MessagePayload(string Text, byte R, byte G, byte B, int TurnNumber, int Count);
    private sealed record PlayerPayload(int X, int Y, int Level, int Experience, int Hp, int MaxHp,
        int Attack, int Defense, List<ItemPayload> Inventory, ItemPayload? Weapon, ItemPayload? Armor,
        List<EffectPayload> Effects);
    private sealed record ItemPayload(string Id, int Count, int X, int Y);
    private sealed record EffectPayload(string Type, int Magnitude, int RemainingTurns);
    private sealed record MonsterPayload(string DefinitionId, int X, int Y, int Hp, int AlertTurns,
        int? LastKnownX, int? LastKnownY);
}
