using Microsoft.Xna.Framework;
using Xunit;
using Roguelike.Content;


namespace Roguelike.Tests;

public sealed class Phase3Tests
{
    [Fact]
    public void DataDrivenItemDefinitionsAreValid()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        Assert.NotEmpty(content.Items);
        Assert.All(content.Items, item =>
        {
            Assert.True(item.Weight > 0);
            Assert.True(item.MinDepth >= 1);
            Assert.InRange(item.MaxStack, 1, 5);
            if (item.Type is ItemType.Weapon or ItemType.Armor) Assert.Equal(1, item.MaxStack);
        });
    }

    [Fact]
    public void InventoryStacksAndSpillsIntoSlots()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        Inventory inventory = new(2);
        ItemContent potionContent = content.GetItem("healing_potion");
        ItemDefinition potion = new(potionContent.Id, potionContent.Name, potionContent.Description, potionContent.Type, potionContent.Color, potionContent.MinDepth, potionContent.Weight, potionContent.MaxStack, potionContent.AttackBonus, potionContent.DefenseBonus, potionContent.Glyph);
        ItemInstance first = new(potion, 5);
        Assert.True(inventory.TryAdd(first).IsComplete);
        Assert.Equal(5, first.Count);
        ItemInstance second = new(potion, 3);
        Assert.True(inventory.TryAdd(second).IsComplete);
        Assert.Equal(2, inventory.Items.Count);
        Assert.Equal(5, inventory.Items[0].Count);
        Assert.Equal(3, inventory.Items[1].Count);
        ItemInstance third = new(potion, 5);
        Assert.False(inventory.TryAdd(third).IsComplete);
        Assert.Equal(2, inventory.Items.Count);
    }

    [Fact]
    public void EquipSwapAndCombatUseBonuses()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        GameState state = new(5, 12, 10, 1, content);
        state.MutableMonsters.Clear();
        ItemContent daggerContent = content.GetItem("dagger");
        ItemDefinition dagger = new(daggerContent.Id, daggerContent.Name, daggerContent.Description, daggerContent.Type, daggerContent.Color, daggerContent.MinDepth, daggerContent.Weight, daggerContent.MaxStack, daggerContent.AttackBonus, daggerContent.DefenseBonus, daggerContent.Glyph);
        state.Player.Inventory.TryAdd(new ItemInstance(dagger));
        ItemContent swordContent = content.GetItem("short_sword");
        ItemDefinition sword = new(swordContent.Id, swordContent.Name, swordContent.Description, swordContent.Type, swordContent.Color, swordContent.MinDepth, swordContent.Weight, swordContent.MaxStack, swordContent.AttackBonus, swordContent.DefenseBonus, swordContent.Glyph);
        state.Player.Inventory.TryAdd(new ItemInstance(sword));
        Assert.True(state.Process(GameAction.EquipItem(1)));
        Assert.Equal(6, state.Player.TotalAttack);
        Assert.True(state.Process(GameAction.EquipItem(1)));
        Assert.Equal(7, state.Player.TotalAttack);
        Assert.Contains(state.Player.Inventory.Items, item => item.Definition.Id == "dagger");
    }

    [Fact]
    public void UnequipRemovesAttackAndDefenseBonuses()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        GameState state = CreateOpenState(1, content);
        ItemContent daggerContent = content.GetItem("dagger");
        ItemDefinition dagger = new(daggerContent.Id, daggerContent.Name, daggerContent.Description, daggerContent.Type, daggerContent.Color, daggerContent.MinDepth, daggerContent.Weight, daggerContent.MaxStack, daggerContent.AttackBonus, daggerContent.DefenseBonus, daggerContent.Glyph);
        state.Player.Inventory.TryAdd(new ItemInstance(dagger));
        ItemContent armorContent = content.GetItem("leather_armor");
        ItemDefinition armor = new(armorContent.Id, armorContent.Name, armorContent.Description, armorContent.Type, armorContent.Color, armorContent.MinDepth, armorContent.Weight, armorContent.MaxStack, armorContent.AttackBonus, armorContent.DefenseBonus, armorContent.Glyph);
        state.Player.Inventory.TryAdd(new ItemInstance(armor));
        Assert.True(state.Process(GameAction.EquipItem(1)));
        Assert.True(state.Process(GameAction.EquipItem(1)));
        Assert.Equal(6, state.Player.TotalAttack);
        Assert.Equal(2, state.Player.TotalDefense);
        Assert.True(state.Process(GameAction.UnequipSlot(0)));
        Assert.True(state.Process(GameAction.UnequipSlot(1)));
        Assert.Equal(5, state.Player.TotalAttack);
        Assert.Equal(1, state.Player.TotalDefense);
    }

    [Fact]
    public void FullHealthPotionDoesNotConsumeOrAdvanceTurn()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        GameState state = new(7, 60, 34, 1, content);
        int before = state.TurnNumber;
        int count = state.Player.Inventory.Items[0].Count;
        Assert.False(state.Process(GameAction.UseItem(0)));
        Assert.Equal(before, state.TurnNumber);
        Assert.Equal(count, state.Player.Inventory.Items[0].Count);
    }

    [Fact]
    public void InventoryActionsNeverMoveOrAttackAndInvalidActionsDoNotChangeHash()
    {
        ContentDatabase content = ContentDatabase.LoadDefault();
        GameState state = CreateOpenState(1, content);
        Point origin = state.Player.Position;
        ItemContent strengthContent = content.GetItem("potion_of_strength");
        ItemDefinition strength = new(strengthContent.Id, strengthContent.Name, strengthContent.Description, strengthContent.Type, strengthContent.Color, strengthContent.MinDepth, strengthContent.Weight, strengthContent.MaxStack, strengthContent.AttackBonus, strengthContent.DefenseBonus, strengthContent.Glyph);
        state.Player.Inventory.TryAdd(new ItemInstance(strength));
        int useTurn = state.TurnNumber;
        Assert.True(state.Process(GameAction.UseItem(1)));
        Assert.Equal(origin, state.Player.Position);
        Assert.Equal(useTurn + 1, state.TurnNumber);


        state.Player.Inventory.TryAdd(new ItemInstance(new ItemDefinition(
            content.GetItem("dagger").Id, content.GetItem("dagger").Name, content.GetItem("dagger").Description,
            content.GetItem("dagger").Type, content.GetItem("dagger").Color, content.GetItem("dagger").MinDepth,
            content.GetItem("dagger").Weight, content.GetItem("dagger").MaxStack, content.GetItem("dagger").AttackBonus,
            content.GetItem("dagger").DefenseBonus, content.GetItem("dagger").Glyph)));
        int turn = state.TurnNumber;
        Assert.True(state.Process(GameAction.EquipItem(1)));
        Assert.Equal(origin, state.Player.Position);
        Assert.Equal(turn + 1, state.TurnNumber);

        ulong beforeInvalidUse = state.StateHash;
        Assert.False(state.Process(GameAction.UseItem(1)));
        Assert.Equal(beforeInvalidUse, state.StateHash);
        Assert.Equal(origin, state.Player.Position);

        int armorSlot = state.Player.Inventory.Items.Count;
        ItemContent leatherContent = content.GetItem("leather_armor");
        ItemDefinition leather = new(leatherContent.Id, leatherContent.Name, leatherContent.Description, leatherContent.Type, leatherContent.Color, leatherContent.MinDepth, leatherContent.Weight, leatherContent.MaxStack, leatherContent.AttackBonus, leatherContent.DefenseBonus, leatherContent.Glyph);
        state.Player.Inventory.TryAdd(new ItemInstance(leather));
        Assert.True(state.Process(GameAction.EquipItem(armorSlot)));
        Assert.Equal(origin, state.Player.Position);
        int beforeUnequip = state.TurnNumber;
        Assert.True(state.Process(GameAction.UnequipSlot(0)));
        Assert.Equal(origin, state.Player.Position);
        Assert.Equal(beforeUnequip + 1, state.TurnNumber);

        ulong beforeInvalidUnequip = state.StateHash;
        Assert.False(state.Process(GameAction.UnequipSlot(0)));
        Assert.Equal(beforeInvalidUnequip, state.StateHash);
    }

    [Fact]
    public void InvalidUseEquipUnequipAndDropDoNotConsumeTurns()
    {
        GameState state = CreateOpenState();
        foreach (GameAction action in new[]
        {
            GameAction.UseItem(99), GameAction.EquipItem(99),
            GameAction.UnequipSlot(0), GameAction.DropItem(99)
        })
        {
            ulong before = state.StateHash;
            int turn = state.TurnNumber;
            Point position = state.Player.Position;
            Assert.False(state.Process(action));
            Assert.Equal(before, state.StateHash);
            Assert.Equal(turn, state.TurnNumber);
            Assert.Equal(position, state.Player.Position);
            Assert.NotEmpty(state.Message);
        }
    }

    [Fact]
    public void EquipDoesNotAttackMonsterAbovePlayer()
    {
        GameState state = CreateOpenState();
        state.Player.Inventory.TryAdd(new ItemInstance(ContentDatabase.LoadDefault().CreateDefinition("dagger")));
        MonsterActor monster = new(Monster(ContentDatabase.LoadDefault(), "rat"),
            state.Player.Position + new Point(0, -1));
        GameStateTestHooks.AddMonster(state, monster);
        int monsterHp = monster.Hp;
        Point origin = state.Player.Position;

        Assert.True(state.Process(GameAction.EquipItem(1)));

        Assert.Equal(origin, state.Player.Position);
        Assert.Equal(monsterHp, monster.Hp);
    }

    [Fact]
    public void StrengthBuffLastsExactlyTwentyFollowingTurnsAndRefreshes()
    {
        GameState state = CreateOpenState();
        ItemDefinition strength = ContentDatabase.LoadDefault().CreateDefinition("potion_of_strength");
        state.Player.Inventory.TryAdd(new ItemInstance(strength, 2));

        Assert.True(state.Process(GameAction.UseItem(1)));
        Assert.Equal(20, state.Player.Effects.Single().RemainingTurns);
        for (int i = 0; i < 5; i++) state.Process(GameAction.Wait);
        Assert.Equal(15, state.Player.Effects.Single().RemainingTurns);
        Assert.True(state.Process(GameAction.UseItem(1)));
        Assert.Equal(20, state.Player.Effects.Single().RemainingTurns);
        for (int i = 0; i < 19; i++) state.Process(GameAction.Wait);
        Assert.Equal(1, state.Player.Effects.Single().RemainingTurns);
        state.Process(GameAction.Wait);
        Assert.Empty(state.Player.Effects);
    }

    [Fact]
    public void TeleportIsValidAcrossFiftySeeds()
    {
        for (int seed = 0; seed < 50; seed++)
        {
            GameState state = CreateOpenState(seed);
            state.Player.Inventory.TryAdd(new ItemInstance(ContentDatabase.LoadDefault().CreateDefinition("scroll_of_teleportation")));
            Point origin = state.Player.Position;
            Assert.True(state.Process(GameAction.UseItem(1)));
            Assert.True(state.Dungeon.IsWalkable(state.Player.Position));
            Assert.False(state.IsOccupied(state.Player.Position));
            Assert.True(Math.Abs(origin.X - state.Player.Position.X) +
                Math.Abs(origin.Y - state.Player.Position.Y) >= 6);
        }
    }

    [Fact]
    public void DropFindsNearestFreeTileWhenPlayerTileIsOccupied()
    {
        GameState state = CreateOpenState();
        Point origin = state.Player.Position;
        state.AddFloorItem(origin, new ItemInstance(ContentDatabase.LoadDefault().CreateDefinition("dagger")));
        state.Player.Inventory.TryAdd(new ItemInstance(ContentDatabase.LoadDefault().CreateDefinition("short_sword")));
        int turn = state.TurnNumber;

        Assert.True(state.Process(GameAction.DropItem(1)));

        Assert.Equal(origin, state.Player.Position);
        Assert.Equal(turn + 1, state.TurnNumber);
        Assert.Contains(state.FloorItems, item => item.Position != origin &&
            item.Item.Definition.Id == "short_sword");
    }

    [Fact]
    public void FullInventoryPickupLeavesFloorItemInPlace()
    {
        GameState state = CreateOpenState();
        for (int i = state.Player.Inventory.Items.Count; i < state.Player.Inventory.Capacity; i++)
            state.Player.Inventory.TryAdd(new ItemInstance(ContentDatabase.LoadDefault().CreateDefinition("dagger")));
        Point destination = state.Player.Position + new Point(1, 0);
        FloorItem floorItem = new(destination, new ItemInstance(ContentDatabase.LoadDefault().CreateDefinition("short_sword")));
        state.AddFloorItem(destination, floorItem.Item);

        Assert.True(state.Process(GameAction.Move(new Point(1, 0))));

        Assert.Contains(state.FloorItems, item => item.Position == destination &&
            item.Item.Definition.Id == floorItem.Item.Definition.Id);
    }

    [Fact]
    public void MonsterDropCanBeForcedThroughGameplayRandomSeam()
    {
        GameState state = CreateOpenState();
        MonsterActor monster = new(Monster(ContentDatabase.LoadDefault(), "rat"),
            state.Player.Position + new Point(2, 0));
        monster.Hp = 0;
        GameStateTestHooks.AddMonster(state, monster);
        state.SetGameplayRandom(new ZeroRandom());

        GameStateTestHooks.DropLoot(state, monster);

        Assert.Contains(state.FloorItems, item => item.Item.Definition.MinDepth <= state.Depth);
    }

    [Fact]
    public void LootAtDepthTwoIgnoresDifferentDepthOneGameplayHistory()
    {
        GameState first = CreateOpenState(101);
        GameState second = CreateOpenState(101);
        first.MutableMonsters.Clear();
        second.MutableMonsters.Clear();
        first.Process(GameAction.Wait);
        first.ConsumeGameplayRandom(17);
        second.Process(GameAction.Wait);
        second.Process(GameAction.Wait);
        first.Player.Position = first.Dungeon.StairsPosition;
        second.Player.Position = second.Dungeon.StairsPosition;
        first.Process(GameAction.Wait);
        second.Process(GameAction.Wait);

        Assert.Equal(2, first.Depth);
        Assert.Equal(FloorSignature(first), FloorSignature(second));
    }

    [Fact]
    public void MappingAndTeleportationAreHeadlessActions()
    {
        GameState state = new(8, 20, 12);
        state.MutableMonsters.Clear();
        TileType[,] map = new TileType[20, 12];
        for (int y = 0; y < 12; y++)
        for (int x = 0; x < 20; x++)
            map[x, y] = TileType.Floor;
        state.ConfigureLevel(new Dungeon(map, new Point(1, 1), new Point(18, 10)), new Point(1, 1));
        state.Player.Inventory.TryAdd(new ItemInstance(ContentDatabase.LoadDefault().CreateDefinition("scroll_of_mapping")));
        Assert.True(state.Process(GameAction.UseItem(1)));
        Assert.True(state.Dungeon.IsExplored(new Point(19, 11)));
        state.Player.Inventory.TryAdd(new ItemInstance(ContentDatabase.LoadDefault().CreateDefinition("scroll_of_teleportation")));
        Point origin = state.Player.Position;
        Assert.True(state.Process(GameAction.UseItem(1)));
        Assert.True(Math.Abs(state.Player.Position.X - origin.X) + Math.Abs(state.Player.Position.Y - origin.Y) >= 6);
    }

    [Fact]
    public void LootIsStableForSeedAndDepth()
    {
        GameState first = new(42, 60, 34, 2);
        GameState second = new(42, 60, 34, 2);
        Assert.Equal(FloorSignature(first), FloorSignature(second));
    }

    [Fact]
    public void ItemActionFuzzPreservesInvariantsAndReplayHash()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            Random random = new(seed * 31 + 9);
            List<GameAction> actions = new();
            for (int turn = 0; turn < 500; turn++)
            {
                GameAction action = random.Next(0, 6) switch
                {
                    0 => GameAction.Wait,
                    1 => GameAction.Move(new Point(0, -1)),
                    2 => GameAction.Move(new Point(0, 1)),
                    3 => GameAction.Move(new Point(-1, 0)),
                    4 => GameAction.Move(new Point(1, 0)),
                    _ => GameAction.Wait
                };
                if (random.Next(4) == 0)
                    action = random.Next(4) switch
                    {
                        0 => GameAction.UseItem(random.Next(10)),
                        1 => GameAction.EquipItem(random.Next(10)),
                        2 => GameAction.DropItem(random.Next(10)),
                        _ => GameAction.UnequipSlot(random.Next(2))
                    };
                actions.Add(action);
            }
            ulong first = RunFuzz(seed, actions);
            ulong second = RunFuzz(seed, actions);
            Assert.Equal(first, second);
        }
    }

    [Fact]
    public void BiasedWalkerReachesDepthThreeForSomeSeeds()
    {
        int deepest = 1;
        int reached = 0;
        for (int seed = 0; seed < 20; seed++)
        {
            GameState state = new(seed);
            Pathfinder pathfinder = new();
            for (int turn = 0; turn < 2500 && state.Depth < 3 && state.Status == GameStatus.Playing; turn++)
            {
                IReadOnlyList<Point> path = pathfinder.FindPath(state.Dungeon, state.Player.Position,
                    state.Dungeon.StairsPosition, point => state.IsOccupied(point));
                state.Process(path.Count > 0 ? GameAction.Move(path[0] - state.Player.Position) : GameAction.Wait);
            }
            deepest = Math.Max(deepest, state.Depth);
            if (state.Depth >= 3) reached++;
        }
        Assert.True(reached > 0, $"walker reached depth {deepest}; expected at least one seed to reach depth 3");
    }

    private static ulong RunFuzz(int seed, IReadOnlyList<GameAction> actions)
    {
        GameState state = new(seed);
        ValidateInitialLoot(state);
        foreach (GameAction action in actions)
        {
            state.Process(action);
            if (state.Status == GameStatus.Dead)
                state.Process(GameAction.Restart);
            Assert.True(state.Player.Inventory.Items.Count <= state.Player.Inventory.Capacity);
            Assert.InRange(state.Player.Hp, 0, state.Player.MaxHp);
            Assert.All(state.Player.Inventory.Items, item =>
                Assert.InRange(item.Count, 1, item.Definition.MaxStack));
            Assert.DoesNotContain(state.Player.Inventory.Items, item =>
                item == state.Player.EquippedWeapon || item == state.Player.EquippedArmor);
            Assert.Equal(state.FloorItems.Count, state.FloorItems.Select(item => item.Position).Distinct().Count());
            Assert.All(state.FloorItems, item =>
            {
                Assert.True(state.Dungeon.IsWalkable(item.Position));
                Assert.DoesNotContain(state.Monsters, monster => monster.Position == item.Position);
            });
        }
        return state.StateHash;
    }

    private static void ValidateInitialLoot(GameState state)
    {
        Assert.Equal(state.FloorItems.Count,
            state.FloorItems.Select(item => item.Position).Distinct().Count());
        Assert.All(state.FloorItems, item =>
        {
            Assert.True(state.Dungeon.IsWalkable(item.Position));
            Assert.NotEqual(state.Dungeon.StairsPosition, item.Position);
            Assert.NotEqual(state.Dungeon.PlayerStart, item.Position);
            Assert.DoesNotContain(state.Monsters, monster => monster.Position == item.Position);
        });
    }

    private static string FloorSignature(GameState state) =>
        string.Join("|", state.FloorItems.OrderBy(item => item.Position.Y).ThenBy(item => item.Position.X)
            .Select(item => $"{item.Position.X},{item.Position.Y}:{item.Item.Definition.Id}:{item.Item.Count}"));

    private static GameState CreateOpenState(int seed = 1, ContentDatabase content = null!)
    {
        if (content is null) content = ContentDatabase.LoadDefault();
        GameState state = new(seed, 20, 12, 1, content);
        TileType[,] map = new TileType[20, 12];
        for (int y = 0; y < 12; y++)
        for (int x = 0; x < 20; x++)
            map[x, y] = TileType.Floor;
        state.ConfigureLevel(new Dungeon(map, new Point(1, 1), new Point(18, 10)),
            new Point(1, 1));
        return state;
    }

    private static MonsterDefinition Monster(ContentDatabase content, string id)
    {
        MonsterContent monster = content.GetMonster(id);
        return new MonsterDefinition(monster.Id, monster.Name, monster.Glyph, monster.MaxHp, monster.Attack,
            monster.Defense, monster.SightRadius, monster.Behavior, monster.Color, monster.MinDepth,
            monster.Xp, monster.Params);
    }

    private sealed class ZeroRandom : IRandom
    {
        public ulong State { get; set; }
        public int Next(int maxValue) => 0;
        public int Next(int minValue, int maxValue) => minValue;
        public double NextDouble() => 0;
    }
}
