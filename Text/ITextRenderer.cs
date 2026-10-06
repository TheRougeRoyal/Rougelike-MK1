using Microsoft.Xna.Framework;

namespace Roguelike;

/// <summary>Draws text independently of the concrete font implementation.</summary>
public interface ITextRenderer
{
    /// <summary>Gets the line height at scale one.</summary>
    int LineHeight { get; }
    /// <summary>Draws a string.</summary>
    void DrawString(string text, Point position, Color color, int scale = 1, bool shadow = false);
    /// <summary>Measures a string.</summary>
    Point MeasureString(string text, int scale = 1);
}
