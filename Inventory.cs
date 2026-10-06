namespace Roguelike;

/// <summary>The result of inserting an item stack.</summary>
public readonly record struct InventoryAddResult(int AddedCount, int LeftoverCount)
{
    /// <summary>Gets whether the complete requested stack was inserted.</summary>
    public bool IsComplete => LeftoverCount == 0;
}

/// <summary>Ten-slot player inventory with stack-aware insertion.</summary>
public sealed class Inventory
{
    /// <summary>Creates an inventory.</summary>
    public Inventory(int capacity = 10) { Capacity = capacity; }
    /// <summary>Gets the maximum number of occupied slots.</summary>
    public int Capacity { get; }
    /// <summary>Gets the occupied item stacks.</summary>
    public List<ItemInstance> Items { get; } = new();
    /// <summary>Gets whether all slots are occupied.</summary>
    public bool IsFull => Items.Count >= Capacity;

    /// <summary>Gets whether the complete stack can be inserted without changing the inventory.</summary>
    public bool CanAdd(ItemInstance incoming)
    {
        int remaining = incoming.Count;
        foreach (ItemInstance stack in Items.Where(item => item.Definition.Id == incoming.Definition.Id))
            remaining -= Math.Min(stack.Definition.MaxStack - stack.Count, remaining);
        int freeSlots = Capacity - Items.Count;
        remaining -= freeSlots * incoming.Definition.MaxStack;
        return remaining <= 0;
    }

    /// <summary>Attempts to add a stack, filling compatible stacks first.</summary>
    public InventoryAddResult TryAdd(ItemInstance incoming)
    {
        int remaining = incoming.Count;
        foreach (ItemInstance stack in Items.Where(item => item.Definition.Id == incoming.Definition.Id))
        {
            int room = stack.Definition.MaxStack - stack.Count;
            int moved = Math.Min(room, remaining);
            stack.Count += moved;
            remaining -= moved;
            if (remaining == 0) return new InventoryAddResult(incoming.Count, 0);
        }
        while (remaining > 0 && Items.Count < Capacity)
        {
            int moved = Math.Min(incoming.Definition.MaxStack, remaining);
            Items.Add(incoming.Clone(moved));
            remaining -= moved;
        }
        return new InventoryAddResult(incoming.Count - remaining, remaining);
    }

    /// <summary>Removes one item from a slot.</summary>
    public ItemInstance? RemoveOne(int slot)
    {
        if (slot < 0 || slot >= Items.Count) return null;
        ItemInstance stack = Items[slot];
        ItemInstance removed = stack.Clone(1);
        stack.Count--;
        if (stack.Count == 0) Items.RemoveAt(slot);
        return removed;
    }

    /// <summary>Removes an entire stack from a slot.</summary>
    public ItemInstance? RemoveStack(int slot)
    {
        if (slot < 0 || slot >= Items.Count) return null;
        ItemInstance item = Items[slot];
        Items.RemoveAt(slot);
        return item;
    }
}
