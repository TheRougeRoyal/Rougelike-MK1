using Microsoft.Xna.Framework;

namespace Roguelike;

/// <summary>An immutable message-log entry.</summary>
public sealed record MessageLogEntry(string Text, Color Color, int TurnNumber, int Count = 1)
{
    /// <summary>Gets the text with a collapsed repeat count.</summary>
    public string DisplayText => Count > 1 ? $"{Text} (x{Count})" : Text;
}

/// <summary>A bounded, consecutive-message-collapsing log.</summary>
public sealed class MessageLog
{
    private readonly List<MessageLogEntry> entries = new();
    /// <summary>Creates a log with a maximum entry count.</summary>
    public MessageLog(int capacity = 100) => Capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    /// <summary>Gets the maximum number of entries.</summary>
    public int Capacity { get; }
    /// <summary>Gets entries oldest first.</summary>
    public IReadOnlyList<MessageLogEntry> Entries => entries;
    /// <summary>Adds a message, collapsing an identical consecutive entry.</summary>
    public void Add(string text, Color color, int turnNumber)
    {
        if (entries.Count > 0 && entries[^1].Text == text && entries[^1].Color == color)
        {
            MessageLogEntry last = entries[^1];
            entries[^1] = last with { Count = last.Count + 1, TurnNumber = turnNumber };
            return;
        }
        entries.Add(new MessageLogEntry(text, color, turnNumber));
        if (entries.Count > Capacity) entries.RemoveAt(0);
    }
    /// <summary>Clears all messages.</summary>
    public void Clear() => entries.Clear();
}
