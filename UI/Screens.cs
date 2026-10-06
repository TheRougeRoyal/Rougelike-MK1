namespace Roguelike;

/// <summary>UI-layer screens.</summary>
public enum ScreenKind { Title, Playing, Paused, GameOver, Help }
/// <summary>Whether the playing screen has an overlay.</summary>
public enum UiOverlay { None, Inventory, Examine, RestartConfirmation }

/// <summary>Small UI state machine with rejected transitions represented by false.</summary>
public sealed class ScreenStateMachine
{
    private ScreenKind helpReturnScreen;
    /// <summary>Gets the current screen.</summary>
    public ScreenKind Screen { get; private set; } = ScreenKind.Title;
    /// <summary>Gets the active playing overlay.</summary>
    public UiOverlay Overlay { get; private set; }
    /// <summary>Gets the selected paused-menu item.</summary>
    public int MenuIndex { get; private set; }
    /// <summary>Starts a run only from the title.</summary>
    public bool Start()
    {
        if (Screen != ScreenKind.Title) return false;
        Screen = ScreenKind.Playing;
        return true;
    }
    /// <summary>Pauses only an unoverlaid playing screen.</summary>
    public bool Pause()
    {
        if (Screen != ScreenKind.Playing || Overlay != UiOverlay.None) return false;
        Screen = ScreenKind.Paused;
        MenuIndex = 0;
        return true;
    }
    /// <summary>Resumes only from the paused screen.</summary>
    public bool Resume()
    {
        if (Screen != ScreenKind.Paused) return false;
        Screen = ScreenKind.Playing;
        return true;
    }
    /// <summary>Shows help and remembers the screen to return to.</summary>
    public bool ShowHelp()
    {
        if (Screen is not (ScreenKind.Title or ScreenKind.Paused)) return false;
        helpReturnScreen = Screen;
        Screen = ScreenKind.Help;
        return true;
    }
    /// <summary>Closes help and returns to its previous screen.</summary>
    public bool CloseHelp()
    {
        if (Screen != ScreenKind.Help) return false;
        Screen = helpReturnScreen;
        return true;
    }
    /// <summary>Shows game over only from a playing run.</summary>
    public bool GameOver()
    {
        if (Screen != ScreenKind.Playing) return false;
        Overlay = UiOverlay.None;
        Screen = ScreenKind.GameOver;
        return true;
    }
    /// <summary>Returns to the title from game over.</summary>
    public bool Title()
    {
        if (Screen != ScreenKind.GameOver) return false;
        Overlay = UiOverlay.None;
        Screen = ScreenKind.Title;
        return true;
    }
    /// <summary>Opens or closes inventory on the playing screen.</summary>
    public bool ToggleInventory()
    {
        if (Screen != ScreenKind.Playing || (Overlay != UiOverlay.None && Overlay != UiOverlay.Inventory)) return false;
        Overlay = Overlay == UiOverlay.Inventory ? UiOverlay.None : UiOverlay.Inventory;
        return true;
    }
    /// <summary>Closes the active inventory or examine overlay.</summary>
    public bool CloseOverlay()
    {
        if (Screen != ScreenKind.Playing || Overlay == UiOverlay.None) return false;
        Overlay = UiOverlay.None;
        return true;
    }
    /// <summary>Requests restart confirmation only during unoverlaid play.</summary>
    public bool RequestRestart()
    {
        if (Screen != ScreenKind.Playing || Overlay != UiOverlay.None) return false;
        Overlay = UiOverlay.RestartConfirmation;
        return true;
    }
    /// <summary>Confirms restart only while the restart overlay is active.</summary>
    public bool ConfirmRestart()
    {
        if (Screen != ScreenKind.Playing || Overlay != UiOverlay.RestartConfirmation) return false;
        Overlay = UiOverlay.None;
        return true;
    }
    /// <summary>Cancels restart confirmation only while it is active.</summary>
    public bool CancelRestart()
    {
        if (Screen != ScreenKind.Playing || Overlay != UiOverlay.RestartConfirmation) return false;
        Overlay = UiOverlay.None;
        return true;
    }
    /// <summary>Immediately enters a freshly restarted run.</summary>
    public bool RestartRun()
    {
        if (Screen is not (ScreenKind.GameOver or ScreenKind.Paused)) return false;
        Overlay = UiOverlay.None;
        Screen = ScreenKind.Playing;
        return true;
    }
    /// <summary>Moves the paused-menu selection.</summary>
    public bool MoveMenu(int delta)
    {
        if (Screen != ScreenKind.Paused) return false;
        MenuIndex = (MenuIndex + delta + 4) % 4;
        return true;
    }
}
