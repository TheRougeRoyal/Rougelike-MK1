namespace Roguelike;

/// <summary>Small UI state machine; it keeps screen transitions testable without MonoGame.</summary>
public sealed class ScreenStateMachine
{
    /// <summary>Gets the current screen.</summary>
    public ScreenKind Screen { get; private set; } = ScreenKind.Title;
    /// <summary>Gets the active playing overlay.</summary>
    public UiOverlay Overlay { get; private set; }
    /// <summary>Gets the selected paused-menu item.</summary>
    public int MenuIndex { get; private set; }
    /// <summary>Starts a run from the title.</summary>
    public void Start() { if (Screen == ScreenKind.Title) Screen = ScreenKind.Playing; }
    /// <summary>Pauses a run when no overlay is open.</summary>
    public void Pause() { if (Screen == ScreenKind.Playing && Overlay == UiOverlay.None) { Screen = ScreenKind.Paused; MenuIndex = 0; } }
    /// <summary>Resumes a paused run.</summary>
    public void Resume() { if (Screen == ScreenKind.Paused) Screen = ScreenKind.Playing; }
    /// <summary>Shows help.</summary>
    public void ShowHelp() => Screen = ScreenKind.Help;
    /// <summary>Shows game over.</summary>
    public void GameOver() { Screen = ScreenKind.GameOver; Overlay = UiOverlay.None; }
    /// <summary>Returns to title.</summary>
    public void Title() { Screen = ScreenKind.Title; Overlay = UiOverlay.None; }
    /// <summary>Opens or closes the inventory overlay.</summary>
    public void ToggleInventory() { if (Screen == ScreenKind.Playing) Overlay = Overlay == UiOverlay.Inventory ? UiOverlay.None : UiOverlay.Inventory; }
    /// <summary>Closes any overlay.</summary>
    public void CloseOverlay() => Overlay = UiOverlay.None;
    /// <summary>Requests restart confirmation during play.</summary>
    public void RequestRestart() { if (Screen == ScreenKind.Playing && Overlay == UiOverlay.None) Overlay = UiOverlay.RestartConfirmation; }
    /// <summary>Confirms a restart.</summary>
    public void ConfirmRestart() { Overlay = UiOverlay.None; Screen = ScreenKind.Playing; }
    /// <summary>Immediately enters a freshly restarted run.</summary>
    public void RestartRun() { Overlay = UiOverlay.None; Screen = ScreenKind.Playing; }
    /// <summary>Moves the paused-menu selection.</summary>
    public void MoveMenu(int delta) { if (Screen == ScreenKind.Paused) MenuIndex = (MenuIndex + delta + 4) % 4; }
}
