using System.Linq;
using System.Text.RegularExpressions;

namespace MMONavigator;

public static class Scrubber {
    private static readonly Regex _numericRegex = new Regex(@"-?\d+(\.\d+)?", RegexOptions.Compiled);

    public static string? ScrubEntry(string? value) {
        if (string.IsNullOrWhiteSpace(value)) return value;
        
        //Used AI to alter this code
        
        // The input to ScrubEntry should never allow multiple lines. If found, return the value
        if (value.Contains('\n') || value.Contains('\r')) return value;

        try {
            // Find all matches that look like numbers (including negative and decimal)
            var matches = _numericRegex.Matches(value).Cast<Match>().Take(4).ToList();
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
        catch {
            return value;
        }
    }
}
