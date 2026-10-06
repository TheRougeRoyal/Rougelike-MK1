using Microsoft.Xna.Framework;

namespace Roguelike;

/// <summary>Executes the documented player, monster, FOV, cleanup turn order.</summary>
public sealed class TurnManager
{
    private readonly Pathfinder pathfinder = new();

    /// <summary>Processes an action, returning whether it consumed a turn.</summary>
    public bool ProcessTurn(GameState state, TurnAction action)
    {
        if (action == TurnAction.Restart) { state.Restart(); return false; }
        if (state.Status != GameStatus.Playing) return false;

        bool consumed = action == TurnAction.Wait || action == TurnAction.Heal;
        if (action == TurnAction.Heal)
        {
            int healed = state.Player.Heal();
            state.SetFeedback(healed > 0 ? $"You heal {healed} HP." : "You are already at full health.",
                Microsoft.Xna.Framework.Color.LightGreen);
        }
        Point direction = Direction(action);
        if (direction != Point.Zero)
        {
            Point destination = state.Player.Position + direction;
            MonsterActor? target = state.MonsterAt(destination);
            if (target is not null)
            {
                CombatResult result = CombatResolver.Resolve(state.Player, target, state.GameplayRandom);
                state.SetFeedback($"You hit {target.Name} for {result.Damage}.",
                    result.Killed ? Microsoft.Xna.Framework.Color.Gold : Microsoft.Xna.Framework.Color.White,
                    target);
                if (result.Killed)
                {
                    state.Player.AddExperience(target.Definition.XpValue);
                    state.SetFeedback($"{target.Name} dies. +{target.Definition.XpValue} XP.",
                        Microsoft.Xna.Framework.Color.Gold, target);
                }
                consumed = true;
            }
            else if (state.Dungeon.IsWalkable(destination) && !state.IsOccupied(destination))
            {
                state.Player.Position = destination;
                consumed = true;
            }
        }
        if (!consumed) return false;

        state.TurnNumber++;
        MonsterTurns(state);
        if (state.Status == GameStatus.Playing && state.Player.Position == state.Dungeon.StairsPosition)
            state.AdvanceDepth();
        state.Dungeon.UpdateFieldOfView(state.Player.Position);
        state.CleanupDead();
        if (state.FeedbackTurns > 0) state.FeedbackTurns--;
        return true;
    }

    private void MonsterTurns(GameState state)
    {
        HashSet<Point> reserved = new();
        for (int i = 0; i < state.MutableMonsters.Count; i++)
        {
            MonsterActor monster = state.MutableMonsters[i];
            if (!monster.IsAlive || !state.Player.IsAlive) break;
            int distance = Distance(monster.Position, state.Player.Position);
            bool seesPlayer = distance <= monster.Definition.SightRadius &&
                              state.Dungeon.HasLineOfSight(monster.Position, state.Player.Position);
            if (seesPlayer) monster.AlertTurns = 5;
            if (monster.Definition.Behavior == MonsterBehavior.Slow && state.TurnNumber % 2 == 0)
            {
                continue;
            }

            if (monster.Definition.Behavior == MonsterBehavior.Ranged &&
                seesPlayer && distance >= 2 && distance <= 5)
            {
                CombatResult ranged = CombatResolver.Resolve(monster, state.Player, state.GameplayRandom);
                state.SetFeedback($"{monster.Name} shoots you for {ranged.Damage}.",
                    Microsoft.Xna.Framework.Color.OrangeRed, state.Player);
                if (!state.Player.IsAlive)
                {
                    state.Status = GameStatus.Dead;
                    state.SetFeedback("You died. Press R to restart.", Microsoft.Xna.Framework.Color.Red);
                }
                continue;
            }
            if (monster.Definition.Behavior == MonsterBehavior.Ranged && distance == 1)
            {
                Point retreat = FindRetreatTile(state, monster);
                if (retreat != monster.Position)
                {
                    monster.Position = retreat;
                }
                monster.AlertTurns = Math.Max(monster.AlertTurns - 1, 0);
                continue;
            }
            if (distance == 1)
            {
                CombatResult melee = CombatResolver.Resolve(monster, state.Player, state.GameplayRandom);
                state.SetFeedback($"{monster.Name} hits you for {melee.Damage}.",
                    Microsoft.Xna.Framework.Color.OrangeRed, state.Player);
                if (!state.Player.IsAlive)
                {
                    state.Status = GameStatus.Dead;
                    state.SetFeedback("You died. Press R to restart.", Microsoft.Xna.Framework.Color.Red);
                }
                continue;
            }
            bool alwaysChase = monster.Definition.Type == MonsterType.Rat;
            if (monster.AlertTurns <= 0 && !alwaysChase) continue;
            IReadOnlyList<Point> path = pathfinder.FindPath(state.Dungeon, monster.Position,
                state.Player.Position, point => state.IsOccupied(point, monster) || reserved.Contains(point));
            if (path.Count == 0) { monster.AlertTurns = Math.Max(monster.AlertTurns - 1, 0); continue; }
            Point next = path[0];
            if (next == state.Player.Position || reserved.Contains(next) || state.IsOccupied(next, monster))
            {
                monster.AlertTurns = Math.Max(monster.AlertTurns - 1, 0);
                continue;
            }
            monster.Position = next;
            reserved.Add(next);
            monster.AlertTurns = Math.Max(monster.AlertTurns - 1, 0);
        }
    }

    private static Point FindRetreatTile(GameState state, MonsterActor monster)
    {
        Point best = monster.Position;
        int bestDistance = Distance(best, state.Player.Position);
        Point[] neighbors =
        {
            new(monster.Position.X - 1, monster.Position.Y),
            new(monster.Position.X + 1, monster.Position.Y),
            new(monster.Position.X, monster.Position.Y - 1),
            new(monster.Position.X, monster.Position.Y + 1)
        };

        foreach (Point candidate in neighbors)
        {
            int distance = Distance(candidate, state.Player.Position);
            if (distance > bestDistance && state.Dungeon.IsWalkable(candidate) &&
                !state.IsOccupied(candidate, monster) &&
                state.Dungeon.HasLineOfSight(candidate, state.Player.Position))
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static int Distance(Point a, Point b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    private static Point Direction(TurnAction action) => action switch
    {
        TurnAction.MoveUp => new Point(0, -1),
        TurnAction.MoveDown => new Point(0, 1),
        TurnAction.MoveLeft => new Point(-1, 0),
        TurnAction.MoveRight => new Point(1, 0),
        _ => Point.Zero
    };
}
