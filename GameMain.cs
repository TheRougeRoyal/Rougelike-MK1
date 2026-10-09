using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Roguelike.Content;
using Roguelike.Persistence;


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
    private readonly ContentDatabase content;
    private SpriteBatch? spriteBatch;
    private GameRenderer? renderer;
    private GameSession? session;
    private readonly InputMapper inputMapper = new();
    private IReadOnlySet<string> previousInputKeys = new HashSet<string>();

    /// <summary>Initializes the game with a deterministic seed.</summary>
    private readonly string saveDirectory;
    public GameMain(int seed, ContentDatabase content, string? saveDirectory = null)
    {
        this.seed = seed;
        this.content = content;
        this.saveDirectory = saveDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Roguelike");
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
        session = new GameSession(seed, MapWidth, MapHeight, content, new Persistence.FileSaveStore(saveDirectory),
            new Runs.FileRunHistoryStore(saveDirectory));
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
        if (session is null) return;
        KeyboardState current = Keyboard.GetState();
        IReadOnlySet<string> currentKeys = GetKeyNames(current);
        UiCommand command = inputMapper.Map(previousInputKeys, currentKeys, session.Screens.Screen, session.Screens.Overlay,
            gameTime.ElapsedGameTime);
        session.Execute(command);
        if (session.QuitRequested) Exit();
        previousInputKeys = currentKeys;
        base.Update(gameTime);
    }

    /// <inheritdoc />
    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);
        if (renderer is not null && session is not null)
        {
            GameState state = session.State;
            var historyRecords = session.Screens.Screen == ScreenKind.RunHistory
                ? new Runs.FileRunHistoryStore(saveDirectory).Read()
                : Array.Empty<Runs.RunRecord>();
            
            int? savedSeed = null;
            if (session.Screens.Screen == ScreenKind.Title && session.HasValidSave)
            {
                LoadResult result = Persistence.GameStatePersistence.Load(
                    new Persistence.FileSaveStore(saveDirectory), 
                    content.ContentHash, content);
                if (result.State is not null)
                    savedSeed = result.State.Seed;
            }
            
            renderer.Draw(state.Dungeon, state.Player, state.Monsters, state.Depth, state.Player.Level,
                state.Player.Experience, state.Message, state.FeedbackTint, state.FeedbackActor, state.FloorItems,
                session.Screens.Overlay == UiOverlay.Inventory, session.InventoryCursor, state.MessageLog, state.TurnNumber,
                session.Screens.Screen, state.RunStats, session.Screens.Overlay == UiOverlay.RestartConfirmation,
                session.Screens.TitleMenuIndex, session.SeedInput, session.HistoryScrollOffset, historyRecords, savedSeed,
                session.Screens.MenuIndex, session.HasValidSave, session.RankPosition, session.TotalRuns, state.Seed,
                session.Screens.Overlay == UiOverlay.Examine, session.ExamineCursor, 
                session.Screens.Overlay == UiOverlay.Examine ? session.GetExamineDescription(session.ExamineCursor) : "");
        }
        base.Draw(gameTime);
    }

    private static IReadOnlySet<string> GetKeyNames(KeyboardState keyboard)
    {
        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
        Keys[] candidates = { Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.W, Keys.A, Keys.S, Keys.D,
            Keys.NumPad8, Keys.NumPad2, Keys.NumPad4, Keys.NumPad6, Keys.Space, Keys.Enter, Keys.Escape,
            Keys.I, Keys.R, Keys.H, Keys.X, Keys.D0, Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9,
            Keys.NumPad0, Keys.NumPad1, Keys.NumPad2, Keys.NumPad3, Keys.NumPad4, Keys.NumPad5, Keys.NumPad6, Keys.NumPad7, Keys.NumPad8, Keys.NumPad9,
            Keys.Y, Keys.N, Keys.Back };
        foreach (Keys key in candidates)
            if (keyboard.IsKeyDown(key))
                keys.Add(key switch
                {
                    Keys.D0 => "0", Keys.D1 => "1", Keys.D2 => "2", Keys.D3 => "3", Keys.D4 => "4",
                    Keys.D5 => "5", Keys.D6 => "6", Keys.D7 => "7", Keys.D8 => "8", Keys.D9 => "9",
                    Keys.NumPad0 => "NumPad0", Keys.NumPad1 => "NumPad1", Keys.NumPad2 => "NumPad2", Keys.NumPad3 => "NumPad3",
                    Keys.NumPad4 => "NumPad4", Keys.NumPad5 => "NumPad5", Keys.NumPad6 => "NumPad6", Keys.NumPad7 => "NumPad7",
                    Keys.NumPad8 => "NumPad8", Keys.NumPad9 => "NumPad9",
                    Keys.Back => "BackSpace",
                    _ => key.ToString()
                });
        return keys;
    }
}
