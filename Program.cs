using System.Globalization;
using Roguelike.Content;

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
        (int seed, string? contentDirectory) = ParseOptions(args);
        try
        {
            _ = contentDirectory is null ? ContentDatabase.LoadDefault() : ContentDatabase.LoadDirectory(contentDirectory);
        }
        catch (ContentLoadException exception)
        {
            Console.Error.WriteLine($"Content validation failed:{Environment.NewLine}{exception.Message}");
            return;
        }
        Console.WriteLine($"Using dungeon seed: {seed}");
        Console.WriteLine($"Using gameplay seed: {GameState.CreateGameplaySeed(seed)}");

        using GameMain game = new(seed);
        game.Run();
    }

    private static (int Seed, string? ContentDirectory) ParseOptions(string[] args)
    {
        int seed = Random.Shared.Next();
        string? content = null;
        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] == "--seed" && index + 1 < args.Length &&
                int.TryParse(args[++index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                seed = parsed;
            else if (args[index] == "--content" && index + 1 < args.Length)
                content = args[++index];
            else if (index == 0 && int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                seed = parsed;
        }
        return (seed, content);
    }
}
