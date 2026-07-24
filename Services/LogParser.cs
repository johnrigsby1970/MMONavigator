using System.Text.RegularExpressions;

namespace MMONavigator.Services;

public static class LogParser {
    public const string Marker = "Your Location is";

    /// <summary>
    /// Attempts to parse a log line for coordinates.
    /// </summary>
    /// <param name="line">The log line to parse.</param>
    /// <param name="userRegex">The user-defined regex pattern from settings.</param>
    /// <param name="coordinates">The parsed coordinates as a space-separated string.</param>
    /// <returns>True if parsing was successful, otherwise false.</returns>
    public static bool TryParseLogLine(string line, string userRegex, out string coordinates) {
        coordinates = string.Empty;
        if (string.IsNullOrWhiteSpace(line)) return false;

        // 1. Try with user-defined regex on each "Your Location is" block, starting from the last one
        try {
            var regex = new Regex(userRegex, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(1000));
            
            // Find all indices of the marker
            var markerIndices = new List<int>();
            int lastIndex = -1;
            while (lastIndex + 1 < line.Length && (lastIndex = line.IndexOf(Marker, lastIndex + 1, StringComparison.OrdinalIgnoreCase)) != -1) {
                markerIndices.Add(lastIndex);
            }

            // Iterate backwards from the last marker
            for (int i = markerIndices.Count - 1; i >= 0; i--) {
                int start = markerIndices[i];
                int end = (i == markerIndices.Count - 1) ? line.Length : markerIndices[i + 1];
                int length = end - start;
                
                if (start < 0 || start >= line.Length || length <= 0 || start + length > line.Length) {
                    continue;
                }
                
                string block = line.Substring(start, length);

                var matches = regex.Matches(block);
                if (matches.Count > 0) {
                    var match = matches[matches.Count - 1];
                    var values = new List<string>();
                    for (int j = 1; j < match.Groups.Count; j++) {
                        if (match.Groups[j].Success && !string.IsNullOrWhiteSpace(match.Groups[j].Value)) {
                            values.Add(match.Groups[j].Value);
                        }
                    }

                    if (values.Count >= 2) {
                        coordinates = string.Join(" ", values);
                        System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] Regex parsed coordinates: {coordinates} from block at index {start} in line: {line}");
                        return true;
                    }
                }
            }
        }
        catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] Regex parsing error: {ex.Message}");
        }

        // 2. Fallback for the default "Your location is" format
        int markerIndex = line.LastIndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0 && markerIndex + Marker.Length <= line.Length) {
            string afterMarker = line.Substring(markerIndex + Marker.Length);
            var numMatches = Regex.Matches(afterMarker, @"-?\d+(?:\.\d+)?");
            if (numMatches.Count >= 2) {
                var values = numMatches.Cast<Match>().Take(4).Select(m => m.Value).ToList();
                coordinates = string.Join(" ", values);
                System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] Fallback parsed coordinates: {coordinates} from LAST match in line: {line}");
                return true;
            }
        }

        System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] Failed to parse log line: {line}");
        return false;
    }
}
