using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using Roguelike.Content;
using Roguelike.Persistence;
using Xunit;

namespace Roguelike.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public void RoundTripPreservesExplorationStatisticsLogAndTransientReset()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        GameState original = new(41, content: content);
        original.Dungeon.RevealAll();
        original.RunStats.Restore(7, 3, 2, 11, 5, 2, "test");
        original.SetFeedback("saved message", Color.CornflowerBlue);
        MemorySaveStore store = new();

        GameStatePersistence.Save(original, store, content.ContentHash);
        LoadResult result = GameStatePersistence.Load(store, content.ContentHash, content);

        Assert.True(result.IsSuccess, result.Reason);
        GameState loaded = result;
        Assert.Equal(original.Dungeon.ExportExplored(), loaded.Dungeon.ExportExplored());
        Assert.Equal(original.RunStats.TurnsSurvived, loaded.RunStats.TurnsSurvived);
        Assert.Equal(original.RunStats.MonstersSlain, loaded.RunStats.MonstersSlain);
        Assert.Equal(original.MessageLog.Entries, loaded.MessageLog.Entries);
        Assert.Equal(original.Message, loaded.Message);
        Assert.Equal(0, loaded.FeedbackTurns);
        Assert.Equal(original.StateHash, loaded.StateHash);
    }

    [Fact]
    public void ContinuationUsesTheSameGameplayRandomState()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        GameState uninterrupted = new(91, content: content);
        for (int i = 0; i < 12; i++) uninterrupted.Process(GameAction.Wait);

        MemorySaveStore store = new();
        GameStatePersistence.Save(uninterrupted, store, content.ContentHash);
        GameState resumed = GameStatePersistence.Load(store, content.ContentHash, content);

        for (int i = 0; i < 20; i++)
        {
            GameAction action = i % 3 == 0 ? GameAction.Wait : GameAction.Move(new Point(1, 0));
            uninterrupted.Process(action);
            resumed.Process(action);
            Assert.Equal(uninterrupted.StateHash, resumed.StateHash);
        }
    }

    [Fact]
    public void CorruptPrimaryFallsBackToBackup()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        string directory = Directory.CreateTempSubdirectory("roguelike-save-").FullName;
        try
        {
            FileSaveStore store = new(directory);
            GameState first = new(1, content: content);
            GameState second = new(2, content: content);
            GameStatePersistence.Save(first, store, content.ContentHash);
            GameStatePersistence.Save(second, store, content.ContentHash);
            File.WriteAllText(Path.Combine(directory, "save.json"), "{ truncated");

            LoadResult result = GameStatePersistence.Load(store, content.ContentHash, content);

            Assert.True(result.IsSuccess, result.Reason);
            Assert.True(result.UsedBackup);
            Assert.Equal(first.Seed, result.State!.Seed);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void CorruptPrimaryDoesNotReplaceExistingBackupOnNextSave()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        string directory = Directory.CreateTempSubdirectory("roguelike-save-").FullName;
        try
        {
            FileSaveStore store = new(directory);
            GameStatePersistence.Save(new GameState(1, content: content), store, content.ContentHash);
            GameStatePersistence.Save(new GameState(2, content: content), store, content.ContentHash);
            File.WriteAllText(Path.Combine(directory, "save.json"), "{ corrupt");
            GameStatePersistence.Save(new GameState(3, content: content), store, content.ContentHash);

            string backup = File.ReadAllText(Path.Combine(directory, "save.json.bak"));
            SaveFileDto backupDto = SaveCodec.Decode(backup);
            Assert.Equal(1, backupDto.RunSeed);

            File.WriteAllText(Path.Combine(directory, "save.json"), "{ corrupt");
            LoadResult result = GameStatePersistence.Load(store, content.ContentHash, content);
            Assert.True(result.IsSuccess, result.Reason);
            Assert.True(result.UsedBackup);
            Assert.Equal(1, result.State!.Seed);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void LockedPrimaryIsSkippedWhenCreatingBackup()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        string directory = Directory.CreateTempSubdirectory("roguelike-save-").FullName;
        try
        {
            FileSaveStore store = new(directory);
            GameStatePersistence.Save(new GameState(1, content: content), store, content.ContentHash);
            string path = Path.Combine(directory, "save.json");
            using (FileStream lockStream = new(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                GameStatePersistence.Save(new GameState(2, content: content), store, content.ContentHash);
            }

            LoadResult result = GameStatePersistence.Load(store, content.ContentHash, content);
            Assert.True(result.IsSuccess, result.Reason);
            Assert.Equal(2, result.State!.Seed);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void FailureReasonsCoverMissingChecksumNewerAndContentMismatch()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        MemorySaveStore store = new();
        LoadResult missing = GameStatePersistence.Load(store, content.ContentHash, content);
        Assert.Contains("missing", missing.Reason, StringComparison.OrdinalIgnoreCase);

        GameStatePersistence.Save(new GameState(2, content: content), store, content.ContentHash);
        JsonObject save = JsonNode.Parse(store.Value!)!.AsObject();
        save["checksum"] = "bad";
        store.Replace(save.ToJsonString());
        Assert.Contains("checksum", GameStatePersistence.Load(store, content.ContentHash, content).Reason,
            StringComparison.OrdinalIgnoreCase);

        store.Replace("{\"schemaVersion\":99}");
        Assert.Contains("newer", GameStatePersistence.Load(store, content.ContentHash, content).Reason,
            StringComparison.OrdinalIgnoreCase);

        GameStatePersistence.Save(new GameState(3, content: content), store, "different");
        Assert.Contains("content hash", GameStatePersistence.Load(store, content.ContentHash, content).Reason,
            StringComparison.OrdinalIgnoreCase);

        GameStatePersistence.Save(new GameState(4, content: content), store, content.ContentHash);
        SaveFileDto dto = JsonSerializer.Deserialize<SaveFileDto>(store.Value!,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        dto.Player.Inventory[0].Id = "missing_item";
        store.Replace(SaveCodec.Encode(dto));
        Assert.Contains("unknown item", GameStatePersistence.Load(store, content.ContentHash, content).Reason,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OlderV1EnvelopeMigratesAfterChecksumVerification()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        GameState state = new(17, content: content);
        MemorySaveStore current = new();
        GameStatePersistence.Save(state, current, content.ContentHash);
        SaveFileDto v2 = JsonSerializer.Deserialize<SaveFileDto>(current.Value!,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        string v1 = CreateV1(v2);
        MemorySaveStore store = new();
        store.Replace(v1);

        LoadResult result = GameStatePersistence.Load(store, content.ContentHash, content);

        Assert.True(result.IsSuccess, result.Reason);
        Assert.Equal(state.Seed, result.State!.Seed);
        Assert.Equal(state.StateHash, result.State.StateHash);
    }

    [Fact]
    public void FailedWriterLeavesPreviousValueIntact()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        FailingStore store = new();
        GameStatePersistence.Save(new GameState(1, content: content), store, content.ContentHash);
        string previous = store.Value!;
        store.FailWrites = true;

        Assert.Throws<IOException>(() =>
            GameStatePersistence.Save(new GameState(2, content: content), store, content.ContentHash));
        Assert.Equal(previous, store.Value);
    }

    [Fact]
    public void TargetedSnapshotsPreserveBuffAdjacentMonsterEquipmentAndLevelUp()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        GameState state = new(33, content: content);
        state.Player.Inventory.TryAdd(new ItemInstance(content.CreateDefinition("potion_of_strength")));
        Assert.True(state.Process(GameAction.UseItem(state.Player.Inventory.Items.Count - 1)));
        MonsterContent monsterContent = content.GetMonster("rat");
        MonsterDefinition definition = new(monsterContent.Id, monsterContent.Name, monsterContent.Glyph,
            monsterContent.MaxHp, monsterContent.Attack, monsterContent.Defense, monsterContent.SightRadius,
            monsterContent.Behavior, monsterContent.Color, monsterContent.MinDepth, monsterContent.Xp,
            monsterContent.Params);
        GameStateTestHooks.AddMonster(state,
            new MonsterActor(definition, state.Player.Position + new Point(1, 0)));
        state.Player.Inventory.TryAdd(new ItemInstance(content.CreateDefinition("dagger")));
        state.Player.Inventory.TryAdd(new ItemInstance(content.CreateDefinition("leather_armor")));
        state.Player.EquippedWeapon = new ItemInstance(content.CreateDefinition("dagger"));
        state.Player.EquippedArmor = new ItemInstance(content.CreateDefinition("leather_armor"));
        state.Player.AddExperience(1000);
        while (state.Player.Inventory.Items.Count < state.Player.Inventory.Capacity)
            state.Player.Inventory.TryAdd(new ItemInstance(content.CreateDefinition("dagger")));

        MemorySaveStore store = new();
        GameStatePersistence.Save(state, store, content.ContentHash);
        GameState loaded = GameStatePersistence.Load(store, content.ContentHash, content);

        Assert.Equal(state.Player.Effects.Select(effect => (effect.Type, effect.Magnitude, effect.RemainingTurns)),
            loaded.Player.Effects.Select(effect => (effect.Type, effect.Magnitude, effect.RemainingTurns)));
        Assert.Equal(state.Player.Level, loaded.Player.Level);
        Assert.Equal(state.Player.Inventory.Items.Select(item => (item.Definition.Id, item.Count)),
            loaded.Player.Inventory.Items.Select(item => (item.Definition.Id, item.Count)));
        Assert.Equal(state.Monsters.Select(monster => (monster.Position, monster.Hp)),
            loaded.Monsters.Select(monster => (monster.Position, monster.Hp)));
    }

    [Fact]
    public void LoadingThroughSessionClearsOverlayState()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        MemorySaveStore store = new();
        GameSession first = new(12, content: content, saveStore: store);
        first.Execute(new UiCommand(UiCommandKind.Start));
        first.Execute(new UiCommand(UiCommandKind.Inventory));
        GameStatePersistence.Save(first.State, store, content.ContentHash);

        GameSession second = new(12, content: content, saveStore: store);
        second.Execute(new UiCommand(UiCommandKind.Continue));

        Assert.Equal(ScreenKind.Playing, second.Screens.Screen);
        Assert.Equal(UiOverlay.None, second.Screens.Overlay);
    }

    [Fact]
    public void ContinuationMatchesAcrossOneHundredSeeds()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        int reachedDepthTwo = 0;
        int savesWithLivingMonsters = 0;
        for (int seed = 0; seed < 100; seed++)
        {
            GameState uninterrupted = new(seed, 20, 12, 1, content);
            GameState resumed = new(seed, 20, 12, 1, content);
            Pcg32 walkerRandom = RandomStreams.Create(seed, 0, 0x57414C4BUL);
            for (int actionIndex = 0; actionIndex < 60; actionIndex++)
            {
                GameAction action = ChooseWalkerAction(uninterrupted, walkerRandom);
                uninterrupted.Process(action);
                resumed.Process(action);
                Assert.True(uninterrupted.StateHash == resumed.StateHash,
                    $"Seed {seed} diverged during warm-up at depth {uninterrupted.Depth}, action {actionIndex}.");
            }
            MemorySaveStore store = new();
            GameStatePersistence.Save(uninterrupted, store, content.ContentHash);
            if (uninterrupted.Monsters.Any(monster => monster.IsAlive)) savesWithLivingMonsters++;
            if (uninterrupted.Depth >= 2) reachedDepthTwo++;
            resumed = GameStatePersistence.Load(store, content.ContentHash, content);
            for (int actionIndex = 0; actionIndex < 60; actionIndex++)
            {
                GameAction action = ChooseWalkerAction(uninterrupted, walkerRandom);
                uninterrupted.Process(action);
                resumed.Process(action);
                Assert.True(uninterrupted.StateHash == resumed.StateHash,
                    $"Seed {seed} failed at depth {uninterrupted.Depth}, action {actionIndex}.");
            }
        }
        Assert.True(reachedDepthTwo >= 20,
            $"Only {reachedDepthTwo} of 100 runs reached depth 2 or deeper.");
        Assert.True(savesWithLivingMonsters >= 10,
            $"Only {savesWithLivingMonsters} of 100 saves contained living monsters.");
    }

    private static GameAction ChooseWalkerAction(GameState state, IRandom random)
    {
        Point[] adjacentDirections =
        {
            new(0, -1), new(1, 0), new(0, 1), new(-1, 0)
        };
        foreach (Point direction in adjacentDirections)
        {
            if (state.Monsters.Any(monster =>
                    monster.IsAlive && monster.Position == state.Player.Position + direction))
                return GameAction.Move(direction);
        }

        if (random.Next(100) < 20)
        {
            if (random.Next(2) == 0) return GameAction.Wait;
            int[] consumableSlots = state.Player.Inventory.Items
                .Select((item, index) => (item, index))
                .Where(value => value.item.Definition.Type == ItemType.Consumable)
                .Select(value => value.index)
                .ToArray();
            return consumableSlots.Length == 0
                ? GameAction.Wait
                : GameAction.UseItem(consumableSlots[random.Next(consumableSlots.Length)]);
        }

        Point target = state.FloorItems
            .OrderBy(item => Manhattan(item.Position, state.Player.Position))
            .Select(item => item.Position)
            .Append(state.Dungeon.StairsPosition)
            .OrderBy(point => Manhattan(point, state.Player.Position))
            .FirstOrDefault();
        IReadOnlyList<Point> path = new Pathfinder().FindPath(state.Dungeon, state.Player.Position, target,
            point => state.IsOccupied(point));
        return path.Count == 0
            ? GameAction.Wait
            : GameAction.Move(path[0] - state.Player.Position);
    }

    private static int Manhattan(Point first, Point second) =>
        Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y);

    private static string CreateV1(SaveFileDto dto)
    {
        object game = new
        {
            Width = dto.Width, Height = dto.Height, Status = dto.Status, Explored = dto.ExploredTiles,
            RunStats = dto.RunStats,
            Messages = dto.MessageLog,
            Player = new
            {
                dto.Player.X, dto.Player.Y, dto.Player.Level, dto.Player.Experience, dto.Player.Hp,
                dto.Player.MaxHp, dto.Player.Attack, dto.Player.Defense,
                dto.Player.Inventory, Weapon = dto.Player.EquippedWeapon,
                Armor = dto.Player.EquippedArmor,
                Effects = dto.Player.Effects.Select(effect => new
                {
                    Type = effect.Type.ToString(), effect.Magnitude, effect.RemainingTurns
                }).ToList()
            },
            dto.Monsters, FloorItems = dto.FloorItems
        };
        object envelope = new
        {
            SchemaVersion = 1, ContentHash = dto.ContentHash, Seed = dto.RunSeed,
            dto.Depth, dto.TurnNumber,
            RandomStates = new Dictionary<string, ulong> { ["gameplay"] = dto.RandomStreams.Gameplay },
            Game = game, Checksum = string.Empty
        };
        JsonSerializerOptions options = new() { WriteIndented = true };
        string unsigned = JsonSerializer.Serialize(envelope, options);
        string checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(unsigned))).ToLowerInvariant();
        return JsonSerializer.Serialize(new
        {
            SchemaVersion = 1, ContentHash = dto.ContentHash, Seed = dto.RunSeed,
            dto.Depth, dto.TurnNumber,
            RandomStates = new Dictionary<string, ulong> { ["gameplay"] = dto.RandomStreams.Gameplay },
            Game = game, Checksum = checksum
        }, options);
    }

    private sealed class FailingStore : ISaveStore
    {
        public string? Value { get; private set; }
        public bool FailWrites { get; set; }
        public string? Read() => Value;
        public void Write(string json)
        {
            if (FailWrites) throw new IOException("writer failed");
            Value = json;
        }
        public void Delete() => Value = null;
    }
}
