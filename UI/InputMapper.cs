using Microsoft.Xna.Framework;

namespace Roguelike;

/// <summary>Commands produced by the pure input mapper.</summary>
public enum UiCommandKind
{
    None, Move, Wait, Accept, Cancel, Quit, Start, Help, CloseHelp, Pause, Resume, Restart,
    ConfirmRestart, CancelRestart, Inventory, Drop, UnequipWeapon, UnequipArmor, MenuUp, MenuDown,
    NewRun, NewRunWithSeed, RunHistory, SaveAndQuit, AbandonRun, Continue, ConfirmAbandon, CancelAbandon
}

/// <summary>A keyboard-independent UI command.</summary>
public readonly record struct UiCommand(UiCommandKind Kind, Point Direction)
{
    /// <summary>Creates a non-directional command.</summary>
    public UiCommand(UiCommandKind kind) : this(kind, Point.Zero) { }
}

/// <summary>Maps plain key names and elapsed time into UI commands.</summary>
public sealed class InputMapper
{
    private static readonly Dictionary<string, Point> directions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Up"] = new(0, -1), ["W"] = new(0, -1), ["NumPad8"] = new(0, -1),
        ["Down"] = new(0, 1), ["S"] = new(0, 1), ["NumPad2"] = new(0, 1),
        ["Left"] = new(-1, 0), ["A"] = new(-1, 0), ["NumPad4"] = new(-1, 0),
        ["Right"] = new(1, 0), ["D"] = new(1, 0), ["NumPad6"] = new(1, 0)
    };
    private string? heldDirection;
    private string? lastPressedDirection;
    private TimeSpan heldFor;

    /// <summary>Maps a frame of plain pressed-key names.</summary>
    public UiCommand Map(IReadOnlySet<string> previous, IReadOnlySet<string> current,
        ScreenKind screen, UiOverlay overlay, TimeSpan elapsed)
    {
        bool pressed(string key) => current.Contains(key) && !previous.Contains(key);
        bool movementContext = screen == ScreenKind.Playing && overlay == UiOverlay.None;
        if (movementContext)
        {
            foreach (string candidate in directions.Keys)
                if (current.Contains(candidate) && !previous.Contains(candidate))
                    lastPressedDirection = candidate;
            string? direction = lastPressedDirection is not null && current.Contains(lastPressedDirection)
                ? lastPressedDirection : directions.Keys.FirstOrDefault(current.Contains);
            if (direction is null)
            {
                heldDirection = null;
                heldFor = TimeSpan.Zero;
            }
            else
            {
                if (heldDirection != direction) { heldDirection = direction; heldFor = TimeSpan.Zero; }
                else heldFor += elapsed;
                if (pressed(direction))
                    return new UiCommand(UiCommandKind.Move, directions[direction]);
                if (heldFor >= TimeSpan.FromMilliseconds(250))
                {
                    heldFor -= TimeSpan.FromMilliseconds(90);
                    return new UiCommand(UiCommandKind.Move, directions[direction]);
                }
            }
        }
        else
        {
            heldDirection = null;
            heldFor = TimeSpan.Zero;
        }

        if (screen == ScreenKind.Title)
        {
            if (pressed("Up") || pressed("W")) return new UiCommand(UiCommandKind.MenuUp);
            if (pressed("Down") || pressed("S")) return new UiCommand(UiCommandKind.MenuDown);
            if (pressed("Enter")) return new UiCommand(UiCommandKind.Start);
            if (pressed("H")) return new UiCommand(UiCommandKind.Help);
            if (pressed("Escape")) return new UiCommand(UiCommandKind.Quit);
        }
        else if (screen == ScreenKind.Help)
        {
            if (pressed("Escape") || pressed("Enter")) return new UiCommand(UiCommandKind.CloseHelp);
        }
        else if (screen == ScreenKind.Paused)
        {
            if (pressed("Escape")) return new UiCommand(UiCommandKind.Resume);
            if (pressed("Up") || pressed("W")) return new UiCommand(UiCommandKind.MenuUp);
            if (pressed("Down") || pressed("S")) return new UiCommand(UiCommandKind.MenuDown);
            if (pressed("Enter")) return new UiCommand(UiCommandKind.Accept);
            if (pressed("H")) return new UiCommand(UiCommandKind.Help);
        }
        else if (screen == ScreenKind.GameOver)
        {
            if (pressed("R")) return new UiCommand(UiCommandKind.Restart);
            if (pressed("Escape")) return new UiCommand(UiCommandKind.Cancel);
        }
        else if (screen == ScreenKind.Playing)
        {
            if (overlay == UiOverlay.RestartConfirmation)
            {
                if (pressed("Y")) return new UiCommand(UiCommandKind.ConfirmRestart);
                if (pressed("N") || pressed("Escape")) return new UiCommand(UiCommandKind.CancelRestart);
            }
            else if (overlay == UiOverlay.Inventory)
            {
                if (pressed("Escape") || pressed("I")) return new UiCommand(UiCommandKind.Cancel);
                if (pressed("Up") || pressed("W")) return new UiCommand(UiCommandKind.MenuUp);
                if (pressed("Down") || pressed("S")) return new UiCommand(UiCommandKind.MenuDown);
                if (pressed("Enter")) return new UiCommand(UiCommandKind.Accept);
                if (pressed("D")) return new UiCommand(UiCommandKind.Drop);
                if (pressed("D1")) return new UiCommand(UiCommandKind.UnequipWeapon);
                if (pressed("D2")) return new UiCommand(UiCommandKind.UnequipArmor);
            }
            else if (overlay == UiOverlay.None)
            {
                if (pressed("Escape")) return new UiCommand(UiCommandKind.Pause);
                if (pressed("I")) return new UiCommand(UiCommandKind.Inventory);
                if (pressed("R")) return new UiCommand(UiCommandKind.Restart);
                if (pressed("Space")) return new UiCommand(UiCommandKind.Wait);
            }
        }
        return new UiCommand(UiCommandKind.None);
    }
}
