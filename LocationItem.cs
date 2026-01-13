namespace MMONavigator;

public class LocationItem {
    public string? Name { get; set; }
    public string? Coordinates { get; set; }
    public string? ScrubbedCoordinates { get; set; }

    public string DisplayName {
        get {
            if (string.IsNullOrEmpty(Name)) return Coordinates ?? "";
            return $"{Name} ({Coordinates})";
        }
    }
}
