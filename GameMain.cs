using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Roguelike;

/// <summary>MonoGame application glue for the UI state machine and headless game state.</summary>
public sealed class GameMain : Game
{
    private const int MapWidth = 60;
    private const int MapHeight = 34;
    private const int TileSize = 16;
    private const int HudHeight = 120;
    private readonly GraphicsDeviceManager graphics;
    private readonly int seed;
    private KeyboardState previousKeyboardState;
    private SpriteBatch? spriteBatch;
    private GameRenderer? renderer;
    private GameState? state;
    private readonly ScreenStateMachine screens = new();
    private readonly InputMapper inputMapper = new();
    private IReadOnlySet<string> previousInputKeys = new HashSet<string>();
    private int inventoryCursor;
    private bool restartPrompt;

    /// <summary>Initializes the game with a deterministic seed.</summary>
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
        Window.Title = $"Roguelike - Seed {seed}";
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
        if (state is null) return;
        KeyboardState current = Keyboard.GetState();
        bool pressed(Keys key) => current.IsKeyDown(key) && !previousKeyboardState.IsKeyDown(key);
        if (screens.Screen == ScreenKind.Title)
        {
            if (pressed(Keys.Enter)) screens.Start();
            else if (pressed(Keys.H)) screens.ShowHelp();
        }
        else if (screens.Screen == ScreenKind.Help)
        {
            if (pressed(Keys.Escape) || pressed(Keys.Enter)) screens.Title();
        }
        else if (screens.Screen == ScreenKind.Paused)
        {
            if (pressed(Keys.Escape)) screens.Resume();
            else if (pressed(Keys.Up) || pressed(Keys.W)) screens.MoveMenu(-1);
            else if (pressed(Keys.Down) || pressed(Keys.S)) screens.MoveMenu(1);
            else if (pressed(Keys.Enter))
            {
                switch (screens.MenuIndex)
                {
                    case 0: screens.Resume(); break;
                    case 1: Restart(); break;
                    case 2: screens.ShowHelp(); break;
                    case 3: Exit(); break;
                }
            }
        }
        else if (screens.Screen == ScreenKind.GameOver)
        {
            if (pressed(Keys.R)) Restart();
            else if (pressed(Keys.Escape)) screens.Title();
        }
        else
        {
            UpdatePlaying(current, gameTime);
            if (state.Status == GameStatus.Dead) screens.GameOver();
        }
        previousKeyboardState = current;
        previousInputKeys = GetKeyNames(current);
        base.Update(gameTime);
    }

    private void UpdatePlaying(KeyboardState current, GameTime gameTime)
    {
        if (restartPrompt)
        {
            if (WasPressed(current, Keys.Y)) { restartPrompt = false; Restart(); }
            else if (WasPressed(current, Keys.N) || WasPressed(current, Keys.Escape)) restartPrompt = false;
            return;
        }
        if (WasPressed(current, Keys.Escape))
        {
            if (screens.Overlay != UiOverlay.None) screens.CloseOverlay();
            else screens.Pause();
            return;
        }
        if (WasPressed(current, Keys.R) && screens.Overlay == UiOverlay.None)
        {
            restartPrompt = true;
            return;
        }
        if (state!.Status == GameStatus.Dead) return;
        if (WasPressed(current, Keys.I))
        {
            screens.ToggleInventory();
            inventoryCursor = 0;
            return;
        }
        if (screens.Overlay == UiOverlay.Inventory)
        {
            if (WasPressed(current, Keys.Up) || WasPressed(current, Keys.W))
                inventoryCursor = Math.Max(0, inventoryCursor - 1);
            else if (WasPressed(current, Keys.Down) || WasPressed(current, Keys.S))
                inventoryCursor = Math.Min(Math.Max(0, state.Player.Inventory.Items.Count - 1), inventoryCursor + 1);
            else if (WasPressed(current, Keys.Enter) && inventoryCursor < state.Player.Inventory.Items.Count)
            {
                ItemType type = state.Player.Inventory.Items[inventoryCursor].Definition.Type;
                state.Process(type == ItemType.Consumable ? GameAction.UseItem(inventoryCursor) : GameAction.EquipItem(inventoryCursor));
            }
            else if (WasPressed(current, Keys.D) && inventoryCursor < state.Player.Inventory.Items.Count)
                state.Process(GameAction.DropItem(inventoryCursor));
            else if (WasPressed(current, Keys.D1)) state.Process(GameAction.UnequipSlot(0));
            else if (WasPressed(current, Keys.D2)) state.Process(GameAction.UnequipSlot(1));
            inventoryCursor = Math.Clamp(inventoryCursor, 0, Math.Max(0, state.Player.Inventory.Items.Count - 1));
            if (state.Status == GameStatus.Dead) screens.CloseOverlay();
            return;
        }
        IReadOnlySet<string> keyNames = GetKeyNames(current);
        UiCommand command = inputMapper.Map(previousInputKeys, keyNames, ScreenKind.Playing,
            UiOverlay.None, gameTime.ElapsedGameTime);
        if (command.Kind == UiCommandKind.Move)
            state.Process(GameAction.Move(command.Direction));
        else if (command.Kind == UiCommandKind.Wait)
            state.Process(GameAction.Wait);
    }

    /// <inheritdoc />
    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);
        if (renderer is not null && state is not null)
            renderer.Draw(state.Dungeon, state.Player, state.Monsters, state.Depth, state.Player.Level,
                state.Player.Experience, state.Message, state.FeedbackTint, state.FeedbackActor, state.FloorItems,
                screens.Overlay == UiOverlay.Inventory, inventoryCursor, state.MessageLog, state.TurnNumber,
                screens.Screen, state.RunStats);
        base.Draw(gameTime);
    }

    private void Restart()
    {
        state!.Restart();
        screens.RestartRun();
        inventoryCursor = 0;
    }
    private bool WasPressed(KeyboardState current, Keys key) =>
        current.IsKeyDown(key) && !previousKeyboardState.IsKeyDown(key);
    private static IReadOnlySet<string> GetKeyNames(KeyboardState keyboard)
    {
        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
        Keys[] candidates = { Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.W, Keys.A, Keys.S, Keys.D,
            Keys.NumPad8, Keys.NumPad2, Keys.NumPad4, Keys.NumPad6, Keys.Space, Keys.Enter, Keys.Escape,
            Keys.I, Keys.R, Keys.H, Keys.D1, Keys.D2, Keys.Y, Keys.N };
        foreach (Keys key in candidates)
            if (keyboard.IsKeyDown(key))
                keys.Add(key switch
                {
                    Keys.NumPad8 => "8", Keys.NumPad2 => "2", Keys.NumPad4 => "4", Keys.NumPad6 => "6",
                    Keys.D1 => "1", Keys.D2 => "2", _ => key.ToString()
                });
        return keys;
    }
}
