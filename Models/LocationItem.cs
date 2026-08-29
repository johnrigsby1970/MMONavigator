using System.ComponentModel;
using System.Runtime.CompilerServices;
using MMONavigator.Base;

namespace MMONavigator.Models;

public class LocationItem : ViewModelBase {
    private string? _name;
    public string? Name {
        get => _name;
        set { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); }
    }

    private string? _coordinates;
    public string? Coordinates {
        get => _coordinates;
        set { _coordinates = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); }
    }

    private string? _scrubbedCoordinates;
    public string? ScrubbedCoordinates {
        get => _scrubbedCoordinates;
        set { _scrubbedCoordinates = value; OnPropertyChanged(); }
    }

    public string DisplayName {
        get {
            if (Items != null) return Header ?? "";
            if (string.IsNullOrEmpty(Name)) return Coordinates ?? "";
            return $"{Name} ({Coordinates})";
        }
    }

    private string? _header;
    public string? Header {
        get => _header;
        set { _header = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); }
    }

    private List<LocationItem>? _items;
    public List<LocationItem>? Items {
        get => _items;
        set { _items = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); }
    }
}
