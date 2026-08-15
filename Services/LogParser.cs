using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace MMONavigator.Services;

public static class LogParser {
    public const string Marker = "Your Location is";
    private static readonly ConcurrentDictionary<string, Regex> _regexCache = new();
    private static readonly Regex _fallbackNumbersRegex = new(@"-?\d+(?:\.\d+)?", RegexOptions.Compiled);

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
        if (!string.IsNullOrWhiteSpace(userRegex)) {
            try {
                // Retrieve or compile regex with timeout protection against ReDoS
                Regex regex = GetOrCreateRegex(userRegex);

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
                            Log.Debug("User regex successfully parsed coordinates '{Coordinates}' from block at index {Index} in log line.", coordinates, start);
                            return true;
                        }
                    }
                }
            }
            catch (ArgumentException ex) {
                Log.Warning(ex, "Invalid user regex pattern provided in settings: '{UserRegex}'. Falling back to default parser.", userRegex);
            }
            catch (RegexMatchTimeoutException ex) {
                Log.Warning(ex, "User regex match timed out on pattern '{UserRegex}'.", userRegex);
            }
            catch (Exception ex) {
                Log.Error(ex, "Unexpected exception evaluating user regex pattern '{UserRegex}'.", userRegex);
            }
        }

        // 2. Fallback for the default "Your location is" format
        try {
            int markerIndex = line.LastIndexOf(Marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0 && markerIndex + Marker.Length <= line.Length) {
                string afterMarker = line.Substring(markerIndex + Marker.Length);
                var numMatches = _fallbackNumbersRegex.Matches(afterMarker);

                if (numMatches.Count >= 2) {
                    var values = numMatches.Cast<Match>().Take(4).Select(m => m.Value).ToList();
                    coordinates = string.Join(" ", values);
                    Log.Debug("Fallback parser extracted coordinates '{Coordinates}' from log line.", coordinates);
                    return true;
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in fallback log line parsing.");
        }

        Log.Verbose("Failed to parse location coordinates from log line.");
        return false;
    }

    private static Regex GetOrCreateRegex(string pattern) {
        return _regexCache.GetOrAdd(pattern, p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(500)));
    }
}