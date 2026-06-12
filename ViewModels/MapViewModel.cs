using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MMONavigator.Models;
using MMONavigator.Services;
using MMONavigator.Helpers;
// ReSharper disable RedundantNameQualifier

namespace MMONavigator.ViewModels;

public class MapViewModel : INotifyPropertyChanged {
    private readonly ISettingsService _settingsService;
    private MapSettings? _settings;
    private CoordinateSystem _coordinateSystem = CoordinateSystem.RightHanded;
    private CoordinateData? _currentPosition;
    private CoordinateData? _targetPosition;
    private string? _currentCoordinatesLabel;
    private string? _hoverCoordinatesLabel;
    private BitmapSource? _mapImage;
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
    private bool _staticMarkersDirty = true;
    private bool _locationMarkersShowing;
    private DispatcherTimer? _fadeTimer;
    private bool _isDrawModeActive;
    private bool _drawModeNeedsCalibration;
    private bool _priorFogState;
    private bool _priorBreadcrumbState;
    private DispatcherTimer? _drawSaveTimer;
    private bool _expandingMap;
    private bool _calibratingNewDrawMap;
    private double? _drawingRadius;
    private int _drawColorIndex;           // 0=white, 1=dodger blue, ..., 12=transparent
    private int _drawSizeMode;             // 0=default, 1=+3, 2=+5, 3=+10, -1=2px fixed
    private bool _drawAntiAlias = true;
    private byte _drawBrushB = 255, _drawBrushG = 255, _drawBrushR = 255;
    private bool _drawLineMode;
    private readonly List<(double X, double Y)> _drawLastPoints = new();
    private const double CursorPositionTopMargin = 5;

    private const double HowFarCanAPersonSee = 30;
    
    // Higher value for the "zoomed in" look
    public double FollowZoomLevel => 2.5;

    private double _previousZoom = 1.0; // Default
    public double PreviousZoom 
    { 
        get => _previousZoom;
        set { _previousZoom = value; OnPropertyChanged(); }
    }

    private double _currentScrollY;
    
    public double CurrentScrollY 
    { 
        get => _currentScrollY;
        set { _currentScrollY = value; OnPropertyChanged(); }
    }
    
    public double CoordinateYPosition
    {
        get 
        {
            // 10 is a small buffer from the top of the screen
            // CurrentScrollY is how far down you've scrolled/panned into the map
            return Math.Max(CursorPositionTopMargin, CurrentScrollY + CursorPositionTopMargin);
        }
    }
    
    
    
// This is the property your Canvas.Top will bind to
    public double StickyTopPosition
    {
        get {
            if (MapImage == null) return CursorPositionTopMargin;
            // 1) Calculate how much empty space is at the top if the image is small
            // (ImageHeight * Zoom) is the actual visual height of the map
            double visualImageHeight = MapImage.Height * (Settings?.ZoomLevel??1);
        
            // If the image is smaller than the window, it's likely centered.
            // The 'top' of the image is at (ViewportHeight - visualImageHeight) / 2
            double imageTopInViewport = (ViewportHeight - visualImageHeight) / 2;

            // 2) If the image is larger than the window, imageTopInViewport will be negative.
            // We want to use the actual top of the image UNLESS it's off-screen.
        
            if (visualImageHeight > ViewportHeight)
            {
                // The image is larger than the window. 
                // We want it to stay at the top (10px padding),
                // but we must account for the fact that the Canvas doesn't scroll.
                return CursorPositionTopMargin;
            }
            else
            {
                // The image is smaller than the window.
                // Move the text to sit exactly at the top of the centered image.
                return Math.Max(CursorPositionTopMargin, imageTopInViewport);
            }
        }
    }
    
    public double StickyLeftPosition
    {
        get
        {
            // This keeps the coordinate box centered horizontally in the viewer
            // (Assuming your coordinate box is roughly 150px wide)
            return (ViewportWidth / 2) - 40;
        }
    }
    // StickyTopPosition, all about that cursor position

    private double _viewportHeight;
    public double ViewportHeight 
    { 
        get => _viewportHeight; 
        set { _viewportHeight = value; OnPropertyChanged(); OnPropertyChanged(nameof(StickyTopPosition)); } 
    }
    
    private double _viewportWidth;
    public double ViewportWidth 
    { 
        get => _viewportWidth; 
        set { _viewportWidth = value; OnPropertyChanged(); OnPropertyChanged(nameof(StickyLeftPosition)); } 
    }
    
    private double _horizontalScrollOffset;
    public double HorizontalScrollOffset 
    { 
        get => _horizontalScrollOffset; 
        set { _horizontalScrollOffset = value; OnPropertyChanged(); } 
    }
    
    private double _verticalScrollOffset;
    public double VerticalScrollOffset 
    { 
        get => _verticalScrollOffset; 
        set { _verticalScrollOffset = value; OnPropertyChanged(); } 
    }

    public ObservableCollection<MapLocation> Locations {
        get => _locations;
        set {
            _locations = value;
            _staticMarkersDirty = true;
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
        get => _settings is { IsCalibrated: true, ShowLocations: true };
        set {
            if(_settings != null && _settings.ShowLocations != value && value){
                _staticMarkersDirty = true;
            }
            if (_settings != null && _settings.ShowLocations != value) {
                _settings.ShowLocations = value;
                OnPropertyChanged();
            }
        }
    }
    
    private bool _isFollowModeActive;
    public bool IsFollowModeActive
    {
        get => _isFollowModeActive;
        set { _isFollowModeActive = value; OnPropertyChanged(); }
    }

    public bool IsDrawModeActive {
        get => _isDrawModeActive;
        set {
            if (_isDrawModeActive != value) {
                _isDrawModeActive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AppTitle));
                OnPropertyChanged(nameof(DrawShowSmallBrush));
            }
        }
    }

    public int DrawColorIndex {
        get => _drawColorIndex;
        set { _drawColorIndex = value; OnPropertyChanged(); }
    }

    public int DrawSizeMode {
        get => _drawSizeMode;
        set { _drawSizeMode = value; _drawingRadius = null; OnPropertyChanged(); }
    }

    public bool DrawAntiAlias {
        get => _drawAntiAlias;
        set { _drawAntiAlias = value; OnPropertyChanged(); }
    }

    public bool DrawShowSmallBrush {
        get {
            try { return GetBaseDrawRadius() >= 5.0; }
            catch { return false; }
        }
    }

    public bool DrawLineMode {
        get => _drawLineMode;
        set { _drawLineMode = value; OnPropertyChanged(); }
    }

    private void PushDrawPoint(double x, double y) {
        _drawLastPoints.Add((x, y));
        if (_drawLastPoints.Count > 5) _drawLastPoints.RemoveAt(0);
    }

    public void SetDrawColor(int index) {
        DrawColorIndex = index;
        (_drawBrushB, _drawBrushG, _drawBrushR) = index switch {
            1  => ((byte)255, (byte)144, (byte)30),   // Dodger Blue  #1E90FF
            2  => ((byte)0,   (byte)128, (byte)0),    // Green        #008000
            3  => ((byte)255, (byte)255, (byte)0),    // Cyan         #00FFFF
            4  => ((byte)42,  (byte)42,  (byte)165),  // Brown        #A52A2A
            5  => ((byte)140, (byte)180, (byte)210),  // Tan          #D2B48C
            6  => ((byte)0,   (byte)255, (byte)255),  // Yellow       #FFFF00
            7  => ((byte)0,   (byte)165, (byte)255),  // Orange       #FFA500
            8  => ((byte)128, (byte)0,   (byte)128),  // Purple       #800080
            9  => ((byte)0,   (byte)0,   (byte)255),  // Red          #FF0000
            10 => ((byte)0,   (byte)0,   (byte)0),    // Black        #000000
            11 => ((byte)128, (byte)128, (byte)128),  // Gray         #808080
            _  => ((byte)255, (byte)255, (byte)255),  // White (default / transparent ignored)
        };
    }

    private void ResetDrawSettings() {
        _drawBrushB = 255; _drawBrushG = 255; _drawBrushR = 255;
        _drawColorIndex = 0;
        _drawSizeMode = 0;
        _drawAntiAlias = true;
        _drawLineMode = false;
        _drawLastPoints.Clear();
        OnPropertyChanged(nameof(DrawColorIndex));
        OnPropertyChanged(nameof(DrawSizeMode));
        OnPropertyChanged(nameof(DrawAntiAlias));
        OnPropertyChanged(nameof(DrawLineMode));
        OnPropertyChanged(nameof(DrawShowSmallBrush));
    }

    public bool ShowCalibrationMarkers {
        get => _settings is { IsCalibrated: true, ShowCalibrationMarkers: true };
        set {
            if (_settings != null && _settings.ShowCalibrationMarkers != value) {
                _settings.ShowCalibrationMarkers = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ShowBreadcrumb {
        get =>  _settings is { IsCalibrated: true, ShowBreadcrumb: true } && BreadcrumbImage!=null;
        set {
            if (_settings != null && _settings.ShowBreadcrumb != value) {
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
        get => _settings is { ShowFogOfWar: true };
        set {
            if (_settings != null && _settings.ShowFogOfWar != value) {
                _settings.ShowFogOfWar = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isHovered = true;//default to hovered, let other code deal with setting it to not hovered.
    //The issue is if opacity = 1 and the user decides to set it down even a notch, then the portion
    //of the window with the opacity slider will collapse, thinking it was previously not hovered. 
    //Until opacity is less than 1 and the user afterward switched to not being hovered, then hide. 
    //There will be a slight 300ms delay on the first load, blinking the overall window.
    public const double Tolerance = 0.0001;

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

    public string AppTitle =>
        string.IsNullOrWhiteSpace(_mapName) ? "Map Overlay" :
        IsDrawModeActive ? $"Map Overlay [{_mapName}] [DRAWING]" :
        $"Map Overlay [{_mapName}]";

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
            byte alpha = 1 * 255;
            return !IsHovered && Opacity < 1
                ? System.Windows.Media.Brushes.Transparent
                : new SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(alpha, 62, 62, 66)); // Black with variable transparency
        }
    }

    public double EffectiveTransparent => IsHovered ? 1.0 : 0;

    public double EffectiveOpacity => IsHovered ? 1.0 : string.IsNullOrEmpty(_settings?.ImagePath) ? 1 : Opacity;
    
    // ReSharper disable once InconsistentNaming
    public Visibility UIVisibility => (IsHovered || Opacity >= 1.0 || string.IsNullOrEmpty(_settings?.ImagePath)) ? Visibility.Visible : Visibility.Collapsed;

    public double Opacity {
        get => AppSettings.Opacity;
        set {
            if (Math.Abs(AppSettings.Opacity - value) > Tolerance) {
                AppSettings.Opacity = value;
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

    private AppSettings _appSettings = null!;

    public AppSettings AppSettings {
        get => _appSettings;
        set {
            _appSettings = value;
            OnPropertyChanged();
        }
    }

    public MapViewModel(MapSettings settings, AppSettings appSettings, ISettingsService settingsService) {
        _settingsService = settingsService;
        _settings = settings;
        if (appSettings.MapWindowPlacement == null) {
            appSettings.MapWindowPlacement = new WindowPlacement();
        }
        

        //was it saved minimized?
        if (appSettings.MapWindowPlacement.Height<=50 || appSettings.MapWindowPlacement.State == WindowState.Minimized) {
            appSettings.MapWindowPlacement.State = WindowState.Normal;
            appSettings.MapWindowPlacement.Height = 600;
            appSettings.MapWindowPlacement.Width = 800;
        }
        
        AppSettings = appSettings;
        _settings.PropertyChanged += Settings_PropertyChanged;
        _settings.Point1.PropertyChanged += MapPoint_PropertyChanged;
        _settings.Point2.PropertyChanged += MapPoint_PropertyChanged;
        LoadImage();
        if (ShowBreadcrumb) {
            _fadeTimer = new DispatcherTimer(DispatcherPriority.Background) {
                Interval = TimeSpan.FromSeconds(2)
            };
            // ReSharper disable once UnusedParameter.Local
            _fadeTimer.Tick += (_, __) => FadeTrail(0.92);
            _fadeTimer.Start();
        }
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (_calibratingNewDrawMap) return;
        if (e.PropertyName == nameof(MapSettings.ImagePath)) {
            //LoadImage();
        }
        else if (e.PropertyName == nameof(MapSettings.IsCalibrated)) {
            OnPropertyChanged(nameof(ShowLocations));
            _staticMarkersDirty = true;
            UpdateMarkers();
        }
        else if (e.PropertyName == nameof(MapSettings.Point1) || e.PropertyName == nameof(MapSettings.Point2)) {
            if(_settings==null) return;
            if (e.PropertyName == nameof(MapSettings.Point1)) {
                _settings.Point1.PropertyChanged -= MapPoint_PropertyChanged;
                _settings.Point1.PropertyChanged += MapPoint_PropertyChanged;
            }
            else {
                _settings.Point2.PropertyChanged -= MapPoint_PropertyChanged;
                _settings.Point2.PropertyChanged += MapPoint_PropertyChanged;
            }

            _staticMarkersDirty = true;
            UpdateMarkers();
        }
        else if (e.PropertyName == nameof(MapSettings.ZoomLevel)) {
            //_staticMarkersDirty = true; //Despite AI adding this, it should not be necessary to recalculate during zoom
            UpdateMarkers();
        }
        else if (e.PropertyName == nameof(MapSettings.ShowLocations)) {
            OnPropertyChanged(nameof(ShowLocations));
        }
        else if (e.PropertyName == nameof(MapSettings.ShowCalibrationMarkers)) {
            OnPropertyChanged(nameof(ShowCalibrationMarkers));
        }
        else if (e.PropertyName == nameof(AppSettings.Opacity)) {
            OnPropertyChanged(nameof(Opacity));
            OnPropertyChanged(nameof(EffectiveOpacity));
            OnPropertyChanged(nameof(UIVisibility));
        }
    }

    public MapSettings? Settings {
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
        _staticMarkersDirty = true;
        OnPropertyChanged(nameof(Settings));
        UpdateMarkers();
    }

    public void SaveSettings() {
        _settingsService.SaveSettings(AppSettings);
    }
    
        
    public CoordinateSystem CoordinateSystem {
        get => _coordinateSystem;
        set {
            if (_coordinateSystem != value) {
                _coordinateSystem = value;
                _staticMarkersDirty = true;
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
    
    public BitmapSource? MapImage {
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
        _fadeTimer = new DispatcherTimer(DispatcherPriority.Background) {
            Interval = TimeSpan.FromSeconds(2)
        };
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
        if (IsDrawModeActive) return;
        _loadingFile = true;
        if (string.IsNullOrEmpty(_settings?.ImagePath)) {
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
            OnPropertyChanged(nameof(UIVisibility));
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
            Settings ??= new MapSettings();
            Settings.ImagePath = string.Empty;
            OnPropertyChanged(nameof(UIVisibility));
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

        Settings ??= new MapSettings();
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
                    Settings.ShowFogOfWar = savedSettings.ShowFogOfWar;
                    Settings.ShowBreadcrumb = savedSettings.ShowBreadcrumb;
                    
                    OnPropertyChanged(nameof(ShowFogOfWar));
                    OnPropertyChanged(nameof(ShowBreadcrumb));
                    OnPropertyChanged(nameof(ShowCalibrationMarkers));
                    OnPropertyChanged(nameof(ShowLocations));
                    calibrated = true;
                }
            }
            catch (Exception ex) {
                Debug.WriteLine($"Error loading map image config: {ex.Message}");
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

    // private double GetPixelsPerGameUnit() {
    //     if(_settings == null)
    //         return 5;
    //     
    //     double dx = _settings.Point2.X - _settings.Point1.X;
    //     double dy = _settings.Point2.Y - _settings.Point1.Y;
    //     double dpx = _settings.Point2.PixelX - _settings.Point1.PixelX;
    //     double dpy = _settings.Point1.PixelY - _settings.Point2.PixelY; // Account for inverted Y
    //
    //     double dReal = Math.Sqrt(dx * dx + dy * dy);
    //     double dPixel = Math.Sqrt(dpx * dpx + dpy * dpy);
    //
    //     return dPixel / dReal; // This is your pixels-per-foot scale
    // }
    
    private double GetPixelsPerGameUnit() {
        // Fallback default: If 1 pixel = 0.1 game units, then 1 game unit = 10 pixels
        if (_settings == null)
            return 2.0; 

        double dx = _settings.Point2.X - _settings.Point1.X;
        double dy = _settings.Point2.Y - _settings.Point1.Y;
        double dpx = _settings.Point2.PixelX - _settings.Point1.PixelX;
        double dpy = _settings.Point1.PixelY - _settings.Point2.PixelY;

        double dReal = Math.Sqrt(dx * dx + dy * dy); // Distance in game units
        double dPixel = Math.Sqrt(dpx * dpx + dpy * dpy); // Distance in pixels

        return dPixel / dReal; // Returns exactly how many pixels represent 1 game unit
    }

    private void PunchTransparentCircle(double centerX, double centerY, double radiusInPixels) {
        if (FogImage == null) return;

        double dpiScaleX = FogImage.PixelWidth / FogImage.Width;
        double dpiScaleY = FogImage.PixelHeight / FogImage.Height;

        double rawCenterX = centerX * dpiScaleX;
        double rawCenterY = centerY * dpiScaleY;
        double rawRadius = radiusInPixels * dpiScaleX; // Assuming uniform scaling

        FogImage.Lock();
        try {
            int radiusSq = (int)(rawRadius * rawRadius); //punch
            int stride = FogImage.BackBufferStride;
            IntPtr pBackBuffer = FogImage.BackBuffer;

            // Loop through a bounding box around the circle for efficiency
            for (int y = (int)(rawCenterY - rawRadius); y < rawCenterY + rawRadius; y++) {
                for (int x = (int)(rawCenterX - rawRadius); x < rawCenterX + rawRadius; x++) {
                    // Check if pixel is within image bounds and within circle radius
                    if (x >= 0 && x < FogImage.PixelWidth && y >= 0 && y < FogImage.PixelHeight) {
                        double dx = x - rawCenterX;
                        double dy = y - rawCenterY;
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
            int xrect = Math.Max(0, (int)(rawCenterX - rawRadius));
            int yrect = Math.Max(0, (int)(rawCenterY - rawRadius));
            int w = Math.Min((int)(rawRadius * 2) + 1, FogImage.PixelWidth - xrect);
            int h = Math.Min((int)(rawRadius * 2) + 1, FogImage.PixelHeight - yrect);
            
            if (w > 0 && h > 0) {
                try {
                    FogImage.AddDirtyRect(new Int32Rect(xrect, yrect, w, h));
                } catch (Exception ex) {
                    Logger.LogError("Error in PunchTransparentCircle.AddDirtyRect", ex);
                }
            }
        }
        finally {
            FogImage.Unlock();
        }
    }
    
    private void PunchBreadcrumbCircle(double centerX, double centerY, double radiusInPixels) {
        if (BreadcrumbImage == null || !ShowBreadcrumb) return;

        double dpiScaleX = BreadcrumbImage.PixelWidth / BreadcrumbImage.Width;
        double dpiScaleY = BreadcrumbImage.PixelHeight / BreadcrumbImage.Height;

        double rawCenterX = centerX * dpiScaleX;
        double rawCenterY = centerY * dpiScaleY;
        double rawRadius = radiusInPixels * dpiScaleX;

        BreadcrumbImage.Lock();
        try {
            int radiusSq = (int)(rawRadius * rawRadius); //punch
            int stride = BreadcrumbImage.BackBufferStride;
            IntPtr pBackBuffer = BreadcrumbImage.BackBuffer;

            // Loop through a bounding box around the circle for efficiency
            for (int y = (int)(rawCenterY - rawRadius); y < rawCenterY + rawRadius; y++) {
                for (int x = (int)(rawCenterX - rawRadius); x < rawCenterX + rawRadius; x++) {
                    if (x >= 0 && x < BreadcrumbImage.PixelWidth && y >= 0 && y < BreadcrumbImage.PixelHeight) {
                        double dx = x - rawCenterX;
                        double dy = y - rawCenterY;
                        double distanceSq = dx * dx + dy * dy;

                        if (distanceSq <= radiusSq) {
                            // 1. Calculate how far we are from the center (0.0 to 1.0)
                            //double distance = Math.Sqrt(distanceSq);
                            //double ratio = distance / rawRadius; // 0.0 is center, 1.0 is edge

                            // // 2. Use an "Ease Out" function for the fade (e.g., squared or cubic)
                            // // 1.0 means opaque at the center, 0.0 means transparent at edge
                            // double softAlpha = Math.Pow(1.0 - ratio, 1);
                            //
                            // // 3. Apply the Alpha (scaled to your target intensity, e.g., 128)
                            // byte finalAlpha = (byte)(softAlpha * 128);

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
            }

            // Tell WPF to update the specific rectangle
            int xrect = Math.Max(0, (int)(rawCenterX - rawRadius));
            int yrect = Math.Max(0, (int)(rawCenterY - rawRadius));
            // Ensure width/height don't exceed image bounds
            int w = Math.Min((int)(rawRadius * 2) + 2, BreadcrumbImage.PixelWidth - xrect);
            int h = Math.Min((int)(rawRadius * 2) + 2, BreadcrumbImage.PixelHeight - yrect);
            
            if (w > 0 && h > 0) {
                try {
                    BreadcrumbImage.AddDirtyRect(new Int32Rect(xrect, yrect, w, h));
                } catch (Exception ex) {
                    Logger.LogError("Error in PunchBreadcrumbCircle.AddDirtyRect", ex);
                }
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
            try {
                BreadcrumbImage.AddDirtyRect(new Int32Rect(0, 0, width, height));
            } catch (Exception ex) {
                Logger.LogError("Error in FadeTrail.AddDirtyRect", ex);
            }
        } finally {
            BreadcrumbImage.Unlock();
        }
    }
    
    public void UpdateMarkers() {
        if (_loadingFile || _expandingMap || _calibratingNewDrawMap) return;
        if (_settings==null) return;

        if (CurrentPosition.HasValue) {
            // Auto-calibrate a newly created draw map on the first coordinate read
            if (IsDrawModeActive && _drawModeNeedsCalibration) {
                CalibrateNewDrawMap(CurrentPosition.Value);
                _drawModeNeedsCalibration = false;
            }
        }

        if (!_settings.IsCalibrated || MapImage == null) {
            CurrentPositionMarkerVisibility = Visibility.Collapsed;
            TargetMarkerVisibility = Visibility.Collapsed;
            if (_locationMarkersShowing) {
                foreach (var loc in Locations) {
                    loc.Visibility = Visibility.Collapsed;
                }

                _locationMarkersShowing = false;
            }

            return;
        }

        if (CurrentPosition.HasValue) {
            // Auto-calibrate a newly created draw map on the first coordinate read
            // if (IsDrawModeActive && _drawModeNeedsCalibration) {
            //     CalibrateNewDrawMap(CurrentPosition.Value);
            //     _drawModeNeedsCalibration = false;
            // }

            (MarkerX, MarkerY, CurrentPositionMarkerVisibility) = CalculatePixelPosition(CurrentPosition.Value);

            if (CurrentPositionMarkerVisibility == Visibility.Visible) {
                if (IsDrawModeActive) {
                    if (ExpandDrawMapIfNeeded(MarkerX, MarkerY)) {
                        (MarkerX, MarkerY, CurrentPositionMarkerVisibility) = CalculatePixelPosition(CurrentPosition.Value);
                    }
                    if (CurrentPositionMarkerVisibility == Visibility.Visible) {
                        if (!_drawingRadius.HasValue) _drawingRadius = GetDrawBrushRadius();
                        double radius = _drawingRadius.Value;
                        if (_drawLineMode && _drawLastPoints.Count > 0)
                            PaintDrawLine(_drawLastPoints[^1].X, _drawLastPoints[^1].Y, MarkerX, MarkerY, radius);
                        else
                            PaintDrawPixels(MarkerX, MarkerY, radius);
                        PushDrawPoint(MarkerX, MarkerY);
                    }
                } else {
                    double pixelsPerFoot = GetPixelsPerGameUnit();
                    double radius = HowFarCanAPersonSee;
                    double radiusInPixels = radius * pixelsPerFoot;
                    double actualRadius = Math.Max(5.0, radiusInPixels);
                    PunchTransparentCircle(MarkerX, MarkerY, actualRadius);

                    var currentScale = Settings?.ZoomLevel ?? 1;
                    var effectiveMarkerDiameter = 6 * currentScale;
                    PunchBreadcrumbCircle(MarkerX, MarkerY, Math.Max(2.0, effectiveMarkerDiameter / 2.0));
                }
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

        if (_staticMarkersDirty) {
            foreach (var loc in Locations) {
                // We use the default "x z y d" because ScrubbedCoordinates are always flattened to numbers
                if (Scrubber.TryParse(loc.Coordinates, "x z y d", out var coords)) {
                    var (x, y, vis) = CalculatePixelPosition(coords);
                    if (Math.Abs(loc.PixelX - x) > 0.1) loc.PixelX = x;
                    if (Math.Abs(loc.PixelY - y) > 0.1) loc.PixelY = y;
                    if (loc.Visibility != vis) {
                        loc.Visibility = vis;
                        if (vis == Visibility.Visible) {
                            _locationMarkersShowing = true;
                        }
                    }
                }
                else {
                    if (loc.Visibility != Visibility.Collapsed) loc.Visibility = Visibility.Collapsed;
                }
            }

            _staticMarkersDirty = false;
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
            //Is the coordinate on the map?
            //If it is, what is the translated position in terms of the image's pixel coordinates.
            
            if(MapImage==null) 
                return (0, 0, Visibility.Collapsed);
            if(_settings==null) 
                return (0, 0, Visibility.Collapsed);
            
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
            // if (px >= -10 && px <= MapImage.PixelWidth + 10 && py >= -10 && py <= MapImage.PixelHeight + 10) {
            //     return (px, py, Visibility.Visible);
            // }
            // else {
            //     return (0, 0, Visibility.Collapsed);
            // }
            //Due to DPI of 72, the PixelWidth and rendered Width may differ. Use the rendered with because that it where the marker is going.
            //Maybe if we were writing to the image file itself, it would be different. This clears a bug where it was
            //getting the right coordinates but deciding that the coordinates didn't fit on the image.
            if (px >= -10 && px <= MapImage.Width + 10 && py >= -10 && py <= MapImage.Height + 10) {
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
            if(_settings==null) return 0;
            
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
                // In left-handed, +X is West. gameHeading 90 is still East (+X, in the game),
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
        if (_settings == null) {
            HoverCoordinatesLabel = string.Empty;
            return;
        }
        
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

            //HoverCoordinatesLabel = $"Cursor: {x:F1} [{px:F1}], {y:F1} [{py:F1}] for {MapImage?.PixelWidth}x{MapImage?.PixelHeight}";
            HoverCoordinatesLabel = $"{x:F1}, {y:F1}";
        }
        catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"Error calculating hover coordinates: {ex.Message}");
            HoverCoordinatesLabel = string.Empty;
        }
    }

    public CoordinateData? GetCoordinatesFromPixels(double px, double py) {
        if(_settings == null) return null;
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
    
    public void ValidateWindowBounds()
    {
        if(Settings==null) return;
        
        var s = Settings.Placement;
    
        // Check if the saved Top/Left is within the bounds of the current desktop
        if (s.Left < SystemParameters.VirtualScreenLeft || 
            s.Left > (SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50) ||
            s.Top < SystemParameters.VirtualScreenTop ||
            s.Top > (SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50))
        {
            // Reset to default if it's out of bounds
            s.Left = 100;
            s.Top = 100;
        }
    }
    
    // ──────────────────────────────────────────────────────────
    // Draw Mode
    // ──────────────────────────────────────────────────────────

    public void StartDrawMode(string mapName) {
        if (IsDrawModeActive) StopDrawMode();
        ResetDrawSettings();

        var mapsDir = Path.Combine(NativeMethods.AppFolder(), "maps");
        if (!Directory.Exists(mapsDir)) Directory.CreateDirectory(mapsDir);

        var imagePath = Path.Combine(mapsDir, mapName + ".png");

        // Save and clear existing fog so a stale file doesn't conflict after expansion
        if (!string.IsNullOrEmpty(FogOfWarFilePath) && FogImage != null)
            ImageHelpers.SaveWriteableBitMap(FogOfWarFilePath, FogImage.Clone());
        FogImage = null;
        FogOfWarFilePath = "";

        _priorFogState = ShowFogOfWar;
        _priorBreadcrumbState = ShowBreadcrumb;
        ShowFogOfWar = false;
        ShowBreadcrumb = false;

        Settings ??= new MapSettings();
        _drawingRadius = null;
        if (File.Exists(imagePath)) {
            Settings.ImagePath = imagePath;
            LoadDrawModeMap(imagePath);
            LoadImageConfig(imagePath);
            if (Settings.IsCalibrated) {
                _drawModeNeedsCalibration = false;
                _drawingRadius = GetDrawBrushRadius();
            }
            else {
                _drawModeNeedsCalibration = true;
            }
        } else {
            Settings.ImagePath = imagePath;
            _drawingRadius = null;
            CreateNewDrawMap(imagePath);
            _drawModeNeedsCalibration = true;
        }

        IsDrawModeActive = true;
        StartDrawAutoSave();
        SaveDrawMap();
    }

    public void StopDrawMode() {
        if (!IsDrawModeActive) return;

        SaveDrawMap();

        _drawSaveTimer?.Stop();
        _drawSaveTimer = null;

        IsDrawModeActive = false;
        _drawModeNeedsCalibration = false;

        ShowFogOfWar = _priorFogState;
        ShowBreadcrumb = _priorBreadcrumbState;
        if (_priorBreadcrumbState) StartFading();

        LoadImage(); // Reload PNG as normal BitmapImage, recreate fog/breadcrumb
    }

    public void SaveDrawMap() {
        if (!IsDrawModeActive || MapImage is not WriteableBitmap bitmap || _settings == null) return;
        if (string.IsNullOrEmpty(_settings.ImagePath)) return;
        try {
            ImageHelpers.SaveWriteableBitMap(_settings.ImagePath, bitmap.Clone());

            var mapsDir = Path.Combine(NativeMethods.AppFolder(), "maps");
            if (!Directory.Exists(mapsDir)) Directory.CreateDirectory(mapsDir);
            var configPath = Path.Combine(mapsDir,
                Path.GetFileNameWithoutExtension(_settings.ImagePath) + ".json");
            File.WriteAllText(configPath, JsonSerializer.Serialize(_settings));
        }
        catch (Exception ex) {
            Logger.LogError("Error saving draw map", ex);
        }
    }

    private void StartDrawAutoSave() {
        _drawSaveTimer = new DispatcherTimer(DispatcherPriority.Background) {
            Interval = TimeSpan.FromSeconds(30)
        };
        _drawSaveTimer.Tick += (_, _) => SaveDrawMap();
        _drawSaveTimer.Start();
    }

    private void CreateNewDrawMap(string imagePath) {
        const int initialSize = 500;
        var bitmap = ImageHelpers.CreateBlackBitmapSize(initialSize, initialSize, 96, 96);
        ImageHelpers.SaveWriteableBitMap(imagePath, bitmap);

        MapImage = bitmap;
        MapPath = imagePath;
        MapName = imagePath;
        BreadcrumbImage = ImageHelpers.CreateTransparentBitmap(bitmap);
        if (_settings != null) _settings.IsCalibrated = false;

        OnPropertyChanged(nameof(UIVisibility));
        UpdateMarkers();
    }

    private void LoadDrawModeMap(string imagePath) {
        var bitmap = LoadAsBgra32WriteableBitmap(imagePath);
        _drawingRadius = null;
        MapImage = bitmap;
        MapPath = imagePath;
        MapName = imagePath;
        BreadcrumbImage = ImageHelpers.CreateTransparentBitmap(bitmap);

        LoadImageConfig(imagePath);
        OnPropertyChanged(nameof(UIVisibility));
        UpdateMarkers();
    }

    private void CalibrateNewDrawMap(CoordinateData initialPos) {
        if (_settings == null || MapImage == null) return;
        _calibratingNewDrawMap = true;
        const double pixelsPerUnit = 3.0;
        double centerX = MapImage.Width / 2.0;
        double centerY = MapImage.Height / 2.0;
        double gameUnitOffset = centerY / pixelsPerUnit;

        _settings.Point1.PixelX = centerX;
        _settings.Point1.PixelY = centerY;
        _settings.Point1.X = initialPos.X;
        _settings.Point1.Y = initialPos.Y;

        // Point2 is directly above center: game +Y = north = up on image
        _settings.Point2.PixelX = centerX;
        _settings.Point2.PixelY = 0;
        _settings.Point2.X = initialPos.X;
        _settings.Point2.Y = initialPos.Y + gameUnitOffset;

        _settings.IsCalibrated = true;
        _calibratingNewDrawMap = false;        
        _drawingRadius = GetDrawBrushRadius();
        SaveDrawMap();
    }

    private bool ExpandDrawMapIfNeeded(double markerX, double markerY) {
        if (_expandingMap || MapImage is not WriteableBitmap bitmap || _settings == null) return false;

        const int threshold = 50;
        const int amount = 50;

        int padLeft = 0, padTop = 0, padRight = 0, padBottom = 0;
        if (markerX < threshold) padLeft = amount;
        if (markerY < threshold) padTop = amount;
        if (markerX > bitmap.Width - threshold) padRight = amount;
        if (markerY > bitmap.Height - threshold) padBottom = amount;

        if (padLeft == 0 && padTop == 0 && padRight == 0 && padBottom == 0) return false;

        _expandingMap = true;
        try {
            double dpiScaleX = bitmap.PixelWidth / bitmap.Width;
            double dpiScaleY = bitmap.PixelHeight / bitmap.Height;

            int padLeftPx  = (int)Math.Round(padLeft   * dpiScaleX);
            int padTopPx   = (int)Math.Round(padTop    * dpiScaleY);
            int padRightPx = (int)Math.Round(padRight  * dpiScaleX);
            int padBotPx   = (int)Math.Round(padBottom * dpiScaleY);

            int newW = bitmap.PixelWidth  + padLeftPx + padRightPx;
            int newH = bitmap.PixelHeight + padTopPx  + padBotPx;

            var newBitmap = ImageHelpers.CreateBlackBitmapSize(newW, newH, bitmap.DpiX, bitmap.DpiY);
            CopyBitmapToOffset(bitmap, newBitmap, padLeftPx, padTopPx);

            // Shift calibration pixel coords (logical pixels) to match new origin
            _settings.Point1.PixelX += padLeft;
            _settings.Point1.PixelY += padTop;
            _settings.Point2.PixelX += padLeft;
            _settings.Point2.PixelY += padTop;

            BreadcrumbImage = ImageHelpers.CreateTransparentBitmap(newBitmap);
            MapImage = newBitmap;
            _staticMarkersDirty = true;
        }
        finally {
            _expandingMap = false;
        }
        return true;
    }

    private static void CopyBitmapToOffset(WriteableBitmap source, WriteableBitmap dest, int destX, int destY) {
        int w = source.PixelWidth;
        int h = source.PixelHeight;
        int stride = w * 4; // Bgra32
        byte[] pixels = new byte[stride * h];
        source.CopyPixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
        dest.WritePixels(new Int32Rect(destX, destY, w, h), pixels, stride, 0);
    }

    // private void PaintDrawPixels(double centerX, double centerY, double radiusInPixels) {
    //     if (MapImage is not WriteableBitmap drawBitmap) return;
    //
    //     double dpiScaleX = drawBitmap.PixelWidth / drawBitmap.Width;
    //     double dpiScaleY = drawBitmap.PixelHeight / drawBitmap.Height;
    //     double rawCx = centerX * dpiScaleX;
    //     double rawCy = centerY * dpiScaleY;
    //     double rawR  = Math.Max(1.0, radiusInPixels * dpiScaleX);
    //
    //     drawBitmap.Lock();
    //     try {
    //         int rSq = (int)(rawR * rawR) + 1;
    //         int stride = drawBitmap.BackBufferStride;
    //         IntPtr buf = drawBitmap.BackBuffer;
    //
    //         int xMin = Math.Max(0, (int)(rawCx - rawR));
    //         int yMin = Math.Max(0, (int)(rawCy - rawR));
    //         int xMax = Math.Min(drawBitmap.PixelWidth  - 1, (int)(rawCx + rawR));
    //         int yMax = Math.Min(drawBitmap.PixelHeight - 1, (int)(rawCy + rawR));
    //
    //         for (int py = yMin; py <= yMax; py++) {
    //             for (int px = xMin; px <= xMax; px++) {
    //                 double dx = px - rawCx, dy = py - rawCy;
    //                 if (dx * dx + dy * dy <= rSq) {
    //                     unsafe {
    //                         byte* p = (byte*)buf + py * stride + px * 4;
    //                         p[0] = p[1] = p[2] = p[3] = 255;
    //                     }
    //                 }
    //             }
    //         }
    //
    //         int dirtyW = Math.Min((int)(rawR * 2) + 2, drawBitmap.PixelWidth  - xMin);
    //         int dirtyH = Math.Min((int)(rawR * 2) + 2, drawBitmap.PixelHeight - yMin);
    //         if (dirtyW > 0 && dirtyH > 0) {
    //             try { drawBitmap.AddDirtyRect(new Int32Rect(xMin, yMin, dirtyW, dirtyH)); }
    //             catch (Exception ex) { Logger.LogError("Error in PaintDrawPixels.AddDirtyRect", ex); }
    //         }
    //     }
    //     finally {
    //         drawBitmap.Unlock();
    //     }
    // }
    
    // Inner loop — must be called while the bitmap is locked. No lock/unlock here.
    private unsafe void PaintCircleCore(byte* buf, int stride, int bitmapW, int bitmapH,
        double rawCx, double rawCy, double rawR) {
        double rawRSq = rawR * rawR;
        int xMin = Math.Max(0, (int)(rawCx - rawR));
        int yMin = Math.Max(0, (int)(rawCy - rawR));
        int xMax = Math.Min(bitmapW - 1, (int)(rawCx + rawR));
        int yMax = Math.Min(bitmapH - 1, (int)(rawCy + rawR));
        for (int py = yMin; py <= yMax; py++) {
            for (int px = xMin; px <= xMax; px++) {
                double dx = px - rawCx, dy = py - rawCy;
                double distSq = dx * dx + dy * dy;
                if (distSq > rawRSq) continue;
                byte* p = buf + (py * stride) + (px * 4);
                if (_drawAntiAlias) {
                    double intensity = 1.0 - Math.Sqrt(distSq) / rawR;
                    p[0] = (byte)(p[0] * (1.0 - intensity) + _drawBrushB * intensity);
                    p[1] = (byte)(p[1] * (1.0 - intensity) + _drawBrushG * intensity);
                    p[2] = (byte)(p[2] * (1.0 - intensity) + _drawBrushR * intensity);
                } else {
                    p[0] = _drawBrushB;
                    p[1] = _drawBrushG;
                    p[2] = _drawBrushR;
                }
                p[3] = 255;
            }
        }
    }

    private void PaintDrawPixels(double centerX, double centerY, double radiusInPixels) {
        if (MapImage is not WriteableBitmap drawBitmap) return;
        if (_drawColorIndex == 12) return;

        double dpiScaleX = drawBitmap.PixelWidth / drawBitmap.Width;
        double rawCx = centerX * dpiScaleX;
        double rawCy = centerY * (drawBitmap.PixelHeight / drawBitmap.Height);
        double rawR  = Math.Max(1.0, radiusInPixels * dpiScaleX);

        int xMin = Math.Max(0, (int)(rawCx - rawR));
        int yMin = Math.Max(0, (int)(rawCy - rawR));
        int xMax = Math.Min(drawBitmap.PixelWidth  - 1, (int)(rawCx + rawR));
        int yMax = Math.Min(drawBitmap.PixelHeight - 1, (int)(rawCy + rawR));

        drawBitmap.Lock();
        try {
            unsafe { PaintCircleCore((byte*)drawBitmap.BackBuffer, drawBitmap.BackBufferStride,
                drawBitmap.PixelWidth, drawBitmap.PixelHeight, rawCx, rawCy, rawR); }
            int dirtyW = xMax - xMin + 1;
            int dirtyH = yMax - yMin + 1;
            if (dirtyW > 0 && dirtyH > 0) {
                try { drawBitmap.AddDirtyRect(new Int32Rect(xMin, yMin, dirtyW, dirtyH)); }
                catch (Exception ex) { Logger.LogError("Error in PaintDrawPixels.AddDirtyRect", ex); }
            }
        }
        finally { drawBitmap.Unlock(); }
    }

    private void PaintDrawLine(double fromX, double fromY, double toX, double toY, double radiusInPixels) {
        if (MapImage is not WriteableBitmap drawBitmap) return;
        if (_drawColorIndex == 12) return;

        double dpiScaleX = drawBitmap.PixelWidth / drawBitmap.Width;
        double dpiScaleY = drawBitmap.PixelHeight / drawBitmap.Height;
        double rawFx = fromX * dpiScaleX, rawFy = fromY * dpiScaleY;
        double rawTx = toX   * dpiScaleX, rawTy = toY   * dpiScaleY;
        double rawR  = Math.Max(1.0, radiusInPixels * dpiScaleX);

        double dx = rawTx - rawFx, dy = rawTy - rawFy;
        double length = Math.Sqrt(dx * dx + dy * dy);

        int dirtXMin = Math.Max(0, (int)(Math.Min(rawFx, rawTx) - rawR));
        int dirtYMin = Math.Max(0, (int)(Math.Min(rawFy, rawTy) - rawR));
        int dirtXMax = Math.Min(drawBitmap.PixelWidth  - 1, (int)(Math.Max(rawFx, rawTx) + rawR) + 1);
        int dirtYMax = Math.Min(drawBitmap.PixelHeight - 1, (int)(Math.Max(rawFy, rawTy) + rawR) + 1);

        drawBitmap.Lock();
        try {
            unsafe {
                byte* buf  = (byte*)drawBitmap.BackBuffer;
                int stride = drawBitmap.BackBufferStride;
                int bmpW   = drawBitmap.PixelWidth, bmpH = drawBitmap.PixelHeight;
                if (length < 0.5) {
                    PaintCircleCore(buf, stride, bmpW, bmpH, rawFx, rawFy, rawR);
                } else {
                    double nx = dx / length, ny = dy / length;
                    // Step every 1px — guarantees solid coverage for any brush radius
                    for (double t = 0.0; t <= length; t += 1.0)
                        PaintCircleCore(buf, stride, bmpW, bmpH, rawFx + nx * t, rawFy + ny * t, rawR);
                    // Ensure the endpoint is always painted
                    PaintCircleCore(buf, stride, bmpW, bmpH, rawTx, rawTy, rawR);
                }
            }
            int dirtyW = dirtXMax - dirtXMin + 1;
            int dirtyH = dirtYMax - dirtYMin + 1;
            if (dirtyW > 0 && dirtyH > 0) {
                try { drawBitmap.AddDirtyRect(new Int32Rect(dirtXMin, dirtYMin, dirtyW, dirtyH)); }
                catch (Exception ex) { Logger.LogError("Error in PaintDrawLine.AddDirtyRect", ex); }
            }
        }
        finally { drawBitmap.Unlock(); }
    }

    private double GetBaseDrawRadius() {
        // Player width is 1.0 game unit; radius = half a unit mapped to pixels, minimum 1.5px
        return Math.Max(1.5, 0.5 * GetPixelsPerGameUnit());
    }

    private double GetDrawBrushRadius() {
        double baseR = GetBaseDrawRadius();
        if (_drawSizeMode == -1) return 2.0;
        return baseR + _drawSizeMode switch {
            1 => 3.0,
            2 => 5.0,
            3 => 10.0,
            _ => 0.0
        };
    }
    
    private static WriteableBitmap LoadAsBgra32WriteableBitmap(string imagePath) {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(imagePath);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();

        if (bmp.Format == PixelFormats.Bgra32)
            return new WriteableBitmap(bmp);

        var converted = new FormatConvertedBitmap();
        converted.BeginInit();
        converted.Source = bmp;
        converted.DestinationFormat = PixelFormats.Bgra32;
        converted.EndInit();
        converted.Freeze();
        return new WriteableBitmap(converted);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}