namespace MMONavigator.Models;

public class LocationItem {
    public string? Name { get; set; }
    public string? Coordinates { get; set; }
    public string? ScrubbedCoordinates { get; set; }

    public string DisplayName {
        get {
            if(Items!=null) return Header ?? "";
            if (string.IsNullOrEmpty(Name)) return Coordinates ?? "";
            return $"{Name} ({Coordinates})";
        }
    }
    
    public string? Header  { get; set; }
    public List<LocationItem>? Items { get; set; }
}
