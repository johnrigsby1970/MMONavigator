using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MMONavigator.Models;

public class MapPoint : INotifyPropertyChanged {
    private double _x;
    public double X {
        get => _x;
        set { _x = value; OnPropertyChanged(); }
    }

    private double _y;
    public double Y {
        get => _y;
        set { _y = value; OnPropertyChanged(); }
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

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class MapSettings : INotifyPropertyChanged {
    private string? _imagePath;
    public string? ImagePath {
        get => _imagePath;
        set { _imagePath = value; OnPropertyChanged(); }
    }

    private MapPoint _point1 = new();
    public MapPoint Point1 {
        get => _point1;
        set { _point1 = value; OnPropertyChanged(); }
    }

    private MapPoint _point2 = new();
    public MapPoint Point2 {
        get => _point2;
        set { _point2 = value; OnPropertyChanged(); }
    }

    private bool _isCalibrated;
    public bool IsCalibrated {
        get => _isCalibrated;
        set { _isCalibrated = value; OnPropertyChanged(); }
    }

    private double _zoomLevel = 1.0;
    public double ZoomLevel {
        get => _zoomLevel;
        set { _zoomLevel = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
