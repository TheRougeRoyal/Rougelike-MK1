namespace Roguelike;

/// <summary>Serializable deterministic random source used by the simulation.</summary>
public interface IRandom
{
    /// <summary>Gets or restores the complete stream state.</summary>
    ulong State { get; set; }
    /// <summary>Returns a value in [0, maxExclusive).</summary>
    int Next(int maxExclusive);
    /// <summary>Returns a value in [minInclusive, maxExclusive).</summary>
    int Next(int minInclusive, int maxExclusive);
    /// <summary>Returns a value in [0, 1).</summary>
    double NextDouble();
}

/// <summary>
/// Small serializable PCG32 stream. The state transition is PCG's LCG and its
/// output permutation is the published XSH-RR permutation; a SplitMix64 mixer
/// derives independent stream states from run seed, depth, and salt.
/// </summary>
public sealed class Pcg32 : IRandom
{
    private ulong state;
    private readonly ulong increment;

    /// <summary>Creates a stream from a deterministic seed.</summary>
    public Pcg32(ulong seed, ulong stream = 1442695040888963407UL)
    {
        increment = (stream << 1) | 1UL;
        state = 0;
        NextUInt();
        state += seed;
        NextUInt();
    }

    /// <inheritdoc />
    public ulong State { get => state; set => state = value; }

    /// <inheritdoc />
    public int Next(int maxExclusive)
    {
        if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        return (int)(NextUInt() % (uint)maxExclusive);
    }

    /// <inheritdoc />
    public int Next(int minInclusive, int maxExclusive)
    {
        if (minInclusive >= maxExclusive) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        return minInclusive + Next(maxExclusive - minInclusive);
    }

    /// <inheritdoc />
    public double NextDouble() => NextUInt() / 4294967296.0;

    private uint NextUInt()
    {
        ulong oldState = state;
        state = unchecked(oldState * 6364136223846793005UL + increment);
        uint xorshifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
        int rotation = (int)(oldState >> 59);
        return (xorshifted >> rotation) | (xorshifted << ((-rotation) & 31));
    }
}

/// <summary>Deterministic seed derivation for independent gameplay streams.</summary>
public static class RandomStreams
{
    /// <summary>Derives a stream using SplitMix64's mixing function.</summary>
    public static Pcg32 Create(int seed, int depth, ulong salt)
    {
        ulong value = unchecked((ulong)(uint)seed) ^ (unchecked((ulong)(uint)depth) * 0x9E3779B97F4A7C15UL) ^ salt;
        return new Pcg32(SplitMix64(value), SplitMix64(value + 1));
    }

    private static ulong SplitMix64(ulong value)
    {
        value = unchecked(value + 0x9E3779B97F4A7C15UL);
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
