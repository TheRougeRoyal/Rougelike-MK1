using Microsoft.Xna.Framework;

namespace Roguelike;

// ponytail: MonsterType deleted as it is replaced by string IDs in ContentDatabase.

/// <summary>Monster decision model.</summary>
public enum MonsterBehavior { Idle, Chase, Ranged, Slow }

/// <summary>Immutable monster definition.</summary>
public sealed record MonsterDefinition(
    string Id, string Name, char Glyph, int MaxHp, int Attack, int Defense,
    int SightRadius, MonsterBehavior Behavior, Color Color, int MinDepth, int XpValue,
    IReadOnlyDictionary<string, int> Params)
{
    public MonsterDefinition(string id, string name, char glyph, int maxHp, int attack, int defense,
        int sightRadius, MonsterBehavior behavior, Color color, int minDepth, int xpValue)
        : this(id, name, glyph, maxHp, attack, defense, sightRadius, behavior, color, minDepth, xpValue,
            new Dictionary<string, int>())
    {
    }
}

// ponytail: deleted MonsterCatalog as it is now replaced by ContentDatabase.

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
