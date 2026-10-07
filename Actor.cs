using Microsoft.Xna.Framework;
using Roguelike.Content;


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
    /// <summary>Gets total attack including actor-specific bonuses.</summary>
    public virtual int TotalAttack => Attack;
    /// <summary>Gets total defense including actor-specific bonuses.</summary>
    public virtual int TotalDefense => Defense;
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
    private readonly record struct LevelUpBonus(int MaxHp, int Attack, int Defense, int DefenseEvery, int Heal);
    /// <summary>Creates a player with the Phase 2 starting statistics.</summary>
    private readonly int experiencePerLevel;
    private readonly LevelUpBonus levelUp;

    public PlayerActor(Point position, ContentDatabase content) : base("Player", position, content.Balance.StartingHp, content.Balance.StartingAttack, content.Balance.StartingDefense)
    {
        experiencePerLevel = content.Balance.XpPerLevel;
        levelUp = new LevelUpBonus(content.Balance.LevelMaxHpBonus, content.Balance.LevelAttackBonus,
            content.Balance.LevelDefenseBonus, content.Balance.LevelDefenseEvery, content.Balance.LevelHealAmount);
        Inventory = new Inventory();
        foreach (string id in content.Balance.StartingLoadout)
            Inventory.TryAdd(new ItemInstance(content.CreateDefinition(id)));
    }

    /// <summary>Gets the current level.</summary>
    public int Level { get; internal set; } = 1;
    /// <summary>Gets accumulated experience.</summary>
    public int Experience { get; internal set; }
    /// <summary>Gets the exact experience threshold for the next level.</summary>
    public int ExperienceToNextLevel => experiencePerLevel * Level;
    /// <summary>Gets the player's glyph.</summary>
    public override char Glyph => '@';
    /// <summary>Gets the player's inventory.</summary>
    public Inventory Inventory { get; }
    /// <summary>Gets active timed effects.</summary>
    public List<StatusEffect> Effects { get; } = new();
    /// <summary>Gets the equipped weapon, if any.</summary>
    public ItemInstance? EquippedWeapon { get; internal set; }
    /// <summary>Gets the equipped armor, if any.</summary>
    public ItemInstance? EquippedArmor { get; internal set; }
    /// <inheritdoc />
    public override int TotalAttack => Attack +
        (EquippedWeapon?.Definition.AttackBonus ?? 0) +
        Effects.Where(effect => effect.Type == StatusEffectType.Strength)
            .Sum(effect => effect.Magnitude);
    /// <inheritdoc />
    public override int TotalDefense => Defense + (EquippedArmor?.Definition.DefenseBonus ?? 0) +
        Effects.Where(effect => effect.Type == StatusEffectType.Defense).Sum(effect => effect.Magnitude);

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
            MaxHp += levelUp.MaxHp;
            Heal(levelUp.Heal);
            Attack += levelUp.Attack;
            if (Level % levelUp.DefenseEvery == 0) Defense += levelUp.Defense;
            leveled = true;
        }
        return leveled;
    }

    internal void Restore(int level, int experience, int hp, int maxHp, int attack, int defense)
    {
        Level = level;
        Experience = experience;
        Hp = hp;
        MaxHp = maxHp;
        Attack = attack;
        Defense = defense;
    }
}
