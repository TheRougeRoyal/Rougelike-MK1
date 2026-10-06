using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Roguelike;

/// <summary>
/// MonoGame application glue for input, turns, levels, and rendering.
/// </summary>
public sealed class GameMain : Game
{
    private const int MapWidth = 60;
    private const int MapHeight = 34;
    private const int TileSize = 16;
    private const int HudHeight = 40;

    private readonly GraphicsDeviceManager graphics;
    private readonly Random gameplayRandom;
    private readonly int seed;
    private KeyboardState previousKeyboardState;
    private SpriteBatch? spriteBatch;
    private GameRenderer? renderer;
    private Dungeon? dungeon;
    private PlayerActor? player;
    private int depth = 1;

    /// <summary>
    /// Initializes the game with a deterministic random seed.
    /// </summary>
    /// <param name="seed">The seed used for all generated levels.</param>
    public GameMain(int seed)
    {
        this.seed = seed;
        gameplayRandom = new Random(CreateGameplaySeed(seed));
        graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = MapWidth * TileSize,
            PreferredBackBufferHeight = MapHeight * TileSize + HudHeight
        };
        IsMouseVisible = false;
        Window.AllowUserResizing = false;
    }

    /// <inheritdoc />
    protected override void Initialize()
    {
        dungeon = CreateLevel();
        player = new PlayerActor(dungeon.PlayerStart);
        dungeon.UpdateFieldOfView(player.Position);
        Window.Title = $"Roguelike - Depth {depth} - Seed {seed}";
        base.Initialize();
    }

    /// <inheritdoc />
    protected override void LoadContent()
    {
        spriteBatch = new SpriteBatch(GraphicsDevice);
        renderer = new GameRenderer(GraphicsDevice, spriteBatch, TileSize, HudHeight);
    }

    /// <inheritdoc />
    protected override void Update(GameTime gameTime)
    {
        KeyboardState currentKeyboardState = Keyboard.GetState();
        if (WasPressed(currentKeyboardState, Keys.Escape))
        {
            Exit();
        }
        else if (TryGetMovement(currentKeyboardState, out Point direction))
        {
            PerformMove(direction);
        }
        else if (WasPressed(currentKeyboardState, Keys.Space))
        {
            PerformTurn();
        }

        previousKeyboardState = currentKeyboardState;
        base.Update(gameTime);
    }

    /// <inheritdoc />
    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);
        if (renderer is not null && dungeon is not null && player is not null)
        {
            renderer.Draw(dungeon, player, depth);
        }

        base.Draw(gameTime);
    }

    private void PerformMove(Point direction)
    {
        if (dungeon is null || player is null)
        {
            return;
        }

        Point destination = player.Position + direction;
        if (!dungeon.IsWalkable(destination))
        {
            return;
        }

        player.Position = destination;
        PerformTurn();

        if (player.Position == dungeon.StairsPosition)
        {
            depth++;
            dungeon = CreateLevel();
            player.Position = dungeon.PlayerStart;
            Window.Title = $"Roguelike - Depth {depth} - Seed {seed}";
            dungeon.UpdateFieldOfView(player.Position);
        }
    }

    private Dungeon CreateLevel()
    {
        Dungeon level = new(MapWidth, MapHeight, new Random(CreateLevelSeed(seed, depth)));
        Console.WriteLine($"Generated depth {depth} layout: {level.LayoutFingerprint:X16}");
        return level;
    }

    private static int CreateLevelSeed(int runSeed, int levelDepth)
    {
        unchecked
        {
            return runSeed * 397 ^ levelDepth * 7919;
        }
    }

    private static int CreateGameplaySeed(int runSeed)
    {
        unchecked
        {
            return runSeed * 1009 + 17;
        }
    }

    private void PerformTurn()
    {
        if (dungeon is not null && player is not null)
        {
            dungeon.UpdateFieldOfView(player.Position);
        }
    }

    private bool WasPressed(KeyboardState current, Keys key) =>
        current.IsKeyDown(key) && !previousKeyboardState.IsKeyDown(key);

    private bool TryGetMovement(KeyboardState current, out Point direction)
    {
        if (WasPressed(current, Keys.Up) || WasPressed(current, Keys.W))
        {
            direction = new Point(0, -1);
            return true;
        }

        if (WasPressed(current, Keys.Down) || WasPressed(current, Keys.S))
        {
            direction = new Point(0, 1);
            return true;
        }

        if (WasPressed(current, Keys.Left) || WasPressed(current, Keys.A))
        {
            direction = new Point(-1, 0);
            return true;
        }

        if (WasPressed(current, Keys.Right) || WasPressed(current, Keys.D))
        {
            direction = new Point(1, 0);
            return true;
        }

        direction = Point.Zero;
        return false;
    }
}
