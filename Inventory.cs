namespace Roguelike;

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

    /// <summary>Attempts to add a stack, filling compatible stacks first.</summary>
    public bool TryAdd(ItemInstance incoming)
    {
        int remaining = incoming.Count;
        foreach (ItemInstance stack in Items.Where(item => item.Definition.Id == incoming.Definition.Id))
        {
            int room = stack.Definition.MaxStack - stack.Count;
            int moved = Math.Min(room, remaining);
            stack.Count += moved;
            remaining -= moved;
            if (remaining == 0) return true;
        }
        while (remaining > 0 && Items.Count < Capacity)
        {
            int moved = Math.Min(incoming.Definition.MaxStack, remaining);
            Items.Add(incoming.Clone(moved));
            remaining -= moved;
        }
        incoming.Count = remaining;
        return remaining == 0;
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
