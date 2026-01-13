using System.Text.RegularExpressions;

namespace MMONavigator;

public record struct CoordinateData(double X, double Y, double? Heading);

public static class Scrubber {
    private static readonly Regex _numericRegex = new Regex(@"-?\d+(\.\d+)?", RegexOptions.Compiled);
    public const int MaxLength = 100;
    private const byte MaxCoordinateComponents = 4; 
    private const byte MinCoordinateComponents = 2;
    private const byte CoordinateCountForNorthUpSouth = 3;
    private const byte DefaultY = 0;
    private const byte XIndexInXZY = 0;
    private const byte XIndexInXY = 0;
    private const byte XIndexInYX = 1;
    private const byte YIndexInYX = 0;
    private const byte YIndexInXZY = 2;
    private const byte DirectionIndexInIndexInXZYD = 3;
    private const byte YIndexInXY = 1;
    
    public static bool TryParse(string? input, string coordinateOrder, out CoordinateData result) {
        result = default;
        var scrubbed = ScrubEntry(input);
        if (string.IsNullOrWhiteSpace(scrubbed)) return false;

        var parts = scrubbed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < MinCoordinateComponents) return false;

        double[] values = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++) {
            if (!double.TryParse(parts[i], out values[i])) return false;
        }

        if (coordinateOrder == "y x") {
            result = new CoordinateData(values[XIndexInYX], values[YIndexInYX], null);
            return true;
        }

        // Default "x z y d"
        double x = values[XIndexInXZY];
        double y = DefaultY;
        double? heading = null;
    
        //x z y and a possible fourth component of the direction (or facing)
        if (values.Length >= CoordinateCountForNorthUpSouth) {
            y = values[YIndexInXZY];
            if (values.Length >= MaxCoordinateComponents) {
                heading = values[DirectionIndexInIndexInXZYD];
            }
        }
        else if (values.Length == MinCoordinateComponents) {
            y = values[YIndexInXY];
        }

        result = new CoordinateData(x, y, heading);
        return true;
    }
    
    public static string? ScrubEntry(string? value) {
        if (string.IsNullOrWhiteSpace(value)) return value;
        
        // If the string is unreasonably long for coordinates, ignore it
        if (value.Length > MaxLength) return value;
        
        // The input to ScrubEntry should never allow multiple lines. If found, return the value
        if (value.Contains('\n') || value.Contains('\r')) return value;

        try {
            // Find all matches that look like numbers (including negative and decimal)
            var matches = _numericRegex.Matches(value).Cast<Match>().Take(MaxCoordinateComponents).ToList();
            if (matches.Count == 0) return value;

            // For ScrubEntry to return a set of numbers, those numbers should never be separated by more than a comma or whitespace.
            for (int i = 0; i < matches.Count - 1; i++) {
                int endOfCurrent = matches[i].Index + matches[i].Length;
                int startOfNext = matches[i + 1].Index;
                string gap = value.Substring(endOfCurrent, startOfNext - endOfCurrent);

                if (gap.Length == 0 || gap.Any(c => !char.IsWhiteSpace(c) && c != ',')) {
                    return value;
                }
            }

            var numbers = matches.Select(m => m.Value);
            return string.Join(" ", numbers);
        }
        catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"Error scrubbing entry: {ex.Message}");
            return value;
        }
    }
}
