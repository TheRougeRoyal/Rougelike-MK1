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
    private readonly int seed;
    private KeyboardState previousKeyboardState;
    private SpriteBatch? spriteBatch;
    private GameRenderer? renderer;
    private GameState? state;
    private bool inventoryOpen;
    private int inventoryCursor;

    /// <summary>
    /// Initializes the game with a deterministic random seed.
    /// </summary>
    /// <param name="seed">The seed used for all generated levels.</param>
    public GameMain(int seed)
    {
        this.seed = seed;
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
        state = new GameState(seed, MapWidth, MapHeight);
        Window.Title = $"Roguelike - Depth {state.Depth} - Seed {seed}";
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
        if (WasPressed(currentKeyboardState, Keys.I))
        {
            inventoryOpen = !inventoryOpen;
        }
        else if (inventoryOpen && state is not null)
        {
            if (WasPressed(currentKeyboardState, Keys.Escape))
                inventoryOpen = false;
            else if (WasPressed(currentKeyboardState, Keys.Up) || WasPressed(currentKeyboardState, Keys.W))
                inventoryCursor = Math.Max(0, inventoryCursor - 1);
            else if (WasPressed(currentKeyboardState, Keys.Down) || WasPressed(currentKeyboardState, Keys.S))
                inventoryCursor = Math.Min(Math.Max(0, state.Player.Inventory.Items.Count - 1), inventoryCursor + 1);
            else if (WasPressed(currentKeyboardState, Keys.Enter) && inventoryCursor < state.Player.Inventory.Items.Count)
            {
                ItemType type = state.Player.Inventory.Items[inventoryCursor].Definition.Type;
                state.Process(type == ItemType.Consumable
                    ? GameAction.UseItem(inventoryCursor) : GameAction.EquipItem(inventoryCursor));
            }
            else if (WasPressed(currentKeyboardState, Keys.D) && inventoryCursor < state.Player.Inventory.Items.Count)
                state.Process(GameAction.DropItem(inventoryCursor));
            else if (WasPressed(currentKeyboardState, Keys.U))
                state.Process(GameAction.UnequipSlot(inventoryCursor == 0 ? 0 : 1));
            inventoryCursor = Math.Clamp(inventoryCursor, 0, Math.Max(0, state.Player.Inventory.Items.Count - 1));
        }
        else if (WasPressed(currentKeyboardState, Keys.Escape))
        {
            Exit();
        }
        else if (state is not null && WasPressed(currentKeyboardState, Keys.R))
        {
            state.Process(TurnAction.Restart);
        }
        else if (TryGetMovement(currentKeyboardState, out Point direction))
        {
            state?.Process(direction switch
            {
                { X: 0, Y: -1 } => TurnAction.MoveUp,
                { X: 0, Y: 1 } => TurnAction.MoveDown,
                { X: -1, Y: 0 } => TurnAction.MoveLeft,
                _ => TurnAction.MoveRight
            });
        }
        else if (WasPressed(currentKeyboardState, Keys.Space))
        {
            state?.Process(TurnAction.Wait);
        }

        if (state is not null)
        {
            if (inventoryOpen && state.Player.Inventory.Items.Count > 0)
            {
                ItemInstance item = state.Player.Inventory.Items[Math.Clamp(inventoryCursor, 0, state.Player.Inventory.Items.Count - 1)];
                Window.Title = $"{item.Definition.Name} - {item.Definition.Description} x{item.Count}";
            }
            else
                Window.Title = $"Seed {seed}  Depth {state.Depth}  HP {state.Player.Hp}/{state.Player.MaxHp}  " +
                    $"Level {state.Player.Level} XP {state.Player.Experience}/{state.Player.ExperienceToNextLevel} - {state.Message}";
        }
        previousKeyboardState = currentKeyboardState;
        base.Update(gameTime);
    }

    /// <inheritdoc />
    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);
        if (renderer is not null && state is not null)
        {
            renderer.Draw(state.Dungeon, state.Player, state.Monsters, state.Depth,
                state.Player.Level, state.Player.Experience, state.Message, state.FeedbackTint,
                state.FeedbackActor, state.FloorItems, inventoryOpen, inventoryCursor);
        }

        base.Draw(gameTime);
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
