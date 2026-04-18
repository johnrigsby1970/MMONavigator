using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MMONavigator.Models;

public class LocationItem : INotifyPropertyChanged {
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

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
