namespace Roguelike;

/// <summary>
/// Result of one resolved attack.
/// </summary>
public readonly record struct CombatResult(Actor Attacker, Actor Defender, int Damage, bool Killed);

/// <summary>
/// Pure combat resolution using a caller-provided random source.
/// </summary>
public static class CombatResolver
{
    /// <summary>
    /// Resolves and applies one attack.
    /// </summary>
    /// <param name="attacker">The attacking actor.</param>
    /// <param name="defender">The defending actor.</param>
    /// <param name="random">The gameplay random source.</param>
    public static CombatResult Resolve(Actor attacker, Actor defender, Random random)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(defender);
        ArgumentNullException.ThrowIfNull(random);

        int roll = random.Next(-1, 2);
        int damage = Math.Max(1, attacker.TotalAttack - defender.TotalDefense + roll);
        defender.Hp = Math.Max(0, defender.Hp - damage);
        return new CombatResult(attacker, defender, damage, !defender.IsAlive);
    }
}
