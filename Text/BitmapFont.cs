using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Roguelike;

/// <summary>A dependency-free 5x7 bitmap font rendered with the pixel texture.</summary>
public sealed class BitmapFont : ITextRenderer
{
    private const int GlyphWidth = 5;
    private const int GlyphHeight = 7;
    private const int Advance = 6;
    private readonly SpriteBatch spriteBatch;
    private readonly Texture2D pixel;
    private readonly byte[][] glyphs = new byte[95][];

    /// <summary>Creates a bitmap font.</summary>
    public BitmapFont(SpriteBatch spriteBatch, Texture2D pixel)
    {
        this.spriteBatch = spriteBatch;
        this.pixel = pixel;
        for (int i = 0; i < glyphs.Length; i++) glyphs[i] = BuildGlyph((char)(i + 32));
    }
    /// <inheritdoc />
    public int LineHeight => 8;
    /// <inheritdoc />
    public Point MeasureString(string text, int scale = 1) =>
        new(text.Length * Advance * scale, LineHeight * scale);
    /// <inheritdoc />
    public void DrawString(string text, Point position, Color color, int scale = 1, bool shadow = false)
    {
        if (scale < 1) throw new ArgumentOutOfRangeException(nameof(scale));
        int x = position.X;
        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];
            if (character < 32 || character > 126) character = '?';
            byte[] rows = glyphs[character - 32];
            if (shadow) DrawGlyph(rows, new Point(x + scale, position.Y + scale), Color.Black, scale);
            DrawGlyph(rows, new Point(x, position.Y), color, scale);
            x += Advance * scale;
        }
    }
    private void DrawGlyph(byte[] rows, Point position, Color color, int scale)
    {
        for (int y = 0; y < GlyphHeight; y++)
        for (int x = 0; x < GlyphWidth; x++)
            if ((rows[y] & (1 << (GlyphWidth - x - 1))) != 0)
                spriteBatch.Draw(pixel, new Rectangle(position.X + x * scale, position.Y + y * scale, scale, scale), color);
    }
    private static byte[] BuildGlyph(char c)
    {
        string[] rows = c switch
        {
            '0' => new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" },
            '1' => new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" },
            '2' => new[] { "01110", "10001", "00001", "00010", "00100", "01000", "11111" },
            '3' => new[] { "11110", "00001", "00001", "01110", "00001", "00001", "11110" },
            '4' => new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" },
            '5' => new[] { "11111", "10000", "10000", "11110", "00001", "00001", "11110" },
            '6' => new[] { "01110", "10000", "10000", "11110", "10001", "10001", "01110" },
            '7' => new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" },
            '8' => new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" },
            '9' => new[] { "01110", "10001", "10001", "01111", "00001", "00001", "01110" },
            'A' or 'a' => new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" },
            'B' or 'b' => new[] { "11110", "10001", "10001", "11110", "10001", "10001", "11110" },
            'C' or 'c' => new[] { "01111", "10000", "10000", "10000", "10000", "10000", "01111" },
            'D' or 'd' => new[] { "11110", "10001", "10001", "10001", "10001", "10001", "11110" },
            'E' or 'e' => new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" },
            'F' or 'f' => new[] { "11111", "10000", "10000", "11110", "10000", "10000", "10000" },
            'G' or 'g' => new[] { "01111", "10000", "10000", "10111", "10001", "10001", "01111" },
            'H' or 'h' => new[] { "10001", "10001", "10001", "11111", "10001", "10001", "10001" },
            'I' or 'i' => new[] { "11111", "00100", "00100", "00100", "00100", "00100", "11111" },
            'J' or 'j' => new[] { "00111", "00010", "00010", "00010", "00010", "10010", "01100" },
            'K' or 'k' => new[] { "10001", "10010", "10100", "11000", "10100", "10010", "10001" },
            'L' or 'l' => new[] { "10000", "10000", "10000", "10000", "10000", "10000", "11111" },
            'M' or 'm' => new[] { "10001", "11011", "10101", "10101", "10001", "10001", "10001" },
            'N' or 'n' => new[] { "10001", "11001", "10101", "10011", "10001", "10001", "10001" },
            'O' or 'o' => new[] { "01110", "10001", "10001", "10001", "10001", "10001", "01110" },
            'P' or 'p' => new[] { "11110", "10001", "10001", "11110", "10000", "10000", "10000" },
            'Q' or 'q' => new[] { "01110", "10001", "10001", "10001", "10101", "10010", "01101" },
            'R' or 'r' => new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" },
            'S' or 's' => new[] { "01111", "10000", "10000", "01110", "00001", "00001", "11110" },
            'T' or 't' => new[] { "11111", "00100", "00100", "00100", "00100", "00100", "00100" },
            'U' or 'u' => new[] { "10001", "10001", "10001", "10001", "10001", "10001", "01110" },
            'V' or 'v' => new[] { "10001", "10001", "10001", "10001", "10001", "01010", "00100" },
            'W' or 'w' => new[] { "10001", "10001", "10001", "10101", "10101", "11011", "10001" },
            'X' or 'x' => new[] { "10001", "10001", "01010", "00100", "01010", "10001", "10001" },
            'Y' or 'y' => new[] { "10001", "10001", "01010", "00100", "00100", "00100", "00100" },
            'Z' or 'z' => new[] { "11111", "00001", "00010", "00100", "01000", "10000", "11111" },
            '!' => new[] { "00100", "00100", "00100", "00100", "00100", "00000", "00100" },
            '?' => new[] { "01110", "10001", "00001", "00010", "00100", "00000", "00100" },
            '>' => new[] { "10000", "01000", "00100", "00010", "00100", "01000", "10000" },
            '/' => new[] { "00001", "00010", "00010", "00100", "01000", "01000", "10000" },
            '[' => new[] { "11100", "10000", "10000", "10000", "10000", "10000", "11100" },
            ']' => new[] { "00111", "00001", "00001", "00001", "00001", "00001", "00111" },
            ':' => new[] { "00000", "00100", "00100", "00000", "00100", "00100", "00000" },
            '+' => new[] { "00000", "00100", "00100", "11111", "00100", "00100", "00000" },
            '-' => new[] { "00000", "00000", "00000", "11111", "00000", "00000", "00000" },
            '.' => new[] { "00000", "00000", "00000", "00000", "00000", "00110", "00110" },
            ' ' => new string[7],
            _ => new[] { "11111", "00001", "00010", "00100", "01000", "00000", "00100" }
        };
        byte[] result = new byte[7];
        for (int row = 0; row < 7; row++)
        {
            string pattern = rows[row] ?? "00000";
            for (int column = 0; column < 5; column++)
                if (pattern[column] == '1') result[row] |= (byte)(1 << (4 - column));
        }
        return result;
    }
}
