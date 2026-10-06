using Microsoft.Xna.Framework;

namespace Roguelike;

/// <summary>A deterministic four-way A* pathfinder.</summary>
public sealed class Pathfinder
{
    /// <summary>Finds a path excluding the start and including the goal.</summary>
    public IReadOnlyList<Point> FindPath(
        Dungeon dungeon, Point start, Point goal, Func<Point, bool>? isBlocked = null)
    {
        if (!dungeon.IsWalkable(start) || !dungeon.IsWalkable(goal) ||
            start == goal)
        {
            return Array.Empty<Point>();
        }

        var open = new PriorityQueue<Point, (int F, int Order)>();
        var cameFrom = new Dictionary<Point, Point>();
        var cost = new Dictionary<Point, int> { [start] = 0 };
        int order = 0;
        open.Enqueue(start, (Heuristic(start, goal), order++));

        while (open.TryDequeue(out Point current, out _))
        {
            if (current == goal)
            {
                return Reconstruct(cameFrom, current);
            }

            foreach (Point next in Neighbors(current))
            {
                if (!dungeon.IsWalkable(next) || (isBlocked?.Invoke(next) ?? false) || next == start)
                {
                    continue;
                }

                int newCost = cost[current] + 1;
                if (!cost.TryGetValue(next, out int oldCost) || newCost < oldCost)
                {
                    cost[next] = newCost;
                    cameFrom[next] = current;
                    open.Enqueue(next, (newCost + Heuristic(next, goal), order++));
                }
            }
        }

        return Array.Empty<Point>();
    }

    private static int Heuristic(Point a, Point b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    private static IReadOnlyList<Point> Reconstruct(Dictionary<Point, Point> cameFrom, Point current)
    {
        List<Point> path = new();
        while (cameFrom.TryGetValue(current, out Point previous))
        {
            path.Add(current);
            current = previous;
        }
        path.Reverse();
        return path;
    }

    private static IEnumerable<Point> Neighbors(Point p)
    {
        yield return new Point(p.X - 1, p.Y);
        yield return new Point(p.X + 1, p.Y);
        yield return new Point(p.X, p.Y - 1);
        yield return new Point(p.X, p.Y + 1);
    }
}
