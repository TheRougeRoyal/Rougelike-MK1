using Microsoft.Xna.Framework;

namespace Roguelike;

/// <summary>Deterministically places level loot without using gameplay randomness.</summary>
public static class LootSpawner
{
    /// <summary>Creates the depth loot list and places it on valid tiles.</summary>
    public static List<FloorItem> Spawn(Dungeon dungeon, int depth, int seed,
        Point playerStart, IReadOnlyList<MonsterActor> monsters)
    {
        IRandom random = RandomStreams.Create(seed, depth, 0x4C4F4F54UL);
        List<Point> candidates = new();
        for (int y = 0; y < dungeon.Height; y++)
        for (int x = 0; x < dungeon.Width; x++)
        {
            Point point = new(x, y);
            if (dungeon.IsWalkable(point) && point != playerStart &&
                point != dungeon.StairsPosition && !dungeon.StartRoom.Contains(point) &&
                monsters.All(monster => monster.Position != point))
                candidates.Add(point);
        }
        int count = random.Next(2, 5);
        List<FloorItem> result = new();
        for (int i = 0; i < count && candidates.Count > 0; i++)
        {
            int pointIndex = random.Next(candidates.Count);
            Point point = candidates[pointIndex];
            candidates.RemoveAt(pointIndex);
            result.Add(new FloorItem(point, new ItemInstance(ItemCatalog.Choose(depth, random))));
        }
        return result;
    }
}
