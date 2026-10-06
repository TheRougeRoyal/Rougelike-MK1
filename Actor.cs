using Microsoft.Xna.Framework;

namespace Roguelike;

/// <summary>Base class for all combatants.</summary>
public abstract class Actor
{
    /// <summary>Gets the actor's name.</summary>
    public string Name { get; protected set; }
    /// <summary>Gets the actor's maximum hit points.</summary>
    public int MaxHp { get; protected set; }
    /// <summary>Gets or sets current hit points.</summary>
    public int Hp { get; set; }
    /// <summary>Gets the actor's attack stat.</summary>
    public int Attack { get; protected set; }
    /// <summary>Gets the actor's defense stat.</summary>
    public int Defense { get; protected set; }
    /// <summary>Gets or sets the actor's map position.</summary>
    public Point Position { get; set; }
    /// <summary>Gets whether the actor is alive.</summary>
    public bool IsAlive => Hp > 0;
    /// <summary>Gets the actor's display glyph.</summary>
    public virtual char Glyph => '?';

    /// <summary>Initializes an actor.</summary>
    protected Actor(string name, Point position, int maxHp, int attack, int defense)
    {
        Name = name;
        Position = position;
        MaxHp = maxHp;
        Hp = maxHp;
        Attack = attack;
        Defense = defense;
    }

}

/// <summary>The player-controlled actor.</summary>
public sealed class PlayerActor : Actor
{
    /// <summary>Creates a player with the Phase 2 starting statistics.</summary>
    public PlayerActor(Point position) : base("Player", position, 30, 5, 1) { }

    /// <summary>Gets the current level.</summary>
    public int Level { get; internal set; } = 1;
    /// <summary>Gets accumulated experience.</summary>
    public int Experience { get; internal set; }
    /// <summary>Gets the exact experience threshold for the next level.</summary>
    public int ExperienceToNextLevel => 20 * Level;
    /// <summary>Gets the player's glyph.</summary>
    public override char Glyph => '@';

    /// <summary>Heals ten points, capped at maximum hit points.</summary>
    public int Heal() => Heal(10);

    /// <summary>Heals up to the requested amount.</summary>
    public int Heal(int amount)
    {
        int before = Hp;
        Hp = Math.Min(MaxHp, Hp + Math.Max(0, amount));
        return Hp - before;
    }

    /// <summary>Adds XP and applies all earned levels.</summary>
    public bool AddExperience(int amount)
    {
        Experience += Math.Max(0, amount);
        bool leveled = false;
        while (Experience >= ExperienceToNextLevel)
        {
            Experience -= ExperienceToNextLevel;
            Level++;
            MaxHp += 5;
            Heal(10);
            Attack++;
            if (Level % 3 == 0) Defense++;
            leveled = true;
        }
        return leveled;
    }
}
