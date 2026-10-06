using Microsoft.Xna.Framework;

namespace Roguelike;

/// <summary>Monster kinds in the game.</summary>
public enum MonsterType { Rat, Goblin, Archer, Brute }

/// <summary>Monster decision model.</summary>
public enum MonsterBehavior { Idle, Chase, Ranged, Slow }

/// <summary>Immutable monster definition.</summary>
public sealed record MonsterDefinition(
    MonsterType Type, string Name, char Glyph, int MaxHp, int Attack, int Defense,
    int SightRadius, MonsterBehavior Behavior, Color Color, int MinDepth, int XpValue)
{
}

/// <summary>Built-in monster catalog.</summary>
public static class MonsterCatalog
{
    private static readonly IReadOnlyList<MonsterDefinition> definitions = Array.AsReadOnly(new MonsterDefinition[]
    {
        new(MonsterType.Rat, "Rat", 'r', 6, 3, 0, 6, MonsterBehavior.Chase, Color.LightGray, 1, 8),
        new(MonsterType.Goblin, "Goblin", 'g', 10, 4, 1, 7, MonsterBehavior.Chase, Color.LimeGreen, 1, 12),
        new(MonsterType.Archer, "Archer", 'a', 8, 4, 2, 9, MonsterBehavior.Ranged, Color.CornflowerBlue, 2, 18),
        new(MonsterType.Brute, "Brute", 'B', 24, 7, 3, 3, MonsterBehavior.Slow, Color.OrangeRed, 3, 30)
    });

    /// <summary>Gets all definitions.</summary>
    public static IReadOnlyList<MonsterDefinition> All => definitions;
    /// <summary>Gets a definition by kind.</summary>
    public static MonsterDefinition Get(MonsterType type)
    {
        for (int i = 0; i < definitions.Count; i++)
            if (definitions[i].Type == type) return definitions[i];
        throw new ArgumentOutOfRangeException(nameof(type));
    }

    /// <summary>Gets definitions available at a depth.</summary>
    public static int CopyForDepth(int depth, Span<MonsterDefinition> destination)
    {
        int count = 0;
        for (int i = 0; i < definitions.Count && count < destination.Length; i++)
            if (definitions[i].MinDepth <= depth) destination[count++] = definitions[i];
        return count;
    }
}

/// <summary>An enemy actor.</summary>
public sealed class MonsterActor : Actor
{
    /// <summary>Creates a monster.</summary>
    public MonsterActor(MonsterDefinition definition, Point position)
        : base(definition.Name, position, definition.MaxHp, definition.Attack, definition.Defense)
    {
        Definition = definition;
    }

    /// <summary>Gets the immutable definition.</summary>
    public MonsterDefinition Definition { get; }
    /// <summary>Gets or sets turns remaining in alert memory.</summary>
    public int AlertTurns { get; internal set; }
    /// <summary>Gets the last player position seen by this monster.</summary>
    public Point? LastKnownPlayerPosition { get; internal set; }
    /// <summary>Gets the glyph.</summary>
    public override char Glyph => Definition.Glyph;
}
