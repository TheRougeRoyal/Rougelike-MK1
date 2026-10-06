namespace Roguelike;

/// <summary>Result of a combat resolution.</summary>
public readonly record struct CombatResult(Actor Attacker, Actor Defender, int Damage, bool Killed);

/// <summary>Pure combat resolver.</summary>
public static class CombatResolver
{
    /// <summary>Resolves combat for either direction using one supplied random source.</summary>
    public static CombatResult Resolve(Actor attacker, Actor defender, Random random)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(defender);
        ArgumentNullException.ThrowIfNull(random);
        int roll = random.Next(-1, 2);
        int damage = Math.Max(1, attacker.Attack - defender.Defense + roll);
        defender.Hp = Math.Max(0, defender.Hp - damage);
        return new CombatResult(attacker, defender, damage, !defender.IsAlive);
    }
}

/// <summary>Compatibility facade for earlier callers.</summary>
public static class Combat
{
    /// <summary>Resolves a player attack with a deterministic default roll.</summary>
    public static CombatResult Attack(PlayerActor attacker, MonsterActor defender) =>
        CombatResolver.Resolve(attacker, defender, new Random(0));
    /// <summary>Resolves a monster attack with a deterministic default roll.</summary>
    public static CombatResult Attack(MonsterActor attacker, PlayerActor defender) =>
        CombatResolver.Resolve(attacker, defender, new Random(0));
}
