using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using MMONavigator.Helpers;
using MMONavigator.Models;
using MMONavigator.Services;

namespace MMONavigator.ViewModels;

public class ThreeDMapViewModel : INotifyPropertyChanged, IDisposable {
    private MapSettings? _settings;
    private CoordinateSystem _coordinateSystem = CoordinateSystem.RightHanded;
    private CoordinateData? _currentPosition;
    private CoordinateData? _targetPosition;
    private string? _currentCoordinatesLabel;
    private string? _hoverCoordinatesLabel;
    private BitmapSource? _mapImage;
    private BitmapSource? _originalMapImage;
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
   // private bool _locationMarkersShowing = false;
    private DispatcherTimer? _fadeTimer;
    private bool _isDrawModeActive;
    private bool _drawModeNeedsCalibration;
    private bool _priorFogState;
    private bool _priorBreadcrumbState;
    private DispatcherTimer? _drawSaveTimer;
    private bool _expandingMap;
    private bool _calibratingNewDrawMap;
    private double? _drawingRadius;
    private int _drawColorIndex; // 0=white, 1=dodger blue, ..., 12=transparent
    private int _drawSizeMode; // 0=default, 1=+3, 2=+5, 3=+10, -1=2px fixed
    private bool _drawAntiAlias = true;
    private byte _drawBrushB = 255, _drawBrushG = 255, _drawBrushR = 255;
    private bool _drawLineMode;
    private readonly List<(double X, double Y)> _drawLastPoints = new();
    private const double CursorPositionTopMargin = 5;

    private const double HowFarCanAPersonSee = 30;

    private double _currentZoomScale = 1.0;

    #region Multi set

    // --- MULTI-LAYER MAP SET PROPERTIES ---
    private string _setName = "Untitled Map Set";

    public string SetName {
        get => _setName;
        set {
            _setName = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<DungeonMapLayerConfig> ActiveSetLayers { get; } = new();

    private DungeonMapLayerConfig? _activeDrawLayer;

    public DungeonMapLayerConfig? ActiveDrawLayer {
        get => _activeDrawLayer;
        set {
            if (_activeDrawLayer != value) {
                if (_activeDrawLayer != null) _activeDrawLayer.IsActiveDrawLayer = false;
                _activeDrawLayer = value;
                if (_activeDrawLayer != null) _activeDrawLayer.IsActiveDrawLayer = true;

                OnPropertyChanged();
                OnPropertyChanged(nameof(ActiveDrawLayerName));
            }
        }
    }

    public string ActiveDrawLayerName => _activeDrawLayer?.LayerId ?? "None";

    private bool _isBreadcrumbVisible = true;

    public bool IsBreadcrumbVisible {
        get => _isBreadcrumbVisible;
        set {
            _isBreadcrumbVisible = value;
            OnPropertyChanged();
        }
    }
    
    public bool HasWorld =>  ActiveSetLayers.Any();
    
    #endregion

    #region 3D map properties

    // 3D Breadcrumb Point Store
    public ObservableCollection<System.Numerics.Vector3> BreadcrumbHistory3D { get; } = new();

// Managed 3D Map Planes
    public ObservableCollection<DungeonMapLayer> DungeonLayers { get; } = new();

// Camera Mode Tracking
    private bool _isHeadingSyncedMode = true;

    public bool IsHeadingSyncedMode {
        get => _isHeadingSyncedMode;
        set {
            _isHeadingSyncedMode = value;
            OnPropertyChanged();
        }
    }

// Intercept CurrentPosition updates to build the 3D Breadcrumb Trail
    public void Record3DPosition(CoordinateData pos) {
        if (pos.Z.HasValue) {
            // Invert Z for Left-Handed coordinate systems (e.g., EverQuest)
            float adjustedZ = (float)(CoordinateSystem == CoordinateSystem.LeftHanded
                ? -pos.Z.Value
                : pos.Z.Value);

            var newPoint = new System.Numerics.Vector3((float)pos.X, (float)pos.Y, adjustedZ);

            if (BreadcrumbHistory3D.Count == 0 ||
                System.Numerics.Vector3.Distance(BreadcrumbHistory3D[^1], newPoint) > 2.0f) {
                BreadcrumbHistory3D.Add(newPoint);
            }
        }
    }

    public ThreeDMapViewModel(MapSettings settings, AppSettings appSettings) {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        if (appSettings.ThreeDMapWindowPlacement == null) {
            appSettings.ThreeDMapWindowPlacement = new WindowPlacement();
        }

        //was it saved minimized?
        if (appSettings.ThreeDMapWindowPlacement.Height <= 50 ||
            appSettings.ThreeDMapWindowPlacement.State == WindowState.Minimized) {
            appSettings.ThreeDMapWindowPlacement.State = WindowState.Normal;
            appSettings.ThreeDMapWindowPlacement.Height = 600;
            appSettings.ThreeDMapWindowPlacement.Width = 800;
        }
        
        CoordinateSystem = appSettings.SelectedProfile.CoordinateSystem;
        
        AppSettings = appSettings;

        _settings.PropertyChanged -= Settings_PropertyChanged;
        _settings.Point1.PropertyChanged -= MapPoint_PropertyChanged;
        _settings.Point2.PropertyChanged -= MapPoint_PropertyChanged;

        _settings.PropertyChanged += Settings_PropertyChanged;
        _settings.Point1.PropertyChanged += MapPoint_PropertyChanged;
        _settings.Point2.PropertyChanged += MapPoint_PropertyChanged;

        // Locations.CollectionChanged -= Locations_CollectionChanged;
        // Locations.CollectionChanged += Locations_CollectionChanged;

        //LoadWorld();

        // Force initial generation of 3D markers on startup
        _staticMarkersDirty = true;
        UpdateMarkers3D();

        if (ShowBreadcrumb) {
            StartFading();
        }
    }

    private bool _isUpdating3DMarkers;

    public void UpdateMarkers3D() {
        if (_loadingFile || _isUpdating3DMarkers) return;

        _isUpdating3DMarkers = true;
        try {
            // If locations collection changed or marked dirty, rebuild/refresh the 3D items
            if (_staticMarkersDirty) {
                var locations3D = new List<MapLocation3D>();
                if (ShowLocations) {
                    foreach (var loc in Locations) {
                        // Parse coordinates string using Scrubber (same as MapViewModel)[cite: 1]
                        if (Scrubber.TryParse(loc.Coordinates, "x z y d", out var coords)) {
                            // Invert Z for Left-Handed systems (EverQuest) if applicable
                            double worldZ = coords.Z ?? 0.0;
                            double adjustedZ = (CoordinateSystem == CoordinateSystem.LeftHanded) ? -worldZ : worldZ;


                            locations3D.Add(new MapLocation3D {
                                Name = loc.DisplayName ?? "Location",
                                X = coords.X,
                                Y = coords.Y,
                                Z = adjustedZ,
                                Visibility = Visibility.Visible
                            });
                        }
                    }
                }

                Locations3D.ReplaceRange(locations3D);
                // Locations3D.Clear();
                // Locations3D.AddRange(locations3D);

                _staticMarkersDirty = false;
            }
        }
        finally {
            _isUpdating3DMarkers = false;
        }
    }

    public event Action? RequestTogglePerspectiveRest;
        
    public event Action? RequestRecenterCamera;

    public void TogglePerspectiveRest() {
        // Re-enable heading sync/follow mode if desired
        IsHeadingSyncedMode = true;
        RequestTogglePerspectiveRest?.Invoke();
    }
    
    /// <summary>
    /// Triggers an event to re-center the 3D camera onto the player's current position.
    /// </summary>
    public void RecenterCamera() {
        // Re-enable heading sync/follow mode if desired
        IsHeadingSyncedMode = true;
        RequestRecenterCamera?.Invoke();
    }

    private bool _isGridVisible = false;
    public bool IsGridVisible {
        get => _isGridVisible;
        set {
            if (_isGridVisible != value) {
                _isGridVisible = value;
                OnPropertyChanged();
                RequestToggleGrid?.Invoke(_isGridVisible);
            }
        }
    }

    public event Action<bool>? RequestToggleGrid;

    public void ToggleGrid() {
        IsGridVisible = !IsGridVisible;
    }
    
    /// <summary>
    /// Calculates 3D world parameters using a specific map layer's calibration settings.
    /// </summary>
    public (Point3D Center, double WorldWidth, double WorldHeight)? GetCalibratedMapWorldBounds(
        MapSettings layerSettings, BitmapSource layerImage, float hardcodedZ = 0.0f) {
        if (layerSettings == null || !layerSettings.IsCalibrated || layerImage == null)
            return null;

        // Calculate scale from the layer's 2 calibration points
        double dxWorld = layerSettings.Point2.X - layerSettings.Point1.X;
        double dyWorld = layerSettings.Point2.Y - layerSettings.Point1.Y;
        double distWorld = Math.Sqrt(dxWorld * dxWorld + dyWorld * dyWorld);

        double dxPx = layerSettings.Point2.PixelX - layerSettings.Point1.PixelX;
        double dyPx = layerSettings.Point2.PixelY - layerSettings.Point1.PixelY;
        double distPx = Math.Sqrt(dxPx * dxPx + dyPx * dyPx);

        if (distWorld < 0.0001 || distPx < 0.0001) return null;

        double pxPerUnit = distPx / distWorld;

        // Convert image dimensions to world units
        double worldWidth = layerImage.Width / pxPerUnit;
        double worldHeight = layerImage.Height / pxPerUnit;

        // Center offset from Point1
        double imageCenterPixelX = layerImage.Width / 2.0;
        double imageCenterPixelY = layerImage.Height / 2.0;

        double deltaPxX = imageCenterPixelX - layerSettings.Point1.PixelX;
        double deltaPxY = layerSettings.Point1.PixelY - imageCenterPixelY; // Screen Y inverted

        double centerWorldX = layerSettings.Point1.X + (deltaPxX / pxPerUnit);
        double centerWorldY = layerSettings.Point1.Y + (deltaPxY / pxPerUnit);

        return (new Point3D(centerWorldX, centerWorldY, hardcodedZ), worldWidth, worldHeight);
    }

    /// <summary>
    /// Calculates 3D world placement bounds for a specific map layer using its own .json calibration file.
    /// </summary>
    /// <summary>
    /// Calculates 3D world placement bounds and rotation angle for a map layer using its calibration file.
    /// </summary>
    public (Point3D Center, double WorldWidth, double WorldHeight, double RotationDegrees, Point3D AnchorPoint1)?
        GetCalibratedMapWorldBoundsForFile(string imagePath, float zElevation) {
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            return null;

        string configPath = Path.ChangeExtension(imagePath, ".json");
        if (!File.Exists(configPath))
            return null;

        MapSettings? layerSettings = null;
        try {
            string json = File.ReadAllText(configPath);
            layerSettings = JsonSerializer.Deserialize<MapSettings>(json);
        }
        catch (Exception ex) {
            Log.Error(ex, "Failed to load layer settings for {ImagePath}", imagePath);
            return null;
        }

        if (layerSettings == null || !layerSettings.IsCalibrated)
            return null;

        BitmapFrame frame;
        using (var stream = File.OpenRead(imagePath)) {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            frame = decoder.Frames[0];
        }

        double imgPixelWidth = frame.PixelWidth;
        double imgPixelHeight = frame.PixelHeight;

        // 1. Distance & Scale Calculations
        double dxWorld = layerSettings.Point2.X - layerSettings.Point1.X;
        double dyWorld = layerSettings.Point2.Y - layerSettings.Point1.Y;
        double distWorld = Math.Sqrt(dxWorld * dxWorld + dyWorld * dyWorld);

        double dpx = layerSettings.Point2.PixelX - layerSettings.Point1.PixelX;
        double dpy = layerSettings.Point1.PixelY - layerSettings.Point2.PixelY; // Invert Screen Y
        double distPx = Math.Sqrt(dpx * dpx + dpy * dpy);

        if (distWorld < 0.0001 || distPx < 0.0001) return null;

        double pxPerUnit = distPx / distWorld;
        double unitsPerPx = distWorld / distPx;

        double worldWidth = imgPixelWidth / pxPerUnit;
        double worldHeight = imgPixelHeight / pxPerUnit;

        // 2. 2D Affine Rotation Angle
        double angleReal = Math.Atan2(dyWorld, dxWorld);
        double anglePixel = Math.Atan2(dpy, dpx);
        double rotationRad = anglePixel - angleReal;

        // Uses ViewModel's CoordinateSystem property
        if (CoordinateSystem == CoordinateSystem.LeftHanded) {
            rotationRad = -rotationRad;
        }

        double rotationDegrees = rotationRad * (180.0 / Math.PI);

        // 3. World Position of Point1 Calibration Anchor
        double worldZ = CoordinateSystem == CoordinateSystem.LeftHanded ? -zElevation : zElevation;
        Point3D anchorPoint1 = new Point3D(layerSettings.Point1.X, layerSettings.Point1.Y, worldZ);

        // 4. Geometric World Center (for camera target positioning)
        double imgCenterPxX = imgPixelWidth / 2.0;
        double imgCenterPxY = imgPixelHeight / 2.0;

        double curDpx = imgCenterPxX - layerSettings.Point1.PixelX;
        double curDpy = layerSettings.Point1.PixelY - imgCenterPxY;

        double cosR = Math.Cos(rotationRad);
        double sinR = Math.Sin(rotationRad);

        double rotX = curDpx * cosR - curDpy * sinR;
        double rotY = curDpx * sinR + curDpy * cosR;

        double centerWorldX = layerSettings.Point1.X + (rotX * unitsPerPx);
        double centerWorldY = layerSettings.Point1.Y + (rotY * unitsPerPx);

        if (CoordinateSystem == CoordinateSystem.LeftHanded) {
            centerWorldX = layerSettings.Point1.X - (rotX * unitsPerPx);
        }

        Point3D worldCenter = new Point3D(centerWorldX, centerWorldY, worldZ);

        return (worldCenter, worldWidth, worldHeight, rotationDegrees, anchorPoint1);
    }
// public (Point3D Center, double WorldWidth, double WorldHeight)? GetCalibratedMapWorldBoundsForFile(string imagePath, float zElevation)
// {
//     if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) 
//         return null;
//
//     string configPath = Path.ChangeExtension(imagePath, ".json");
//     if (!File.Exists(configPath)) 
//         return null;
//
//     MapSettings? layerSettings = null;
//     try
//     {
//         string json = File.ReadAllText(configPath);
//         layerSettings = JsonSerializer.Deserialize<MapSettings>(json);
//     }
//     catch (Exception ex)
//     {
//         Log.Error(ex, "Failed to load layer settings for {ImagePath}", imagePath);
//         return null;
//     }
//
//     if (layerSettings == null || !layerSettings.IsCalibrated) 
//         return null;
//
//     // Load bitmap metadata to get pixel width/height without full decoding
//     BitmapFrame frame;
//     using (var stream = File.OpenRead(imagePath))
//     {
//         var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
//         frame = decoder.Frames[0];
//     }
//
//     double imgPixelWidth = frame.PixelWidth;
//     double imgPixelHeight = frame.PixelHeight;
//
//     // Calculate scale from calibration points
//     double dxWorld = layerSettings.Point2.X - layerSettings.Point1.X;
//     double dyWorld = layerSettings.Point2.Y - layerSettings.Point1.Y;
//     double distWorld = Math.Sqrt(dxWorld * dxWorld + dyWorld * dyWorld);
//
//     double dxPx = layerSettings.Point2.PixelX - layerSettings.Point1.PixelX;
//     double dyPx = layerSettings.Point2.PixelY - layerSettings.Point1.PixelY;
//     double distPx = Math.Sqrt(dxPx * dxPx + dyPx * dyPx);
//
//     if (distWorld < 0.0001 || distPx < 0.0001) return null;
//
//     double pxPerUnit = distPx / distWorld;
//
//     // World dimensions
//     double worldWidth = imgPixelWidth / pxPerUnit;
//     double worldHeight = imgPixelHeight / pxPerUnit;
//
//     // Image center offset from Point1
//     double imgCenterPxX = imgPixelWidth / 2.0;
//     double imgCenterPxY = imgPixelHeight / 2.0;
//
//     double deltaPxX = imgCenterPxX - layerSettings.Point1.PixelX;
//     double deltaPxY = layerSettings.Point1.PixelY - imgCenterPxY; // Invert Screen Y
//
//     double centerWorldX = layerSettings.Point1.X + (deltaPxX / pxPerUnit);
//     double centerWorldY = layerSettings.Point1.Y + (deltaPxY / pxPerUnit);
//
//     // Invert Z-elevation if system is Left-Handed
//     double worldZ = CoordinateSystem == CoordinateSystem.LeftHanded 
//         ? -zElevation 
//         : zElevation;
//     
//     return (new Point3D(centerWorldX, centerWorldY, zElevation), worldWidth, worldHeight);
// }

    /// <summary>
    /// Verifies if two map layers map the exact same game world coordinate (X, Y) 
    /// to identical positions in world space using their respective calibration settings.
    /// </summary>
    public bool TestMapCoordinateOverlap(
        string mapAPath,
        string mapBPath,
        double testWorldX,
        double testWorldY,
        out string testLog) {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"--- Testing World Coordinate Alignment ({testWorldX:F2}, {testWorldY:F2}) ---");

        // 1. Load settings for Map A
        string configAPath = Path.ChangeExtension(mapAPath, ".json");
        string configBPath = Path.ChangeExtension(mapBPath, ".json");

        if (!File.Exists(configAPath) || !File.Exists(configBPath)) {
            testLog = "Error: One or both map .json calibration files are missing.";
            return false;
        }

        var settingsA = JsonSerializer.Deserialize<MapSettings>(File.ReadAllText(configAPath));
        var settingsB = JsonSerializer.Deserialize<MapSettings>(File.ReadAllText(configBPath));

        if (settingsA == null || !settingsA.IsCalibrated || settingsB == null || !settingsB.IsCalibrated) {
            testLog = "Error: One or both maps are not marked as calibrated.";
            return false;
        }

        var testCoord = new CoordinateData(testWorldX, testWorldY, null, null);

        // 2. Temporarily swap settings to calculate Pixel Position on Map A
        var originalSettings = _settings;

        _settings = settingsA;
        var (pixelXA, pixelYA, visA) = CalculatePixelPosition(testCoord);
        CoordinateData? worldA = visA == Visibility.Visible ? GetCoordinatesFromPixels(pixelXA, pixelYA) : null;

        // 3. Swap settings to calculate Pixel Position on Map B
        _settings = settingsB;
        var (pixelXB, pixelYB, visB) = CalculatePixelPosition(testCoord);
        CoordinateData? worldB = visB == Visibility.Visible ? GetCoordinatesFromPixels(pixelXB, pixelYB) : null;

        // Restore original active map settings
        _settings = originalSettings;

        // 4. Evaluate visibility and alignment
        sb.AppendLine($"Map A [{Path.GetFileName(mapAPath)}]:");
        sb.AppendLine($"  - Projected Pixel: ({pixelXA:F1}, {pixelYA:F1}) | Visibility: {visA}");
        if (worldA.HasValue)
            sb.AppendLine($"  - Reverse World:   ({worldA.Value.X:F2}, {worldA.Value.Y:F2})");

        sb.AppendLine($"Map B [{Path.GetFileName(mapBPath)}]:");
        sb.AppendLine($"  - Projected Pixel: ({pixelXB:F1}, {pixelYB:F1}) | Visibility: {visB}");
        if (worldB.HasValue)
            sb.AppendLine($"  - Reverse World:   ({worldB.Value.X:F2}, {worldB.Value.Y:F2})");

        if (visA != Visibility.Visible || visB != Visibility.Visible) {
            sb.AppendLine("RESULT: Target coordinate falls outside the bounds of one or both map images.");
            testLog = sb.ToString();
            return false;
        }

        double deltaX = Math.Abs(worldA!.Value.X - worldB!.Value.X);
        double deltaY = Math.Abs(worldA.Value.Y - worldB.Value.Y);
        double distanceDiff = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

        sb.AppendLine($"Calculated Position Delta: {distanceDiff:F4} world units.");

        if (distanceDiff < 0.5) // Less than half a game unit difference
        {
            sb.AppendLine("RESULT: PASSED — Math and calibration are consistent between both maps.");
            testLog = sb.ToString();
            return true;
        }

        sb.AppendLine("RESULT: FAILED — Discrepancy detected in calibration scale, rotation, or point placement.");
        testLog = sb.ToString();
        return false;
    }

    #endregion

    private bool _zoomToCenter;
    public bool ZoomToCenter {
        get => _zoomToCenter;
        set {
            _zoomToCenter = value;
            OnPropertyChanged();
        }
    }
    
    private double _markerSize = 12.5;

    public double MarkerSize {
        get => _markerSize;
        set {
            _markerSize = value;
            OnPropertyChanged();
        }
    }

    private Thickness _markerMargin = new Thickness(-6.25, -6.25, 0, 0);

    public Thickness MarkerMargin {
        get => _markerMargin;
        set {
            _markerMargin = value;
            OnPropertyChanged();
        }
    }

    public event Action? RequestClearGhostTerrain;

    /// <summary>
    /// Triggers an event to clear the ghost terrain mesh in the 3D Viewport.
    /// </summary>
    public void ClearGhostTerrain() {
        RequestClearGhostTerrain?.Invoke();
    }

    public void AddMapToSet(string imagePath, float zElevation, double opacity = 0.30, bool setAsActiveDraw = false) {
        if (!File.Exists(imagePath)) return;

        // Read image dimensions natively from file header
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(imagePath);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
    
        double imgWidth = bitmap.PixelWidth;
        double imgHeight = bitmap.PixelHeight;
        
        string layerId = Path.GetFileNameWithoutExtension(imagePath);

        // Remove existing entry if re-adding
        var existing =
            ActiveSetLayers.FirstOrDefault(l => l.LayerId.Equals(layerId, StringComparison.OrdinalIgnoreCase));
        if (existing != null) ActiveSetLayers.Remove(existing);

        var newLayer = new DungeonMapLayerConfig {
            LayerId = layerId,
            ImagePath = imagePath,
            ZElevation = zElevation,
            Opacity = opacity,
            IsActiveDrawLayer = setAsActiveDraw,
            Width = imgWidth,   // Store if your config supports it
            Height = imgHeight
        };

        ActiveSetLayers.Add(newLayer);

        if (setAsActiveDraw || ActiveDrawLayer == null) {
            ActiveDrawLayer = newLayer;
        }
        
        OnPropertyChanged(nameof(ActiveSetLayers));
        OnPropertyChanged(nameof(HasWorld));
        
        _staticMarkersDirty = true;
        UpdateMarkers();
    }

    public void RemoveMapFromSet(DungeonMapLayerConfig layer) {
        if (layer == null) return;

        ActiveSetLayers.Remove(layer);
        if (ActiveDrawLayer == layer) {
            ActiveDrawLayer = ActiveSetLayers.FirstOrDefault();
        }
    }

    public void ClearSet() {
        ActiveSetLayers.Clear();
        ActiveDrawLayer = null;
        SetName = "Untitled Map Set";
    }

    /// <summary>
    /// Loads a complete multi-layer dungeon set from JSON.
    /// </summary>
    public void LoadMapSet(string setFilePath) {
        if (!File.Exists(setFilePath)) return;

        string json = File.ReadAllText(setFilePath);
        var set = JsonSerializer.Deserialize<DungeonMapSet>(json);
        if (set == null) return;

        ActiveSetLayers.Clear();
        double maxLayerWidth = 0;
        double maxLayerHeight = 0;
        
        foreach (var layer in set.Layers) {
            // Fallback or re-measure if the file exists on disk to catch external edits
            if (File.Exists(layer.ImagePath)) {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(layer.ImagePath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                layer.Width = bitmap.PixelWidth;
                layer.Height = bitmap.PixelHeight;
            }

            if (layer.Width > maxLayerWidth) maxLayerWidth = layer.Width;
            if (layer.Height > maxLayerHeight) maxLayerHeight = layer.Height;
            
            ActiveSetLayers.Add(layer);
            if (layer.IsActiveDrawLayer) {
                ActiveDrawLayer = layer;
            }
        }
        
        OnPropertyChanged(nameof(ActiveSetLayers));
        OnPropertyChanged(nameof(HasWorld));
        
        _staticMarkersDirty = true;
        IsLoadingFile = false;
        UpdateMarkers();
        
        // Immediately configure bounds based on the actual active map dimensions
        if (maxLayerWidth > 0 && maxLayerHeight > 0) {
            // Assuming you expose a reference or event to the viewport from the VM, 
            // or trigger it from your window's load callback:
            RequestConfigureBounds?.Invoke(maxLayerWidth, maxLayerHeight);
        }
    }

    public event Action<double, double>? RequestConfigureBounds;
    
    /// <summary>
    /// Saves the current multi-layer configuration to disk.
    /// </summary>
    public void SaveMapSet(string setFilePath) {
        var set = new DungeonMapSet {
            SetName = Path.GetFileNameWithoutExtension(setFilePath),
            Layers = new List<DungeonMapLayerConfig>(ActiveSetLayers)
        };

        string json = JsonSerializer.Serialize(set, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(setFilePath, json);
    }

    // Call this method whenever your map zoom changes
    public void UpdateMarkerScale(double currentZoomScale) {
        _currentZoomScale = currentZoomScale;

        double baseSize = 12.5;

        // If zoom scale gets small (zoomed out), we increase the visual size.
        // Example: At 0.1 zoom, this makes the marker roughly 24 pixels.
        if (currentZoomScale < 0.5) {
            // Smoothly scale up the marker the further we zoom out
            MarkerSize = baseSize + ((0.5 - currentZoomScale) * 30.0);
        }
        else {
            MarkerSize = baseSize;
        }

        // Keep it centered: Margin must always be negative half of the size
        double halfSize = -MarkerSize / 2.0;
        MarkerMargin = new Thickness(halfSize, halfSize, 0, 0);
    }

    // Higher value for the "zoomed in" look
    public double FollowZoomLevel => 2.5;

    private double _previousZoom = 1.0;

    public double PreviousZoom {
        get => _previousZoom;
        set {
            _previousZoom = value;
            OnPropertyChanged();
        }
    }

    private double _currentScrollY;

    public double CurrentScrollY {
        get => _currentScrollY;
        set {
            _currentScrollY = value;
            OnPropertyChanged();
        }
    }

    public double CoordinateYPosition {
        get {
            // 10 is a small buffer from the top of the screen
            // CurrentScrollY is how far down you've scrolled/panned into the map
            return Math.Max(CursorPositionTopMargin, CurrentScrollY + CursorPositionTopMargin);
        }
    }

    // This is the property your Canvas.Top will bind to
    public double StickyTopPosition {
        get {
            if (MapImage == null) return CursorPositionTopMargin;

            // 1) Calculate how much empty space is at the top if the image is small
            // (ImageHeight * Zoom) is the actual visual height of the map
            double visualImageHeight = MapImage.Height * (Settings?.ZoomLevel ?? 1);
            // If the image is smaller than the window, it's likely centered.
            // The 'top' of the image is at (ViewportHeight - visualImageHeight) / 2
            double imageTopInViewport = (ViewportHeight - visualImageHeight) / 2;

            // 2) If the image is larger than the window, imageTopInViewport will be negative.
            // We want to use the actual top of the image UNLESS it's off-screen.

            if (visualImageHeight > ViewportHeight) {
                // The image is larger than the window. 
                // We want it to stay at the top (10px padding),
                // but we must account for the fact that the Canvas doesn't scroll.
                return CursorPositionTopMargin;
            }
            else {
                // The image is smaller than the window.
                // Move the text to sit exactly at the top of the centered image.
                return Math.Max(CursorPositionTopMargin, imageTopInViewport);
            }
        }
    }

    public double StickyLeftPosition {
        get {
            // This keeps the coordinate box centered horizontally in the viewer
            // (Assuming your coordinate box is roughly 150px wide)
            return (ViewportWidth / 2) - 40;
        }
    }

    private double _viewportHeight;

    public double ViewportHeight {
        get => _viewportHeight;
        set {
            _viewportHeight = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StickyTopPosition));
        }
    }

    private double _viewportWidth;

    public double ViewportWidth {
        get => _viewportWidth;
        set {
            _viewportWidth = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StickyLeftPosition));
        }
    }

    private double _horizontalScrollOffset;

    public double HorizontalScrollOffset {
        get => _horizontalScrollOffset;
        set {
            _horizontalScrollOffset = value;
            OnPropertyChanged();
        }
    }

    private double _verticalScrollOffset;

    public double VerticalScrollOffset {
        get => _verticalScrollOffset;
        set {
            _verticalScrollOffset = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<MapLocation> Locations {
        get => _locations;
        set {
            if (_locations != value) {
                //if (_locations != null) _locations.CollectionChanged -= Locations_CollectionChanged;
                _locations = value;
                //if (_locations != null) _locations.CollectionChanged += Locations_CollectionChanged;
                _staticMarkersDirty = true;
                OnPropertyChanged();
                UpdateMarkers();
            }
        }
    }

    public FastObservableCollection<MapLocation3D> Locations3D { get; } = new();

    // private void Locations_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    // {
    //     _staticMarkersDirty = true;
    //     UpdateMarkers3D();
    // }

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
            if (_settings != null && _settings.ShowLocations != value && value) {
                _staticMarkersDirty = true;
            }

            if (_settings != null && _settings.ShowLocations != value) {
                _settings.ShowLocations = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isFollowModeActive;

    public bool IsFollowModeActive {
        get => _isFollowModeActive;
        set {
            _isFollowModeActive = value;
            OnPropertyChanged();
        }
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
        set {
            _drawColorIndex = value;
            OnPropertyChanged();
        }
    }

    public int DrawSizeMode {
        get => _drawSizeMode;
        set {
            _drawSizeMode = value;
            _drawingRadius = null;
            OnPropertyChanged();
        }
    }

    public bool DrawAntiAlias {
        get => _drawAntiAlias;
        set {
            _drawAntiAlias = value;
            OnPropertyChanged();
        }
    }

    public bool DrawShowSmallBrush {
        get {
            try {
                return GetBaseDrawRadius() >= 5.0;
            }
            catch {
                return false;
            }
        }
    }

    public bool DrawLineMode {
        get => _drawLineMode;
        set {
            _drawLineMode = value;
            OnPropertyChanged();
        }
    }

    private void PushDrawPoint(double x, double y) {
        _drawLastPoints.Add((x, y));
        if (_drawLastPoints.Count > 5) _drawLastPoints.RemoveAt(0);
    }

    public void SetDrawColor(int index) {
        DrawColorIndex = index;
        (_drawBrushB, _drawBrushG, _drawBrushR) = index switch {
            1 => ((byte)255, (byte)144, (byte)30), // Dodger Blue  #1E90FF
            2 => ((byte)0, (byte)128, (byte)0), // Green        #008000
            3 => ((byte)255, (byte)255, (byte)0), // Cyan         #00FFFF
            4 => ((byte)42, (byte)42, (byte)165), // Brown        #A52A2A
            5 => ((byte)140, (byte)180, (byte)210), // Tan          #D2B48C
            6 => ((byte)0, (byte)255, (byte)255), // Yellow       #FFFF00
            7 => ((byte)0, (byte)165, (byte)255), // Orange       #FFA500
            8 => ((byte)128, (byte)0, (byte)128), // Purple       #800080
            9 => ((byte)0, (byte)0, (byte)255), // Red          #FF0000
            10 => ((byte)0, (byte)0, (byte)0), // Black        #000000
            11 => ((byte)128, (byte)128, (byte)128), // Gray         #808080
            _ => ((byte)255, (byte)255, (byte)255), // White (default / transparent ignored)
        };
    }

    private void ResetDrawSettings() {
        _drawBrushB = 255;
        _drawBrushG = 255;
        _drawBrushR = 255;
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
        get => _settings is { IsCalibrated: true, ShowBreadcrumb: true } && BreadcrumbImage != null;
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

    private bool _isHovered = true; //default to hovered, let other code deal with setting it to not hovered.

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
                    _mapName = Path.GetFileName(value);
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
            byte alpha = 255;
            return !IsHovered && Opacity < 1
                ? System.Windows.Media.Brushes.Transparent
                : new SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(alpha, 62, 62, 66)); // Black with variable transparency
        }
    }

    public double EffectiveTransparent => IsHovered ? 1.0 : 0;

    public double EffectiveOpacity => IsHovered ? 1.0 : string.IsNullOrEmpty(_settings?.ImagePath) ? 1 : Opacity;

    public Visibility UIVisibility => (IsHovered || Opacity >= 1.0 || string.IsNullOrEmpty(_settings?.ImagePath))
        ? Visibility.Visible
        : Visibility.Collapsed;

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


    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (_calibratingNewDrawMap) return;

        if (e.PropertyName == nameof(MapSettings.IsCalibrated)) {
            OnPropertyChanged(nameof(ShowLocations));
            _staticMarkersDirty = true;
            UpdateMarkers();
        }
        else if (e.PropertyName == nameof(MapSettings.Point1) || e.PropertyName == nameof(MapSettings.Point2)) {
            if (_settings == null) return;
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
            if (_settings != null) UpdateMarkerScale(_settings.ZoomLevel);
            UpdateMarkers();
        }
        else if (e.PropertyName == nameof(MapSettings.ShowLocations)) {
            _staticMarkersDirty = true;
            UpdateMarkers();
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
        // Handled centrally by MainViewModel via PropertyChanged subscriptions
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
            // Prevent infinite property loop if the position value hasn't changed
            if (Nullable.Equals(_currentPosition, value)) return;
            _currentPosition = value;
            OnPropertyChanged();
            UpdateMarkers();
        }
    }

    public CoordinateData? TargetPosition {
        get => _targetPosition;
        set {
            // Prevent infinite property loop if the target value hasn't changed
            if (Nullable.Equals(_targetPosition, value)) return;
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
        set {
            _mapImage = value;
            OnPropertyChanged();
        }
    }

    public BitmapSource? OriginalMapImage {
        get => _originalMapImage;
        set {
            _originalMapImage = value;
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

    #region For rotation

    public event Action<DungeonMapLayerConfig>? RequestFocusOnLayer;

    /// <summary>
    /// Fires an event requesting the 3D viewport to center camera orbit around a specific map layer.
    /// </summary>
    public void FocusCameraOnLayer(DungeonMapLayerConfig layer) {
        if (layer == null) return;
        RequestFocusOnLayer?.Invoke(layer);
    }

    #endregion

    private void StartFading() {
        if (_fadeTimer == null) {
            _fadeTimer = new DispatcherTimer(DispatcherPriority.Background) {
                Interval = TimeSpan.FromSeconds(2)
            };
            _fadeTimer.Tick += FadeTrail_Tick;
            _fadeTimer.Start();
        }
    }

    private void FadeTrail_Tick(object? sender, EventArgs eventArgs) {
        FadeTrail(0.92);
    }

    public void StopFading() {
        if (_fadeTimer != null) {
            _fadeTimer.Stop();
            _fadeTimer.Tick -= FadeTrail_Tick;
            _fadeTimer = null;
        }
    }

    public void LoadImage() {
        if (IsDrawModeActive) return;
        _loadingFile = true;

        if (string.IsNullOrEmpty(_settings?.ImagePath)) {
            ResetMapState();
            return;
        }

        try {
            string targetPath = _settings.ImagePath;

            // 1. Guard against missing files / Handle AppData Roaming -> Local migration
            if (!File.Exists(targetPath)) {
                string localFallbackPath = TryResolveMigratedLocalPath(targetPath);

                if (File.Exists(localFallbackPath)) {
                    Log.Information("Migrated map path from Roaming to Local: '{OldPath}' -> '{NewPath}'", targetPath,
                        localFallbackPath);
                    targetPath = localFallbackPath;
                    _settings.ImagePath = localFallbackPath;
                    SaveSettings();
                }
                else {
                    Log.Warning("Map image file not found at path '{ImagePath}'. Resetting map state.", targetPath);
                    ResetMapState();
                    return;
                }
            }

            // 2. Safely decode the image
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(targetPath, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze(); // Makes it cross-thread safe and optimizes performance

            MapImage = image;
            OriginalMapImage = image;

            // 3. Handle Fog of War (.fog)
            var fogFilePath = Path.ChangeExtension(targetPath, ".fog");
            if (File.Exists(fogFilePath)) {
                var fogImage = new BitmapImage();
                fogImage.BeginInit();
                fogImage.UriSource = new Uri(fogFilePath, UriKind.Absolute);
                fogImage.CacheOption = BitmapCacheOption.OnLoad;
                fogImage.EndInit();

                if (!string.IsNullOrEmpty(FogOfWarFilePath) && FogImage != null) {
                    ImageHelpers.SaveWriteableBitMap(FogOfWarFilePath, FogImage.Clone());
                }

                FogOfWarFilePath = fogFilePath;
                FogImage = new WriteableBitmap(fogImage);
            }
            else {
                if (!string.IsNullOrEmpty(FogOfWarFilePath) && FogImage != null) {
                    ImageHelpers.SaveWriteableBitMap(FogOfWarFilePath, FogImage.Clone());
                }

                FogOfWarFilePath = fogFilePath;
                FogImage = ImageHelpers.CreateBlackBitmap(MapImage);
            }

            BreadcrumbImage = ImageHelpers.CreateTransparentBitmap(MapImage);
            MapPath = targetPath;
            MapName = targetPath;

            OnPropertyChanged(nameof(UIVisibility));
            LoadImageConfig(targetPath);
            UpdateMarkers();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error loading map image from '{ImagePath}'", _settings?.ImagePath);
            ResetMapState();
        }
        finally {
            _loadingFile = false;
        }
    }

    /// <summary>
    /// Helper to reset map state when a file fails to load or is missing.
    /// </summary>
    private void ResetMapState() {
        MapName = string.Empty;
        MapPath = string.Empty;
        MapImage = null;
        BreadcrumbImage = null;

        if (!string.IsNullOrEmpty(FogOfWarFilePath) && FogImage != null) {
            ImageHelpers.SaveWriteableBitMap(FogOfWarFilePath, FogImage.Clone());
        }

        FogOfWarFilePath = string.Empty;
        FogImage = null;

        if (Settings != null) {
            Settings.ImagePath = string.Empty;
        }

        OnPropertyChanged(nameof(UIVisibility));
        _loadingFile = false;
        UpdateMarkers();
    }

    /// <summary>
    /// Tries replacing AppData\Roaming in a stale path with AppData\Local.
    /// </summary>
    private static string TryResolveMigratedLocalPath(string originalPath) {
        if (string.IsNullOrWhiteSpace(originalPath)) return originalPath;

        string roamingPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string localPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (originalPath.StartsWith(roamingPath, StringComparison.OrdinalIgnoreCase)) {
            return originalPath.Replace(roamingPath, localPath, StringComparison.OrdinalIgnoreCase);
        }

        return originalPath;
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
            catch (JsonException jex) {
                Log.Warning(jex, "JSON error loading map config for '{Path}'", imagePath);
                System.Windows.MessageBox.Show(
                    $"The map configuration file for '{Path.GetFileName(imagePath)}' is corrupted and could not be loaded. It will be recalibrated.\n\nError: {jex.Message}",
                    "Map Configuration Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                Settings.ImagePath = imagePath;
                Settings.IsCalibrated = false;
                calibrated = false;
            }
            catch (Exception ex) {
                Log.Error(ex, "Error loading map image config for '{Path}'", imagePath);
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

    private double GetPixelsPerGameUnit() {
        // Fallback default: If 1 pixel = 0.1 game units, then 1 game unit = 10 pixels
        if (_settings == null) return 2.0;

        double dx = _settings.Point2.X - _settings.Point1.X;
        double dy = _settings.Point2.Y - _settings.Point1.Y;
        double dpx = _settings.Point2.PixelX - _settings.Point1.PixelX;
        double dpy = _settings.Point1.PixelY - _settings.Point2.PixelY;

        double dReal = Math.Sqrt(dx * dx + dy * dy); // Distance in game units
        double dPixel = Math.Sqrt(dpx * dpx + dpy * dpy); // Distance in pixels

        if (dReal < 0.0001) return 2.0;

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

            int yMin = Math.Max(0, (int)(rawCenterY - rawRadius));
            int yMax = Math.Min(FogImage.PixelHeight, (int)(rawCenterY + rawRadius));
            int xMin = Math.Max(0, (int)(rawCenterX - rawRadius));
            int xMax = Math.Min(FogImage.PixelWidth, (int)(rawCenterX + rawRadius));

            for (int y = yMin; y < yMax; y++) {
                for (int x = xMin; x < xMax; x++) {
                    double dx = x - rawCenterX;
                    double dy = y - rawCenterY;
                    if (dx * dx + dy * dy <= radiusSq) {
                        unsafe {
                            byte* pPixel = (byte*)pBackBuffer + (y * stride) + (x * 4);
                            pPixel[3] = 0;
                        }
                    }
                }
            }

            // Tell WPF to update the specific rectangle
            int w = Math.Min((int)(rawRadius * 2) + 1, FogImage.PixelWidth - xMin);
            int h = Math.Min((int)(rawRadius * 2) + 1, FogImage.PixelHeight - yMin);

            if (w > 0 && h > 0) {
                try {
                    FogImage.AddDirtyRect(new Int32Rect(xMin, yMin, w, h));
                }
                catch (Exception ex) {
                    Log.Error(ex, "Error in PunchTransparentCircle.AddDirtyRect.");
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in PunchTransparentCircle.");
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
            int yMin = Math.Max(0, (int)(rawCenterY - rawRadius));
            int yMax = Math.Min(BreadcrumbImage.PixelHeight, (int)(rawCenterY + rawRadius));
            int xMin = Math.Max(0, (int)(rawCenterX - rawRadius));
            int xMax = Math.Min(BreadcrumbImage.PixelWidth, (int)(rawCenterX + rawRadius));

            for (int y = yMin; y < yMax; y++) {
                for (int x = xMin; x < xMax; x++) {
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

            // Tell WPF to update the specific rectangle
            int w = Math.Min((int)(rawRadius * 2) + 2, BreadcrumbImage.PixelWidth - xMin);
            int h = Math.Min((int)(rawRadius * 2) + 2, BreadcrumbImage.PixelHeight - yMin);

            // Ensure width/height don't exceed image bounds
            if (w > 0 && h > 0) {
                try {
                    BreadcrumbImage.AddDirtyRect(new Int32Rect(xMin, yMin, w, h));
                }
                catch (Exception ex) {
                    Log.Error(ex, "Error in PunchBreadcrumbCircle.AddDirtyRect.");
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in PunchBreadcrumbCircle.");
        }
        finally {
            BreadcrumbImage.Unlock();
        }
    }

    private void FadeTrail(double decayFactor) {
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
                        byte* pPixel = pRow + (x * 4);

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
            }
            catch (Exception ex) {
                Log.Error(ex, "Error in FadeTrail.AddDirtyRect.");
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing FadeTrail.");
        }
        finally {
            BreadcrumbImage.Unlock();
        }
    }

    private bool _isUpdatingMarkers;

    public void UpdateMarkers() {
        // Prevent re-entrancy / infinite recursion lockups
        if (_isUpdatingMarkers || _loadingFile || _expandingMap || _calibratingNewDrawMap) return;
        if (_settings == null) return;

        _isUpdatingMarkers = true;
        try {
            if (CurrentPosition.HasValue) {
                // Auto-calibrate a newly created draw map on the first coordinate read
                if (IsDrawModeActive && _drawModeNeedsCalibration) {
                    CalibrateNewDrawMap(CurrentPosition.Value);
                    _drawModeNeedsCalibration = false;
                }
            }

            // if (!_settings.IsCalibrated || MapImage == null) {
            //     CurrentPositionMarkerVisibility = Visibility.Collapsed;
            //     TargetMarkerVisibility = Visibility.Collapsed;
            //     if (_locationMarkersShowing) {
            //         foreach (var loc in Locations) {
            //             loc.Visibility = Visibility.Collapsed;
            //         }
            //
            //         _locationMarkersShowing = false;
            //     }
            //
            //     return;
            // }

            if (CurrentPosition.HasValue) {
                var (mx, my, vis) = CalculatePixelPosition(CurrentPosition.Value);
                MarkerX = mx;
                MarkerY = my;
                CurrentPositionMarkerVisibility = vis;

                if (CurrentPositionMarkerVisibility == Visibility.Visible) {
                    if (IsDrawModeActive) {
                        if (ExpandDrawMapIfNeeded(MarkerX, MarkerY)) {
                            var (expMx, expMy, expVis) = CalculatePixelPosition(CurrentPosition.Value);
                            MarkerX = expMx;
                            MarkerY = expMy;
                            CurrentPositionMarkerVisibility = expVis;
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
                    }
                    else {
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
                var (tx, ty, tvis) = CalculatePixelPosition(TargetPosition.Value);
                TargetMarkerX = tx;
                TargetMarkerY = ty;
                TargetMarkerVisibility = tvis;
            }
            else {
                TargetMarkerVisibility = Visibility.Collapsed;
            }

            // Update 3D elements safely
            UpdateMarkers3D();

            // Only process static location pixel mapping if marked dirty and locations are enabled
            if (_staticMarkersDirty && _settings.ShowLocations) {
                foreach (var loc in Locations) {
                    if (Scrubber.TryParse(loc.Coordinates, "x z y d", out var coords)) {
                        var (x, y, vis) = CalculatePixelPosition(coords);
                        if (Math.Abs(loc.PixelX - x) > 0.1) loc.PixelX = x;
                        if (Math.Abs(loc.PixelY - y) > 0.1) loc.PixelY = y;
                        if (loc.Visibility != vis) {
                            loc.Visibility = vis;
                            // if (vis == Visibility.Visible) {
                            //     _locationMarkersShowing = true;
                            // }
                        }
                    }
                    else {
                        if (loc.Visibility != Visibility.Collapsed) loc.Visibility = Visibility.Collapsed;
                    }
                }

                _staticMarkersDirty = false;
            }
        }
        finally {
            _isUpdatingMarkers = false;
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

            if (MapImage == null || _settings == null)
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

            // Apply a safe limit to the scale factor to prevent WPF layout overflows
            // A scale of 10,000 pixels per game unit is extremely high and should be sufficient.
            if (scale > 10000) {
                return (0, 0, Visibility.Collapsed);
            }

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
            Log.Error(ex, "Error updating marker position in CalculatePixelPosition.");
            return (0, 0, Visibility.Collapsed);
        }
    }

    private double CalculatePixelHeading(double gameHeading) {
        try {
            if (_settings == null) return 0;

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
        catch (Exception ex) {
            Log.Error(ex, "Error calculating pixel heading.");
            return 0;
        }
    }

    public void UpdateHoverCoordinates(double px, double py) {
        if (_settings == null || !_settings.IsCalibrated || MapImage == null) {
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

            // Apply a safe limit to the scale factor. 
            // 10,000 game units per pixel is extremely large (one pixel jump = 10km).
            if (scale > 10000) {
                HoverCoordinatesLabel = string.Empty;
                return;
            }

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

            HoverCoordinatesLabel = $"{x:F1}, {y:F1}";
        }
        catch (Exception ex) {
            Log.Error(ex, "Error calculating hover coordinates.");
            HoverCoordinatesLabel = string.Empty;
        }
    }

    public CoordinateData? GetCoordinatesFromPixels(double px, double py) {
        if (_settings == null || !_settings.IsCalibrated || MapImage == null) {
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

            // Apply a safe limit to the scale factor.
            if (scale > 10000) {
                return null;
            }

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
            Log.Error(ex, "Error calculating coordinates from pixels.");
            return null;
        }
    }

    public void SelectDestination(CoordinateData coords) {
        TargetPosition = coords;
        UpdateMarkers();
        DestinationSelected?.Invoke(coords);
    }

    public void ValidateWindowBounds() {
        if (Settings == null) return;

        var s = Settings.Placement;

        // Check if the saved Top/Left is within the bounds of the current desktop
        if (s.Left < SystemParameters.VirtualScreenLeft ||
            s.Left > (SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50) ||
            s.Top < SystemParameters.VirtualScreenTop ||
            s.Top > (SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50)) {
            // Reset to default if it's out of bounds
            s.Left = 100;
            s.Top = 100;
        }
    }

    public void SaveMapImage() {
        if (Settings == null || string.IsNullOrWhiteSpace(Settings.ImagePath)) return;
        string originalPath = Settings.ImagePath;

        if (File.Exists(originalPath)) {
            // Generate a backup path (e.g., "C:/Maps/world_map.png.bak")
            string backupPath = originalPath + ".bak";

            try {
                // Copy the original file on disk, overwriting any previous backup
                File.Copy(originalPath, backupPath, overwrite: true);
            }
            catch (Exception ex) {
                Log.Warning(ex, "Failed to create file backup for '{Path}'", originalPath);
            }
        }
    }

    /// <summary>
    /// Removes a specific map layer from the active set and requests 3D viewport cleanup.
    /// </summary>
    public void RemoveLayer(DungeonMapLayerConfig layer) {
        if (layer == null) return;

        if (ActiveSetLayers.Contains(layer)) {
            ActiveSetLayers.Remove(layer);
        }

        if (ActiveDrawLayer == layer) {
            ActiveDrawLayer = ActiveSetLayers.FirstOrDefault();
        }

        // Raise an event or property change so the view cleans up the 3D viewport mesh
        OnPropertyChanged(nameof(ActiveSetLayers));
    }

    public void StartDrawMode(string mapName) {
        if (IsDrawModeActive) StopDrawMode();
        ResetDrawSettings();
        _drawingRadius = null; // Ensure fresh radius calculation on first tick

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
            if (imagePath != Settings.ImagePath) {
                Settings.ImagePath = imagePath;
                LoadDrawModeMap(imagePath);
            }
            else {
                SaveMapImage();
            }

            _drawingRadius = null;
            if (Settings.IsCalibrated) {
                _drawModeNeedsCalibration = false;
                _drawingRadius = GetDrawBrushRadius();
            }
            else {
                _drawModeNeedsCalibration = true;
            }
        }
        else {
            Settings.ImagePath = imagePath;
            _drawingRadius = null;
            CreateNewDrawMap(imagePath);
            _drawModeNeedsCalibration = true;
        }

        if (MapImage != null) {
            BreadcrumbImage = ImageHelpers.CreateTransparentBitmap(MapImage);
        }

        IsDrawModeActive = true;
        StartDrawAutoSave();
        SaveDrawMap();
    }

    public void StopDrawMode() {
        if (!IsDrawModeActive) return;

        SaveDrawMap();

        // Cleanly stop and unhook the timer
        StopDrawAutoSave();

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

            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });

            // Atomic write for map configuration
            var tempPath = configPath + ".tmp";
            File.WriteAllText(tempPath, json);
            if (File.Exists(configPath)) {
                File.Replace(tempPath, configPath, configPath + ".old");
            }
            else {
                File.Move(tempPath, configPath);
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error saving draw map.");
        }
    }

    private void StartDrawAutoSave() {
        // 1. Stop and unhook any existing timer first to prevent duplicate ticks/leaks
        StopDrawAutoSave();

        // 2. Safely initialize and subscribe
        _drawSaveTimer = new DispatcherTimer(DispatcherPriority.Background) {
            Interval = TimeSpan.FromSeconds(30)
        };
        _drawSaveTimer.Tick += DrawSaveTimer_Tick;
        _drawSaveTimer.Start();
    }

    private void StopDrawAutoSave() {
        if (_drawSaveTimer != null) {
            _drawSaveTimer.Stop();
            _drawSaveTimer.Tick -= DrawSaveTimer_Tick;
            _drawSaveTimer = null;
        }
    }

    private void DrawSaveTimer_Tick(object? sender, EventArgs e) {
        try {
            SaveDrawMap();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing auto-save in DrawSaveTimer_Tick.");
        }
    }

    private void CreateNewDrawMap(string imagePath) {
        const int initialSize = 500;
        var bitmap = ImageHelpers.CreateBlackBitmapSize(initialSize, initialSize, 96, 96);
        ImageHelpers.SaveWriteableBitMap(imagePath, bitmap);

        MapImage = bitmap;
        MapPath = imagePath;
        MapName = imagePath;

        if (_settings != null) _settings.IsCalibrated = false;

        OnPropertyChanged(nameof(UIVisibility));
        UpdateMarkers();
    }

    private void LoadDrawModeMap(string imagePath) {
        var bitmap = LoadAsPbgra32WriteableBitmap(imagePath);

        MapImage = bitmap;
        MapPath = imagePath;
        MapName = imagePath;

        LoadImageConfig(imagePath);
        OnPropertyChanged(nameof(UIVisibility));
        UpdateMarkers();
    }

    private void CalibrateNewDrawMap(CoordinateData initialPos) {
        if (_settings == null || MapImage == null) return;

        // 1. Clear the trigger flag FIRST so UpdateMarkers won't try re-entering CalibrateNewDrawMap
        _drawModeNeedsCalibration = false;
        _calibratingNewDrawMap = true;

        try {
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

            // This property change event will now be ignored by Settings_PropertyChanged
            // because _calibratingNewDrawMap is still true!
            _settings.IsCalibrated = true;

            _drawingRadius = GetDrawBrushRadius();
            SaveDrawMap();
        }
        finally {
            // 2. Safely lower the guard flag AFTER all property mutations finish
            _calibratingNewDrawMap = false;
        }

        // 3. Force an immediate marker and brush stroke update cleanly
        _staticMarkersDirty = true;
        UpdateMarkers();
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

            int padLeftPx = (int)Math.Round(padLeft * dpiScaleX);
            int padTopPx = (int)Math.Round(padTop * dpiScaleY);
            int padRightPx = (int)Math.Round(padRight * dpiScaleX);
            int padBotPx = (int)Math.Round(padBottom * dpiScaleY);

            int newW = bitmap.PixelWidth + padLeftPx + padRightPx;
            int newH = bitmap.PixelHeight + padTopPx + padBotPx;

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
            
            //We have the last drawpoint, but its now obsolete
            //_drawLastPoints.Clear();
            // Shift any existing recent draw points so they align with the new bitmap origin
            //clearing means ther ecould be a gap when drawing in "lines" as opposed to dots
            //lets translate the old last position to the new coordinates/pixels in the new map size
            if (padLeft > 0 || padTop > 0) {
                for (int i = 0; i < _drawLastPoints.Count; i++) {
                    _drawLastPoints[i] = (_drawLastPoints[i].X + padLeft, _drawLastPoints[i].Y + padTop);
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing ExpandDrawMapIfNeeded.");
        }
        finally {
            _expandingMap = false;
        }

        return true;
    }

    private static void CopyBitmapToOffset(WriteableBitmap source, WriteableBitmap dest, int destX, int destY) {
        int w = source.PixelWidth;
        int h = source.PixelHeight;
        int stride = w * 4;
        byte[] pixels = new byte[stride * h];
        source.CopyPixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
        dest.WritePixels(new Int32Rect(destX, destY, w, h), pixels, stride, 0);
    }

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
                }
                else {
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
        double rawR = Math.Max(1.0, radiusInPixels * dpiScaleX);

        int xMin = Math.Max(0, (int)(rawCx - rawR));
        int yMin = Math.Max(0, (int)(rawCy - rawR));
        int xMax = Math.Min(drawBitmap.PixelWidth - 1, (int)(rawCx + rawR));
        int yMax = Math.Min(drawBitmap.PixelHeight - 1, (int)(rawCy + rawR));

        drawBitmap.Lock();
        try {
            unsafe {
                PaintCircleCore((byte*)drawBitmap.BackBuffer, drawBitmap.BackBufferStride,
                    drawBitmap.PixelWidth, drawBitmap.PixelHeight, rawCx, rawCy, rawR);
            }

            int dirtyW = xMax - xMin + 1;
            int dirtyH = yMax - yMin + 1;
            if (dirtyW > 0 && dirtyH > 0) {
                try {
                    drawBitmap.AddDirtyRect(new Int32Rect(xMin, yMin, dirtyW, dirtyH));
                }
                catch (Exception ex) {
                    Log.Error(ex, "Error in PaintDrawPixels.AddDirtyRect");
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing PaintDrawPixels.");
        }
        finally {
            drawBitmap.Unlock();
        }
    }

    private void PaintDrawLine(double fromX, double fromY, double toX, double toY, double radiusInPixels) {
        if (MapImage is not WriteableBitmap drawBitmap) return;
        if (_drawColorIndex == 12) return;

        double dpiScaleX = drawBitmap.PixelWidth / drawBitmap.Width;
        double dpiScaleY = drawBitmap.PixelHeight / drawBitmap.Height;
        double rawFx = fromX * dpiScaleX, rawFy = fromY * dpiScaleY;
        double rawTx = toX * dpiScaleX, rawTy = toY * dpiScaleY;
        double rawR = Math.Max(1.0, radiusInPixels * dpiScaleX);

        double dx = rawTx - rawFx, dy = rawTy - rawFy;
        double length = Math.Sqrt(dx * dx + dy * dy);

        int dirtXMin = Math.Max(0, (int)(Math.Min(rawFx, rawTx) - rawR));
        int dirtYMin = Math.Max(0, (int)(Math.Min(rawFy, rawTy) - rawR));
        int dirtXMax = Math.Min(drawBitmap.PixelWidth - 1, (int)(Math.Max(rawFx, rawTx) + rawR) + 1);
        int dirtYMax = Math.Min(drawBitmap.PixelHeight - 1, (int)(Math.Max(rawFy, rawTy) + rawR) + 1);

        drawBitmap.Lock();
        try {
            unsafe {
                byte* buf = (byte*)drawBitmap.BackBuffer;
                int stride = drawBitmap.BackBufferStride;
                int bmpW = drawBitmap.PixelWidth, bmpH = drawBitmap.PixelHeight;
                if (length < 0.5) {
                    PaintCircleCore(buf, stride, bmpW, bmpH, rawFx, rawFy, rawR);
                }
                else {
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
                try {
                    drawBitmap.AddDirtyRect(new Int32Rect(dirtXMin, dirtYMin, dirtyW, dirtyH));
                }
                catch (Exception ex) {
                    Log.Error(ex, "Error in PaintDrawLine.AddDirtyRect");
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing PaintDrawLine.");
        }
        finally {
            drawBitmap.Unlock();
        }
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

    private static WriteableBitmap LoadAsPbgra32WriteableBitmap(string imagePath) {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(imagePath);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze(); // This freeze is perfect and safe

        // 1. Check if it's already in our preferred high-performance format
        if (bmp.Format == PixelFormats.Pbgra32) {
            return new WriteableBitmap(bmp);
        }

        // 2. Normalize to Pbgra32 if it's any other format (RGB24, Bgra32, Indexed, etc.)
        var converted = new FormatConvertedBitmap();
        converted.BeginInit();
        converted.Source = bmp;
        converted.DestinationFormat = PixelFormats.Pbgra32;
        converted.EndInit();

        return new WriteableBitmap(converted);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose() {
        StopFading();
        StopDrawAutoSave();

        // Clean up settings hooks
        if (_settings != null) {
            _settings.PropertyChanged -= Settings_PropertyChanged;
            _settings.Point1.PropertyChanged -= MapPoint_PropertyChanged;
            _settings.Point2.PropertyChanged -= MapPoint_PropertyChanged;
        }
        // Clean up collection hooks
        //_locations.CollectionChanged -= Locations_CollectionChanged;
    }
}