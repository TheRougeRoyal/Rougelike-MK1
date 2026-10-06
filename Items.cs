using Microsoft.Xna.Framework;

namespace Roguelike;

/// <summary>Classification of an item.</summary>
public enum ItemType { Consumable, Weapon, Armor }

/// <summary>Stable identifiers for catalog items.</summary>
public enum ItemId
{
    HealingPotion, GreaterHealingPotion, PotionOfStrength, ScrollOfTeleportation, ScrollOfMapping,
    Dagger, ShortSword, BattleAxe, LeatherArmor, ChainMail, PlateArmor
}

/// <summary>Immutable data-driven item definition.</summary>
public sealed record ItemDefinition(
    ItemId Id, string Name, string Description, ItemType Type, Color Color,
    int MinDepth, int Weight, int MaxStack, int HealAmount = 0, int AttackBonus = 0,
    int DefenseBonus = 0, int BuffDuration = 0, char Glyph = '?');

/// <summary>One mutable stack of an immutable item definition.</summary>
public sealed class ItemInstance
{
    /// <summary>Creates an item stack.</summary>
    public ItemInstance(ItemDefinition definition, int count = 1)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        if (count < 1 || count > definition.MaxStack) throw new ArgumentOutOfRangeException(nameof(count));
        Count = count;
    }

    /// <summary>Gets the immutable definition.</summary>
    public ItemDefinition Definition { get; }
    /// <summary>Gets or sets the stack count.</summary>
    public int Count { get; internal set; }
    /// <summary>Creates a copy of this stack.</summary>
    public ItemInstance Clone(int count) => new(Definition, count);
}

/// <summary>An item lying on a dungeon tile.</summary>
public sealed class FloorItem
{
    /// <summary>Creates a floor item.</summary>
    public FloorItem(Point position, ItemInstance item) { Position = position; Item = item; }
    /// <summary>Gets or sets the item position.</summary>
    public Point Position { get; set; }
    /// <summary>Gets the item stack.</summary>
    public ItemInstance Item { get; }
}

/// <summary>Built-in item definitions and depth-weighted lookup.</summary>
public static class ItemCatalog
{
    private static readonly IReadOnlyList<ItemDefinition> definitions = Array.AsReadOnly(new ItemDefinition[]
    {
        new(ItemId.HealingPotion, "Healing Potion", "Restores 15 HP.", ItemType.Consumable, Color.Red, 1, 30, 5, HealAmount: 15, Glyph: '!'),
        new(ItemId.GreaterHealingPotion, "Greater Healing Potion", "Restores 35 HP.", ItemType.Consumable, Color.Magenta, 3, 12, 5, HealAmount: 35, Glyph: '!'),
        new(ItemId.PotionOfStrength, "Potion of Strength", "Grants +3 Attack for 20 turns.", ItemType.Consumable, Color.Orange, 1, 14, 5, AttackBonus: 3, BuffDuration: 20, Glyph: '!'),
        new(ItemId.ScrollOfTeleportation, "Scroll of Teleportation", "Teleports to a safe distant tile.", ItemType.Consumable, Color.Cyan, 1, 8, 5, Glyph: '?'),
        new(ItemId.ScrollOfMapping, "Scroll of Mapping", "Reveals the current level.", ItemType.Consumable, Color.Yellow, 1, 7, 5, Glyph: '?'),
        new(ItemId.Dagger, "Dagger", "+1 Attack.", ItemType.Weapon, Color.LightGray, 1, 15, 1, AttackBonus: 1, Glyph: '/'),
        new(ItemId.ShortSword, "Short Sword", "+2 Attack.", ItemType.Weapon, Color.Silver, 1, 10, 1, AttackBonus: 2, Glyph: '/'),
        new(ItemId.BattleAxe, "Battle Axe", "+4 Attack.", ItemType.Weapon, Color.DarkRed, 4, 5, 1, AttackBonus: 4, Glyph: '/'),
        new(ItemId.LeatherArmor, "Leather Armor", "+1 Defense.", ItemType.Armor, Color.SaddleBrown, 1, 15, 1, DefenseBonus: 1, Glyph: '['),
        new(ItemId.ChainMail, "Chain Mail", "+2 Defense.", ItemType.Armor, Color.Gray, 3, 9, 1, DefenseBonus: 2, Glyph: '['),
        new(ItemId.PlateArmor, "Plate Armor", "+4 Defense.", ItemType.Armor, Color.DarkSlateGray, 5, 4, 1, DefenseBonus: 4, Glyph: '[')
    });

    /// <summary>Gets all definitions.</summary>
    public static IReadOnlyList<ItemDefinition> All => definitions;
    /// <summary>Gets a definition by identifier.</summary>
    public static ItemDefinition Get(ItemId id) => definitions.First(definition => definition.Id == id);
    /// <summary>Returns a weighted, depth-appropriate item.</summary>
    public static ItemDefinition Choose(int depth, Random random)
    {
        ItemDefinition[] available = definitions.Where(item => item.MinDepth <= depth).ToArray();
        int total = available.Sum(item => item.Weight);
        int roll = random.Next(total);
        foreach (ItemDefinition item in available)
        {
            if (roll < item.Weight) return item;
            roll -= item.Weight;
        }
        return available[^1];
    }
}
