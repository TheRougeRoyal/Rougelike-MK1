using Microsoft.Xna.Framework;
using Roguelike.Content;
using Roguelike.Persistence;
using Roguelike.Runs;


namespace Roguelike;

/// <summary>Headless UI/session coordinator shared by the game loop and tests.</summary>
public sealed class GameSession
{
    private readonly ContentDatabase content;
    private readonly ISaveStore saveStore;
    private readonly IRunHistoryStore historyStore;
    private bool historyRecorded;
    /// <summary>Creates a session with a fresh run.</summary>
    public GameSession(int seed, int width = 60, int height = 34, ContentDatabase? content = null,
        ISaveStore? saveStore = null, IRunHistoryStore? historyStore = null, string? saveDirectory = null)
    {
        this.content = content ?? ContentDatabase.LoadDefault();
        this.saveStore = saveStore ?? (saveDirectory is null
            ? new MemorySaveStore()
            : new FileSaveStore(saveDirectory));
        this.historyStore = historyStore ?? new MemoryRunHistoryStore();
        State = new GameState(seed, width, height, 1, this.content);
        Screens = new ScreenStateMachine();
    }
    /// <summary>Gets the current game state.</summary>
    public GameState State { get; private set; }
    /// <summary>Gets the screen state machine.</summary>
    public ScreenStateMachine Screens { get; }
    /// <summary>Gets the inventory cursor.</summary>
    public int InventoryCursor { get; private set; }
    /// <summary>Gets whether the UI requested application exit.</summary>
    public bool QuitRequested { get; private set; }
    public bool HasValidSave
    {
        get
        {
            return GameStatePersistence.Load(saveStore, content.ContentHash, content).IsSuccess;
        }
    }

    /// <summary>Executes one UI command without any window dependency.</summary>
    public void Execute(UiCommand command)
    {
        if (State.Status == GameStatus.Dead && Screens.Screen == ScreenKind.Playing)
        {
            saveStore.Delete();
            RecordHistory("dead");
            Screens.GameOver();
        }
        switch (command.Kind)
        {
            case UiCommandKind.Quit: QuitRequested = true; break;
            case UiCommandKind.Start:
                if (Screens.Screen == ScreenKind.Title)
                {
                    if (State.Status == GameStatus.Dead) State.Restart();
                    Screens.Start();
                    InventoryCursor = 0;
                }
                break;
            case UiCommandKind.NewRun:
                if (Screens.Screen == ScreenKind.Title) StartNewRun(State.Seed);
                break;
            case UiCommandKind.Continue:
                if (Screens.Screen == ScreenKind.Title && HasValidSave)
                {
                    LoadResult result = GameStatePersistence.Load(saveStore, content.ContentHash, content);
                    if (result.State is not null)
                    {
                        State = result.State;
                        Screens.Start();
                    }
                }
                break;
            case UiCommandKind.NewRunWithSeed:
                if (Screens.Screen == ScreenKind.Title) Screens.ShowSeedEntry();
                break;
            case UiCommandKind.RunHistory:
                Screens.ShowRunHistory();
                break;
            case UiCommandKind.SaveAndQuit:
                GameStatePersistence.Save(State, saveStore, content.ContentHash);
                QuitRequested = true;
                break;
            case UiCommandKind.AbandonRun:
                if (Screens.Overlay == UiOverlay.AbandonConfirmation)
                {
                    RecordHistory("retired");
                    saveStore.Delete();
                    Screens.ConfirmAbandon();
                }
                else Screens.RequestAbandon();
                break;
            case UiCommandKind.ConfirmAbandon:
                if (Screens.Overlay == UiOverlay.AbandonConfirmation)
                {
                    RecordHistory("retired");
                    saveStore.Delete();
                    Screens.ConfirmAbandon();
                }
                break;
            case UiCommandKind.CancelAbandon:
                if (Screens.Overlay == UiOverlay.AbandonConfirmation) Screens.CloseOverlay();
                break;
            case UiCommandKind.Help: Screens.ShowHelp(); break;
            case UiCommandKind.CloseHelp: Screens.CloseHelp(); break;
            case UiCommandKind.Resume: Screens.Resume(); break;
            case UiCommandKind.MenuUp:
                if (Screens.Overlay == UiOverlay.Inventory) InventoryCursor = Math.Max(0, InventoryCursor - 1);
                else Screens.MoveMenu(-1);
                break;
            case UiCommandKind.MenuDown:
                if (Screens.Overlay == UiOverlay.Inventory)
                    InventoryCursor = Math.Min(Math.Max(0, State.Player.Inventory.Items.Count - 1), InventoryCursor + 1);
                else Screens.MoveMenu(1);
                break;
            case UiCommandKind.Accept: Accept(); break;
            case UiCommandKind.Cancel:
                if (Screens.Screen == ScreenKind.GameOver) Screens.Title();
                else if (Screens.Screen is ScreenKind.SeedEntry or ScreenKind.RunHistory) Screens.ReturnToTitle();
                else if (!Screens.CloseOverlay()) Screens.ReturnToTitle();
                break;
            case UiCommandKind.Inventory:
                if (Screens.ToggleInventory()) InventoryCursor = 0;
                break;
            case UiCommandKind.Drop:
                if (InventoryCursor < State.Player.Inventory.Items.Count) State.Process(GameAction.DropItem(InventoryCursor));
                ClampCursor();
                break;
            case UiCommandKind.UnequipWeapon: State.Process(GameAction.UnequipSlot(0)); ClampCursor(); break;
            case UiCommandKind.UnequipArmor: State.Process(GameAction.UnequipSlot(1)); ClampCursor(); break;
            case UiCommandKind.Pause: Screens.Pause(); break;
            case UiCommandKind.Restart:
                if (Screens.Screen == ScreenKind.Playing) Screens.RequestRestart();
                else if (Screens.Screen is ScreenKind.GameOver or ScreenKind.Paused) RestartImmediate();
                break;
            case UiCommandKind.ConfirmRestart:
                if (Screens.ConfirmRestart()) RestartImmediate();
                break;
            case UiCommandKind.CancelRestart: Screens.CancelRestart(); break;
            case UiCommandKind.Move: ProcessAndAutosave(GameAction.Move(command.Direction)); break;
            case UiCommandKind.Wait: ProcessAndAutosave(GameAction.Wait); break;
            case UiCommandKind.None: break;
            default: throw new InvalidOperationException($"Unhandled UI command: {command.Kind}");
        }
        if (State.Status == GameStatus.Dead && Screens.Screen == ScreenKind.Playing)
        {
            Screens.GameOver();
            InventoryCursor = 0;
        }
    }

    private void Accept()
    {
        if (Screens.Screen == ScreenKind.Paused)
        {
            switch (Screens.MenuIndex)
            {
                case 0: Screens.Resume(); break;
                case 1: RestartImmediate(); break;
                case 2: Screens.ShowHelp(); break;
                case 3: QuitRequested = true; break;
            }
        }
        else if (Screens.Overlay == UiOverlay.Inventory && InventoryCursor < State.Player.Inventory.Items.Count)
        {
            ItemType type = State.Player.Inventory.Items[InventoryCursor].Definition.Type;
            State.Process(type == ItemType.Consumable
                ? GameAction.UseItem(InventoryCursor) : GameAction.EquipItem(InventoryCursor));
            ClampCursor();
        }
    }

    private void RestartImmediate()
    {
        State.Restart();
        historyRecorded = false;
        Screens.RestartRun();
        InventoryCursor = 0;
    }

    private void ClampCursor() =>
        InventoryCursor = Math.Clamp(InventoryCursor, 0, Math.Max(0, State.Player.Inventory.Items.Count - 1));

    private void StartNewRun(int seed)
    {
        State = new GameState(seed, State.Width, State.Height, 1, content);
        historyRecorded = false;
        Screens.Start();
        InventoryCursor = 0;
    }

    private void RecordHistory(string cause)
    {
        if (historyRecorded) return;
        historyRecorded = true;
        historyStore.Append(new RunRecord(Guid.NewGuid().ToString("N"), State.Seed, DateTime.UtcNow,
            State.RunStats.MaxDepth, State.Player.Level, State.RunStats.TurnsSurvived,
            State.RunStats.MonstersSlain, cause, content.ContentHash));
    }

    private void ProcessAndAutosave(GameAction action)
    {
        int depth = State.Depth;
        State.Process(action);
        if (State.Status == GameStatus.Playing && State.Depth != depth)
            GameStatePersistence.Save(State, saveStore, content.ContentHash);
    }
}
