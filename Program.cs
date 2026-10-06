using System.Globalization;

namespace Roguelike;

/// <summary>
/// Starts the roguelike application and selects its dungeon seed.
/// </summary>
public static class Program
{
    /// <summary>
    /// Creates and runs the game.
    /// </summary>
    /// <param name="args">Optional first argument containing an integer seed.</param>
    public static void Main(string[] args)
    {
        int seed = ParseSeed(args);
        Console.WriteLine($"Using dungeon seed: {seed}");
        Console.WriteLine($"Using gameplay seed: {GameState.CreateGameplaySeed(seed)}");

        using GameMain game = new(seed);
        game.Run();
    }

    private static int ParseSeed(string[] args)
    {
        if (args.Length > 0 && int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
        {
            return seed;
        }

        return Random.Shared.Next();
    }
}
