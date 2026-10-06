using Microsoft.Xna.Framework;

namespace Roguelike;

/// <summary>
/// Base class for anything that occupies one dungeon tile.
/// </summary>
public abstract class Actor
{
    /// <summary>
    /// Gets or sets the actor's tile position.
    /// </summary>
    public Point Position { get; set; }

    /// <summary>
    /// Initializes an actor at a tile position.
    /// </summary>
    /// <param name="position">The initial tile position.</param>
    protected Actor(Point position)
    {
        Position = position;
    }
}

/// <summary>
/// The player-controlled actor.
/// </summary>
public sealed class PlayerActor : Actor
{
    /// <summary>
    /// Initializes the player at a tile position.
    /// </summary>
    /// <param name="position">The player's initial tile position.</param>
    public PlayerActor(Point position)
        : base(position)
    {
    }
}
