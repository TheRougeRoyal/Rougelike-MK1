using Microsoft.Xna.Framework;

namespace Roguelike;

/// <summary>UI-layer screens.</summary>
public enum ScreenKind { Title, Playing, Paused, GameOver, Help }
/// <summary>Whether the playing screen has an overlay.</summary>
public enum UiOverlay { None, Inventory, Examine, RestartConfirmation }
/// <summary>Commands produced by the pure input mapper.</summary>
public enum UiCommandKind { None, Move, Wait, Accept, Cancel, Inventory, Drop, UnequipWeapon, UnequipArmor, Help, Pause, Restart, MenuUp, MenuDown }
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
        ["Up"] = new(0, -1), ["W"] = new(0, -1), ["8"] = new(0, -1),
        ["Down"] = new(0, 1), ["S"] = new(0, 1), ["2"] = new(0, 1),
        ["Left"] = new(-1, 0), ["A"] = new(-1, 0), ["4"] = new(-1, 0),
        ["Right"] = new(1, 0), ["D"] = new(1, 0), ["6"] = new(1, 0)
    };
    private string? heldDirection;
    private TimeSpan heldFor;
    /// <summary>Maps a frame of plain pressed-key names.</summary>
    public UiCommand Map(IReadOnlySet<string> previous, IReadOnlySet<string> current,
        ScreenKind screen, UiOverlay overlay, TimeSpan elapsed)
    {
        string? direction = directions.Keys.FirstOrDefault(current.Contains);
        bool pressed(string key) => current.Contains(key) && !previous.Contains(key);
        if (screen == ScreenKind.Playing && overlay == UiOverlay.None && direction is not null)
        {
            if (heldDirection != direction) { heldDirection = direction; heldFor = TimeSpan.Zero; }
            else heldFor += elapsed;
            if (pressed(direction) || heldFor >= TimeSpan.FromMilliseconds(250))
            {
                if (!pressed(direction)) heldFor -= TimeSpan.FromMilliseconds(90);
                return new UiCommand(UiCommandKind.Move, directions[direction]);
            }
        }
        else { heldDirection = null; heldFor = TimeSpan.Zero; }
        if (pressed("Escape") && (overlay != UiOverlay.None || screen == ScreenKind.Help))
            return new UiCommand(UiCommandKind.Cancel);
        if (pressed("Enter")) return new UiCommand(UiCommandKind.Accept);
        if (screen == ScreenKind.Title && pressed("H")) return new UiCommand(UiCommandKind.Help);
        if (screen == ScreenKind.Playing)
        {
            if (overlay == UiOverlay.None && pressed("I")) return new UiCommand(UiCommandKind.Inventory);
            if (overlay == UiOverlay.None && pressed("Escape")) return new UiCommand(UiCommandKind.Pause);
            if (overlay != UiOverlay.None && pressed("D")) return new UiCommand(UiCommandKind.Drop);
            if (overlay != UiOverlay.None && pressed("1")) return new UiCommand(UiCommandKind.UnequipWeapon);
            if (overlay != UiOverlay.None && pressed("2")) return new UiCommand(UiCommandKind.UnequipArmor);
            if (overlay == UiOverlay.None && pressed("Space")) return new UiCommand(UiCommandKind.Wait);
            if (overlay == UiOverlay.None && pressed("R")) return new UiCommand(UiCommandKind.Restart);
        }
        if ((screen == ScreenKind.Paused || screen == ScreenKind.GameOver) && pressed("R"))
            return new UiCommand(UiCommandKind.Restart);
        if ((screen == ScreenKind.Paused || screen == ScreenKind.Help) && pressed("Up"))
            return new UiCommand(UiCommandKind.MenuUp);
        if ((screen == ScreenKind.Paused || screen == ScreenKind.Help) && pressed("Down"))
            return new UiCommand(UiCommandKind.MenuDown);
        return new UiCommand(UiCommandKind.None);
    }
}
