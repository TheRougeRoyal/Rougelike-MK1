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
    private string seedInput = string.Empty;
    private int historyScrollOffset = 0;
    private Point examineCursor = Point.Zero;
    private string? lastRecordedRunId = null;
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
    /// <summary>Gets the current seed input text.</summary>
    public string SeedInput => seedInput;
    /// <summary>Gets the run history scroll offset.</summary>
    public int HistoryScrollOffset => historyScrollOffset;
    /// <summary>Gets the rank position if the run is recorded in history.</summary>
    public int? RankPosition { get; private set; }
    /// <summary>Gets the total number of runs in history.</summary>
    public int? TotalRuns { get; private set; }
    /// <summary>Gets the examine cursor position.</summary>
    public Point ExamineCursor => examineCursor;
    /// <summary>Gets the description of what's at the examined position.</summary>
    public string GetExamineDescription(Point position)
    {
        if (!State.Dungeon.IsVisible(position)) return string.Empty;
        
        // Check for monster
        MonsterActor? monster = State.Monsters.FirstOrDefault(m => m.IsAlive && m.Position == position);
        if (monster is not null) return $"{monster.Name} HP {monster.Hp}/{monster.MaxHp}";
        
        // Check for item
        FloorItem? item = State.FloorItems.FirstOrDefault(fi => fi.Position == position);
        if (item is not null) return item.Item.Definition.Name;
        
        // Check terrain
        if (position == State.Dungeon.StairsPosition) return "STAIRS";
        TileType tile = State.Dungeon[position];
        return tile switch
        {
            TileType.Wall => "WALL",
            TileType.Floor => "FLOOR",
            _ => "UNKNOWN"
        };
    }
    public bool HasValidSave
    {
        get
        {
            LoadResult result = GameStatePersistence.Load(saveStore, content.ContentHash, content);
            return result.State is { Status: GameStatus.Playing };
        }
    }

    /// <summary>Executes one UI command without any window dependency.</summary>
    public void Execute(UiCommand command)
    {
        bool wasDead = State.Status == GameStatus.Dead;
        switch (command.Kind)
        {
            case UiCommandKind.Quit: QuitRequested = true; break;
            case UiCommandKind.Accept:
                if (Screens.Screen == ScreenKind.Title) HandleTitleMenuAccept();
                else Accept();
                break;
            case UiCommandKind.Start:
                if (Screens.Screen == ScreenKind.Title)
                {
                    if (State.Status == GameStatus.Dead) State.Restart();
                    Screens.Start();
                    InventoryCursor = 0;
                }
                break;
            case UiCommandKind.NewRun:
                if (Screens.Screen == ScreenKind.Title) StartNewRun(Random.Shared.Next());
                break;
            case UiCommandKind.Continue:
                if (Screens.Screen == ScreenKind.Title)
                {
                    LoadResult result = GameStatePersistence.Load(saveStore, content.ContentHash, content);
                    if (result.State is not null)
                    {
                        if (result.State.Status == GameStatus.Dead)
                        {
                            if (TryDeleteSave())
                                State.SetFeedback("Save is from a finished run and was removed.",
                                    Microsoft.Xna.Framework.Color.Yellow);
                        }
                        else
                        {
                            State = result.State;
                            historyRecorded = false;
                            Screens.Start();
                        }
                    }
                    else if (result.Reason is not null)
                        State.SetFeedback(result.Reason, Microsoft.Xna.Framework.Color.Yellow);
                }
                break;
            case UiCommandKind.NewRunWithSeed:
                if (Screens.Screen == ScreenKind.Title) Screens.ShowSeedEntry();
                break;
            case UiCommandKind.RunHistory:
                Screens.ShowRunHistory();
                break;
            case UiCommandKind.SaveAndQuit:
                SaveAndQuit();
                break;
            case UiCommandKind.AbandonRun:
                if (Screens.Overlay == UiOverlay.AbandonConfirmation)
                {
                    if (TryDeleteSave())
                    {
                        RecordHistory("retired");
                        Screens.ConfirmAbandon();
                    }
                }
                else Screens.RequestAbandon();
                break;
            case UiCommandKind.ConfirmAbandon:
                if (Screens.Overlay == UiOverlay.AbandonConfirmation)
                {
                    if (TryDeleteSave())
                    {
                        RecordHistory("retired");
                        Screens.ConfirmAbandon();
                    }
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
                else if (Screens.Screen == ScreenKind.RunHistory) historyScrollOffset = Math.Max(0, historyScrollOffset - 1);
                else Screens.MoveMenu(-1);
                break;
            case UiCommandKind.MenuDown:
                if (Screens.Overlay == UiOverlay.Inventory)
                    InventoryCursor = Math.Min(Math.Max(0, State.Player.Inventory.Items.Count - 1), InventoryCursor + 1);
                else if (Screens.Screen == ScreenKind.RunHistory) historyScrollOffset = Math.Max(0, historyScrollOffset + 1);
                else Screens.MoveMenu(1);
                break;
            case UiCommandKind.Cancel:
                if (Screens.Screen == ScreenKind.GameOver) Screens.Title();
                else if (Screens.Screen is ScreenKind.SeedEntry or ScreenKind.RunHistory) Screens.ReturnToTitle();
                else if (Screens.Overlay == UiOverlay.Examine) Screens.ExitExamine();
                else if (!Screens.CloseOverlay()) Screens.ReturnToTitle();
                break;
            case UiCommandKind.Inventory:
                if (Screens.ToggleInventory()) InventoryCursor = 0;
                break;
            case UiCommandKind.Examine:
                if (Screens.EnterExamine()) examineCursor = State.Player.Position;
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
            case UiCommandKind.Move:
                if (Screens.Overlay == UiOverlay.Examine)
                {
                    // Move examine cursor within visible bounds
                    Point newPos = examineCursor + command.Direction;
                    if (State.Dungeon.InBounds(newPos) && State.Dungeon.IsVisible(newPos))
                        examineCursor = newPos;
                }
                else
                    ProcessAndAutosave(GameAction.Move(command.Direction));
                break;
            case UiCommandKind.Wait: ProcessAndAutosave(GameAction.Wait); break;
            case UiCommandKind.SeedDigit:
                if (Screens.Screen == ScreenKind.SeedEntry && seedInput.Length < 9)
                    seedInput += command.Digit;
                break;
            case UiCommandKind.SeedBackspace:
                if (Screens.Screen == ScreenKind.SeedEntry && seedInput.Length > 0)
                    seedInput = seedInput[..^1];
                break;
            case UiCommandKind.None: break;
            default: throw new InvalidOperationException($"Unhandled UI command: {command.Kind}");
        }
        FinalizeDeath(wasDead, command.Kind);
    }

    private void HandleTitleMenuAccept()
    {
        int menuIndex = Screens.TitleMenuIndex;
        switch (menuIndex)
        {
            case 0: // Continue
                if (!HasValidSave)
                {
                    State.SetFeedback("No saved run to continue.", Microsoft.Xna.Framework.Color.Yellow);
                    return;
                }
                {
                    LoadResult result = GameStatePersistence.Load(saveStore, content.ContentHash, content);
                    if (result.State is not null && result.State.Status == GameStatus.Playing)
                    {
                        State = result.State;
                        historyRecorded = false;
                        Screens.Start();
                    }
                    else if (result.Reason is not null)
                        State.SetFeedback(result.Reason, Microsoft.Xna.Framework.Color.Yellow);
                }
                break;
            case 1: // New Run
                StartNewRun(Random.Shared.Next());
                break;
            case 2: // New Run With Seed
                seedInput = string.Empty;
                Screens.ShowSeedEntry();
                break;
            case 3: // Run History
                historyScrollOffset = 0;
                Screens.ShowRunHistory();
                break;
            case 4: // Help
                Screens.ShowHelp();
                break;
            case 5: // Quit
                QuitRequested = true;
                break;
        }
    }

    private void Accept()
    {
        if (Screens.Screen == ScreenKind.SeedEntry)
        {
            if (SeedParser.TryParse(seedInput, out int seed, out string error))
            {
                StartNewRun(seed);
            }
            else
            {
                State.SetFeedback(error, Microsoft.Xna.Framework.Color.Yellow);
            }
        }
        else if (Screens.Screen == ScreenKind.Paused)
        {
            switch (Screens.MenuIndex)
            {
                case 0: Screens.Resume(); break;
                case 1: SaveAndQuit(); break;
                case 2: Screens.RequestAbandon(); break;
                case 3: Screens.ShowHelp(); break;
                case 4: Screens.Title(); break;
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
        
        string recordId = Guid.NewGuid().ToString("N");
        try
        {
            historyStore.Append(new RunRecord(recordId, State.Seed, DateTime.UtcNow,
                State.RunStats.MaxDepth, State.Player.Level, State.RunStats.TurnsSurvived,
                State.RunStats.MonstersSlain, cause, content.ContentHash));
            lastRecordedRunId = recordId;
        }
        catch
        {
            // If append fails, the run is not recorded
            lastRecordedRunId = null;
            TotalRuns = null;
            RankPosition = null;
            return;
        }
        
        // Find rank in history - match by ID, not by seed
        var allRecords = historyStore.Read();
        TotalRuns = allRecords.Count;
        
        var ranked = RunRanking.OrderedByRank(allRecords).ToList();
        int rankIndex = ranked.FindIndex(r => r.Id == recordId);
        RankPosition = rankIndex >= 0 ? rankIndex + 1 : null; // 1-indexed
    }

    private void ProcessAndAutosave(GameAction action)
    {
        int depth = State.Depth;
        State.Process(action);
        if (State.Status == GameStatus.Playing && State.Depth != depth)
        {
            try { GameStatePersistence.Save(State, saveStore, content.ContentHash); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                State.SetFeedback($"Could not save: {exception.Message}", Microsoft.Xna.Framework.Color.Yellow);
            }
        }
    }

    private void SaveAndQuit()
    {
        if (State.Status == GameStatus.Dead) return;
        try
        {
            GameStatePersistence.Save(State, saveStore, content.ContentHash);
            QuitRequested = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            State.SetFeedback($"Could not save: {exception.Message}", Microsoft.Xna.Framework.Color.Yellow);
        }
    }

    private void FinalizeDeath(bool wasDead, UiCommandKind commandKind)
    {
        if (State.Status != GameStatus.Dead) return;
        TryDeleteSave();
        RecordHistory("dead");
        if (Screens.Screen == ScreenKind.Playing)
        {
            Screens.GameOver();
            InventoryCursor = 0;
        }
        if (wasDead && commandKind == UiCommandKind.Cancel && Screens.Screen == ScreenKind.GameOver)
        {
            Screens.Title();
        }
    }

    private bool TryDeleteSave()
    {
        try
        {
            saveStore.Delete();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            State.SetFeedback($"Could not delete save: {exception.Message}",
                Microsoft.Xna.Framework.Color.Yellow);
            return false;
        }
    }
}
