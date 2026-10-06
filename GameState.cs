using Microsoft.Xna.Framework;

namespace Roguelike;

/// <summary>Lifecycle states for a run.</summary>
public enum GameStatus { Playing, Dead, Won }

/// <summary>Actions accepted by the turn manager.</summary>
public enum TurnAction { Wait, Heal, MoveUp, MoveDown, MoveLeft, MoveRight, Restart }

/// <summary>Deterministic headless game state.</summary>
public sealed class GameState
{
    private readonly int runSeed;
    private Random gameplayRandom;
    private readonly List<MonsterActor> monsters = new();
    private readonly TurnManager turnManager = new();

    /// <summary>Creates a new run.</summary>
    public GameState(int seed, int width = 60, int height = 34, int startingDepth = 1)
    {
        if (width < 12 || height < 10) throw new ArgumentOutOfRangeException(nameof(width));
        if (startingDepth < 1) throw new ArgumentOutOfRangeException(nameof(startingDepth));
        runSeed = seed;
        Width = width;
        Height = height;
        gameplayRandom = new Random(CreateGameplaySeed(seed));
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
    /// <summary>Gets run seed.</summary>
    public int Seed => runSeed;
    /// <summary>Gets current layout fingerprint.</summary>
    public ulong LayoutFingerprint => Dungeon.LayoutFingerprint;
    /// <summary>Gets a deterministic hash of the complete gameplay state.</summary>
    public ulong StateHash => ComputeStateHash();

    /// <summary>Processes one action.</summary>
    public bool Process(TurnAction action) => turnManager.ProcessTurn(this, action);
    /// <summary>Restarts the run, including gameplay RNG.</summary>
    public void Restart() => ResetRun();

    internal Random GameplayRandom => gameplayRandom;
    internal List<MonsterActor> MutableMonsters => monsters;

    internal void ResetRun()
    {
        Depth = 1;
        TurnNumber = 0;
        Status = GameStatus.Playing;
        gameplayRandom = new Random(CreateGameplaySeed(runSeed));
        monsters.Clear();
        SetFeedback("Explore the dungeon.", Microsoft.Xna.Framework.Color.White);
        CreateLevel(true);
    }

    internal void CreateLevel()
    {
        CreateLevel(true);
    }

    private void CreateLevel(bool resetPlayer)
    {
        Dungeon = new Dungeon(Width, Height, new Random(CreateLevelSeed(runSeed, Depth)));
        if (resetPlayer)
        {
            Player = new PlayerActor(Dungeon.PlayerStart);
        }
        else
        {
            Player.Position = Dungeon.PlayerStart;
            Player.Heal((Player.MaxHp + 3) / 4);
        }
        monsters.Clear();
        SpawnMonsters();
        Dungeon.UpdateFieldOfView(Player.Position);
    }

    internal void AdvanceDepth()
    {
        Depth++;
        CreateLevel(false);
        SetFeedback($"You descend to depth {Depth}.", Microsoft.Xna.Framework.Color.Gold);
    }

    internal void SpawnMonsters()
    {
        int targetCount = Math.Min(12, 2 + Depth * 2);
        List<Point> candidates = new();
        for (int y = 1; y < Dungeon.Height - 1; y++)
        for (int x = 1; x < Dungeon.Width - 1; x++)
        {
            Point point = new(x, y);
            if (Dungeon.IsWalkable(point) && point != Dungeon.PlayerStart &&
                point != Dungeon.StairsPosition && Distance(point, Dungeon.PlayerStart) >= 8)
                candidates.Add(point);
        }
        MonsterDefinition[] available = new MonsterDefinition[4];
        int availableCount = MonsterCatalog.CopyForDepth(Depth, available);
        for (int i = 0; i < targetCount && candidates.Count > 0; i++)
        {
            int index = gameplayRandom.Next(candidates.Count);
            Point position = candidates[index];
            candidates.RemoveAt(index);
            MonsterDefinition baseDefinition = available[gameplayRandom.Next(availableCount)];
            int scale = Math.Max(0, Depth - baseDefinition.MinDepth);
            MonsterDefinition definition = baseDefinition with
            {
                MaxHp = baseDefinition.MaxHp + scale * 2,
                Attack = baseDefinition.Attack + scale / 2,
                Defense = baseDefinition.Defense + scale / 3
            };
            monsters.Add(new MonsterActor(definition, position));
        }
    }

    internal bool IsOccupied(Point point, MonsterActor? except = null)
    {
        for (int i = 0; i < monsters.Count; i++)
            if (monsters[i].IsAlive && monsters[i] != except && monsters[i].Position == point) return true;
        return false;
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
        FeedbackTurns = 3;
    }
    internal void CleanupDead()
    {
        for (int i = monsters.Count - 1; i >= 0; i--)
            if (!monsters[i].IsAlive) monsters.RemoveAt(i);
    }
    internal ulong ComputeStateHash()
    {
        const ulong prime = 1099511628211UL;
        ulong hash = 14695981039346656037UL;
        void Mix(int value) { unchecked { hash ^= (uint)value; hash *= prime; } }
        Mix(Depth); Mix(TurnNumber); Mix((int)Status); Mix(Player.Position.X); Mix(Player.Position.Y);
        Mix(Player.Hp); Mix(Player.MaxHp); Mix(Player.Level); Mix(Player.Experience);
        for (int i = 0; i < monsters.Count; i++)
        {
            MonsterActor monster = monsters[i];
            Mix((int)monster.Definition.Type); Mix(monster.Position.X); Mix(monster.Position.Y); Mix(monster.Hp);
        }
        return hash;
    }

    private static int Distance(Point a, Point b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    /// <summary>Derives an independent generation seed for a run depth.</summary>
    public static int CreateLevelSeed(int seed, int depth)
    {
        unchecked { return seed * 397 ^ depth * 7919; }
    }
    /// <summary>Derives the independent gameplay RNG seed for a run.</summary>
    public static int CreateGameplaySeed(int seed)
    {
        unchecked { return seed * 1009 + 17; }
    }
}
