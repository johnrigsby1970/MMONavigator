using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
    private WriteableBitmap? _breadcrumbImage;
    private WriteableBitmap? _fogImage;
    private double _markerX;
    private double _markerY;
    private Visibility _currentPositionMarkerVisibility = Visibility.Collapsed;
    private double _markerHeading;
    private Visibility _headingVisibility = Visibility.Collapsed;
    private double _targetMarkerX;
    private double _targetMarkerY;
    private Visibility _targetMarkerVisibility = Visibility.Collapsed;
    private ObservableCollection<MapLocation> _locations = new();
    private bool _loadingFile;
    private DispatcherTimer? _fadeTimer;
    
    public ObservableCollection<MapLocation> Locations {
        get => _locations;
        set {
            _locations = value;
            OnPropertyChanged();
            UpdateMarkers();
        }
    }

    public bool IsLoadingFile {
        get => _loadingFile;
        set {
            if (_loadingFile != value) {
                _loadingFile = value;
                OnPropertyChanged();
            }
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

    public bool ShowBreadcrumb {
        get =>  _settings.IsCalibrated && _settings.ShowBreadcrumb && BreadcrumbImage!=null;
        set {
            if (_settings.ShowBreadcrumb != value) {
                _settings.ShowBreadcrumb = value;
                
                if (value) {
                    StartFading();
                }
                else {
                    StopFading();
                }
                
                OnPropertyChanged();
            }
        }
    }
    
    public bool ShowFogOfWar {
        get => _settings.ShowFogOfWar;
        set {
            if (_settings.ShowFogOfWar != value) {
                _settings.ShowFogOfWar = value;
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
                OnPropertyChanged(nameof(EffectiveBackgroundBrush));
                OnPropertyChanged(nameof(EffectiveTransparentBrush));
                OnPropertyChanged(nameof(EffectiveTransparent));
            }
        }
    }

    private string? _mapPath;

    public string? MapPath {
        get => _mapPath;
        set {
            if (_mapPath != value) {
                _mapPath = value;
                OnPropertyChanged();
            }
        }
    }

    private string? _mapName;

    public string? MapName {
        get => _mapName;
        set {
            if (_mapName != value) {
                if (string.IsNullOrEmpty(value)) {
                    _mapName = string.Empty;
                }
                else {
                    _mapName = System.IO.Path.GetFileName(value);
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(AppTitle));
            }
        }
    }

    public string? AppTitle =>
        string.IsNullOrWhiteSpace(_mapName) ? "Map Overlay" : "Map Overlay" + " [" + _mapName + "]";

    // In your ViewModel
    public SolidColorBrush EffectiveBackgroundBrush {
        get {
            // Assume _opacityLevel is your 0.0 - 1.0 double
            byte alpha = (byte)(EffectiveOpacity * 255);
            return new SolidColorBrush(
                System.Windows.Media.Color.FromArgb(alpha, 62, 62, 66)); // Black with variable transparency
        }
    }

    public SolidColorBrush EffectiveTransparentBrush {
        get {
            // Assume _opacityLevel is your 0.0 - 1.0 double
            byte alpha = (byte)(1 * 255);
            return !IsHovered && Opacity < 1
                ? System.Windows.Media.Brushes.Transparent
                : new SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(alpha, 62, 62, 66)); // Black with variable transparency
        }
    }

    public double EffectiveTransparent => IsHovered ? 1.0 : 0;

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

    private AppSettings _appSettings;

    public AppSettings AppSettings {
        get => _appSettings;
        set {
            _appSettings = value;
            OnPropertyChanged();
        }
    }

    public MapViewModel(MapSettings settings, AppSettings appSettings) {
        _settings = settings;
        _appSettings = appSettings;
        _settings.PropertyChanged += Settings_PropertyChanged;
        _settings.Point1.PropertyChanged += MapPoint_PropertyChanged;
        _settings.Point2.PropertyChanged += MapPoint_PropertyChanged;
        LoadImage();
        if (ShowBreadcrumb) {
            _fadeTimer = new DispatcherTimer(DispatcherPriority.Background) {
                Interval = TimeSpan.FromSeconds(2)
            };
            _fadeTimer.Tick += (s, e) => FadeTrail(0.92);
            _fadeTimer.Start();
        }
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(MapSettings.ImagePath)) {
            //LoadImage();
        }
        else if (e.PropertyName == nameof(MapSettings.IsCalibrated)) {
            OnPropertyChanged(nameof(ShowLocations));
            UpdateMarkers();
        }
        else if (e.PropertyName == nameof(MapSettings.Point1) || e.PropertyName == nameof(MapSettings.Point2)) {
            if (e.PropertyName == nameof(MapSettings.Point1)) {
                _settings.Point1.PropertyChanged -= MapPoint_PropertyChanged;
                _settings.Point1.PropertyChanged += MapPoint_PropertyChanged;
            }
            else {
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
            LoadImage();
            UpdateMarkers();
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

    public WriteableBitmap? BreadcrumbImage {
        get => _breadcrumbImage;
        private set {
            _breadcrumbImage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowBreadcrumb));
        }
    }
    
    public WriteableBitmap? FogImage {
        get => _fogImage;
        private set {
            _fogImage = value;
            OnPropertyChanged();
        }
    }

    private string? _fogOfWarFilePath;

    public string? FogOfWarFilePath {
        get => _fogOfWarFilePath;
        set {
            if (_fogOfWarFilePath != value) {
                _fogOfWarFilePath = value;
                OnPropertyChanged();
            }
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
        private set {
            _mapImage = value;
            OnPropertyChanged();
        }
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

    public Visibility CurrentPositionMarkerVisibility {
        get => _currentPositionMarkerVisibility;
        private set {
            _currentPositionMarkerVisibility = value;
            OnPropertyChanged();
        }
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
        private set {
            _headingVisibility = value;
            OnPropertyChanged();
        }
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
        private set {
            _targetMarkerVisibility = value;
            OnPropertyChanged();
        }
    }

    private void StartFading() 
    {
        _fadeTimer = new DispatcherTimer(DispatcherPriority.Background);
        _fadeTimer.Interval = TimeSpan.FromSeconds(2);
        _fadeTimer.Tick += FadeTrail_Tick;
        _fadeTimer.Start();
    }

    private void FadeTrail_Tick(object? sender, EventArgs eventArgs) {
        FadeTrail(0.92);
    }
    
    public void StopFading() 
    {
        if (_fadeTimer != null) 
        {
            _fadeTimer.Stop();
            _fadeTimer.Tick -= FadeTrail_Tick;
            _fadeTimer = null;
        }
    }
    
    public void LoadImage() {
        _loadingFile = true;
        if (string.IsNullOrEmpty(_settings.ImagePath)) {
            MapName = string.Empty;
            MapPath = string.Empty;
            MapImage = null;
            if (FogImage != null) {
                //save old fog image
                if (!string.IsNullOrEmpty(FogOfWarFilePath) && FogImage != null) {
                    ImageHelpers.SaveWriteableBitMap(FogOfWarFilePath, FogImage.Clone());
                }
            }

            FogOfWarFilePath = string.Empty;
            FogImage = null;
            BreadcrumbImage = null;
            _loadingFile = false;
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
            var fogFilePath = Path.ChangeExtension(_settings.ImagePath, ".fog");
            if (File.Exists(fogFilePath)) {
                var fogImage = new BitmapImage();
                fogImage.BeginInit();
                fogImage.UriSource = new Uri(fogFilePath);
                fogImage.CacheOption = BitmapCacheOption.OnLoad;
                fogImage.EndInit();
                fogImage.CreateOptions = BitmapCreateOptions.None;
                if (FogImage != null) {
                    //save old fog image
                    if (!string.IsNullOrEmpty(FogOfWarFilePath) && FogImage != null) {
                        ImageHelpers.SaveWriteableBitMap(FogOfWarFilePath, FogImage.Clone());
                    }
                }

                var fogFile = new WriteableBitmap(fogImage);
                FogOfWarFilePath = fogFilePath; //This will save the previous, if exists.
                FogImage = fogFile;
            }
            else {
                if (FogImage != null) {
                    //save old fog image
                    if (!string.IsNullOrEmpty(FogOfWarFilePath) && FogImage != null) {
                        ImageHelpers.SaveWriteableBitMap(FogOfWarFilePath, FogImage.Clone());
                    }
                }

                FogOfWarFilePath = fogFilePath; //This will save the previous, if exists.
                FogImage = ImageHelpers.CreateBlackBitmap(MapImage);
            }
            BreadcrumbImage = ImageHelpers.CreateTransparentBitmap(MapImage);
            MapPath = _settings.ImagePath;
            MapName = _settings.ImagePath;
            LoadImageConfig(_settings.ImagePath);
            _loadingFile = false;
            UpdateMarkers();
        }
        catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"Error loading map image: {ex.Message}");
            MapName = string.Empty;
            MapPath = string.Empty;
            MapImage = null;
            BreadcrumbImage = null;
            Settings.ImagePath = string.Empty;
            _loadingFile = false;
            UpdateMarkers();
        }
        finally {
            _loadingFile = false;
        }
    }

    public bool LoadImageConfig(string? imagePath) {
        bool calibrated = false;
        if (imagePath == null) return calibrated;
        var configPath = Path.ChangeExtension(imagePath, ".json");

        if (File.Exists(configPath)) {
            try {
                var json = File.ReadAllText(configPath);
                var savedSettings = JsonSerializer.Deserialize<MapSettings>(json);
                if (savedSettings != null) {
                    Settings.ImagePath = imagePath;
                    Settings.Point1.X = savedSettings.Point1.X;
                    Settings.Point1.Y = savedSettings.Point1.Y;
                    Settings.Point1.PixelX = savedSettings.Point1.PixelX;
                    Settings.Point1.PixelY = savedSettings.Point1.PixelY;
                    Settings.Point2.X = savedSettings.Point2.X;
                    Settings.Point2.Y = savedSettings.Point2.Y;
                    Settings.Point2.PixelX = savedSettings.Point2.PixelX;
                    Settings.Point2.PixelY = savedSettings.Point2.PixelY;
                    Settings.IsCalibrated = savedSettings.IsCalibrated;
                    Settings.ZoomLevel = savedSettings.ZoomLevel;
                    Settings.ShowLocations = savedSettings.ShowLocations;
                    Settings.ShowCalibrationMarkers = savedSettings.ShowCalibrationMarkers;
                    
                    calibrated = true;
                }
            }
            catch (Exception ex) {
                Settings.ImagePath = imagePath;
                Settings.IsCalibrated = false;
                calibrated = false;
            }
        }
        else {
            Settings.ImagePath = imagePath;
            Settings.IsCalibrated = false;
            calibrated = false;
        }
        return calibrated;
    }

    private double GetPixelsPerFoot() {
        double dx = _settings.Point2.X - _settings.Point1.X;
        double dy = _settings.Point2.Y - _settings.Point1.Y;
        double dpx = _settings.Point2.PixelX - _settings.Point1.PixelX;
        double dpy = _settings.Point1.PixelY - _settings.Point2.PixelY; // Account for inverted Y

        double dReal = Math.Sqrt(dx * dx + dy * dy);
        double dPixel = Math.Sqrt(dpx * dpx + dpy * dpy);

        return dPixel / dReal; // This is your pixels-per-foot scale
    }

    private void PunchTransparentCircle(double centerX, double centerY, double radiusInPixels) {
        if (FogImage == null) return;

        FogImage.Lock();
        try {
            int radiusSq = (int)(radiusInPixels * radiusInPixels); //punch
            int stride = FogImage.BackBufferStride;
            IntPtr pBackBuffer = FogImage.BackBuffer;

            // Loop through a bounding box around the circle for efficiency
            for (int y = (int)(centerY - radiusInPixels); y < centerY + radiusInPixels; y++) {
                for (int x = (int)(centerX - radiusInPixels); x < centerX + radiusInPixels; x++) {
                    // Check if pixel is within image bounds and within circle radius
                    if (x >= 0 && x < FogImage.PixelWidth && y >= 0 && y < FogImage.PixelHeight) {
                        double dx = x - centerX;
                        double dy = y - centerY;
                        if (dx * dx + dy * dy <= radiusSq) {
                            // Set the pixel to Transparent (Alpha = 0)
                            // In Bgra32, the bytes are B, G, R, A. A is at index 3
                            unsafe {
                                byte* pPixel = (byte*)pBackBuffer + (y * stride) + (x * 4);
                                pPixel[3] = 0; // Alpha = 0 (Transparent)
                            }
                        }
                    }
                }
            }

            // Tell WPF to update the specific rectangle
            //Errors have resulted, so for ease of debug, set local variables
            var xrec = (int)(centerX - radiusInPixels);
            var yrec = (int)(centerY - radiusInPixels);
            var w = (int)(radiusInPixels * 2);
            var h = (int)(radiusInPixels * 2);
            if (xrec > 0 && yrec > 0 && w > 0 && h > 0) {
                FogImage.AddDirtyRect(new Int32Rect(xrec, yrec, w, h));
            }
        }
        finally {
            FogImage.Unlock();
        }
    }
    
    private void PunchBreadcrumbCircle(double centerX, double centerY, double radiusInPixels) {
        if (BreadcrumbImage == null || !ShowBreadcrumb) return;

        BreadcrumbImage.Lock();
        try {
            int radiusSq = (int)(radiusInPixels * radiusInPixels); //punch
            int stride = BreadcrumbImage.BackBufferStride;
            IntPtr pBackBuffer = BreadcrumbImage.BackBuffer;

            // Loop through a bounding box around the circle for efficiency
            for (int y = (int)(centerY - radiusInPixels); y < centerY + radiusInPixels; y++) {
                for (int x = (int)(centerX - radiusInPixels); x < centerX + radiusInPixels; x++) {
                    // Inside the x/y loop, replace your current if condition:
                    double dx = x - centerX;
                    double dy = y - centerY;
                    double distanceSq = dx * dx + dy * dy;

                    if (distanceSq <= radiusSq) {
                        // 1. Calculate how far we are from the center (0.0 to 1.0)
                        double distance = Math.Sqrt(distanceSq);
                        double ratio = distance / radiusInPixels; // 0.0 is center, 1.0 is edge

                        // 2. Use a "Ease Out" function for the fade (e.g., squared or cubic)
                        // 1.0 means opaque at center, 0.0 means transparent at edge
                        double softAlpha = Math.Pow(1.0 - ratio, 1); 
                        //double softAlpha = 1.0;// - Math.Pow(ratio, 4);

                        // 3. Apply the Alpha (scaled to your target intensity, e.g., 128)
                        byte finalAlpha = (byte)(softAlpha * 128);

                        unsafe {
                            byte* pPixel = (byte*)pBackBuffer + (y * stride) + (x * 4);
                            //blend the new Alpha with the existing Alpha.
                            //This makes the trail stay solid (or get slightly more solid)
                            //rather than resetting the transparency.
                            // Get the existing Alpha at this pixel
                            byte currentAlpha = pPixel[3];
    
                            // Add the new alpha to the current one, but don't exceed 255 (Opaque)
                            int newAlpha = Math.Min(255, currentAlpha + 128);
                            
                            pPixel[0] = 255; 
                            pPixel[1] = 255; 
                            pPixel[2] = 255; 
                            pPixel[3] = (byte)newAlpha;
                        }
                    }
                }
            }

            // Tell WPF to update the specific rectangle
            var xrect = Math.Max(0, (int)(centerX - radiusInPixels));
            var yrect = Math.Max(0, (int)(centerY - radiusInPixels));
            // Ensure width/height don't exceed image bounds
            int w = Math.Min((int)(radiusInPixels * 2) + 2, BreadcrumbImage.PixelWidth - xrect);
            int h = Math.Min((int)(radiusInPixels * 2) + 2, BreadcrumbImage.PixelHeight - yrect);
            
            if (xrect > 0 && yrect > 0 && w > 0 && h > 0) {
                BreadcrumbImage.AddDirtyRect(new Int32Rect(xrect, yrect, w, h));
            }
        }
        finally {
            BreadcrumbImage.Unlock();
        }
    }

    private void FadeTrail(double decayFactor) 
    {
        if (BreadcrumbImage == null || !ShowBreadcrumb) return;

        BreadcrumbImage.Lock();
        try {
            int stride = BreadcrumbImage.BackBufferStride;
            int height = BreadcrumbImage.PixelHeight;
            int width = BreadcrumbImage.PixelWidth;
        
            unsafe {
                byte* pBuffer = (byte*)BreadcrumbImage.BackBuffer;

                // Iterate through every pixel
                for (int y = 0; y < height; y++) {
                    byte* pRow = pBuffer + (y * stride);
                    for (int x = 0; x < width; x++) {
                        byte* pPixel = pRow + (x * 4); // Bgra32: B, G, R, A

                        // pPixel[3] is the Alpha channel
                        if (pPixel[3] > 0) {
                            // Apply decay: newAlpha = oldAlpha * decayFactor
                            int newAlpha = (int)(pPixel[3] * decayFactor);
                            pPixel[3] = (byte)Math.Max(0, newAlpha);
                        }
                    }
                }
            }
            // Notify WPF to redraw the entire bitmap
            BreadcrumbImage.AddDirtyRect(new Int32Rect(0, 0, width, height));
        } finally {
            BreadcrumbImage.Unlock();
        }
    }
    
    public void UpdateMarkers() {
        if (_loadingFile) return;
        if (!_settings.IsCalibrated || MapImage == null) {
            CurrentPositionMarkerVisibility = Visibility.Collapsed;
            TargetMarkerVisibility = Visibility.Collapsed;
            foreach (var loc in Locations) {
                loc.Visibility = Visibility.Collapsed;
            }

            return;
        }

        if (CurrentPosition.HasValue) {
            var x = FogOfWarFilePath;
            var y = MapPath;
            (MarkerX, MarkerY, CurrentPositionMarkerVisibility) = CalculatePixelPosition(CurrentPosition.Value);

            if (CurrentPositionMarkerVisibility == Visibility.Visible) {
                double pixelsPerFoot = GetPixelsPerFoot();
                //2.0 for a persons width when doing cartography and wanting to know 
                //for fog of war and how far a person can see in a game lets go with a radius of 15 feet
                double radius = 15; //2.0;
                double radiusInPixels = radius * pixelsPerFoot; // "2 feet" converted to pixels
                double actualRadius =
                    Math.Max(5.0, radiusInPixels); //scale of map may not allow for a 2 foot diameter draw
                PunchTransparentCircle(MarkerX, MarkerY, actualRadius);
                
                var currentScale = Settings.ZoomLevel;
                var effectiveMarkerDiameter = 6 * currentScale;//should end up the same size as the marker
                
                //Should be same size as position marker regardless of zoom
                PunchBreadcrumbCircle(MarkerX, MarkerY,Math.Max(2.0, effectiveMarkerDiameter / 2.0));
            }

            if (CurrentPosition.Value.Heading.HasValue) {
                MarkerHeading = CalculatePixelHeading(CurrentPosition.Value.Heading.Value);
                HeadingVisibility = CurrentPositionMarkerVisibility;
            }
            else {
                HeadingVisibility = Visibility.Collapsed;
            }
        }
        else {
            CurrentPositionMarkerVisibility = Visibility.Collapsed;
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

    // This code uses a 2D Affine Transformation.
    // It is calculating a rotation matrix and a scale factor based on two calibration points.
    // This allows the map to be rotated at any angle relative to the game's coordinate system
    // while still mapping correctly to the pixels.

    //The "Left-Handed" coordinate system by negating curDx. This flips the
    //X-axis across the Y-axis. See: double dpy = py1 - py2;
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

            //Given the size of the image, is the coordinate even on the map?
            if (px >= -10 && px <= MapImage.PixelWidth + 10 && py >= -10 && py <= MapImage.PixelHeight + 10) {
                return (px, py, Visibility.Visible);
            }
            else {
                return (0, 0, Visibility.Collapsed);
            }
        }
        catch (Exception ex) {
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
        }
        catch {
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
        }
        catch (Exception ex) {
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
        }
        catch (Exception ex) {
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