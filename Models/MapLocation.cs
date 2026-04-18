using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace MMONavigator.Models;

public class MapLocation : INotifyPropertyChanged {
    private string _displayName = string.Empty;
    public string DisplayName {
        get => _displayName;
        set { _displayName = value; OnPropertyChanged(); }
    }

    private string _tooltip = string.Empty;
    public string Tooltip {
        get => _tooltip;
        set { _tooltip = value; OnPropertyChanged(); }
    }

    private string _coordinates = string.Empty;
    public string Coordinates {
        get => _coordinates;
        set { _coordinates = value; OnPropertyChanged(); }
    }

    private double _pixelX;
    public double PixelX {
        get => _pixelX;
        set { _pixelX = value; OnPropertyChanged(); }
    }

    private double _pixelY;
    public double PixelY {
        get => _pixelY;
        set { _pixelY = value; OnPropertyChanged(); }
    }

    private Visibility _visibility = Visibility.Collapsed;
    public Visibility Visibility {
        get => _visibility;
        set { _visibility = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
