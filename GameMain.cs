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
    private SpriteBatch? spriteBatch;
    private GameRenderer? renderer;
    private GameState? state;
    private readonly ScreenStateMachine screens = new();
    private readonly InputMapper inputMapper = new();
    private IReadOnlySet<string> previousInputKeys = new HashSet<string>();
    private int inventoryCursor;

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
        IReadOnlySet<string> currentKeys = GetKeyNames(current);
        UiCommand command = inputMapper.Map(previousInputKeys, currentKeys, screens.Screen, screens.Overlay,
            gameTime.ElapsedGameTime);
        Execute(command);
        previousInputKeys = currentKeys;
        base.Update(gameTime);
    }

    private void Execute(UiCommand command)
    {
        if (state is null) return;
        switch (command.Kind)
        {
            case UiCommandKind.Quit:
                Exit();
                break;
            case UiCommandKind.Start:
                if (state.Status == GameStatus.Dead) state.Restart();
                screens.Start();
                break;
            case UiCommandKind.Help:
                screens.ShowHelp();
                break;
            case UiCommandKind.CloseHelp:
                screens.CloseHelp();
                break;
            case UiCommandKind.Resume:
                screens.Resume();
                break;
            case UiCommandKind.MenuUp:
                if (screens.Overlay == UiOverlay.Inventory) inventoryCursor = Math.Max(0, inventoryCursor - 1);
                else screens.MoveMenu(-1);
                break;
            case UiCommandKind.MenuDown:
                if (screens.Overlay == UiOverlay.Inventory)
                    inventoryCursor = Math.Min(Math.Max(0, state.Player.Inventory.Items.Count - 1), inventoryCursor + 1);
                else screens.MoveMenu(1);
                break;
            case UiCommandKind.Accept:
                ExecuteAccept();
                break;
            case UiCommandKind.Cancel:
                screens.CloseOverlay();
                break;
            case UiCommandKind.Inventory:
                if (screens.ToggleInventory()) inventoryCursor = 0;
                break;
            case UiCommandKind.Drop:
                if (inventoryCursor < state.Player.Inventory.Items.Count)
                    state.Process(GameAction.DropItem(inventoryCursor));
                break;
            case UiCommandKind.UnequipWeapon:
                state.Process(GameAction.UnequipSlot(0));
                break;
            case UiCommandKind.UnequipArmor:
                state.Process(GameAction.UnequipSlot(1));
                break;
            case UiCommandKind.Pause:
                screens.Pause();
                break;
            case UiCommandKind.Restart:
                if (screens.Screen == ScreenKind.Playing) screens.RequestRestart();
                else if (screens.Screen == ScreenKind.GameOver || screens.Screen == ScreenKind.Paused) RestartImmediate();
                break;
            case UiCommandKind.ConfirmRestart:
                if (screens.ConfirmRestart()) RestartImmediate();
                break;
            case UiCommandKind.CancelRestart:
                screens.CancelRestart();
                break;
            case UiCommandKind.Move:
                state.Process(GameAction.Move(command.Direction));
                break;
            case UiCommandKind.Wait:
                state.Process(GameAction.Wait);
                break;
        }
        if (state.Status == GameStatus.Dead && screens.Screen == ScreenKind.Playing)
        {
            screens.GameOver();
            inventoryCursor = 0;
        }
    }

    private void ExecuteAccept()
    {
        if (state is null) return;
        if (screens.Screen == ScreenKind.Paused)
        {
            switch (screens.MenuIndex)
            {
                case 0: screens.Resume(); break;
                case 1: RestartImmediate(); break;
                case 2: screens.ShowHelp(); break;
                case 3: Exit(); break;
            }
        }
        else if (screens.Overlay == UiOverlay.Inventory &&
                 inventoryCursor < state.Player.Inventory.Items.Count)
        {
            ItemType type = state.Player.Inventory.Items[inventoryCursor].Definition.Type;
            state.Process(type == ItemType.Consumable
                ? GameAction.UseItem(inventoryCursor)
                : GameAction.EquipItem(inventoryCursor));
        }
    }

    private void RestartImmediate()
    {
        state!.Restart();
        screens.RestartRun();
        inventoryCursor = 0;
    }

    /// <inheritdoc />
    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);
        if (renderer is not null && state is not null)
            renderer.Draw(state.Dungeon, state.Player, state.Monsters, state.Depth, state.Player.Level,
                state.Player.Experience, state.Message, state.FeedbackTint, state.FeedbackActor, state.FloorItems,
                screens.Overlay == UiOverlay.Inventory, inventoryCursor, state.MessageLog, state.TurnNumber,
                screens.Screen, state.RunStats, screens.Overlay == UiOverlay.RestartConfirmation);
        base.Draw(gameTime);
    }

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
                    Keys.D1 => "D1", Keys.D2 => "D2",
                    Keys.NumPad8 => "NumPad8", Keys.NumPad2 => "NumPad2",
                    Keys.NumPad4 => "NumPad4", Keys.NumPad6 => "NumPad6",
                    _ => key.ToString()
                });
        return keys;
    }
}
