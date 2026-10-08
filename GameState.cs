using Microsoft.Xna.Framework;
using Roguelike.Content;


namespace Roguelike;

/// <summary>Lifecycle states for a run.</summary>
public enum GameStatus { Playing, Dead }

/// <summary>Explicit kinds of player actions.</summary>
public enum ActionKind { Wait, Move, UseItem, EquipItem, UnequipSlot, DropItem, Restart }

/// <summary>Payload-bearing player action.</summary>
public readonly record struct GameAction(ActionKind Kind, Point Direction, int Slot)
{
    /// <summary>Creates a wait action.</summary>
    public static GameAction Wait => new(ActionKind.Wait, Point.Zero, -1);
    /// <summary>Creates a movement action.</summary>
    public static GameAction Move(Point direction) => new(ActionKind.Move, direction, -1);
    /// <summary>Creates a use-item action.</summary>
    public static GameAction UseItem(int slot) => new(ActionKind.UseItem, Point.Zero, slot);
    /// <summary>Creates an equip-item action.</summary>
    public static GameAction EquipItem(int slot) => new(ActionKind.EquipItem, Point.Zero, slot);
    /// <summary>Creates an unequip action.</summary>
    public static GameAction UnequipSlot(int slot) => new(ActionKind.UnequipSlot, Point.Zero, slot);
    /// <summary>Creates a drop-item action.</summary>
    public static GameAction DropItem(int slot) => new(ActionKind.DropItem, Point.Zero, slot);
    /// <summary>Creates a restart action.</summary>
    public static GameAction Restart => new(ActionKind.Restart, Point.Zero, -1);
}

/// <summary>Deterministic headless game state.</summary>
public sealed class GameState
{
    private readonly int runSeed;
    private readonly ContentDatabase content;
    private IRandom gameplayRandom;
    private readonly List<MonsterActor> monsters = new();
    private readonly List<FloorItem> floorItems = new();
    private readonly TurnManager turnManager;

    /// <summary>Creates a new run.</summary>
    public GameState(int seed, int width = 60, int height = 34, int startingDepth = 1, ContentDatabase content = null!)
        : this(seed, width, height, startingDepth, content, true)
    {
    }

    private GameState(int seed, int width, int height, int startingDepth, ContentDatabase content, bool initialize)
    {
        if (width < 12 || height < 10) throw new ArgumentOutOfRangeException(nameof(width));
        if (startingDepth < 1) throw new ArgumentOutOfRangeException(nameof(startingDepth));
        runSeed = seed;
        this.content = content ?? ContentDatabase.LoadDefault();
        turnManager = new TurnManager(this.content);
        Width = width;
        Height = height;
        gameplayRandom = RandomStreams.Create(seed, 0, 0x47504C59UL);
        if (!initialize) return;
        ResetRun();
        if (startingDepth > 1)
        {
            Depth = startingDepth;
            CreateLevel(true);
        }
    }
    /// <summary>Gets map width.</summary>
    public int Width { get; }
    /// <summary>Gets map height.</summary>
    public int Height { get; }
    /// <summary>Gets current depth.</summary>
    public int Depth { get; private set; }
    /// <summary>Gets current dungeon.</summary>
    public Dungeon Dungeon { get; private set; } = null!;
    /// <summary>Gets player.</summary>
    public PlayerActor Player { get; private set; } = null!;
    /// <summary>Gets monsters in stable spawn order.</summary>
    public IReadOnlyList<MonsterActor> Monsters => monsters;
    /// <summary>Gets items currently lying on the floor.</summary>
    public IReadOnlyList<FloorItem> FloorItems => floorItems;
    /// <summary>Gets lifecycle status.</summary>
    public GameStatus Status { get; internal set; }
    /// <summary>Gets latest feedback message.</summary>
    public string Message { get; internal set; } = string.Empty;
    /// <summary>Gets remaining feedback display time.</summary>
    public int FeedbackTurns { get; internal set; }
    /// <summary>Gets feedback tint.</summary>
    public Microsoft.Xna.Framework.Color FeedbackTint { get; internal set; } = Microsoft.Xna.Framework.Color.White;
    /// <summary>Gets the actor most recently affected by combat feedback.</summary>
    public Actor? FeedbackActor { get; internal set; }
    /// <summary>Gets completed turns.</summary>
    public int TurnNumber { get; internal set; }
    /// <summary>Gets the bounded message history for the current run.</summary>
    public MessageLog MessageLog { get; } = new();
    /// <summary>Gets run statistics. These are presentation metadata and are excluded from StateHash.</summary>
    public RunStats RunStats { get; } = new();
    /// <summary>Gets run seed.</summary>
    public int Seed => runSeed;
    /// <summary>Gets current layout fingerprint.</summary>
    public ulong LayoutFingerprint => Dungeon.LayoutFingerprint;
    /// <summary>Gets a deterministic hash of the complete gameplay state.</summary>
    public ulong StateHash => ComputeStateHash();

    /// <summary>Processes an inventory action.</summary>
    public bool Process(GameAction action) => turnManager.ProcessTurn(this, action);
    /// <summary>Restarts the run, including gameplay RNG.</summary>
    public void Restart() => ResetRun();

    internal IRandom GameplayRandom => gameplayRandom;
    /// <summary>Gets the serializable gameplay RNG state.</summary>
    public ulong GameplayRandomState => gameplayRandom.State;
    /// <summary>Gets the deterministic level-generation stream state for this depth.</summary>
    public ulong LevelRandomState => RandomStreams.Create(runSeed, Depth, 0x4C455645UL).State;
    /// <summary>Gets the deterministic monster-placement stream state for this depth.</summary>
    public ulong MonsterRandomState => RandomStreams.Create(runSeed, Depth, 0x4D4F4E53UL).State;
    /// <summary>Gets the deterministic loot stream state for this depth.</summary>
    public ulong LootRandomState => RandomStreams.Create(runSeed, Depth, 0x4C4F4F54UL).State;
    internal List<MonsterActor> MutableMonsters => monsters;

    internal void ResetRun()
    {
        Depth = 1;
        TurnNumber = 0;
        Status = GameStatus.Playing;
        gameplayRandom = RandomStreams.Create(runSeed, 0, 0x47504C59UL);
        RunStats.Reset();
        MessageLog.Clear();
        monsters.Clear();
        floorItems.Clear();
        SetFeedback("Explore the dungeon.", Microsoft.Xna.Framework.Color.White);
        CreateLevel(true);
    }

    internal void CreateLevel()
    {
        CreateLevel(true);
    }

    private void CreateLevel(bool resetPlayer)
    {
        Dungeon = new Dungeon(Width, Height, RandomStreams.Create(runSeed, Depth, 0x4C455645UL));
        if (resetPlayer)
        {
            Player = new PlayerActor(Dungeon.PlayerStart, content);
        }
        else
        {
            Player.Position = Dungeon.PlayerStart;
            int healAmount = (int)(((long)Player.MaxHp * content.Balance.StairHealPercent + 99) / 100);
            Player.Heal(healAmount);
        }
        monsters.Clear();
        floorItems.Clear();
        SpawnMonsters();
        floorItems.AddRange(LootSpawner.Spawn(Dungeon, Depth, runSeed, Dungeon.PlayerStart, monsters, content));
        Dungeon.UpdateFieldOfView(Player.Position);
    }

    internal void AdvanceDepth()
    {
        Depth++;
        RunStats.MaxDepth = Math.Max(RunStats.MaxDepth, Depth);
        CreateLevel(false);
        SetFeedback($"You descend to depth {Depth}.", Microsoft.Xna.Framework.Color.Gold);
    }

    internal void SpawnMonsters()
    {
        IRandom levelMonsterRandom = RandomStreams.Create(runSeed, Depth, 0x4D4F4E53UL);
        int targetCount = content.Balance.SpawnBase + (Depth - 1) * content.Balance.SpawnPerDepth;
        List<Point> candidates = new();
        for (int y = 1; y < Dungeon.Height - 1; y++)
        for (int x = 1; x < Dungeon.Width - 1; x++)
        {
            Point point = new(x, y);
            if (Dungeon.IsWalkable(point) && point != Dungeon.PlayerStart &&
                point != Dungeon.StairsPosition && !Dungeon.StartRoom.Contains(point) &&
                Distance(point, Dungeon.PlayerStart) >= content.Balance.MinSpawnDistance)
                candidates.Add(point);
        }
        MonsterContent[] available = content.Monsters.Where(item => item.MinDepth <= Depth).ToArray();
        int availableCount = available.Length;
        for (int i = 0; i < targetCount && candidates.Count > 0; i++)
        {
            int index = levelMonsterRandom.Next(candidates.Count);
            Point position = candidates[index];
            candidates.RemoveAt(index);
            MonsterContent baseContent = available[levelMonsterRandom.Next(availableCount)];
            int scale = Math.Max(0, Depth - baseContent.MinDepth);
            MonsterDefinition definition = new(
                baseContent.Id, baseContent.Name, baseContent.Glyph,
                baseContent.MaxHp + scale * content.Balance.DepthHpScale,
                baseContent.Attack + scale * content.Balance.DepthAttackScale,
                baseContent.Defense + scale * content.Balance.DepthDefenseScale,
                baseContent.SightRadius,
                baseContent.Behavior,
                baseContent.Color,
                baseContent.MinDepth,
                baseContent.Xp,
                baseContent.Params);
            monsters.Add(new MonsterActor(definition, position));
        }
    }

    internal bool IsOccupied(Point point, MonsterActor? except = null)
    {
        for (int i = 0; i < monsters.Count; i++)
            if (monsters[i].IsAlive && monsters[i] != except && monsters[i].Position == point) return true;
        return false;
    }
    internal bool IsOccupiedByPlayerOrMonster(Point point, MonsterActor? except = null) =>
        Player.Position == point || IsOccupied(point, except);
    internal FloorItem? FloorItemAt(Point point) => floorItems.FirstOrDefault(item => item.Position == point);
    internal void RemoveFloorItem(FloorItem item) => floorItems.Remove(item);
    internal bool AddFloorItem(Point point, ItemInstance item)
    {
        if (!Dungeon.IsWalkable(point) || FloorItemAt(point) is not null) return false;
        floorItems.Add(new FloorItem(point, item));
        return true;
    }
    /// <summary>Places an item on an unoccupied walkable floor tile.</summary>
    public bool TryPlaceFloorItem(Point point, ItemInstance item) => AddFloorItem(point, item);
    internal Point? FindNearestFreeTile(Point origin)
    {
        Queue<Point> queue = new();
        HashSet<Point> visited = new() { origin };
        queue.Enqueue(origin);
        while (queue.Count > 0)
        {
            Point current = queue.Dequeue();
            if (Dungeon.IsWalkable(current) && !IsOccupiedByPlayerOrMonster(current) &&
                FloorItemAt(current) is null) return current;
            foreach (Point next in new[] { new Point(current.X - 1, current.Y), new Point(current.X + 1, current.Y),
                new Point(current.X, current.Y - 1), new Point(current.X, current.Y + 1) })
                if (Dungeon.InBounds(next) && visited.Add(next)) queue.Enqueue(next);
        }
        return null;
    }
    internal MonsterActor? MonsterAt(Point point)
    {
        for (int i = 0; i < monsters.Count; i++)
            if (monsters[i].IsAlive && monsters[i].Position == point) return monsters[i];
        return null;
    }
    internal void SetFeedback(string message, Microsoft.Xna.Framework.Color tint, Actor? actor = null)
    {
        Message = message;
        FeedbackTint = tint;
        FeedbackActor = actor;
        FeedbackTurns = content.Balance.FeedbackDuration;
        MessageLog.Add(message, tint, TurnNumber);
    }
    internal void CleanupDead()
    {
        for (int i = monsters.Count - 1; i >= 0; i--)
            if (!monsters[i].IsAlive) monsters.RemoveAt(i);
    }

    internal void DropLoot(MonsterActor monster)
    {
        if (GameplayRandom.Next(100) >= content.Balance.DropChancePercent) return;
        Point? target = FindNearestFreeTile(monster.Position);
        if (target is not null)
            AddFloorItem(target.Value, new ItemInstance(LootSpawner.Choose(Depth, GameplayRandom, content)));
    }

    internal void ConfigureLevel(Dungeon dungeon, Point playerPosition)
    {
        Dungeon = dungeon;
        Player.Position = playerPosition;
        monsters.Clear();
        floorItems.Clear();
        Dungeon.UpdateFieldOfView(Player.Position);
    }
    internal void RestoreGameplayRandomState(ulong state) => gameplayRandom.State = state;

    /// <summary>Restores a complete run without spawning new actors or consuming gameplay randomness.</summary>
    public static GameState Restore(GameStateSnapshot snapshot, ContentDatabase content)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(content);
        GameState state = new(snapshot.Seed, snapshot.Width, snapshot.Height, snapshot.Depth, content, false)
        {
            Dungeon = new Dungeon(snapshot.Width, snapshot.Height,
                RandomStreams.Create(snapshot.Seed, snapshot.Depth, 0x4C455645UL))
        };
        state.Player = new PlayerActor(state.Dungeon.PlayerStart, content);
        state.Depth = snapshot.Depth;
        state.Player.Restore(snapshot.Player.Level, snapshot.Player.Experience, snapshot.Player.Hp,
            snapshot.Player.MaxHp, snapshot.Player.Attack, snapshot.Player.Defense);
        state.Player.Inventory.Items.Clear();
        foreach (ItemInstance item in snapshot.Player.Inventory)
            state.Player.Inventory.Items.Add(item);
        state.Player.EquippedWeapon = snapshot.Player.EquippedWeapon;
        state.Player.EquippedArmor = snapshot.Player.EquippedArmor;
        state.Player.Effects.AddRange(snapshot.Player.Effects);
        state.Player.Position = snapshot.PlayerPosition;
        state.Dungeon.RestoreExplored(snapshot.ExploredTiles);
        state.Dungeon.RecomputeVisible(snapshot.PlayerPosition);
        state.monsters.AddRange(snapshot.Monsters);
        state.floorItems.AddRange(snapshot.FloorItems);
        state.TurnNumber = snapshot.TurnNumber;
        state.Status = snapshot.Status;
        state.RunStats.Restore(snapshot.Stats.TurnsSurvived, snapshot.Stats.MonstersSlain,
            snapshot.Stats.ItemsPickedUp, snapshot.Stats.DamageDealt, snapshot.Stats.DamageTaken,
            snapshot.Stats.MaxDepth, snapshot.Stats.CauseOfDeath);
        state.MessageLog.Restore(snapshot.Messages);
        state.gameplayRandom.State = snapshot.GameplayRandomState;
        if (snapshot.Messages.LastOrDefault() is MessageLogEntry last)
        {
            state.Message = last.Text;
            state.FeedbackTint = last.Color;
        }
        return state;
    }

    internal void RestoreSnapshot(Dungeon dungeon, Point playerPosition, int turnNumber, GameStatus status,
        string explored, RunStatsSnapshot stats, IEnumerable<MessageLogEntry> messages)
    {
        Dungeon = dungeon;
        monsters.Clear();
        floorItems.Clear();
        Player.Position = playerPosition;
        TurnNumber = turnNumber;
        Status = status;
        RunStats.Restore(stats.TurnsSurvived, stats.MonstersSlain, stats.ItemsPickedUp,
            stats.DamageDealt, stats.DamageTaken, stats.MaxDepth, stats.CauseOfDeath);
        MessageLog.Restore(messages);
        dungeon.RestoreExplored(explored);
    }

    public readonly record struct RunStatsSnapshot(int TurnsSurvived, int MonstersSlain, int ItemsPickedUp,
        int DamageDealt, int DamageTaken, int MaxDepth, string? CauseOfDeath);

    internal void RestoreActors(IEnumerable<MonsterActor> restoredMonsters, IEnumerable<FloorItem> restoredItems)
    {
        monsters.Clear();
        floorItems.Clear();
        monsters.AddRange(restoredMonsters);
        floorItems.AddRange(restoredItems);
    }

    internal void SetGameplayRandom(IRandom random)
    {
        gameplayRandom = random ?? throw new ArgumentNullException(nameof(random));
    }
    internal void ConsumeGameplayRandom(int draws)
    {
        for (int i = 0; i < Math.Max(0, draws); i++) gameplayRandom.Next(int.MaxValue);
    }
    internal ulong ComputeStateHash()
    {
        const ulong prime = 1099511628211UL;
        ulong hash = 14695981039346656037UL;
        void Mix(int value) { unchecked { hash ^= (uint)value; hash *= prime; } }
        Mix(Depth); Mix(TurnNumber); Mix((int)Status); Mix(Player.Position.X); Mix(Player.Position.Y);
        Mix(Player.Hp); Mix(Player.MaxHp); Mix(Player.Level); Mix(Player.Experience);
        Mix(Player.TotalAttack); Mix(Player.TotalDefense);
        foreach (ItemInstance item in Player.Inventory.Items)
        {
            MixString(item.Definition.Id); Mix(item.Count);
        }
        MixString(Player.EquippedWeapon?.Definition.Id ?? string.Empty);
        MixString(Player.EquippedArmor?.Definition.Id ?? string.Empty);
        foreach (StatusEffect effect in Player.Effects)
        {
            Mix((int)effect.Type); Mix(effect.Magnitude); Mix(effect.RemainingTurns);
        }
        foreach (FloorItem item in floorItems.OrderBy(item => item.Position.Y).ThenBy(item => item.Position.X))
        {
            Mix(item.Position.X); Mix(item.Position.Y);             MixString(item.Item.Definition.Id); Mix(item.Item.Count);
        }
        for (int i = 0; i < monsters.Count; i++)
        {
            MonsterActor monster = monsters[i];
            MixString(monster.Definition.Id); Mix(monster.Position.X); Mix(monster.Position.Y); Mix(monster.Hp);
        }
        Mix(unchecked((int)gameplayRandom.State));
        Mix(unchecked((int)(gameplayRandom.State >> 32)));
        return hash;

        void MixString(string value)
        {
            foreach (char character in value) Mix(character);
            Mix(0);
        }
    }

    private static int Distance(Point a, Point b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    /// <summary>Derives an independent generation seed for a run depth.</summary>
    public static int CreateLevelSeed(int seed, int depth)
    {
        unchecked { return seed * 397 ^ depth * 7919; }
    }

    /// <summary>In-memory state transferred from the persistence DTO layer to GameState.</summary>
    public sealed record GameStateSnapshot(
        int Seed, int Width, int Height, int Depth, int TurnNumber, GameStatus Status,
        Point PlayerPosition, PlayerSnapshot Player, string ExploredTiles,
        GameState.RunStatsSnapshot Stats, IReadOnlyList<MessageLogEntry> Messages,
        IReadOnlyList<MonsterActor> Monsters, IReadOnlyList<FloorItem> FloorItems,
        ulong GameplayRandomState);

    /// <summary>Saved player values and owned item state.</summary>
    public sealed record PlayerSnapshot(
        int Level, int Experience, int Hp, int MaxHp, int Attack, int Defense,
        IReadOnlyList<ItemInstance> Inventory, ItemInstance? EquippedWeapon,
        ItemInstance? EquippedArmor, IReadOnlyList<StatusEffect> Effects);
    /// <summary>Derives the independent gameplay RNG seed for a run.</summary>
    public static int CreateGameplaySeed(int seed)
    {
        unchecked { return seed * 1009 + 17; }
    }
    /// <summary>Derives the independent loot seed for a run depth.</summary>
    public static int CreateLootSeed(int seed, int depth)
    {
        unchecked { return seed * 1543 ^ depth * 104729 ^ 0x5EED123; }
    }
    /// <summary>Derives the independent monster placement seed for a run depth.</summary>
    public static int CreateMonsterSeed(int seed, int depth)
    {
        unchecked { return seed * 2029 ^ depth * 65537 ^ 0x4D4F4E; }
    }
}
