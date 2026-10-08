using Microsoft.Xna.Framework;
using Roguelike.Content;


namespace Roguelike;

/// <summary>Applies one JSON item effect using the supplied deterministic random source.</summary>
public interface IItemEffectHandler
{
    bool Apply(GameState state, EffectJson effect, IRandom random);
}

/// <summary>Executes the documented player, monster, FOV, cleanup turn order.</summary>
public sealed class TurnManager
{
    private readonly ContentDatabase content;
    private readonly Pathfinder pathfinder = new();
    private static readonly IReadOnlyDictionary<string, IItemEffectHandler> EffectHandlers =
        new Dictionary<string, IItemEffectHandler>(StringComparer.OrdinalIgnoreCase)
        {
            ["heal"] = new HealEffectHandler(),
            ["buff"] = new BuffEffectHandler(),
            ["teleport"] = new TeleportEffectHandler(),
            ["reveal_map"] = new RevealMapEffectHandler()
        };

    public TurnManager(ContentDatabase content) => this.content = content ?? throw new ArgumentNullException(nameof(content));

    /// <summary>Processes a movement, wait, or inventory action.</summary>
    public bool ProcessTurn(GameState state, GameAction action)
    {
        if (action.Kind == ActionKind.Restart) { state.Restart(); return false; }
        if (state.Status != GameStatus.Playing) return false;

        bool consumed;
        switch (action.Kind)
        {
            case ActionKind.Wait:
                TickEffects(state);
                consumed = true;
                break;
            case ActionKind.Move:
                consumed = ProcessMovement(state, action.Direction);
                if (consumed) TickEffects(state);
                break;
            case ActionKind.UseItem:
                consumed = UseItem(state, action.Slot, content);
                break;
            case ActionKind.EquipItem:
            case ActionKind.UnequipSlot:
            case ActionKind.DropItem:
                consumed = ProcessInventoryAction(state, action, content);
                break;
            default:
                consumed = false;
                break;
        }
        if (!consumed) return false;


        state.TurnNumber++;
        state.RunStats.TurnsSurvived = state.TurnNumber;
        MonsterTurns(state);
        if (state.Status == GameStatus.Playing && state.Player.Position == state.Dungeon.StairsPosition)
            state.AdvanceDepth();
        state.Dungeon.UpdateFieldOfView(state.Player.Position);
        foreach (MonsterActor monster in state.Monsters.Where(monster => !monster.IsAlive).ToArray())
            state.DropLoot(monster);
        state.CleanupDead();
        if (state.FeedbackTurns > 0) state.FeedbackTurns--;
        return true;
    }

    private static bool ProcessMovement(GameState state, Point direction)
    {
        if (direction == Point.Zero) return false;
        Point destination = state.Player.Position + direction;
        MonsterActor? target = state.MonsterAt(destination);
        if (target is not null)
        {
            CombatResult result = CombatResolver.Resolve(state.Player, target, state.GameplayRandom);
            state.RunStats.DamageDealt += result.Damage;
            state.SetFeedback($"You hit {target.Name} for {result.Damage}.",
                result.Killed ? Microsoft.Xna.Framework.Color.Gold : Microsoft.Xna.Framework.Color.White,
                target);
            if (result.Killed)
            {
                state.RunStats.MonstersSlain++;
                int previousLevel = state.Player.Level;
                state.Player.AddExperience(target.Definition.XpValue);
                state.SetFeedback($"{target.Name} dies. +{target.Definition.XpValue} XP.",
                    Microsoft.Xna.Framework.Color.Gold, target);
                if (state.Player.Level > previousLevel)
                    state.SetFeedback($"Level {state.Player.Level}!",
                        Microsoft.Xna.Framework.Color.LimeGreen, state.Player);
            }
            return true;
        }
        if (state.Dungeon.IsWalkable(destination) && !state.IsOccupied(destination))
        {
            state.Player.Position = destination;
            TryPickup(state);
            return true;
        }
        return false;
    }

    private static bool ProcessInventoryAction(GameState state, GameAction action, ContentDatabase content) =>
        action.Kind switch
        {
            ActionKind.UseItem => UseItem(state, action.Slot, content),
            ActionKind.EquipItem => EquipItem(state, action.Slot),
            ActionKind.UnequipSlot => UnequipItem(state, action.Slot),
            ActionKind.DropItem => DropItem(state, action.Slot),
            _ => false
        };

    private static void PrepareConsumedInventoryAction(GameState state)
    {
        TickEffects(state);
    }

    private static bool UseItem(GameState state, int slot, ContentDatabase content)
    {
        if (slot < 0 || slot >= state.Player.Inventory.Items.Count)
        {
            state.SetFeedback("That inventory slot is empty.", Microsoft.Xna.Framework.Color.Yellow);
            return false;
        }
        ItemDefinition definition = state.Player.Inventory.Items[slot].Definition;
        ItemContent itemContent = content.GetItem(definition.Id);
        if (definition.Type != ItemType.Consumable)
        {
            state.SetFeedback("That item cannot be used.", Microsoft.Xna.Framework.Color.Yellow);
            return false;
        }
        bool canHeal = itemContent.Effects.Any(e => e.Type == "heal");
        if (canHeal && state.Player.Hp >= state.Player.MaxHp)
        {
            state.SetFeedback("You are already at full health.", Microsoft.Xna.Framework.Color.Yellow);
            return false;
        }
        foreach (EffectJson effect in itemContent.Effects)
        {
            if (string.IsNullOrWhiteSpace(effect.Type) ||
                !EffectHandlers.TryGetValue(effect.Type, out IItemEffectHandler? handler))
                throw new InvalidOperationException($"No item effect handler registered for '{effect.Type}'.");
            if (!handler.Apply(state, effect, state.GameplayRandom)) return false;
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
        ItemInstance? oldEquipped = item.Definition.Type == ItemType.Weapon
            ? state.Player.EquippedWeapon : state.Player.EquippedArmor;
        ItemInstance equipped = item.Clone(1);
        ItemInstance? removed = state.Player.Inventory.RemoveOne(slot);
        if (removed is null) return false;
        if (oldEquipped is not null)
        {
            InventoryAddResult canReturn = state.Player.Inventory.TryAdd(oldEquipped);
            if (!canReturn.IsComplete)
            {
                state.Player.Inventory.TryAdd(removed);
                state.SetFeedback("There is no room to swap that equipment.", Microsoft.Xna.Framework.Color.Yellow);
                return false;
            }
        }
        if (item.Definition.Type == ItemType.Weapon)
        {
            state.Player.EquippedWeapon = equipped;
        }
        else
        {
            state.Player.EquippedArmor = equipped;
        }
        PrepareConsumedInventoryAction(state);
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
        if (!state.Player.Inventory.CanAdd(equipped))
        {
            state.SetFeedback("Your inventory is full.", Microsoft.Xna.Framework.Color.Yellow);
            return false;
        }
        PrepareConsumedInventoryAction(state);
        InventoryAddResult result = state.Player.Inventory.TryAdd(equipped);
        if (!result.IsComplete) throw new InvalidOperationException("Inventory preflight disagreed with insertion.");
        if (slot == 0) state.Player.EquippedWeapon = null;
        else state.Player.EquippedArmor = null;
        state.SetFeedback($"Unequipped {equipped.Definition.Name}.", Microsoft.Xna.Framework.Color.Gold);
        return true;
    }

    private static bool DropItem(GameState state, int slot)
    {
        if (slot < 0 || slot >= state.Player.Inventory.Items.Count)
        {
            state.SetFeedback("That inventory slot is empty.", Microsoft.Xna.Framework.Color.Yellow);
            return false;
        }
        Point? destination = state.FloorItemAt(state.Player.Position) is null
            ? state.Player.Position : state.FindNearestFreeTile(state.Player.Position);
        if (destination is null)
        {
            state.SetFeedback("There is no room to drop that item.", Microsoft.Xna.Framework.Color.Yellow);
            return false;
        }
        ItemInstance item = state.Player.Inventory.RemoveStack(slot)!;
        PrepareConsumedInventoryAction(state);
        state.AddFloorItem(destination.Value, item);
        state.SetFeedback($"Dropped {item.Definition.Name}.", Microsoft.Xna.Framework.Color.White);
        return true;
    }

    private static void TryPickup(GameState state)
    {
        FloorItem? floorItem = state.FloorItemAt(state.Player.Position);
        if (floorItem is null) return;
        ItemInstance offered = floorItem.Item;
        InventoryAddResult result = state.Player.Inventory.TryAdd(offered);
        if (result.AddedCount == 0)
        {
            state.SetFeedback("Your inventory is full.", Microsoft.Xna.Framework.Color.Yellow);
            return;
        }
        if (result.LeftoverCount == 0)
            state.RemoveFloorItem(floorItem);
        else
            offered.Count = result.LeftoverCount;
        state.RunStats.ItemsPickedUp += result.AddedCount;
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
        for (int i = 0; i < state.Monsters.Count; i++)
        {
            MonsterActor monster = state.Monsters[i];
            if (!state.Player.IsAlive) break;
            if (!monster.IsAlive) continue;
            int distance = Distance(monster.Position, state.Player.Position);
            bool seesPlayer = distance <= monster.Definition.SightRadius &&
                              state.Dungeon.HasLineOfSight(monster.Position, state.Player.Position);
            if (seesPlayer && monster.Definition.Behavior != MonsterBehavior.Idle)
            {
                monster.AlertTurns = RequiredParam(monster, "alertTurns");
                monster.LastKnownPlayerPosition = state.Player.Position;
            }
            int actEvery = monster.Definition.Behavior == MonsterBehavior.Slow
                ? RequiredParam(monster, "actEveryNTurns") : 1;
            if (actEvery > 1 && (state.TurnNumber - 1) % actEvery != 0)
            {
                continue;
            }

            if (monster.Definition.Behavior == MonsterBehavior.Ranged &&
                seesPlayer &&
                distance >= RequiredParam(monster, "minRange") &&
                distance <= RequiredParam(monster, "maxRange"))
            {
                CombatResult ranged = CombatResolver.Resolve(monster, state.Player, state.GameplayRandom);
                state.RunStats.DamageTaken += ranged.Damage;
                state.SetFeedback($"{monster.Name} shoots you for {ranged.Damage}.",
                    Microsoft.Xna.Framework.Color.OrangeRed, state.Player);
                if (!state.Player.IsAlive)
                {
                    state.Status = GameStatus.Dead;
                    state.RunStats.CauseOfDeath = monster.Name;
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
                state.RunStats.DamageTaken += melee.Damage;
                state.SetFeedback($"{monster.Name} hits you for {melee.Damage}.",
                    Microsoft.Xna.Framework.Color.OrangeRed, state.Player);
                if (!state.Player.IsAlive)
                {
                    state.Status = GameStatus.Dead;
                    state.RunStats.CauseOfDeath = monster.Name;
                    state.SetFeedback("You died. Press R to restart.", Microsoft.Xna.Framework.Color.Red);
                }
                continue;
            }
            bool alwaysChase = monster.Definition.Params.GetValueOrDefault("alwaysChase", 0) != 0;
            if (monster.AlertTurns <= 0 && !alwaysChase) continue;
            Point targetPosition = monster.LastKnownPlayerPosition ?? state.Player.Position;
            IReadOnlyList<Point> path = pathfinder.FindPath(state.Dungeon, monster.Position,
                targetPosition, point => state.IsOccupied(point, monster) ||
                    reserved.Contains(point) || state.FloorItemAt(point) is not null);
            if (path.Count == 0) { monster.AlertTurns = Math.Max(monster.AlertTurns - 1, 0); continue; }
            Point next = path[0];
            if (next == state.Player.Position || reserved.Contains(next) ||
                state.IsOccupied(next, monster) || state.FloorItemAt(next) is not null)
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
                state.FloorItemAt(candidate) is null &&
                state.Dungeon.HasLineOfSight(candidate, state.Player.Position))
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static int Distance(Point a, Point b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private static int RequiredParam(MonsterActor monster, string name) =>
        monster.Definition.Params.TryGetValue(name, out int value)
            ? value
            : throw new InvalidOperationException(
                $"Monster '{monster.Definition.Id}' is missing required parameter '{name}'.");

    private sealed class HealEffectHandler : IItemEffectHandler
    {
        public bool Apply(GameState state, EffectJson effect, IRandom random)
        {
            PrepareConsumedInventoryAction(state);
            state.Player.Heal(effect.Amount ??
                throw new InvalidOperationException("Heal effect is missing amount."));
            return true;
        }
    }

    private sealed class BuffEffectHandler : IItemEffectHandler
    {
        public bool Apply(GameState state, EffectJson effect, IRandom random)
        {
            PrepareConsumedInventoryAction(state);
            StatusEffectType stat = effect.Stat?.Equals("defense", StringComparison.OrdinalIgnoreCase) == true
                ? StatusEffectType.Defense : StatusEffectType.Strength;
            StatusEffect? active = state.Player.Effects.FirstOrDefault(item => item.Type == stat);
            if (active is null)
            {
                state.Player.Effects.Add(new StatusEffect(stat,
                    effect.Amount ?? throw new InvalidOperationException("Buff effect is missing amount."),
                    effect.Turns ?? throw new InvalidOperationException("Buff effect is missing turns.")));
            }
            else
            {
                active.Magnitude = effect.Amount ??
                    throw new InvalidOperationException("Buff effect is missing amount.");
                active.RemainingTurns = effect.Turns ??
                    throw new InvalidOperationException("Buff effect is missing turns.");
            }
            return true;
        }
    }

    private sealed class TeleportEffectHandler : IItemEffectHandler
    {
        public bool Apply(GameState state, EffectJson effect, IRandom random)
        {
            List<Point> candidates = new();
            for (int y = 0; y < state.Dungeon.Height; y++)
            for (int x = 0; x < state.Dungeon.Width; x++)
            {
                Point point = new(x, y);
                if (state.Dungeon.IsWalkable(point) && point != state.Dungeon.StairsPosition &&
                    Distance(point, state.Player.Position) >= effect.MinDistance &&
                    !state.IsOccupiedByPlayerOrMonster(point))
                    candidates.Add(point);
            }
            if (candidates.Count == 0)
            {
                state.SetFeedback("There is nowhere safe to teleport.", Microsoft.Xna.Framework.Color.Yellow);
                return false;
            }
            PrepareConsumedInventoryAction(state);
            state.Player.Position = candidates[random.Next(candidates.Count)];
            state.Dungeon.UpdateFieldOfView(state.Player.Position);
            return true;
        }
    }

    private sealed class RevealMapEffectHandler : IItemEffectHandler
    {
        public bool Apply(GameState state, EffectJson effect, IRandom random)
        {
            PrepareConsumedInventoryAction(state);
            state.Dungeon.RevealAll();
            return true;
        }
    }
}
