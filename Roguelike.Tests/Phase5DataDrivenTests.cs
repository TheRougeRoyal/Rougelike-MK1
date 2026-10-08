using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using Roguelike.Content;
using Xunit;

namespace Roguelike.Tests;

public sealed class Phase5DataDrivenTests
{
    [Fact]
    public void RatChasesUnawarePlayerButGoblinWaitsUntilAlerted()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        GameState state = CreateOpenState(content);
        MonsterContent ratContent = content.GetMonster("rat");
        MonsterContent goblinContent = content.GetMonster("goblin");
        MonsterActor rat = new(ToDefinition(ratContent) with { SightRadius = 0 }, new Point(3, 1));
        MonsterActor goblin = new(ToDefinition(goblinContent) with { SightRadius = 0 }, new Point(3, 3));
        state = GameStateTestHooks.AddMonster(state, rat);
        state = GameStateTestHooks.AddMonster(state, goblin);

        Assert.True(state.Process(GameAction.Wait));
        Assert.Equal(new Point(2, 1), rat.Position);
        Assert.Equal(new Point(3, 3), goblin.Position);

        goblin.AlertTurns = 1;
        Assert.True(state.Process(GameAction.Wait));
        Assert.NotEqual(new Point(3, 3), goblin.Position);
    }

    [Fact]
    public void JsonOnlySlimeSpawnsThroughNormalGameStateGeneration()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        Assert.Contains(content.Monsters, monster => monster.Id == "slime");
        Assert.Contains(Enumerable.Range(0, 50), seed =>
        {
            GameState state = new(seed, 60, 34, seed % 3 + 1, content);
            return state.Monsters.Any(monster => monster.Definition.Id == "slime");
        });
    }

    [Fact]
    public void JsonOnlyMinorPotionIsPickedUpAndUsed()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        GameState state = CreateOpenState(content);
        Point destination = state.Player.Position + new Point(1, 0);
        Assert.True(state.TryPlaceFloorItem(destination,
            new ItemInstance(content.CreateDefinition("minor_healing_potion"))));
        state.Player.Hp = 1;

        Assert.True(state.Process(GameAction.Move(new Point(1, 0))));
        int slot = state.Player.Inventory.Items.FindIndex(item =>
            item.Definition.Id == "minor_healing_potion");
        Assert.True(slot >= 0);
        int inventoryCount = state.Player.Inventory.Items.Count;
        Assert.True(state.Process(GameAction.UseItem(slot)));
        Assert.Equal(9, state.Player.Hp);
        Assert.Equal(inventoryCount - 1, state.Player.Inventory.Items.Count);
    }

    [Fact]
    public void CustomContentChangesStartingAndMonsterStats()
    {
        string directory = CreateCustomContent((monsters, _, balance) =>
        {
            balance["startingHp"] = 41;
            JsonObject rat = monsters["monsters"]!.AsArray()
                .First(node => node!["id"]!.GetValue<string>() == "rat")!.AsObject();
            rat["maxHp"] = 19;
        });
        try
        {
            ContentDatabase content = ContentDatabase.LoadDirectory(directory);
            GameState state = new(12, 60, 34, 1, content);
            Assert.Equal(41, state.Player.MaxHp);
            Assert.Equal(19, content.GetMonster("rat").MaxHp);
            Assert.Contains(Enumerable.Range(0, 100), seed =>
            {
                GameState candidate = new(seed, 60, 34, 1, content);
                return candidate.Monsters.Any(monster =>
                    monster.Definition.Id == "rat" && monster.MaxHp == 19);
            });
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void CustomBalanceValuesChangeStairHealingFeedbackAndSpawnDistance()
    {
        string directory = CreateCustomContent((_, _, balance) =>
        {
            balance["stairHealPercent"] = 50;
            balance["feedbackDuration"] = 9;
            balance["minSpawnDistance"] = 100;
        });
        try
        {
            ContentDatabase content = ContentDatabase.LoadDirectory(directory);
            GameState state = new(3, 20, 12, 1, content);
            Assert.Empty(state.Monsters);
            state.SetFeedback("custom", Color.White);
            Assert.Equal(9, state.FeedbackTurns);
            state.Player.Hp = 1;
            state.Player.Position = state.Dungeon.StairsPosition;
            Assert.True(state.Process(GameAction.Wait));
            Assert.Equal(16, state.Player.Hp);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void CustomMonsterParamsAreLoadedAndMissingRequiredParamsAreRejected()
    {
        string directory = CreateCustomContent((monsters, _, _) =>
        {
            JsonObject archer = monsters["monsters"]!.AsArray()
                .First(node => node!["id"]!.GetValue<string>() == "archer")!.AsObject();
            archer["params"]!["minRange"] = 4;
            archer["params"]!["maxRange"] = 6;
        });
        try
        {
            ContentDatabase content = ContentDatabase.LoadDirectory(directory);
            Assert.Equal(4, content.GetMonster("archer").Params["minRange"]);
            Assert.Equal(6, content.GetMonster("archer").Params["maxRange"]);
        }
        finally { Directory.Delete(directory, true); }

        string invalid = CreateCustomContent((monsters, _, _) =>
        {
            JsonObject archer = monsters["monsters"]!.AsArray()
                .First(node => node!["id"]!.GetValue<string>() == "archer")!.AsObject();
            archer["params"]!.AsObject().Remove("maxRange");
        });
        try
        {
            ContentLoadException exception = Assert.Throws<ContentLoadException>(
                () => ContentDatabase.LoadDirectory(invalid));
            Assert.Contains("maxRange is required", exception.Message);
        }
        finally { Directory.Delete(invalid, true); }
    }

    [Fact]
    public void ContentHashChangesOnlyWhenContentChanges()
    {
        string firstDirectory = CreateCustomContent((_, _, _) => { });
        string secondDirectory = CreateCustomContent((_, _, _) => { });
        string changedDirectory = CreateCustomContent((_, _, balance) => balance["startingHp"] = 31);
        try
        {
            string first = ContentDatabase.LoadDirectory(firstDirectory).ContentHash;
            Assert.Equal(first, ContentDatabase.LoadDirectory(secondDirectory).ContentHash);
            Assert.NotEqual(first, ContentDatabase.LoadDirectory(changedDirectory).ContentHash);
        }
        finally
        {
            Directory.Delete(firstDirectory, true);
            Directory.Delete(secondDirectory, true);
            Directory.Delete(changedDirectory, true);
        }
    }

    [Fact]
    public void HealEffectClampsToMaximum()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        GameState state = CreateOpenState(content);
        state.Player.Hp = state.Player.MaxHp - 2;
        state.Player.Inventory.TryAdd(new ItemInstance(content.CreateDefinition("minor_healing_potion")));
        Assert.True(state.Process(GameAction.UseItem(1)));
        Assert.Equal(state.Player.MaxHp, state.Player.Hp);
    }

    [Fact]
    public void BuffEffectRefreshesAndExpiresExactlyOnItsTurn()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        GameState state = CreateOpenState(content);
        state.Player.Inventory.TryAdd(new ItemInstance(content.CreateDefinition("potion_of_strength"), 2));
        Assert.True(state.Process(GameAction.UseItem(1)));
        Assert.Equal(20, state.Player.Effects.Single().RemainingTurns);
        for (int i = 0; i < 5; i++) state.Process(GameAction.Wait);
        Assert.True(state.Process(GameAction.UseItem(1)));
        Assert.Equal(20, state.Player.Effects.Single().RemainingTurns);
        for (int i = 0; i < 20; i++) state.Process(GameAction.Wait);
        Assert.Empty(state.Player.Effects);
    }

    [Fact]
    public void TeleportEffectUsesSafeDistantWalkableTile()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        GameState state = CreateOpenState(content);
        Point origin = state.Player.Position;
        state.Player.Inventory.TryAdd(new ItemInstance(content.CreateDefinition("scroll_of_teleportation")));
        Assert.True(state.Process(GameAction.UseItem(1)));
        int distance = Math.Abs(origin.X - state.Player.Position.X) +
            Math.Abs(origin.Y - state.Player.Position.Y);
        Assert.True(distance >= 6);
        Assert.True(state.Dungeon.IsWalkable(state.Player.Position));
        Assert.False(state.IsOccupied(state.Player.Position));
    }

    [Fact]
    public void RevealMapEffectExploresEveryWalkableTile()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        GameState state = CreateOpenState(content);
        state.Player.Inventory.TryAdd(new ItemInstance(content.CreateDefinition("scroll_of_mapping")));
        Assert.True(state.Process(GameAction.UseItem(1)));
        for (int y = 0; y < state.Dungeon.Height; y++)
        for (int x = 0; x < state.Dungeon.Width; x++)
        {
            Point point = new(x, y);
            if (state.Dungeon.IsWalkable(point)) Assert.True(state.Dungeon.IsExplored(point));
        }
    }

    private static GameState CreateOpenState(ContentDatabase content)
    {
        GameState state = new(1, 20, 12, 1, content);
        TileType[,] map = new TileType[20, 12];
        for (int y = 0; y < 12; y++)
        for (int x = 0; x < 20; x++)
            map[x, y] = TileType.Floor;
        state.ConfigureLevel(new Dungeon(map, new Point(1, 1), new Point(18, 10)), new Point(1, 1));
        state = GameStateTestHooks.ClearMonsters(state);
        return state;
    }

    private static MonsterDefinition ToDefinition(MonsterContent monster) =>
        new(monster.Id, monster.Name, monster.Glyph, monster.MaxHp, monster.Attack, monster.Defense,
            monster.SightRadius, monster.Behavior, monster.Color, monster.MinDepth, monster.Xp, monster.Params);

    private static string CreateCustomContent(
        Action<JsonObject, JsonObject, JsonObject> mutate)
    {
        string source = FindContentDirectory();
        string target = Directory.CreateTempSubdirectory("roguelike-content-").FullName;
        foreach (string name in new[] { "monsters.json", "items.json", "balance.json" })
            File.Copy(Path.Combine(source, name), Path.Combine(target, name));
        JsonObject monsters = JsonNode.Parse(File.ReadAllText(Path.Combine(target, "monsters.json")))!.AsObject();
        JsonObject items = JsonNode.Parse(File.ReadAllText(Path.Combine(target, "items.json")))!.AsObject();
        JsonObject balance = JsonNode.Parse(File.ReadAllText(Path.Combine(target, "balance.json")))!.AsObject();
        mutate(monsters, items, balance);
        Write(monsters, Path.Combine(target, "monsters.json"));
        Write(items, Path.Combine(target, "items.json"));
        Write(balance, Path.Combine(target, "balance.json"));
        return target;
    }

    private static void Write(JsonObject value, string path) =>
        File.WriteAllText(path, value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

    private static string FindContentDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "Content");
            if (File.Exists(Path.Combine(candidate, "balance.json"))) return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Default Content directory was not found.");
    }
}
