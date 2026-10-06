namespace Roguelike;

/// <summary>Pure helpers for fitting text into UI regions.</summary>
public static class TextLayout
{
    /// <summary>Wraps words to the supplied character width, splitting long words when necessary.</summary>
    public static IReadOnlyList<string> WordWrap(string text, int width)
    {
        if (width < 1) throw new ArgumentOutOfRangeException(nameof(width));
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
        List<string> lines = new();
        foreach (string paragraph in text.Replace("\r", string.Empty).Split('\n'))
        {
            string remaining = paragraph.Trim();
            if (remaining.Length == 0) { lines.Add(string.Empty); continue; }
            while (remaining.Length > width)
            {
                int split = remaining.LastIndexOf(' ', width - 1);
                if (split <= 0) split = width;
                lines.Add(remaining[..split].TrimEnd());
                remaining = remaining[split..].TrimStart();
            }
            lines.Add(remaining);
        }
        return lines;
    }
    /// <summary>Truncates text and appends a Unicode ellipsis when needed.</summary>
    public static string Truncate(string text, int width)
    {
        if (width < 1) throw new ArgumentOutOfRangeException(nameof(width));
        return text.Length <= width ? text : width == 1 ? "…" : text[..(width - 1)] + "…";
    }
    /// <summary>Gets the left coordinate for an aligned string.</summary>
    public static int AlignX(int containerLeft, int containerWidth, int textWidth, TextAlignment alignment) =>
        alignment switch
        {
            TextAlignment.Left => containerLeft,
            TextAlignment.Center => containerLeft + (containerWidth - textWidth) / 2,
            TextAlignment.Right => containerLeft + containerWidth - textWidth,
            _ => throw new ArgumentOutOfRangeException(nameof(alignment))
        };
}

/// <summary>Horizontal text alignment.</summary>
public enum TextAlignment { Left, Center, Right }
