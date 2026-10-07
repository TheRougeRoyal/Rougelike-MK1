namespace Roguelike;

/// <summary>Supported timed player effects.</summary>
public enum StatusEffectType { Strength, Defense }

/// <summary>A timed effect with refreshable duration.</summary>
public sealed class StatusEffect
{
    /// <summary>Creates an effect.</summary>
    public StatusEffect(StatusEffectType type, int magnitude, int remainingTurns)
    {
        Type = type; Magnitude = magnitude; RemainingTurns = remainingTurns;
    }
    /// <summary>Gets the effect type.</summary>
    public StatusEffectType Type { get; }
    /// <summary>Gets the effect magnitude.</summary>
    public int Magnitude { get; internal set; }
    /// <summary>Gets remaining consumed turns.</summary>
    public int RemainingTurns { get; internal set; }
}
