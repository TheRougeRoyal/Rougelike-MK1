using Microsoft.Xna.Framework;

namespace Roguelike;

/// <summary>Small deterministic runner useful for tests and CI.</summary>
public sealed class HeadlessSimulation
{
    /// <summary>Runs actions and returns the resulting state.</summary>
    public GameState Run(int seed, IEnumerable<GameAction> actions, int width = 60, int height = 34)
    {
        GameState state = new(seed, width, height);
        foreach (GameAction action in actions)
        {
            state.Process(action);
            if (state.Status != GameStatus.Playing) break;
        }
        return state;
    }

    /// <summary>Runs a deterministic wait-only simulation for a fixed number of turns.</summary>
    public GameState RunWaits(int seed, int turns, int width = 60, int height = 34) =>
        Run(seed, Enumerable.Repeat(GameAction.Wait, Math.Max(0, turns)), width, height);

    /// <summary>Runs actions and returns a deterministic state hash.</summary>
    public ulong RunHash(int seed, IEnumerable<GameAction> actions, int width = 60, int height = 34) =>
        Run(seed, actions, width, height).ComputeStateHash();

    /// <summary>Gets a deterministic hash of a state.</summary>
    public static ulong StateHash(GameState state) => state.ComputeStateHash();
}
