using System.Globalization;

namespace Roguelike;

/// <summary>Pure parser for the seed-entry screen.</summary>
public static class SeedParser
{
    /// <summary>Parses at most nine decimal digits; empty input requests a random seed.</summary>
    public static bool TryParse(string input, out int seed, out string error)
    {
        seed = 0;
        error = string.Empty;
        if (string.IsNullOrEmpty(input)) { seed = Random.Shared.Next(); return true; }
        if (input.Length > 9 || input.Any(character => character is < '0' or > '9'))
        {
            error = "Seed must contain at most 9 digits.";
            return false;
        }
        if (!int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out seed))
        {
            error = "Seed is outside the supported integer range.";
            return false;
        }
        return true;
    }
}
