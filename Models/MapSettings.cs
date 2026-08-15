using CommunityToolkit.Mvvm.ComponentModel;

namespace MMONavigator.Models;

public partial class MapPoint : ObservableObject {
    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    [ObservableProperty]
    private double _pixelX;

    [ObservableProperty]
    private double _pixelY;
}

public partial class MapSettings : ObservableObject {
    [ObservableProperty]
    private string? _imagePath;

    [ObservableProperty]
    private MapPoint _point1 = new();

    [ObservableProperty]
    private MapPoint _point2 = new();

    [ObservableProperty]
    private bool _isCalibrated;

    [ObservableProperty]
    private WindowPlacement _placement = new();

    [ObservableProperty]
    private bool _showLocations;

    [ObservableProperty]
    private bool _showCalibrationMarkers = true;

    [ObservableProperty]
    private bool _showBreadcrumb = true;

    [ObservableProperty]
    private bool _showFogOfWar;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InverseZoom))]
    private double _zoomLevel = 1.0;

    // Computed property dependent on ZoomLevel
    public double InverseZoom => 1.0 / ZoomLevel;
}
