namespace Roguelike;

/// <summary>Mutable statistics for the current run, kept outside the deterministic gameplay hash.</summary>
public sealed class RunStats
{
    /// <summary>Gets completed turns survived.</summary>
    public int TurnsSurvived { get; internal set; }
    /// <summary>Gets monsters slain.</summary>
    public int MonstersSlain { get; internal set; }
    /// <summary>Gets items picked up.</summary>
    public int ItemsPickedUp { get; internal set; }
    /// <summary>Gets damage dealt.</summary>
    public int DamageDealt { get; internal set; }
    /// <summary>Gets damage taken.</summary>
    public int DamageTaken { get; internal set; }
    /// <summary>Gets the deepest level reached.</summary>
    public int MaxDepth { get; internal set; } = 1;
    /// <summary>Gets the cause of death, if the run ended.</summary>
    public string? CauseOfDeath { get; internal set; }

    internal void Reset()
    {
        TurnsSurvived = 0;
        MonstersSlain = 0;
        ItemsPickedUp = 0;
        DamageDealt = 0;
        DamageTaken = 0;
        MaxDepth = 1;
        CauseOfDeath = null;
    }
}
