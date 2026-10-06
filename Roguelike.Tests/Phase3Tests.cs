using Microsoft.Xna.Framework;
using Xunit;

namespace Roguelike.Tests;

public sealed class Phase3Tests
{
    [Fact]
    public void ItemCatalogDefinitionsAreValid()
    {
        Assert.Equal(11, ItemCatalog.All.Count);
        Assert.All(ItemCatalog.All, item =>
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
        Inventory inventory = new(2);
        ItemDefinition potion = ItemCatalog.Get(ItemId.HealingPotion);
        ItemInstance first = new(potion, 5);
        Assert.True(inventory.TryAdd(first));
        ItemInstance second = new(potion, 3);
        Assert.True(inventory.TryAdd(second));
        Assert.Equal(2, inventory.Items.Count);
        Assert.Equal(5, inventory.Items[0].Count);
        Assert.Equal(3, inventory.Items[1].Count);
        ItemInstance third = new(potion, 5);
        Assert.False(inventory.TryAdd(third));
        Assert.Equal(2, inventory.Items.Count);
    }

    [Fact]
    public void EquipSwapAndCombatUseBonuses()
    {
        GameState state = new(5, 12, 10);
        state.MutableMonsters.Clear();
        state.Player.Inventory.TryAdd(new ItemInstance(ItemCatalog.Get(ItemId.Dagger)));
        state.Player.Inventory.TryAdd(new ItemInstance(ItemCatalog.Get(ItemId.ShortSword)));
        Assert.True(state.Process(GameAction.EquipItem(2)));
        Assert.Equal(7, state.Player.TotalAttack);
        Assert.True(state.Process(GameAction.EquipItem(1)));
        Assert.Equal(6, state.Player.TotalAttack);
        Assert.Contains(state.Player.Inventory.Items, item => item.Definition.Id == ItemId.ShortSword);
    }

    [Fact]
    public void FullHealthPotionDoesNotConsumeOrAdvanceTurn()
    {
        GameState state = new(7);
        int before = state.TurnNumber;
        int count = state.Player.Inventory.Items[0].Count;
        Assert.False(state.Process(GameAction.UseItem(0)));
        Assert.Equal(before, state.TurnNumber);
        Assert.Equal(count, state.Player.Inventory.Items[0].Count);
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
        state.ConfigureLevelForTesting(new Dungeon(map, new Point(1, 1), new Point(18, 10)), new Point(1, 1));
        state.Player.Inventory.TryAdd(new ItemInstance(ItemCatalog.Get(ItemId.ScrollOfMapping)));
        Assert.True(state.Process(GameAction.UseItem(1)));
        Assert.True(state.Dungeon.IsExplored(new Point(19, 11)));
        state.Player.Inventory.TryAdd(new ItemInstance(ItemCatalog.Get(ItemId.ScrollOfTeleportation)));
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
                GameAction action = new((TurnAction)random.Next(0, 5));
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

    private static ulong RunFuzz(int seed, IReadOnlyList<GameAction> actions)
    {
        GameState state = new(seed);
        foreach (GameAction action in actions)
        {
            state.Process(action);
            Assert.True(state.Player.Inventory.Items.Count <= state.Player.Inventory.Capacity);
            Assert.InRange(state.Player.Hp, 0, state.Player.MaxHp);
            Assert.DoesNotContain(state.Player.Inventory.Items, item =>
                item == state.Player.EquippedWeapon || item == state.Player.EquippedArmor);
            Assert.Equal(state.FloorItems.Count, state.FloorItems.Select(item => item.Position).Distinct().Count());
            Assert.All(state.FloorItems, item => Assert.True(state.Dungeon.IsWalkable(item.Position)));
        }
        return state.StateHash;
    }

    private static string FloorSignature(GameState state) =>
        string.Join("|", state.FloorItems.OrderBy(item => item.Position.Y).ThenBy(item => item.Position.X)
            .Select(item => $"{item.Position.X},{item.Position.Y}:{item.Item.Definition.Id}:{item.Item.Count}"));
}
