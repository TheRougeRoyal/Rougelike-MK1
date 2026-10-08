using Microsoft.Xna.Framework;
using Roguelike.Content;

namespace Roguelike;

/// <summary>Classification of an item.</summary>
public enum ItemType { Consumable, Weapon, Armor }

/// <summary>Stable identifiers for catalog items.</summary>
/// <summary>Immutable data-driven item definition.</summary>
public sealed record ItemDefinition(
    string Id, string Name, string Description, ItemType Type, Color Color,
    int MinDepth, int Weight, int MaxStack, int AttackBonus = 0,
    int DefenseBonus = 0, char Glyph = '?',
    IReadOnlyList<EffectJson>? Effects = null, string? Slot = null);

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
