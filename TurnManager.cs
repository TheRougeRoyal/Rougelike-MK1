using Microsoft.Xna.Framework;

namespace Roguelike;

/// <summary>Executes the documented player, monster, FOV, cleanup turn order.</summary>
public sealed class TurnManager
{
    private readonly Pathfinder pathfinder = new();

    /// <summary>Processes an action, returning whether it consumed a turn.</summary>
    public bool ProcessTurn(GameState state, TurnAction action)
        => ProcessTurn(state, new GameAction(action));

    /// <summary>Processes a movement, wait, or inventory action.</summary>
    public bool ProcessTurn(GameState state, GameAction action)
    {
        if (action.Movement == TurnAction.Restart) { state.Restart(); return false; }
        if (state.Status != GameStatus.Playing) return false;

        bool inventoryAction = action.Slot >= 0;
        bool consumed = inventoryAction
            ? ProcessInventoryAction(state, action)
            : action.Movement == TurnAction.Wait;
        Point direction = Direction(action.Movement);
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
                    int previousLevel = state.Player.Level;
                    state.Player.AddExperience(target.Definition.XpValue);
                    state.SetFeedback($"{target.Name} dies. +{target.Definition.XpValue} XP.",
                        Microsoft.Xna.Framework.Color.Gold, target);
                    if (state.Player.Level > previousLevel)
                    {
                        state.SetFeedback($"Level {state.Player.Level}!",
                            Microsoft.Xna.Framework.Color.LimeGreen, state.Player);
                    }
                }
                consumed = true;
            }
            else if (state.Dungeon.IsWalkable(destination) && !state.IsOccupied(destination))
            {
                state.Player.Position = destination;
                TryPickup(state);
                consumed = true;
            }
        }
        if (!consumed) return false;

        state.TurnNumber++;
        MonsterTurns(state);
        TickEffects(state);
        if (state.Status == GameStatus.Playing && state.Player.Position == state.Dungeon.StairsPosition)
            state.AdvanceDepth();
        state.Dungeon.UpdateFieldOfView(state.Player.Position);
        foreach (MonsterActor monster in state.MutableMonsters.Where(monster => !monster.IsAlive).ToArray())
            state.DropLoot(monster);
        state.CleanupDead();
        if (state.FeedbackTurns > 0) state.FeedbackTurns--;
        return true;
    }

    private static bool ProcessInventoryAction(GameState state, GameAction action)
    {
        if (action.Movement == TurnAction.Wait)
            return UseItem(state, action.Slot);
        if (action.Movement == TurnAction.MoveUp)
            return EquipItem(state, action.Slot);
        if (action.Movement == TurnAction.MoveDown)
            return UnequipItem(state, action.Slot);
        if (action.Movement == TurnAction.MoveLeft)
            return DropItem(state, action.Slot);
        return false;
    }

    private static bool UseItem(GameState state, int slot)
    {
        if (slot < 0 || slot >= state.Player.Inventory.Items.Count)
        {
            state.SetFeedback("That inventory slot is empty.", Microsoft.Xna.Framework.Color.Yellow);
            return false;
        }
        ItemDefinition definition = state.Player.Inventory.Items[slot].Definition;
        if (definition.Type != ItemType.Consumable)
        {
            state.SetFeedback("That item cannot be used.", Microsoft.Xna.Framework.Color.Yellow);
            return false;
        }
        if (definition.HealAmount > 0 && state.Player.Hp >= state.Player.MaxHp)
        {
            state.SetFeedback("You are already at full health.", Microsoft.Xna.Framework.Color.Yellow);
            return false;
        }
        if (definition.Id == ItemId.ScrollOfTeleportation)
        {
            List<Point> candidates = new();
            for (int y = 0; y < state.Dungeon.Height; y++)
            for (int x = 0; x < state.Dungeon.Width; x++)
            {
                Point point = new(x, y);
                if (state.Dungeon.IsWalkable(point) && Distance(point, state.Player.Position) >= 6 &&
                    !state.IsOccupiedByPlayerOrMonster(point))
                    candidates.Add(point);
            }
            if (candidates.Count == 0)
            {
                state.SetFeedback("There is nowhere safe to teleport.", Microsoft.Xna.Framework.Color.Yellow);
                return false;
            }
            state.Player.Position = candidates[state.GameplayRandom.Next(candidates.Count)];
            state.Dungeon.UpdateFieldOfView(state.Player.Position);
        }
        else if (definition.Id == ItemId.ScrollOfMapping)
        {
            state.Dungeon.RevealAll();
        }
        else if (definition.HealAmount > 0)
        {
            state.Player.Heal(definition.HealAmount);
        }
        else if (definition.BuffDuration > 0)
        {
            StatusEffect? effect = state.Player.Effects.FirstOrDefault(item => item.Type == StatusEffectType.Strength);
            if (effect is null) state.Player.Effects.Add(new StatusEffect(StatusEffectType.Strength,
                definition.AttackBonus, definition.BuffDuration));
            else { effect.Magnitude = definition.AttackBonus; effect.RemainingTurns = definition.BuffDuration; }
        }
        state.Player.Inventory.RemoveOne(slot);
        state.SetFeedback($"Used {definition.Name}.", Microsoft.Xna.Framework.Color.LimeGreen);
        return true;
    }

    private static bool EquipItem(GameState state, int slot)
    {
        if (slot < 0 || slot >= state.Player.Inventory.Items.Count)
        {
            state.SetFeedback("That inventory slot is empty.", Microsoft.Xna.Framework.Color.Yellow);
            return false;
        }
        ItemInstance item = state.Player.Inventory.Items[slot];
        if (item.Definition.Type is not (ItemType.Weapon or ItemType.Armor))
        {
            state.SetFeedback("Only weapons and armor can be equipped.", Microsoft.Xna.Framework.Color.Yellow);
            return false;
        }
        ItemInstance equipped = state.Player.Inventory.RemoveOne(slot)!;
        if (item.Definition.Type == ItemType.Weapon)
        {
            if (state.Player.EquippedWeapon is not null) state.Player.Inventory.TryAdd(state.Player.EquippedWeapon);
            state.Player.EquippedWeapon = equipped;
        }
        else
        {
            if (state.Player.EquippedArmor is not null) state.Player.Inventory.TryAdd(state.Player.EquippedArmor);
            state.Player.EquippedArmor = equipped;
        }
        state.SetFeedback($"Equipped {equipped.Definition.Name}.", Microsoft.Xna.Framework.Color.Gold);
        return true;
    }

    private static bool UnequipItem(GameState state, int slot)
    {
        ItemInstance? equipped = slot == 0 ? state.Player.EquippedWeapon : slot == 1 ? state.Player.EquippedArmor : null;
        if (equipped is null)
        {
            state.SetFeedback("That equipment slot is empty.", Microsoft.Xna.Framework.Color.Yellow);
            return false;
        }
        if (!state.Player.Inventory.TryAdd(equipped))
        {
            state.SetFeedback("Your inventory is full.", Microsoft.Xna.Framework.Color.Yellow);
            return false;
        }
        if (slot == 0) state.Player.EquippedWeapon = null;
        else state.Player.EquippedArmor = null;
        state.SetFeedback($"Unequipped {equipped.Definition.Name}.", Microsoft.Xna.Framework.Color.Gold);
        return true;
    }

    private static bool DropItem(GameState state, int slot)
    {
        ItemInstance? item = state.Player.Inventory.RemoveStack(slot);
        if (item is null)
        {
            state.SetFeedback("That inventory slot is empty.", Microsoft.Xna.Framework.Color.Yellow);
            return false;
        }
        Point? destination = state.FloorItemAt(state.Player.Position) is null
            ? state.Player.Position : state.FindNearestFreeTile(state.Player.Position);
        if (destination is null)
        {
            state.Player.Inventory.TryAdd(item);
            state.SetFeedback("There is no room to drop that item.", Microsoft.Xna.Framework.Color.Yellow);
            return false;
        }
        state.AddFloorItem(destination.Value, item);
        state.SetFeedback($"Dropped {item.Definition.Name}.", Microsoft.Xna.Framework.Color.White);
        return true;
    }

    private static void TryPickup(GameState state)
    {
        FloorItem? floorItem = state.FloorItemAt(state.Player.Position);
        if (floorItem is null) return;
        ItemInstance offered = floorItem.Item;
        if (!state.Player.Inventory.TryAdd(offered))
        {
            state.SetFeedback("Your inventory is full.", Microsoft.Xna.Framework.Color.Yellow);
            return;
        }
        state.RemoveFloorItem(floorItem);
        state.SetFeedback($"Picked up {offered.Definition.Name}.", Microsoft.Xna.Framework.Color.LimeGreen);
    }

    private static void TickEffects(GameState state)
    {
        for (int i = state.Player.Effects.Count - 1; i >= 0; i--)
        {
            StatusEffect effect = state.Player.Effects[i];
            effect.RemainingTurns--;
            if (effect.RemainingTurns <= 0) state.Player.Effects.RemoveAt(i);
        }
    }

    private void MonsterTurns(GameState state)
    {
        HashSet<Point> reserved = new();
        for (int i = 0; i < state.MutableMonsters.Count; i++)
        {
            MonsterActor monster = state.MutableMonsters[i];
            if (!state.Player.IsAlive) break;
            if (!monster.IsAlive) continue;
            int distance = Distance(monster.Position, state.Player.Position);
            bool seesPlayer = distance <= monster.Definition.SightRadius &&
                              state.Dungeon.HasLineOfSight(monster.Position, state.Player.Position);
            if (seesPlayer)
            {
                monster.AlertTurns = 5;
                monster.LastKnownPlayerPosition = state.Player.Position;
            }
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
                    monster.AlertTurns = Math.Max(monster.AlertTurns - 1, 0);
                    continue;
                }
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
            Point targetPosition = monster.LastKnownPlayerPosition ?? state.Player.Position;
            IReadOnlyList<Point> path = pathfinder.FindPath(state.Dungeon, monster.Position,
                targetPosition, point => state.IsOccupied(point, monster) || reserved.Contains(point));
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
