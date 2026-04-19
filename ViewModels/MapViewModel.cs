using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MMONavigator.Models;
using MMONavigator.Services;

namespace MMONavigator.ViewModels;

public class MapViewModel : INotifyPropertyChanged {
    private MapSettings _settings;
    private CoordinateSystem _coordinateSystem = CoordinateSystem.RightHanded;
    private CoordinateData? _currentPosition;
    private CoordinateData? _targetPosition;
    private string? _currentCoordinatesLabel;
    private string? _hoverCoordinatesLabel;
    private BitmapImage? _mapImage;
    private double _markerX;
    private double _markerY;
    private Visibility _markerVisibility = Visibility.Collapsed;
    private double _markerHeading;
    private Visibility _headingVisibility = Visibility.Collapsed;
    private double _targetMarkerX;
    private double _targetMarkerY;
    private Visibility _targetMarkerVisibility = Visibility.Collapsed;
    private ObservableCollection<MapLocation> _locations = new();

    public ObservableCollection<MapLocation> Locations {
        get => _locations;
        set {
            _locations = value;
            OnPropertyChanged();
            UpdateMarkers();
        }
    }

    public bool ShowLocations {
        get => _settings.IsCalibrated && _settings.ShowLocations;
        set {
            if (_settings.ShowLocations != value) {
                _settings.ShowLocations = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ShowCalibrationMarkers {
        get => _settings.IsCalibrated && _settings.ShowCalibrationMarkers;
        set {
            if (_settings.ShowCalibrationMarkers != value) {
                _settings.ShowCalibrationMarkers = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isHovered;
    public bool IsHovered {
        get => _isHovered;
        set {
            if (_isHovered != value) {
                _isHovered = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EffectiveOpacity));
                OnPropertyChanged(nameof(UIVisibility));
            }
        }
    }

    public double EffectiveOpacity => IsHovered ? 1.0 : Opacity;

    public Visibility UIVisibility => (IsHovered || Opacity >= 1.0) ? Visibility.Visible : Visibility.Collapsed;

    public double Opacity {
        get => _settings.Opacity;
        set {
            if (_settings.Opacity != value) {
                _settings.Opacity = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EffectiveOpacity));
                OnPropertyChanged(nameof(UIVisibility));
            }
        }
    }

    public event Action<CoordinateData>? DestinationSelected;
    public event Action<CoordinateData>? PinRequested;

    public void RequestPin(CoordinateData coords) {
        PinRequested?.Invoke(coords);
    }

    public MapViewModel(MapSettings settings) {
        _settings = settings;
        _settings.PropertyChanged += Settings_PropertyChanged;
        _settings.Point1.PropertyChanged += MapPoint_PropertyChanged;
        _settings.Point2.PropertyChanged += MapPoint_PropertyChanged;
        LoadImage();
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(MapSettings.ImagePath)) {
            LoadImage();
        }
        else if (e.PropertyName == nameof(MapSettings.IsCalibrated)) {
            OnPropertyChanged(nameof(ShowLocations));
            UpdateMarkers();
        }
        else if (e.PropertyName == nameof(MapSettings.Point1) || e.PropertyName == nameof(MapSettings.Point2)) {
            if (e.PropertyName == nameof(MapSettings.Point1)) {
                _settings.Point1.PropertyChanged -= MapPoint_PropertyChanged;
                _settings.Point1.PropertyChanged += MapPoint_PropertyChanged;
            } else {
                _settings.Point2.PropertyChanged -= MapPoint_PropertyChanged;
                _settings.Point2.PropertyChanged += MapPoint_PropertyChanged;
            }
            UpdateMarkers();
        }
        else if (e.PropertyName == nameof(MapSettings.ZoomLevel)) {
            UpdateMarkers();
        }
        else if (e.PropertyName == nameof(MapSettings.ShowLocations)) {
            OnPropertyChanged(nameof(ShowLocations));
        }
        else if (e.PropertyName == nameof(MapSettings.ShowCalibrationMarkers)) {
            OnPropertyChanged(nameof(ShowCalibrationMarkers));
        }
        else if (e.PropertyName == nameof(MapSettings.Opacity)) {
            OnPropertyChanged(nameof(Opacity));
            OnPropertyChanged(nameof(EffectiveOpacity));
            OnPropertyChanged(nameof(UIVisibility));
        }
    }

    public MapSettings Settings {
        get => _settings;
        set {
            if (_settings != null) {
                _settings.PropertyChanged -= Settings_PropertyChanged;
                _settings.Point1.PropertyChanged -= MapPoint_PropertyChanged;
                _settings.Point2.PropertyChanged -= MapPoint_PropertyChanged;
            }
            _settings = value;
            if (_settings != null) {
                _settings.PropertyChanged += Settings_PropertyChanged;
                _settings.Point1.PropertyChanged += MapPoint_PropertyChanged;
                _settings.Point2.PropertyChanged += MapPoint_PropertyChanged;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowLocations));
            OnPropertyChanged(nameof(ShowCalibrationMarkers));
            UpdateMarkers();
            LoadImage();
        }
    }

    private void MapPoint_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        // Trigger notification on the Settings property itself so that MainViewModel.Profile_PropertyChanged
        // picks up the change and calls SaveSettings().
        OnPropertyChanged(nameof(Settings)); 
        UpdateMarkers();
    }

    public CoordinateSystem CoordinateSystem {
        get => _coordinateSystem;
        set {
            if (_coordinateSystem != value) {
                _coordinateSystem = value;
                OnPropertyChanged();
                UpdateMarkers();
            }
        }
    }

    public CoordinateData? CurrentPosition {
        get => _currentPosition;
        set {
            _currentPosition = value;
            OnPropertyChanged();
            UpdateMarkers();
        }
    }

    public CoordinateData? TargetPosition {
        get => _targetPosition;
        set {
            _targetPosition = value;
            OnPropertyChanged();
            UpdateMarkers();
        }
    }

    public string? CurrentCoordinatesLabel {
        get => _currentCoordinatesLabel;
        set {
            _currentCoordinatesLabel = value;
            OnPropertyChanged();
        }
    }

    public string? HoverCoordinatesLabel {
        get => _hoverCoordinatesLabel;
        set {
            _hoverCoordinatesLabel = value;
            OnPropertyChanged();
        }
    }

    public BitmapImage? MapImage {
        get => _mapImage;
        private set { _mapImage = value; OnPropertyChanged(); }
    }

    public double MarkerX {
        get => _markerX;
        private set {
            if (Math.Abs(_markerX - value) > 0.0001) {
                _markerX = value;
                OnPropertyChanged();
            }
        }
    }

    public double MarkerY {
        get => _markerY;
        private set {
            if (Math.Abs(_markerY - value) > 0.0001) {
                _markerY = value;
                OnPropertyChanged();
            }
        }
    }

    public Visibility MarkerVisibility {
        get => _markerVisibility;
        private set { _markerVisibility = value; OnPropertyChanged(); }
    }

    public double MarkerHeading {
        get => _markerHeading;
        private set {
            if (Math.Abs(_markerHeading - value) > 0.0001) {
                _markerHeading = value;
                OnPropertyChanged();
            }
        }
    }

    public Visibility HeadingVisibility {
        get => _headingVisibility;
        private set { _headingVisibility = value; OnPropertyChanged(); }
    }

    public double TargetMarkerX {
        get => _targetMarkerX;
        private set {
            if (Math.Abs(_targetMarkerX - value) > 0.0001) {
                _targetMarkerX = value;
                OnPropertyChanged();
            }
        }
    }

    public double TargetMarkerY {
        get => _targetMarkerY;
        private set {
            if (Math.Abs(_targetMarkerY - value) > 0.0001) {
                _targetMarkerY = value;
                OnPropertyChanged();
            }
        }
    }

    public Visibility TargetMarkerVisibility {
        get => _targetMarkerVisibility;
        private set { _targetMarkerVisibility = value; OnPropertyChanged(); }
    }

    private void LoadImage() {
        if (string.IsNullOrEmpty(_settings.ImagePath)) {
            MapImage = null;
            UpdateMarkers();
            return;
        }

        try {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(_settings.ImagePath);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            MapImage = image;
            UpdateMarkers();
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"Error loading map image: {ex.Message}");
            MapImage = null;
            UpdateMarkers();
        }
    }

    public void UpdateMarkers() {
        if (!_settings.IsCalibrated || MapImage == null) {
            MarkerVisibility = Visibility.Collapsed;
            TargetMarkerVisibility = Visibility.Collapsed;
            foreach (var loc in Locations) {
                loc.Visibility = Visibility.Collapsed;
            }
            return;
        }

        if (CurrentPosition.HasValue) {
            (MarkerX, MarkerY, MarkerVisibility) = CalculatePixelPosition(CurrentPosition.Value);
            if (CurrentPosition.Value.Heading.HasValue) {
                MarkerHeading = CalculatePixelHeading(CurrentPosition.Value.Heading.Value);
                HeadingVisibility = MarkerVisibility;
            } else {
                HeadingVisibility = Visibility.Collapsed;
            }
        } else {
            MarkerVisibility = Visibility.Collapsed;
            HeadingVisibility = Visibility.Collapsed;
        }

        if (TargetPosition.HasValue) {
            (TargetMarkerX, TargetMarkerY, TargetMarkerVisibility) = CalculatePixelPosition(TargetPosition.Value);
        } else {
            TargetMarkerVisibility = Visibility.Collapsed;
        }

        foreach (var loc in Locations) {
            // We use the default "x z y d" because ScrubbedCoordinates are always flattened to numbers
            if (Scrubber.TryParse(loc.Coordinates, "x z y d", out var coords)) {
                var (x, y, vis) = CalculatePixelPosition(coords);
                if (Math.Abs(loc.PixelX - x) > 0.1) loc.PixelX = x;
                if (Math.Abs(loc.PixelY - y) > 0.1) loc.PixelY = y;
                if (loc.Visibility != vis) loc.Visibility = vis;
            } else {
                if (loc.Visibility != Visibility.Collapsed) loc.Visibility = Visibility.Collapsed;
            }
        }
    }

    private (double x, double y, Visibility vis) CalculatePixelPosition(CoordinateData pos) {
        try {
            double x1 = _settings.Point1.X;
            double y1 = _settings.Point1.Y;
            double px1 = _settings.Point1.PixelX;
            double py1 = _settings.Point1.PixelY;

            double x2 = _settings.Point2.X;
            double y2 = _settings.Point2.Y;
            double px2 = _settings.Point2.PixelX;
            double py2 = _settings.Point2.PixelY;

            double dx = x2 - x1;
            double dy = y2 - y1;
            double dpx = px2 - px1;
            double dpy = py1 - py2; // Screen Y is inverted compared to Game Y

            if (Math.Abs(dx) < 0.0001 && Math.Abs(dy) < 0.0001) {
                return (0, 0, Visibility.Collapsed);
            }

            double dReal = Math.Sqrt(dx * dx + dy * dy);
            double dPixel = Math.Sqrt(dpx * dpx + dpy * dpy);

            if (dReal < 0.0001) {
                return (0, 0, Visibility.Collapsed);
            }

            double scale = dPixel / dReal;
            double angleReal = Math.Atan2(dy, dx);
            double anglePixel = Math.Atan2(dpy, dpx);
            double rotation = anglePixel - angleReal;

            double curDx = pos.X - x1;
            double curDy = pos.Y - y1;

            if (CoordinateSystem == CoordinateSystem.LeftHanded) {
                curDx = -curDx;
            }

            double cosR = Math.Cos(rotation);
            double sinR = Math.Sin(rotation);

            double rotX = curDx * cosR - curDy * sinR;
            double rotY = curDx * sinR + curDy * cosR;

            double px = px1 + rotX * scale;
            double py = py1 - rotY * scale;

            if (px >= -10 && px <= MapImage.PixelWidth + 10 && py >= -10 && py <= MapImage.PixelHeight + 10) {
                return (px, py, Visibility.Visible);
            } else {
                return (0, 0, Visibility.Collapsed);
            }
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"Error updating marker position: {ex.Message}");
            return (0, 0, Visibility.Collapsed);
        }
    }

    private double CalculatePixelHeading(double gameHeading) {
        try {
            double x1 = _settings.Point1.X;
            double y1 = _settings.Point1.Y;
            double px1 = _settings.Point1.PixelX;
            double py1 = _settings.Point1.PixelY;

            double x2 = _settings.Point2.X;
            double y2 = _settings.Point2.Y;
            double px2 = _settings.Point2.PixelX;
            double py2 = _settings.Point2.PixelY;

            double dx = x2 - x1;
            double dy = y2 - y1;
            double dpx = px2 - px1;
            double dpy = py1 - py2;

            if (Math.Abs(dx) < 0.0001 && Math.Abs(dy) < 0.0001) return 0;

            double angleReal = Math.Atan2(dy, dx);
            double anglePixel = Math.Atan2(dpy, dpx);
            double rotation = anglePixel - angleReal;

            // gameHeading: 0 is North (+Y), 90 is East (+X)
            // We want angle in radians where 0 is +X, PI/2 is +Y (standard Cartesian)
            // gameHeading 0 -> angle PI/2
            // gameHeading 90 -> angle 0
            // angle = PI/2 - gameHeadingRad
            double gameHeadingRad = gameHeading * (Math.PI / 180.0);
            double cartesianAngle = (Math.PI / 2.0) - gameHeadingRad;

            if (CoordinateSystem == CoordinateSystem.LeftHanded) {
                // In left-handed, +X is West. gameHeading 90 is still East (+X in game)
                // but our dx was negated in GetDirection? No, NavigationCalculator says:
                // if (coordinateSystem == CoordinateSystem.LeftHanded) dx = -dx;
                // This means "game +X" is actually "-X in Cartesian".
                // So if facing East (90), in Cartesian it's facing West (PI).
                cartesianAngle = Math.PI - cartesianAngle;
            }

            double rotatedAngle = cartesianAngle + rotation;

            // Now convert back to degrees for RotateTransform
            // WPF RotateTransform: 0 is Up (+Y in Cartesian? No, 0 is Up in WPF too but clockwise)
            // In WPF, 0 degrees is (0, -1). 90 degrees is (1, 0).
            // Cartesian: 0 is (1, 0), PI/2 is (0, 1).
            // WPF Angle = 90 - CartesianAngleDeg
            double rotatedAngleDeg = rotatedAngle * (180.0 / Math.PI);
            return 90 - rotatedAngleDeg;
        } catch {
            return 0;
        }
    }

    public void UpdateHoverCoordinates(double px, double py) {
        if (!_settings.IsCalibrated || MapImage == null) {
            HoverCoordinatesLabel = string.Empty;
            return;
        }

        try {
            double x1 = _settings.Point1.X;
            double y1 = _settings.Point1.Y;
            double px1 = _settings.Point1.PixelX;
            double py1 = _settings.Point1.PixelY;

            double x2 = _settings.Point2.X;
            double y2 = _settings.Point2.Y;
            double px2 = _settings.Point2.PixelX;
            double py2 = _settings.Point2.PixelY;

            double dx = x2 - x1;
            double dy = y2 - y1;
            double dpx = px2 - px1;
            double dpy = py1 - py2; // Screen Y is inverted compared to Game Y

            if (Math.Abs(dpx) < 0.0001 && Math.Abs(dpy) < 0.0001) {
                HoverCoordinatesLabel = string.Empty;
                return;
            }

            double dReal = Math.Sqrt(dx * dx + dy * dy);
            double dPixel = Math.Sqrt(dpx * dpx + dpy * dpy);

            if (dPixel < 0.0001) {
                HoverCoordinatesLabel = string.Empty;
                return;
            }

            double scale = dReal / dPixel;
            double angleReal = Math.Atan2(dy, dx);
            double anglePixel = Math.Atan2(dpy, dpx);
            double rotation = angleReal - anglePixel;

            double curDpx = px - px1;
            double curDpy = py1 - py; // Screen Y is inverted compared to Game Y

            double cosR = Math.Cos(rotation);
            double sinR = Math.Sin(rotation);

            double rotX = curDpx * cosR - curDpy * sinR;
            double rotY = curDpx * sinR + curDpy * cosR;

            double x = x1 + rotX * scale;
            double y = y1 + rotY * scale;

            if (CoordinateSystem == CoordinateSystem.LeftHanded) {
                x = x1 - rotX * scale;
            }

            HoverCoordinatesLabel = $"Cursor: {x:F1}, {y:F1}";
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"Error calculating hover coordinates: {ex.Message}");
            HoverCoordinatesLabel = string.Empty;
        }
    }

    public CoordinateData? GetCoordinatesFromPixels(double px, double py) {
        if (!_settings.IsCalibrated || MapImage == null) {
            return null;
        }

        try {
            double x1 = _settings.Point1.X;
            double y1 = _settings.Point1.Y;
            double px1 = _settings.Point1.PixelX;
            double py1 = _settings.Point1.PixelY;

            double x2 = _settings.Point2.X;
            double y2 = _settings.Point2.Y;
            double px2 = _settings.Point2.PixelX;
            double py2 = _settings.Point2.PixelY;

            double dx = x2 - x1;
            double dy = y2 - y1;
            double dpx = px2 - px1;
            double dpy = py1 - py2;

            if (Math.Abs(dpx) < 0.0001 && Math.Abs(dpy) < 0.0001) {
                return null;
            }

            double dReal = Math.Sqrt(dx * dx + dy * dy);
            double dPixel = Math.Sqrt(dpx * dpx + dpy * dpy);

            if (dPixel < 0.0001) {
                return null;
            }

            double scale = dReal / dPixel;
            double angleReal = Math.Atan2(dy, dx);
            double anglePixel = Math.Atan2(dpy, dpx);
            double rotation = angleReal - anglePixel;

            double curDpx = px - px1;
            double curDpy = py1 - py;

            double cosR = Math.Cos(rotation);
            double sinR = Math.Sin(rotation);

            double rotX = curDpx * cosR - curDpy * sinR;
            double rotY = curDpx * sinR + curDpy * cosR;

            double x = x1 + rotX * scale;
            double y = y1 + rotY * scale;

            if (CoordinateSystem == CoordinateSystem.LeftHanded) {
                x = x1 - rotX * scale;
            }

            return new CoordinateData(x, y, null, null);
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"Error calculating coordinates from pixels: {ex.Message}");
            return null;
        }
    }

    public void SelectDestination(CoordinateData coords) {
        TargetPosition = coords;
        UpdateMarkers();
        DestinationSelected?.Invoke(coords);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
