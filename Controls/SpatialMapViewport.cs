using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using MMONavigator.Models;

namespace MMONavigator.Controls;

public class SpatialMapViewport : System.Windows.Controls.UserControl {
    private Viewport3D _viewport = null!;
    private Model3DGroup _worldGroup = null!;
    private PerspectiveCamera _camera = null!;
    private bool _isInitialized = false;

    private CoordinateSystem _coordinateSystem = CoordinateSystem.RightHanded;

    public CoordinateSystem CoordinateSystem {
        get => _coordinateSystem;
        set {
            if (_coordinateSystem != value) {
                _coordinateSystem = value;
                // Force a re-render of camera or layers if coordinate system flips
                UpdateCameraPosition();
            }
        }
    }

    private Model3DGroup _locationMarkersGroup = new Model3DGroup();
    private readonly Dictionary<GeometryModel3D, MapLocation3D> _markerToLocationMap = new();

    public static BitmapSource DrawTargetCrosshair(string imagePath, double targetX, double targetY) {
        BitmapImage original = new BitmapImage();
        original.BeginInit();
        original.UriSource = new Uri(imagePath, UriKind.Absolute);
        original.CacheOption = BitmapCacheOption.OnLoad;
        original.EndInit();

        DrawingVisual visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen()) {
            // 1. Draw original map image
            dc.DrawImage(original, new System.Windows.Rect(0, 0, original.PixelWidth, original.PixelHeight));

            // 2. Setup bright red pen for target marker
            System.Windows.Media.Pen linePen = new System.Windows.Media.Pen(System.Windows.Media.Brushes.Red, 4);
            System.Windows.Media.Pen centerPen = new System.Windows.Media.Pen(System.Windows.Media.Brushes.Yellow, 2);

            // Vertical line through target X
            dc.DrawLine(linePen, new System.Windows.Point(targetX, 0),
                new System.Windows.Point(targetX, original.PixelHeight));

            // Horizontal line through target Y
            dc.DrawLine(linePen, new System.Windows.Point(0, targetY),
                new System.Windows.Point(original.PixelWidth, targetY));

            // Small yellow target dot at exact intersection (1262, 272)
            dc.DrawEllipse(System.Windows.Media.Brushes.Yellow, centerPen, new System.Windows.Point(targetX, targetY),
                8, 8);
        }

        RenderTargetBitmap rtb = new RenderTargetBitmap(
            original.PixelWidth,
            original.PixelHeight,
            original.DpiX,
            original.DpiY,
            PixelFormats.Pbgra32);

        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    #region Ground/Floor projected markers (one per map layer)

    private readonly Dictionary<string, GeometryModel3D> _projectedFloorMarkers = new();

    /// <summary>
    /// Creates a flat, 2D circular projected marker mesh sitting directly on a map plane,
    /// matching the exact base radius of the avatar triangle.
    /// </summary>
    private GeometryModel3D CreateProjectedFloorCircleMesh(System.Windows.Media.Color color, double radius = 1.2) {
        MeshGeometry3D circleMesh = new MeshGeometry3D();
        int segments = 24;

        // Center vertex
        circleMesh.Positions.Add(new Point3D(0, 0, 0));

        for (int i = 0; i < segments; i++) {
            double angle = i * 2.0 * Math.PI / segments;
            double x = radius * Math.Cos(angle);
            double y = radius * Math.Sin(angle);
            circleMesh.Positions.Add(new Point3D(x, y, 0.05)); // Offset slightly above quad to prevent Z-fighting
        }

        // CCW Triangle Fan
        for (int i = 1; i <= segments; i++) {
            int next = (i % segments) + 1;
            circleMesh.TriangleIndices.Add(0);
            circleMesh.TriangleIndices.Add(next);
            circleMesh.TriangleIndices.Add(i);
        }

        SolidColorBrush brush = new SolidColorBrush(color);
        MaterialGroup matGroup = new MaterialGroup();
        matGroup.Children.Add(new DiffuseMaterial(brush));
        matGroup.Children.Add(new EmissiveMaterial(brush));

        return new GeometryModel3D { Geometry = circleMesh, Material = matGroup, BackMaterial = matGroup };
    }

    #endregion

    // Camera Orbit Properties
    private Point3D _cameraTarget = new Point3D(0, 0, 0); // Focal Point
    private float _cameraYaw = 0.0f; // Horizontal Rotation (Degrees)
    private float _cameraPitch = 45.0f; // Vertical Elevation Angle (Degrees)
    private float _cameraRadius = 150.0f; // Distance to Target
    private bool _allowFlip = true;
    private float _zoomLimit = 50000.0f;

    // Visual Elements Tracking
    private GeometryModel3D? _breadcrumbLineModel;
    private readonly List<Point3D> _breadcrumbPoints = new();
    private readonly Dictionary<string, (GeometryModel3D Model, ImageBrush Brush, float ZElevation)> _mapLayers = new();

    // Visual Tracking Elements for Dual-Avatar System
    private GeometryModel3D? _playerGroundMarkerModel; // Flat/Grounded Directional Arrow
    //private GeometryModel3D? _playerTrueSphereModel; // Floating 3D White Sphere
    private GeometryModel3D? _playerTetherLineModel; // Vertical Tether (Ground Z to True Z)

    //Destination marker
    private GeometryModel3D? _destinationGroundMarkerModel; // Flat/Grounded Directional Arrow
    private GeometryModel3D? _destinationPulseRingModel;
    private GeometryModel3D? _destinationOuterPulseRingModel;
    
    // Pulse Rings
    private GeometryModel3D? _pulseRingModel;
    private MeshGeometry3D? _locationRingModel;
    private MeshGeometry3D? _locationModel;
   //private double _pulseScale = 1.0;
    private GeometryModel3D? _outerPulseRingModel;
    private double _outerPulseScale = 1.0;

    // Position Caching for Idle Frame Loop
    private Point3D _lastKnownGroundPos;
    private bool _hasPlayerPosition = false;
    private Point3D _destinationGroundPos;
    private bool _hasDestinationPosition = false;

    public Model3DGroup WorldGroup => _worldGroup;

    public SpatialMapViewport() {
        Initialize3DViewport();
        Loaded += (s, e) => EnsureInitialized();
    }

    private void EnsureInitialized() {
        if (_isInitialized) return;
        Initialize3DViewport();
    }

    private void Initialize3DViewport() {
        if (_isInitialized) return;

        _viewport = new Viewport3D {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch
        };
        _viewport.MouseLeftButtonDown += Viewport_MouseLeftButtonDown;
        _viewport.MouseMove += Viewport_MouseMove;
        //_viewport.MouseWheel += Viewport_MouseWheel;
        
        _worldGroup = new Model3DGroup();

        // Perspective Camera Setup
        _camera = new PerspectiveCamera {
            FieldOfView = 55,
            UpDirection = new Vector3D(0, 0, 1), // Z is UP
            NearPlaneDistance = 1.0, // Prevent clipping close objects
            FarPlaneDistance = 50000.0 // Expanded far-clipping plane
        };
        _viewport.Camera = _camera;

        // Add Ambient and Directional Lighting
        _worldGroup.Children.Add(new AmbientLight(System.Windows.Media.Color.FromRgb(255, 255, 255)));
        _worldGroup.Children.Add(new DirectionalLight(Colors.White, new Vector3D(0, 0, -1))); // Top light
        _worldGroup.Children.Add(new DirectionalLight(Colors.White, new Vector3D(0, 0, 1))); // Bottom light

        // Add Ambient Light so dark corners, underside faces, and off-Z map planes remain bright
        _worldGroup.Children.Add(new AmbientLight(System.Windows.Media.Color.FromRgb(180, 180, 180)));

// Top directional light
        _worldGroup.Children.Add(new DirectionalLight(Colors.White, new Vector3D(0, 0, -1)));

// Bottom directional light (lights maps when looking up from below)
        _worldGroup.Children.Add(new DirectionalLight(Colors.White, new Vector3D(0, 0, 1)));

        _worldGroup.Children.Add(_locationMarkersGroup);

        ModelVisual3D modelVisual = new ModelVisual3D { Content = _worldGroup };
        _viewport.Children.Add(modelVisual);

        Content = _viewport;

        _isInitialized = true;

        UpdateCameraPosition();

        // Hook per-frame render loop for continuous, smooth pulse animations
        System.Windows.Media.CompositionTarget.Rendering -= OnRenderingTick;
        System.Windows.Media.CompositionTarget.Rendering += OnRenderingTick;
    }
    #region Zoom to mouse
    
    // private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e) {
    //     float zoomDelta = e.Delta > 0 ? -15.0f : 15.0f;
    //     System.Windows.Point mousePos = e.GetPosition(_viewport);
    //
    //     ZoomCameraToMouse(zoomDelta, mousePos);
    //     e.Handled = true;
    // }
    
    #endregion
    private void OnRenderingTick(object? sender, EventArgs e) {
        // 1. Smoothly glide the camera if a transition is active
        AnimateCameraTransition();
        
        double baseScale = Math.Max(1.0, _cameraRadius / 100.0);

        if (_hasPlayerPosition) {
            // Drive player avatar pulse geometry continuously at the cached ground position
            UpdatePulseRing(_lastKnownGroundPos, baseScale);
            UpdateOuterRedPulseRing(_lastKnownGroundPos, baseScale);
        }

        if (_hasDestinationPosition) {
            // Drive destination pulse geometry continuously at the cached destination position
            UpdateDestinationPulseRing(_destinationGroundPos, baseScale);
            UpdateDestinationOuterRedPulseRing(_destinationGroundPos, baseScale);
        }
    }
    
    #region Locations

    public static readonly DependencyProperty Locations3DProperty =
        DependencyProperty.Register(
            nameof(Locations3D),
            typeof(IEnumerable<MapLocation3D>),
            typeof(SpatialMapViewport),
            new PropertyMetadata(null, OnLocations3DChanged));

    public IEnumerable<MapLocation3D> Locations3D {
        get => (IEnumerable<MapLocation3D>)GetValue(Locations3DProperty);
        set => SetValue(Locations3DProperty, value);
    }

    //saved map locations
    private static void OnLocations3DChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is SpatialMapViewport viewport) {
            // Unsubscribe from old collection events if necessary
            if (e.OldValue is INotifyCollectionChanged oldCollection) {
                oldCollection.CollectionChanged -= viewport.Locations_CollectionChanged;
            }

            // Subscribe to new collection events if valid
            if (e.NewValue is INotifyCollectionChanged newCollection) {
                newCollection.CollectionChanged += viewport.Locations_CollectionChanged;
            }

            // Cast or fallback to an empty collection so clearing/toggling off works instantly
            var locations = e.NewValue as IEnumerable<MapLocation3D> ?? Array.Empty<MapLocation3D>();
            viewport.UpdateLocationMarkers3D(locations);
        }
    }

    private bool _isHandlingCollectionChange;

    private void Locations_CollectionChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e) {
// Guard against re-entrant event loops
        if (_isHandlingCollectionChange) return;

        _isHandlingCollectionChange = true;
        try {
            if (Locations3D != null) {
                UpdateLocationMarkers3D(Locations3D);
            }
        }
        finally {
            _isHandlingCollectionChange = false;
        }
    }

    private bool _isUpdatingLocations;

    public double GetClosestLayerElevation(double x, double y, double z) {
        var closestZ = z;
        if (_mapLayers.Count > 0) {
            // Find the layer closest by Z elevation that actually contains or matches this location, 
            // or fallback safely if none match.
            var closestLayer = _mapLayers.Values
                .OrderBy(l => Math.Abs(l.ZElevation - (float)z))
                .FirstOrDefault(l => {
                    // Assuming you have access to the layer's bounds or settings here.
                    // Replace this with your actual world boundary check for the layer:
                    return IsCoordinateWithinLayerBounds(l, x, y);
                });

            double surfaceOffset = 0.03;

            if (closestLayer.Model != null) {
                closestZ = closestLayer.ZElevation + surfaceOffset;
            }
        }
        return closestZ;
    }
    
    public void UpdateLocationMarkers3D(IEnumerable<MapLocation3D> locations) {
        if (_isUpdatingLocations) return;
        _isUpdatingLocations = true;
        try {
            Debug.WriteLine($"UpdateLocationMarkers3D starts");
            EnsureInitialized();
            _locationMarkersGroup.Children.Clear();
            _markerToLocationMap.Clear();
            
            // Check collection count directly if it implements ICollection, avoiding multiple enumeration
            if (locations is ICollection<MapLocation3D> collection && collection.Count == 0) {
                Debug.WriteLine($"Updated 3D location exits A");
                EnforceTransparentRenderOrder();
                return;
            }
            else if (locations == null) {
                Debug.WriteLine($"Updated 3D location exits B");
                EnforceTransparentRenderOrder();
                return;
            }

            var redBrush = new SolidColorBrush(Colors.Red);
            var materialGroup = new MaterialGroup();
            materialGroup.Children.Add(new DiffuseMaterial(redBrush));
            materialGroup.Children.Add(new EmissiveMaterial(redBrush));
            Debug.WriteLine($"UpdateLocationMarkers3D starts targz loop");
            var layersExist = _mapLayers.Count > 0;


            if(_locationModel==null) _locationModel = CreateFlagMesh(height: 8.0);
            if (_locationRingModel == null) _locationRingModel = CreatePulseRingGeometry();
            
            // Reusable material group for the rings (you can cache this too if you want!)
            var ringMaterialGroup = new MaterialGroup();
            var redRingBrush = new SolidColorBrush(Colors.Red) { Opacity = 0.6 };
            ringMaterialGroup.Children.Add(new DiffuseMaterial(redRingBrush));
            ringMaterialGroup.Children.Add(new EmissiveMaterial(redRingBrush));
            
            foreach (var loc in locations) {
                if (loc.Visibility != Visibility.Visible) continue;

                double targetZ = loc.Z;

                if (layersExist) {
                    // Find the layer closest by Z elevation that actually contains or matches this location, 
                    // or fallback safely if none match.
                    var closestLayer = _mapLayers.Values
                        .OrderBy(l => Math.Abs(l.ZElevation - (float)loc.Z))
                        .FirstOrDefault(l => {
                            // Assuming you have access to the layer's bounds or settings here.
                            // Replace this with your actual world boundary check for the layer:
                            return IsCoordinateWithinLayerBounds(l, loc.X, loc.Y);
                        });

                    double surfaceOffset = 0.03;

                    if (closestLayer.Model != null) {
                        targetZ = closestLayer.ZElevation + surfaceOffset;

                        Debug.WriteLine($"UpdateLocationMarkers3D starts targz adjusted {targetZ} and {targetZ > 800}");
                    }
                    else {
                        // Fallback to native Z if no map layers exist or match
                        continue;
                        //targetZ = loc.Z + surfaceOffset;
                    }
                }
                else {
                    continue;
                    //targetZ = loc.Z + 0.03;
                }

                GeometryModel3D puckModel = new GeometryModel3D {
                    Geometry = _locationModel,
                    Material = materialGroup,
                    BackMaterial = materialGroup
                };

                // Create a separate ring model, but reuse the cached _locationRingMesh!
                GeometryModel3D ringModel = new GeometryModel3D {
                    Geometry = _locationRingModel, // Shared mesh, separate model instance
                    Material = materialGroup,
                    BackMaterial = materialGroup
                };
                
                Transform3DGroup transformGroup = new Transform3DGroup();
                transformGroup.Children.Add(new TranslateTransform3D(loc.X, loc.Y, targetZ));
                
                puckModel.Transform = transformGroup;
                ringModel.Transform = transformGroup;
                
                _locationMarkersGroup.Children.Add(puckModel);
                _locationMarkersGroup.Children.Add(ringModel);
                
                // Track the relationship for click testing
                _markerToLocationMap[puckModel] = loc;
            }

            Debug.WriteLine(
                $"Updated 3D location markers EnforceTransparentRenderOrder {_locationMarkersGroup.Children.Count}");
            EnforceTransparentRenderOrder();
        }
        catch (Exception ex) {
            Debug.WriteLine($"Error updating 3D location markers: {ex.Message}");
        }
        finally {
            _isUpdatingLocations = false;
        }
    }

    
    
    private bool IsCoordinateWithinLayerBounds((GeometryModel3D Model, ImageBrush Brush, float ZElevation) layer,
        double worldX, double worldY) {
        if (layer.Model.Geometry is not MeshGeometry3D mesh || mesh.Positions.Count == 0)
            return false;

        // 1. Extract min/max bounds from the mesh positions
        double minX = mesh.Positions.Min(p => p.X);
        double maxX = mesh.Positions.Max(p => p.X);
        double minY = mesh.Positions.Min(p => p.Y);
        double maxY = mesh.Positions.Max(p => p.Y);

        // 2. Account for the layer's translation transform (if any applied to the model)
        if (layer.Model.Transform is Transform3DGroup transformGroup) {
            foreach (var transform in transformGroup.Children) {
                if (transform is TranslateTransform3D translate) {
                    minX += translate.OffsetX;
                    maxX += translate.OffsetX;
                    minY += translate.OffsetY;
                    maxY += translate.OffsetY;
                }
            }
        }

        // 3. Check if the coordinate falls strictly within the rectangle
        return worldX >= minX && worldX <= maxX && worldY >= minY && worldY <= maxY;
    }

//     public void UpdateLocationMarkers3D(IEnumerable<MapLocation3D> locations) {
//         if (_isUpdatingLocations) return;
//         _isUpdatingLocations = true;
//         try {
//             EnsureInitialized();
//             _locationMarkersGroup.Children.Clear();
//
//             // Red hockey puck material (Diffuse + Emissive so it pops cleanly against dark dungeon floors)
//             var redBrush = new SolidColorBrush(Colors.Red);
//             var materialGroup = new MaterialGroup();
//             materialGroup.Children.Add(new DiffuseMaterial(redBrush));
//             materialGroup.Children.Add(new EmissiveMaterial(redBrush));
// Debug.WriteLine($"Updating {locations.Count()} location markers");
// Debug.WriteLine($"Updating {locations.Count(x=>x.Visibility == Visibility.Visible)} location markers");
//             foreach (var loc in locations) {
//                 if (loc.Visibility != Visibility.Visible) continue;
//                 loc.Z = 800 + .03;
//                 // Build a low-poly cylinder / disk (hockey puck)
//                // MeshGeometry3D locationMesh = CreateCylinderMesh(radius: 6.0, height: 1.8, segments: 16);
//                 MeshGeometry3D locationMesh = CreateFlagMesh(height: 8.0);
//                 GeometryModel3D puckModel = new GeometryModel3D {
//                     Geometry = locationMesh,
//                     Material = materialGroup,
//                     BackMaterial = materialGroup
//                 };
//
//                 // Translate the puck to its world coordinates
//                 Transform3DGroup transformGroup = new Transform3DGroup();
//                 transformGroup.Children.Add(new TranslateTransform3D(loc.X, loc.Y, loc.Z));
//                 puckModel.Transform = transformGroup;
//
//                 _locationMarkersGroup.Children.Add(puckModel);
//             }
//         }
//         catch (Exception ex) {
//             var s = ex.Message;
//         }
//         finally {
//             Debug.WriteLine($"Finally {locations.Count(x=>x.Visibility == Visibility.Visible)} location markers");
//             Debug.WriteLine($"Finally {locations.Count(x=>x.Z > 800)} Z location markers");
//             _isUpdatingLocations = false;
//         }
//     }

    /// <summary>
    /// Procedurally generates a 3D flag mesh (vertical pole + banner).
    /// </summary>
    private MeshGeometry3D CreateFlagMesh(double height = 16.0) {
        MeshGeometry3D mesh = new MeshGeometry3D();
        double poleRadius = 0.4;
        double bannerWidth = 8.0;
        double bannerHeight = 5;
        double topZ = height;
        double bottomZ = 0.0;

        // --- 1. Vertical Pole (Thin Cylinder / Box) ---
        // We can create a simple 4-sided square column for the pole
        int p1 = mesh.Positions.Count;
        mesh.Positions.Add(new Point3D(-poleRadius, -poleRadius, bottomZ));
        mesh.Positions.Add(new Point3D(poleRadius, -poleRadius, bottomZ));
        mesh.Positions.Add(new Point3D(poleRadius, poleRadius, bottomZ));
        mesh.Positions.Add(new Point3D(-poleRadius, poleRadius, bottomZ));

        int p2 = mesh.Positions.Count;
        mesh.Positions.Add(new Point3D(-poleRadius, -poleRadius, topZ));
        mesh.Positions.Add(new Point3D(poleRadius, -poleRadius, topZ));
        mesh.Positions.Add(new Point3D(poleRadius, poleRadius, topZ));
        mesh.Positions.Add(new Point3D(-poleRadius, poleRadius, topZ));

        // Simple quad walls for the pole
        int[] poleIndices = {
            0, 1, 5, 0, 5, 4,
            1, 2, 6, 1, 6, 5,
            2, 3, 7, 2, 7, 6,
            3, 0, 4, 3, 4, 7
        };
        foreach (var idx in poleIndices) mesh.TriangleIndices.Add(p1 + idx);

        // --- 2. Flag Banner (Rectangle flying from top of the pole) ---
        int bStart = mesh.Positions.Count;
        double bannerZBottom = topZ - bannerHeight;

        mesh.Positions.Add(new Point3D(poleRadius, 0, topZ)); // Top-Left (attached to pole)
        mesh.Positions.Add(new Point3D(poleRadius + bannerWidth, 0, topZ)); // Top-Right (outer edge)
        mesh.Positions.Add(new Point3D(poleRadius + bannerWidth, 0, bannerZBottom)); // Bottom-Right
        mesh.Positions.Add(new Point3D(poleRadius, 0, bannerZBottom)); // Bottom-Left

        // Double-sided banner triangles
        mesh.TriangleIndices.Add(bStart);
        mesh.TriangleIndices.Add(bStart + 1);
        mesh.TriangleIndices.Add(bStart + 2);

        mesh.TriangleIndices.Add(bStart);
        mesh.TriangleIndices.Add(bStart + 2);
        mesh.TriangleIndices.Add(bStart + 3);

        // Backface triangles
        mesh.TriangleIndices.Add(bStart);
        mesh.TriangleIndices.Add(bStart + 2);
        mesh.TriangleIndices.Add(bStart + 1);

        mesh.TriangleIndices.Add(bStart);
        mesh.TriangleIndices.Add(bStart + 3);
        mesh.TriangleIndices.Add(bStart + 2);

        return mesh;
    }

// Helper to procedurally generate a flat 3D disk/cylinder mesh
    private MeshGeometry3D CreateCylinderMesh(double radius, double height, int segments) {
        MeshGeometry3D mesh = new MeshGeometry3D();
        double halfH = height / 2.0;

        // Center vertex bottom, center vertex top
        mesh.Positions.Add(new Point3D(0, 0, -halfH)); // 0: Bottom center
        mesh.Positions.Add(new Point3D(0, 0, halfH)); // 1: Top center

        int bottomCenterIndex = 0;
        int topCenterIndex = 1;

        for (int i = 0; i < segments; i++) {
            double angle = (i / (double)segments) * 2.0 * Math.PI;
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);

            // Bottom ring vertex
            mesh.Positions.Add(new Point3D(cos * radius, sin * radius, -halfH));
            // Top ring vertex
            mesh.Positions.Add(new Point3D(cos * radius, sin * radius, halfH));
        }

        // Build triangles for bottom, top, and outer walls
        for (int i = 0; i < segments; i++) {
            int next = (i + 1) % segments;
            int b1 = 2 + (i * 2);
            int t1 = b1 + 1;
            int b2 = 2 + (next * 2);
            int t2 = b2 + 1;

            // Bottom face triangle
            mesh.TriangleIndices.Add(bottomCenterIndex);
            mesh.TriangleIndices.Add(b2);
            mesh.TriangleIndices.Add(b1);

            // Top face triangle
            mesh.TriangleIndices.Add(topCenterIndex);
            mesh.TriangleIndices.Add(t1);
            mesh.TriangleIndices.Add(t2);

            // Side quad triangle 1
            mesh.TriangleIndices.Add(b1);
            mesh.TriangleIndices.Add(b2);
            mesh.TriangleIndices.Add(t1);

            // Side quad triangle 2
            mesh.TriangleIndices.Add(t1);
            mesh.TriangleIndices.Add(b2);
            mesh.TriangleIndices.Add(t2);
        }

        return mesh;
    }

    #endregion

    // -------------------------------------------------------------------
    // CAMERA CONTROLS
    // -------------------------------------------------------------------
    #region Camera controls

    public void OrbitCamera(float deltaYaw, float deltaPitch) {
        _cameraYaw = (_cameraYaw + deltaYaw) % 360.0f;
        _cameraPitch = Math.Clamp(_cameraPitch - deltaPitch, _allowFlip ? -89f : 5.0f, _allowFlip ? 89.0f : 85.0f);
        UpdateCameraPosition();
    }

    public void PanCamera(float deltaX, float deltaY) {
        double yawRad = _cameraYaw * (Math.PI / 180.0);
        double cosY = Math.Cos(yawRad);
        double sinY = Math.Sin(yawRad);

        // Add a pan sensitivity multiplier (e.g., 3.0) to move faster per mouse drag
        float panMultiplier = 20.0f;

        double moveX = (deltaX * cosY - deltaY * sinY) * panMultiplier;
        double moveY = (deltaX * sinY + deltaY * cosY) * panMultiplier;

        _cameraTarget.X += moveX;
        _cameraTarget.Y += moveY;

        UpdateCameraPosition();
    }

    public void RecenterOnPlayer(Point3D playerPos) {
        EnsureInitialized();
        _cameraTarget = new Point3D(playerPos.X, playerPos.Y, 0.0);
        UpdateCameraPosition();
    }

    /// <summary>
    /// Instantly re-centers the focal target of the 3D camera back onto the player's coordinates.
    /// </summary>
    public void RecenterCameraOnPlayer(float x, float y, float z) {
        EnsureInitialized();

        var adjustedZ = (double)z;
        var layersExist = _mapLayers.Count > 0;
        if (layersExist) {
            // Find the layer closest by Z elevation that actually contains or matches this location, 
            // or fallback safely if none match.
            var closestLayer = _mapLayers.Values
                .OrderBy(l => Math.Abs(l.ZElevation - (float)z))
                .FirstOrDefault(l => {
                    // Assuming you have access to the layer's bounds or settings here.
                    // Replace this with your actual world boundary check for the layer:
                    return IsCoordinateWithinLayerBounds(l, x, y);
                });

            double surfaceOffset = 0.35;

            if (closestLayer.Model != null) {
                adjustedZ = closestLayer.ZElevation + surfaceOffset;
            }
            else {
                // Fallback to native Z if no map layers exist or match
            }
        }

        _cameraTarget = new Point3D(x, y, adjustedZ);
        UpdateCameraPosition();
    }

    public void SetTopDownOverview(Point3D mapCenter, double mapWidth, double mapHeight) {
        EnsureInitialized();
        _cameraTarget = mapCenter;
        _cameraYaw = 0.0f;
        _cameraPitch = 89.0f;
        double maxDim = Math.Max(mapWidth, mapHeight);
        _cameraRadius = (float)(maxDim / Math.Tan(45.0 * Math.PI / 360.0));
        UpdateCameraPosition();
    }

    private void UpdateCameraPosition() {
        double yawRad = _cameraYaw * (Math.PI / 180.0);
        double pitchRad = _cameraPitch * (Math.PI / 180.0);

        double x = _cameraTarget.X + _cameraRadius * Math.Cos(pitchRad) * Math.Sin(yawRad);
        double y = _cameraTarget.Y - _cameraRadius * Math.Cos(pitchRad) * Math.Cos(yawRad);
        double z = _cameraTarget.Z + _cameraRadius * Math.Sin(pitchRad);

        _camera.Position = new Point3D(x, y, z);
        _camera.LookDirection = new Vector3D(_cameraTarget.X - x, _cameraTarget.Y - y, _cameraTarget.Z - z);
    }

    #endregion
    
    // -------------------------------------------------------------------
    // MAP LAYER MANAGEMENT
    // -------------------------------------------------------------------
    #region Map layer management

    /// <summary>
    /// Re-orders _worldGroup.Children so opaque 3D models (avatars, spheres, tether, breadcrumbs) 
    /// render FIRST, and transparent map quad layers render LAST.
    /// </summary>
    /// <summary>
    /// Re-orders _worldGroup.Children so opaque 3D models (avatars, spheres, tether, pulse rings, breadcrumbs) 
    /// render FIRST, and transparent map quad layers render LAST.
    /// </summary>
    private void EnforceTransparentRenderOrder() {
        if (_worldGroup == null) return;

        var sortedMapLayers = _mapLayers.Values
            .Where(layer => _worldGroup.Children.Contains(layer.Model))
            .OrderBy(layer => layer.ZElevation)
            .Select(layer => layer.Model)
            .ToList();

        List<GeometryModel3D> overlayModels = new();
        if (_ghostTerrainModel != null && _worldGroup.Children.Contains(_ghostTerrainModel))
            overlayModels.Add(_ghostTerrainModel);
        if (_breadcrumbLineModel != null && _worldGroup.Children.Contains(_breadcrumbLineModel))
            overlayModels.Add(_breadcrumbLineModel);
        if (_pulseRingModel != null && _worldGroup.Children.Contains(_pulseRingModel))
            overlayModels.Add(_pulseRingModel);
        if (_outerPulseRingModel != null && _worldGroup.Children.Contains(_outerPulseRingModel))
            overlayModels.Add(_outerPulseRingModel);
        if (_destinationPulseRingModel != null && _worldGroup.Children.Contains(_destinationPulseRingModel))
            overlayModels.Add(_destinationPulseRingModel);
        if (_destinationOuterPulseRingModel != null && _worldGroup.Children.Contains(_destinationOuterPulseRingModel))
            overlayModels.Add(_destinationOuterPulseRingModel);
        
        // Remove them
        foreach (var model in sortedMapLayers) _worldGroup.Children.Remove(model);
        foreach (var model in overlayModels) _worldGroup.Children.Remove(model);
        _worldGroup.Children.Remove(_locationMarkersGroup); // Temporarily remove markers group

        // Re-append in correct drawing order:
        foreach (var model in overlayModels) {
            _worldGroup.Children.Add(model);
        }

        foreach (var model in sortedMapLayers) {
            _worldGroup.Children.Add(model);
        }

        // FORCE location markers to render LAST so they are never occluded by transparent floor maps
        _worldGroup.Children.Add(_locationMarkersGroup);
    }
    
    // private void EnforceTransparentRenderOrder() {
    //     if (_worldGroup == null) return;
    //
    //     // 1. Gather active map quad models and sort by ZElevation ascending (Bottom-to-Top)
    //     var sortedMapLayers = _mapLayers.Values
    //         .Where(layer => _worldGroup.Children.Contains(layer.Model))
    //         .OrderBy(layer => layer.ZElevation)
    //         .Select(layer => layer.Model)
    //         .ToList();
    //
    //     // 2. Gather ALL overlay & indicator models (Breadcrumbs, Ghost Terrain, and Pulse Rings)
    //     List<GeometryModel3D> overlayModels = new();
    //     if (_ghostTerrainModel != null && _worldGroup.Children.Contains(_ghostTerrainModel))
    //         overlayModels.Add(_ghostTerrainModel);
    //     if (_breadcrumbLineModel != null && _worldGroup.Children.Contains(_breadcrumbLineModel))
    //         overlayModels.Add(_breadcrumbLineModel);
    //     if (_pulseRingModel != null && _worldGroup.Children.Contains(_pulseRingModel))
    //         overlayModels.Add(_pulseRingModel);
    //     if (_outerPulseRingModel != null && _worldGroup.Children.Contains(_outerPulseRingModel))
    //         overlayModels.Add(_outerPulseRingModel);
    //
    //     // 3. Remove transparent map and overlay models from _worldGroup
    //     foreach (var model in sortedMapLayers) {
    //         _worldGroup.Children.Remove(model);
    //     }
    //
    //     foreach (var model in overlayModels) {
    //         _worldGroup.Children.Remove(model);
    //     }
    //
    //     // 4. Re-append in strict back-to-front rendering order
    //     // A. Overlays (Pulse Rings, Ghost Terrain, Breadcrumbs)
    //     foreach (var model in overlayModels) {
    //         _worldGroup.Children.Add(model);
    //     }
    //
    //     // B. Map Quads (Sorted Bottom-to-Top so lower maps render beneath higher maps like glass)
    //     foreach (var model in sortedMapLayers) {
    //         _worldGroup.Children.Add(model);
    //     }
    // }

    public void AddOrUpdateMapLayer(
        string layerId,
        string imagePath,
        CalibrationPoint p1, // Point1 (PixelX, PixelY, World X, World Y) in logical canvas space
        CalibrationPoint p2, // Point2 (PixelX, PixelY, World X, World Y) in logical canvas space
        double imgPixelWidth,
        double imgPixelHeight,
        float zElevation,
        double opacity = 0.30) {
        EnsureInitialized();

        // 1. Remove existing layer model cleanly
        if (_mapLayers.TryGetValue(layerId, out var existingLayer)) {
            _worldGroup.Children.Remove(existingLayer.Model);
            _mapLayers.Remove(layerId);
        }

        if (!System.IO.File.Exists(imagePath)) return;

        // Load actual bitmap dimensions
        BitmapImage bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();

        double actualWidth = bitmap.PixelWidth;
        double actualHeight = bitmap.PixelHeight;

        // --- Compute DPI Scale Ratio (Logical Canvas to True Bitmap Pixels) ---
        double dpiScaleX = actualWidth / bitmap.Width;
        double dpiScaleY = actualHeight / bitmap.Height;

        // Convert logical calibration pixel points into true bitmap pixel space
        double p1PixelX = p1.PixelX * dpiScaleX;
        double p1PixelY = p1.PixelY * dpiScaleY;
        double p2PixelX = p2.PixelX * dpiScaleX;
        double p2PixelY = p2.PixelY * dpiScaleY;

        // --- 2. Compute Scale (unitsPerPx) matching CalculatePixelPosition ---
        double dx = p2.X - p1.X;
        double dy = p2.Y - p1.Y;
        double dpx = p2PixelX - p1PixelX;
        double dpy = p1PixelY - p2PixelY; // Invert Screen Y vs Game Y

        double dReal = Math.Sqrt(dx * dx + dy * dy);
        double dPixel = Math.Sqrt(dpx * dpx + dpy * dpy);

        if (dReal < 0.0001 || dPixel < 0.0001) return;

        double scale = dPixel / dReal;
        double unitsPerPx = 1.0 / scale;

        // --- 3. Compute Rotation Angle Matching 2D Logic ---
        double angleReal = Math.Atan2(dy, dx);
        double anglePixel = Math.Atan2(dpy, dpx);
        double rotationRad = anglePixel - angleReal;

        double rotationDegrees = rotationRad * (180.0 / Math.PI);

        // --- 4. Local Mesh Bounds Anchored at Point1 (0,0,0) using True Bitmap Dimensions ---
        double scaleCorrection = 1;
        double adjustedUnitsPerPx = unitsPerPx * scaleCorrection;

        double xMin = -p1PixelX * adjustedUnitsPerPx;
        double xMax = (actualWidth - p1PixelX) * adjustedUnitsPerPx;
        double yMax = p1PixelY * adjustedUnitsPerPx;
        double yMin = -(actualHeight - p1PixelY) * adjustedUnitsPerPx;

        MeshGeometry3D quadMesh = new MeshGeometry3D();

        // SINGLE SET of 4 quad vertices
        quadMesh.Positions.Add(new Point3D(xMin, yMin, 0)); // Index 0: Bottom-Left
        quadMesh.Positions.Add(new Point3D(xMax, yMin, 0)); // Index 1: Bottom-Right
        quadMesh.Positions.Add(new Point3D(xMax, yMax, 0)); // Index 2: Top-Right
        quadMesh.Positions.Add(new Point3D(xMin, yMax, 0)); // Index 3: Top-Left

        // UV Coordinates (0,0 is Top-Left, 1,1 is Bottom-Right)
        quadMesh.TextureCoordinates.Add(new System.Windows.Point(0, 1)); // Bottom-Left
        quadMesh.TextureCoordinates.Add(new System.Windows.Point(1, 1)); // Bottom-Right
        quadMesh.TextureCoordinates.Add(new System.Windows.Point(1, 0)); // Top-Right
        quadMesh.TextureCoordinates.Add(new System.Windows.Point(0, 0)); // Top-Left

        // Triangle Indices
        quadMesh.TriangleIndices.Add(0);
        quadMesh.TriangleIndices.Add(1);
        quadMesh.TriangleIndices.Add(2);
        quadMesh.TriangleIndices.Add(0);
        quadMesh.TriangleIndices.Add(2);
        quadMesh.TriangleIndices.Add(3);
        
        // Triangle Indices (Back Face / Reverse Winding so it renders looking up from underneath)
        quadMesh.TriangleIndices.Add(0);
        quadMesh.TriangleIndices.Add(2);
        quadMesh.TriangleIndices.Add(1);
        quadMesh.TriangleIndices.Add(0);
        quadMesh.TriangleIndices.Add(3);
        quadMesh.TriangleIndices.Add(2);

        ImageBrush brush = new ImageBrush(bitmap) { Opacity = opacity };

        MaterialGroup materialGroup = new MaterialGroup();
        materialGroup.Children.Add(new DiffuseMaterial(brush));
        // Ensure emissive material is applied to both sides so the bottom isn't pitch black
        materialGroup.Children.Add(new EmissiveMaterial(brush));

        GeometryModel3D mapModel = new GeometryModel3D {
            Geometry = quadMesh,
            Material = materialGroup,
            BackMaterial = materialGroup // This keeps it colored, but let's check normal/winding order
        };

        // --- 5. Transform Pipeline ---
        Transform3DGroup transformGroup = new Transform3DGroup();

        // Step A: Inverse Rotation around local origin (0,0,0)
        if (Math.Abs(rotationDegrees) > 0.001) {
            transformGroup.Children.Add(new RotateTransform3D(
                new AxisAngleRotation3D(new Vector3D(0, 0, 1), -rotationDegrees), 0, 0, 0));
        }

        // Step B: Translate local (0,0,0) to World (p1.X, p1.Y, zElevation)
        double worldZ = CoordinateSystem == CoordinateSystem.LeftHanded ? -zElevation : zElevation;
        transformGroup.Children.Add(new TranslateTransform3D(p1.X, p1.Y, worldZ));

        mapModel.Transform = transformGroup;

        _worldGroup.Children.Add(mapModel);
        _mapLayers[layerId] = (mapModel, brush, zElevation);

        EnforceTransparentRenderOrder();
    }

    /// <summary>
    /// Automatically inspects all loaded map layers and calculates the optimal 
    /// center point and zoom radius for a top-down overview.
    /// </summary>
    public void AutoFitToLoadedWorld() {
        EnsureInitialized();

        if (_mapLayers.Count == 0) return;

        // Calculate true bounding box across all active map layer meshes
        double minX = double.MaxValue;
        double maxX = double.MinValue;
        double minY = double.MaxValue;
        double maxY = double.MinValue;
        double maxZ = double.MinValue;
        double minZ = double.MaxValue;

        foreach (var kvp in _mapLayers) {
            var layer = kvp.Value;
            if (layer.Model.Geometry is MeshGeometry3D mesh && mesh.Positions.Count > 0) {
                // Transform local mesh positions into world space based on the layer's transform
                var transform = layer.Model.Transform;

                foreach (var pos in mesh.Positions) {
                    Point3D worldPos = transform != null ? transform.Transform(pos) : pos;

                    if (worldPos.X < minX) minX = worldPos.X;
                    if (worldPos.X > maxX) maxX = worldPos.X;
                    if (worldPos.Y < minY) minY = worldPos.Y;
                    if (worldPos.Y > maxY) maxY = worldPos.Y;
                    if (worldPos.Z < minZ) minZ = worldPos.Z;
                    if (worldPos.Z > maxZ) maxZ = worldPos.Z;
                }
            }
        }

        if (minX <= maxX && minY <= maxY) {
            double centerX = (minX + maxX) / 2.0;
            double centerY = (minY + maxY) / 2.0;
            double centerZ = (minZ + maxZ) / 2.0;

            Point3D worldCenter = new Point3D(centerX, centerY, centerZ);
            double worldWidth = maxX - minX;
            double worldHeight = maxY - minY;
            double maxDimension = Math.Max(worldWidth, worldHeight);

            // Ensure a safe fallback if layers are single points or flat unscaled bounds
            if (maxDimension < 1.0) maxDimension = 100.0;

            // 1. Configure viewport bounds scaling (handles clipping & micro-grid zoom rules)
            ConfigureForMapBounds(worldWidth, worldHeight);

            // 2. Pass to top-down framing overview
            SetTopDownOverview(worldCenter, worldWidth, worldHeight);
        }
    }

    // public void AddOrUpdateMapLayer(string layerId, string imagePath, Point3D center, double width, double height,
    //     float zElevation, double opacity = 0.70) {
    //     EnsureInitialized();
    //
    //     if (_mapLayers.TryGetValue(layerId, out var existingLayer)) {
    //         _worldGroup.Children.Remove(existingLayer.Model);
    //         _mapLayers.Remove(layerId);
    //     }
    //
    //     if (!System.IO.File.Exists(imagePath)) return;
    //
    //     MeshGeometry3D quadMesh = new MeshGeometry3D();
    //     double halfW = width / 2.0;
    //     double halfH = height / 2.0;
    //
    //     quadMesh.Positions.Add(new Point3D(center.X - halfW, center.Y - halfH, zElevation));
    //     quadMesh.Positions.Add(new Point3D(center.X + halfW, center.Y - halfH, zElevation));
    //     quadMesh.Positions.Add(new Point3D(center.X + halfW, center.Y + halfH, zElevation));
    //     quadMesh.Positions.Add(new Point3D(center.X - halfW, center.Y + halfH, zElevation));
    //
    //     quadMesh.TextureCoordinates.Add(new System.Windows.Point(0, 1));
    //     quadMesh.TextureCoordinates.Add(new System.Windows.Point(1, 1));
    //     quadMesh.TextureCoordinates.Add(new System.Windows.Point(1, 0));
    //     quadMesh.TextureCoordinates.Add(new System.Windows.Point(0, 0));
    //
    //     quadMesh.TriangleIndices.Add(0); quadMesh.TriangleIndices.Add(1); quadMesh.TriangleIndices.Add(2);
    //     quadMesh.TriangleIndices.Add(0); quadMesh.TriangleIndices.Add(2); quadMesh.TriangleIndices.Add(3);
    //
    //     BitmapImage bitmap = new BitmapImage();
    //     bitmap.BeginInit();
    //     bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
    //     bitmap.CacheOption = BitmapCacheOption.OnLoad;
    //     bitmap.EndInit();
    //
    //     ImageBrush brush = new ImageBrush(bitmap) { Opacity = opacity };
    //
    //     MaterialGroup materialGroup = new MaterialGroup();
    //     materialGroup.Children.Add(new DiffuseMaterial(brush));
    //     materialGroup.Children.Add(new EmissiveMaterial(brush));
    //
    //     GeometryModel3D mapModel = new GeometryModel3D {
    //         Geometry = quadMesh,
    //         Material = materialGroup,
    //         BackMaterial = materialGroup
    //     };
    //
    //     _worldGroup.Children.Add(mapModel);
    //     _mapLayers[layerId] = (mapModel, brush, zElevation);
    //
    //     _cameraTarget = center;
    //     UpdateCameraPosition();
    // }


    public void UpdateLayerOpacities(float playerZ, double targetOpacity = 0.50) {
        foreach (var layer in _mapLayers.Values) {
            float deltaZ = Math.Abs(playerZ - layer.ZElevation);

            if (deltaZ < 15.0f) {
                // Primary active floor (~50% to 60% opacity)
                layer.Brush.Opacity = 0.55;
            }
            else if (deltaZ < 45.0f) {
                // Adjacent floors (~35% opacity - clearly visible)
                layer.Brush.Opacity = 0.35;
            }
            else {
                // Distant floors (~25% opacity - faint but clearly readable)
                layer.Brush.Opacity = 0.25;
            }
        }
    }

    // public void UpdateLayerOpacities(float playerZ, double targetOpacity = 0.30) {
    //     foreach (var layer in _mapLayers.Values) {
    //         float deltaZ = Math.Abs(playerZ - layer.ZElevation);
    //         if (deltaZ < 15.0f)
    //             layer.Brush.Opacity = targetOpacity; // Uses targetOpacity (0.30) instead of 0.70
    //         else if (deltaZ < 45.0f)
    //             layer.Brush.Opacity = targetOpacity * 0.5;
    //         else
    //             layer.Brush.Opacity = targetOpacity * 0.2;
    //     }
    // }

    // -------------------------------------------------------------------
    // PLAYER AVATAR & TELEMETRY UPDATES
    // -------------------------------------------------------------------

    #endregion
    
    // -------------------------------------------------------------------
    // PLAYER AVATAR & TELEMETRY UPDATES
    // -------------------------------------------------------------------
    #region Player avatar & telemetry updates
    public void UpdatePlayerPosition(float x, float y, float z, float heading, bool syncHeadingCamera,
        float mapPlaneZ = 0.0f) {
        EnsureInitialized();

        Point3D actualPos = new Point3D(x, y, z);

        // Determine active floor Z (fallback to mapPlaneZ or z)
        float activeFloorZ = mapPlaneZ != 0.0f ? mapPlaneZ : (float)(_mapLayers.Values.FirstOrDefault().ZElevation);

        // Cache ground position so per-frame OnRenderingTick pulse rings follow the player
        _lastKnownGroundPos = actualPos; //new Point3D(x, y, activeFloorZ);
        _hasPlayerPosition = true;

        // Fixed world-space dimensions
        const double avatarRadius = 1.2;

        // Elevation clearance above map quad to allow ambient illumination and prevent Z-fighting
        double avatarElevatedZ = z + 0.35;

        // -------------------------------------------------------------
        // 1. TRUE-Z DIRECTIONAL AVATAR TRIANGLE (+0.35 Z Clearance)
        // -------------------------------------------------------------
        if (_playerGroundMarkerModel == null) {
            _playerGroundMarkerModel =
                CreatePlayerTriangleAvatarMesh(width: avatarRadius, length: avatarRadius * 2, height: 3.0);
            //playerGroundMarkerModel = CreatePlayerTriangleAvatarMesh(baseRadius: avatarRadius, height: 3.0);
            _worldGroup.Children.Add(_playerGroundMarkerModel);
        }

        var layersExist = _mapLayers.Count > 0;
        if (layersExist) {
            // Find the layer closest by Z elevation that actually contains or matches this location, 
            // or fallback safely if none match.
            var closestLayer = _mapLayers.Values
                .OrderBy(l => Math.Abs(l.ZElevation - (float)z))
                .FirstOrDefault(l => {
                    // Assuming you have access to the layer's bounds or settings here.
                    // Replace this with your actual world boundary check for the layer:
                    return IsCoordinateWithinLayerBounds(l, x, y);
                });

            double surfaceOffset = 0.35;

            if (closestLayer.Model != null) {
                _lastKnownGroundPos.Z = closestLayer.ZElevation;
                actualPos.Z = closestLayer.ZElevation;
                avatarElevatedZ = closestLayer.ZElevation + surfaceOffset;
            }
            else {
                // Fallback to native Z if no map layers exist or match
            }
        }

        Transform3DGroup avatarTransform = new Transform3DGroup();
        avatarTransform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), -heading)));
        avatarTransform.Children.Add(new TranslateTransform3D(x, y,
            avatarElevatedZ)); // Elevated Z for lighting & visibility
        _playerGroundMarkerModel.Transform = avatarTransform;


        // -------------------------------------------------------------
        // 2. PROJECTED FLOOR CIRCLES (Sits slightly above each map quad)
        // -------------------------------------------------------------
        foreach (var layerKvp in _mapLayers) {
            string layerId = layerKvp.Key;
            float layerZ = layerKvp.Value.ZElevation;

            if (!_projectedFloorMarkers.TryGetValue(layerId, out var floorMarker)) {
                float deltaZ = Math.Abs(z - layerZ);
                System.Windows.Media.Color markerColor = deltaZ < 15.0f
                    ? Colors.Purple
                    : System.Windows.Media.Color.FromArgb(120, 0, 255, 255);

                floorMarker = CreateProjectedFloorCircleMesh(markerColor, radius: avatarRadius);
                _projectedFloorMarkers[layerId] = floorMarker;
                _worldGroup.Children.Add(floorMarker);
            }

            // Translate to map floor Z + 0.1 clearance offset
            Transform3DGroup floorTransform = new Transform3DGroup();
            floorTransform.Children.Add(new TranslateTransform3D(x, y, layerZ + 0.1));
            floorMarker.Transform = floorTransform;
        }


        // -------------------------------------------------------------
        // 3. VERTICAL TETHER LINE THROUGH FLOORS
        // -------------------------------------------------------------
        if (_mapLayers.Count > 0) {
            float minLayerZ = _mapLayers.Values.Min(l => l.ZElevation);
            float maxLayerZ = _mapLayers.Values.Max(l => l.ZElevation);

            Point3D bottomAnchor = new Point3D(x, y, Math.Min(z, minLayerZ));
            Point3D topAnchor = new Point3D(x, y, Math.Max(z, maxLayerZ));

            if (_playerTetherLineModel != null) {
                _worldGroup.Children.Remove(_playerTetherLineModel);
            }

            _playerTetherLineModel = CreateVerticalTetherMesh(bottomAnchor, topAnchor, Colors.White, thickness: 0.25);
            _worldGroup.Children.Add(_playerTetherLineModel);
        }

        // -------------------------------------------------------------
        // 4. OVERLAYS, RENDER ORDER & CAMERA TRACKING
        // -------------------------------------------------------------
        // AddGhostTerrainPoint(actualPos);
        AddBreadcrumbNode(actualPos);

        EnforceTransparentRenderOrder();

        if (syncHeadingCamera) {
            _cameraTarget = actualPos;
            UpdateCameraPosition();
        }
    }
    
    public void RemovePlayerMarker() {
        _hasPlayerPosition = false;
    }
        /// <summary>
    /// Creates a sharp, directional 3D triangle pyramid representing the player avatar.
    /// Base sits on the Z plane pointing along the +Y heading direction.
    /// </summary>
    // private GeometryModel3D CreatePlayerTriangleAvatarMesh(double baseRadius = 1.2, double height = 3.0) {
    //     MeshGeometry3D pyramidMesh = new MeshGeometry3D();
    //
    //     // Base Triangle Vertices (Sitting on ground plane Z = 0)
    //     Point3D tip = new Point3D(0, baseRadius * 1.5, 0); // Forward Nose (+Y)
    //     Point3D left = new Point3D(-baseRadius, -baseRadius * 0.8, 0); // Rear Left
    //     Point3D right = new Point3D(baseRadius, -baseRadius * 0.8, 0); // Rear Right
    //     Point3D apex = new Point3D(0, 0, height); // Top Peak (+Z)
    //
    //     pyramidMesh.Positions.Add(tip); // Index 0
    //     pyramidMesh.Positions.Add(left); // Index 1
    //     pyramidMesh.Positions.Add(right); // Index 2
    //     pyramidMesh.Positions.Add(apex); // Index 3
    //
    //     // Pyramid Side Faces (Triangles)
    //     pyramidMesh.TriangleIndices.Add(0);
    //     pyramidMesh.TriangleIndices.Add(3);
    //     pyramidMesh.TriangleIndices.Add(1); // Left side
    //     pyramidMesh.TriangleIndices.Add(0);
    //     pyramidMesh.TriangleIndices.Add(2);
    //     pyramidMesh.TriangleIndices.Add(3); // Right side
    //     pyramidMesh.TriangleIndices.Add(1);
    //     pyramidMesh.TriangleIndices.Add(3);
    //     pyramidMesh.TriangleIndices.Add(2); // Back side
    //
    //     // Bottom Cap
    //     pyramidMesh.TriangleIndices.Add(0);
    //     pyramidMesh.TriangleIndices.Add(1);
    //     pyramidMesh.TriangleIndices.Add(2);
    //
    //     SolidColorBrush yellowBrush = new SolidColorBrush(Colors.Yellow);
    //     MaterialGroup matGroup = new MaterialGroup();
    //     matGroup.Children.Add(new DiffuseMaterial(yellowBrush));
    //     matGroup.Children.Add(new EmissiveMaterial(yellowBrush));
    //
    //     return new GeometryModel3D { Geometry = pyramidMesh, Material = matGroup, BackMaterial = matGroup };
    // }

    /// <summary>
    /// Creates an unambiguous 3D navigation arrow (Stealth Dart) pointing along +Y.
    /// Features an indented rear notch and a sloped center spine.
    /// </summary>
    private GeometryModel3D
        CreatePlayerTriangleAvatarMesh(double width = 2.0, double length = 3.5, double height = 1.2) {
        MeshGeometry3D arrowMesh = new MeshGeometry3D();

        // -------------------------------------------------------------
        // Vertices defining an Arrowhead/Dart with an indented tail
        // -------------------------------------------------------------
        Point3D nose = new Point3D(0, length * 0.7, 0.2); // Index 0: Front Nose (+Y)
        Point3D leftWing = new Point3D(-width, -length * 0.5, 0); // Index 1: Rear Left Wing
        Point3D rightWing = new Point3D(width, -length * 0.5, 0); // Index 2: Rear Right Wing
        Point3D tailNotch = new Point3D(0, -length * 0.1, 0.2); // Index 3: Indented Rear Tail
        Point3D spinePeak = new Point3D(0, -length * 0.1, height); // Index 4: Raised Center Ridge

        arrowMesh.Positions.Add(nose); // 0
        arrowMesh.Positions.Add(leftWing); // 1
        arrowMesh.Positions.Add(rightWing); // 2
        arrowMesh.Positions.Add(tailNotch); // 3
        arrowMesh.Positions.Add(spinePeak); // 4

        // -------------------------------------------------------------
        // Upper Hull (Sloped 3D Ridge Faces)
        // -------------------------------------------------------------
        // Top-Left Nose Slope
        arrowMesh.TriangleIndices.Add(0);
        arrowMesh.TriangleIndices.Add(4);
        arrowMesh.TriangleIndices.Add(1);

        // Top-Right Nose Slope
        arrowMesh.TriangleIndices.Add(0);
        arrowMesh.TriangleIndices.Add(2);
        arrowMesh.TriangleIndices.Add(4);

        // Rear-Left Wing Slope
        arrowMesh.TriangleIndices.Add(1);
        arrowMesh.TriangleIndices.Add(4);
        arrowMesh.TriangleIndices.Add(3);

        // Rear-Right Wing Slope
        arrowMesh.TriangleIndices.Add(3);
        arrowMesh.TriangleIndices.Add(4);
        arrowMesh.TriangleIndices.Add(2);

        // -------------------------------------------------------------
        // Bottom Cap (Underbelly)
        // -------------------------------------------------------------
        arrowMesh.TriangleIndices.Add(0);
        arrowMesh.TriangleIndices.Add(1);
        arrowMesh.TriangleIndices.Add(3);

        arrowMesh.TriangleIndices.Add(0);
        arrowMesh.TriangleIndices.Add(3);
        arrowMesh.TriangleIndices.Add(2);

        // High-visibility emissive yellow material
        SolidColorBrush yellowBrush = new SolidColorBrush(Colors.Yellow);
        MaterialGroup matGroup = new MaterialGroup();
        matGroup.Children.Add(new DiffuseMaterial(yellowBrush));
        matGroup.Children.Add(new EmissiveMaterial(yellowBrush));

        return new GeometryModel3D {
            Geometry = arrowMesh,
            Material = matGroup,
            BackMaterial = matGroup
        };
    }
    
    /// <summary>
    /// Renders a completely fixed yellow anchor ring (no pulsing, no camera distance scaling).
    /// </summary>
    private void UpdatePulseRing(Point3D position, double baseScale) {
        if (_pulseRingModel == null) {
            _pulseRingModel = CreatePulseRingMesh(Colors.Red);
            _worldGroup.Children.Add(_pulseRingModel);
        }

        // Fixed 1.0 multiplier (ignores camera baseScale and pulse animation)
        double fixedRadius = 1.0;

        Transform3DGroup ringTransform = new Transform3DGroup();
        ringTransform.Children.Add(new ScaleTransform3D(fixedRadius, fixedRadius, 1.0));
        ringTransform.Children.Add(new TranslateTransform3D(position.X, position.Y, position.Z + 0.3));
        _pulseRingModel.Transform = ringTransform;
    }

    private void UpdateOuterRedPulseRing(Point3D position, double baseScale) {
        if (_outerPulseRingModel == null) {
            _outerPulseRingModel = CreateOuterPulseRingMesh();
            _worldGroup.Children.Add(_outerPulseRingModel);
        }

        // SLOWER PULSE SPEED: Reduce step from 0.01 to 0.003
        double pulseStep = 0.002;
        // Smooth pulse speed (0.01 step)
        double maxScale = 1.4; // 1.4x maximum diameter
        _outerPulseScale += pulseStep;
        if (_outerPulseScale > maxScale) _outerPulseScale = 1.0;

        double progress = (_outerPulseScale - 1.0) / (maxScale - 1.0); // Range = 0.4

        // Yellow (255, 255, 0) -> Red (255, 0, 0)
        byte red = 255;
        byte green = (byte)(255 * (1.0 - progress));
        byte alpha = (byte)(60 * (1.0 - progress));

        System.Windows.Media.Color dynamicColor = System.Windows.Media.Color.FromArgb(alpha, red, green, 0);

        SolidColorBrush brush = new SolidColorBrush(dynamicColor);
        MaterialGroup matGroup = new MaterialGroup();
        matGroup.Children.Add(new DiffuseMaterial(brush));
        matGroup.Children.Add(new EmissiveMaterial(brush));

        _outerPulseRingModel.Material = matGroup;
        _outerPulseRingModel.BackMaterial = matGroup;

        double currentRadius = baseScale * _outerPulseScale;

        Transform3DGroup ringTransform = new Transform3DGroup();
        ringTransform.Children.Add(new ScaleTransform3D(currentRadius, currentRadius, 1.0));
        ringTransform.Children.Add(new TranslateTransform3D(position.X, position.Y, position.Z + 0.4));
        _outerPulseRingModel.Transform = ringTransform;
    }

    private MeshGeometry3D CreatePulseRingGeometry() {
        MeshGeometry3D ringMesh = new MeshGeometry3D();
        int segments = 24;
        double innerR = 5.0, outerR = 24;

        for (int i = 0; i < segments; i++) {
            double angle1 = i * 2 * Math.PI / segments;
            double angle2 = (i + 1) * 2 * Math.PI / segments;

            ringMesh.Positions.Add(new Point3D(innerR * Math.Cos(angle1), innerR * Math.Sin(angle1), 0));
            ringMesh.Positions.Add(new Point3D(outerR * Math.Cos(angle1), outerR * Math.Sin(angle1), 0));
            ringMesh.Positions.Add(new Point3D(outerR * Math.Cos(angle2), outerR * Math.Sin(angle2), 0));
            ringMesh.Positions.Add(new Point3D(innerR * Math.Cos(angle2), innerR * Math.Sin(angle2), 0));

            int idx = i * 4;
            // CCW Winding for Top-Down Perspective Camera (Z-up)
            ringMesh.TriangleIndices.Add(idx);
            ringMesh.TriangleIndices.Add(idx + 2);
            ringMesh.TriangleIndices.Add(idx + 1);
            ringMesh.TriangleIndices.Add(idx);
            ringMesh.TriangleIndices.Add(idx + 3);
            ringMesh.TriangleIndices.Add(idx + 2);
        }

        return ringMesh;
    }
    
    private GeometryModel3D CreatePulseRingMesh(System.Windows.Media.Color color) {
        MeshGeometry3D ringMesh = new MeshGeometry3D();
        int segments = 24;
        double innerR = 2.0, outerR = 2.3;

        for (int i = 0; i < segments; i++) {
            double angle1 = i * 2 * Math.PI / segments;
            double angle2 = (i + 1) * 2 * Math.PI / segments;

            ringMesh.Positions.Add(new Point3D(innerR * Math.Cos(angle1), innerR * Math.Sin(angle1), 0));
            ringMesh.Positions.Add(new Point3D(outerR * Math.Cos(angle1), outerR * Math.Sin(angle1), 0));
            ringMesh.Positions.Add(new Point3D(outerR * Math.Cos(angle2), outerR * Math.Sin(angle2), 0));
            ringMesh.Positions.Add(new Point3D(innerR * Math.Cos(angle2), innerR * Math.Sin(angle2), 0));

            int idx = i * 4;
            // CCW Winding for Top-Down Perspective Camera (Z-up)
            ringMesh.TriangleIndices.Add(idx);
            ringMesh.TriangleIndices.Add(idx + 2);
            ringMesh.TriangleIndices.Add(idx + 1);
            ringMesh.TriangleIndices.Add(idx);
            ringMesh.TriangleIndices.Add(idx + 3);
            ringMesh.TriangleIndices.Add(idx + 2);
        }

        MaterialGroup matGroup = new MaterialGroup();
        matGroup.Children.Add(new DiffuseMaterial(new SolidColorBrush(color) { Opacity = 0.6 }));
        matGroup.Children.Add(new EmissiveMaterial(new SolidColorBrush(color) { Opacity = 0.6 }));

        return new GeometryModel3D { Geometry = ringMesh, Material = matGroup, BackMaterial = matGroup };
    }

    private GeometryModel3D CreateOuterPulseRingMesh() {
        MeshGeometry3D ringMesh = new MeshGeometry3D();
        int segments = 32;
        double innerR = 2.0, outerR = 2.8;

        for (int i = 0; i < segments; i++) {
            double angle1 = i * 2 * Math.PI / segments;
            double angle2 = (i + 1) * 2 * Math.PI / segments;

            ringMesh.Positions.Add(new Point3D(innerR * Math.Cos(angle1), innerR * Math.Sin(angle1), 0));
            ringMesh.Positions.Add(new Point3D(outerR * Math.Cos(angle1), outerR * Math.Sin(angle1), 0));
            ringMesh.Positions.Add(new Point3D(outerR * Math.Cos(angle2), outerR * Math.Sin(angle2), 0));
            ringMesh.Positions.Add(new Point3D(innerR * Math.Cos(angle2), innerR * Math.Sin(angle2), 0));

            int idx = i * 4;
            // CCW Winding for Top-Down Perspective Camera (Z-up)
            ringMesh.TriangleIndices.Add(idx);
            ringMesh.TriangleIndices.Add(idx + 2);
            ringMesh.TriangleIndices.Add(idx + 1);
            ringMesh.TriangleIndices.Add(idx);
            ringMesh.TriangleIndices.Add(idx + 3);
            ringMesh.TriangleIndices.Add(idx + 2);
        }

        SolidColorBrush brush = new SolidColorBrush(Colors.Red);
        MaterialGroup matGroup = new MaterialGroup();
        matGroup.Children.Add(new DiffuseMaterial(brush));
        matGroup.Children.Add(new EmissiveMaterial(brush));

        return new GeometryModel3D { Geometry = ringMesh, Material = matGroup, BackMaterial = matGroup };
    }
    
    private GeometryModel3D CreateDestinationOuterPulseRingMesh() {
        MeshGeometry3D ringMesh = new MeshGeometry3D();
        int segments = 32;
        double innerR = 2.0, outerR = 2.8;

        for (int i = 0; i < segments; i++) {
            double angle1 = i * 2 * Math.PI / segments;
            double angle2 = (i + 1) * 2 * Math.PI / segments;

            ringMesh.Positions.Add(new Point3D(innerR * Math.Cos(angle1), innerR * Math.Sin(angle1), 0));
            ringMesh.Positions.Add(new Point3D(outerR * Math.Cos(angle1), outerR * Math.Sin(angle1), 0));
            ringMesh.Positions.Add(new Point3D(outerR * Math.Cos(angle2), outerR * Math.Sin(angle2), 0));
            ringMesh.Positions.Add(new Point3D(innerR * Math.Cos(angle2), innerR * Math.Sin(angle2), 0));

            int idx = i * 4;
            // CCW Winding for Top-Down Perspective Camera (Z-up)
            ringMesh.TriangleIndices.Add(idx);
            ringMesh.TriangleIndices.Add(idx + 2);
            ringMesh.TriangleIndices.Add(idx + 1);
            ringMesh.TriangleIndices.Add(idx);
            ringMesh.TriangleIndices.Add(idx + 3);
            ringMesh.TriangleIndices.Add(idx + 2);
        }

        SolidColorBrush brush = new SolidColorBrush(Colors.Yellow);
        MaterialGroup matGroup = new MaterialGroup();
        matGroup.Children.Add(new DiffuseMaterial(brush));
        matGroup.Children.Add(new EmissiveMaterial(brush));

        return new GeometryModel3D { Geometry = ringMesh, Material = matGroup, BackMaterial = matGroup };
    }

    private GeometryModel3D CreatePlayerGroundMarkerMesh() {
        MeshGeometry3D pyramidMesh = new MeshGeometry3D();
        double width = 1.5;
        double length = 2.25;
        double height = 1.8;

        // Base rests strictly at local Z = 0.0
        pyramidMesh.Positions.Add(new Point3D(0, length / 2, 0.0)); // 0: Apex
        pyramidMesh.Positions.Add(new Point3D(-width / 2, -length / 2, 0.0)); // 1: Bottom-Left
        pyramidMesh.Positions.Add(new Point3D(width / 2, -length / 2, 0.0)); // 2: Bottom-Right
        pyramidMesh.Positions.Add(new Point3D(0, 0, height)); // 3: Tip

        // CCW Triangle Winding
        pyramidMesh.TriangleIndices.Add(0);
        pyramidMesh.TriangleIndices.Add(2);
        pyramidMesh.TriangleIndices.Add(1);
        pyramidMesh.TriangleIndices.Add(0);
        pyramidMesh.TriangleIndices.Add(1);
        pyramidMesh.TriangleIndices.Add(3);
        pyramidMesh.TriangleIndices.Add(1);
        pyramidMesh.TriangleIndices.Add(2);
        pyramidMesh.TriangleIndices.Add(3);
        pyramidMesh.TriangleIndices.Add(2);
        pyramidMesh.TriangleIndices.Add(0);
        pyramidMesh.TriangleIndices.Add(3);

        MaterialGroup matGroup = new MaterialGroup();
        matGroup.Children.Add(new DiffuseMaterial(new SolidColorBrush(Colors.Yellow)));
        matGroup.Children.Add(new EmissiveMaterial(new SolidColorBrush(Colors.Yellow))); // Self-illuminat

        return new GeometryModel3D { Geometry = pyramidMesh, Material = matGroup, BackMaterial = matGroup };
    }

    private GeometryModel3D CreateTruePositionSphereMesh(double radius) {
        MeshGeometry3D sphereMesh = new MeshGeometry3D();
        int latSegments = 10, lonSegments = 14;

        for (int lat = 0; lat <= latSegments; lat++) {
            double theta = lat * Math.PI / latSegments;
            double sinTheta = Math.Sin(theta);
            double cosTheta = Math.Cos(theta);

            for (int lon = 0; lon <= lonSegments; lon++) {
                double phi = lon * 2 * Math.PI / lonSegments;
                double x = radius * sinTheta * Math.Cos(phi);
                double y = radius * sinTheta * Math.Sin(phi);
                double z = radius * cosTheta;

                sphereMesh.Positions.Add(new Point3D(x, y, z));
            }
        }

        for (int lat = 0; lat < latSegments; lat++) {
            for (int lon = 0; lon < lonSegments; lon++) {
                int current = (lat * (lonSegments + 1)) + lon;
                int next = current + lonSegments + 1;

                sphereMesh.TriangleIndices.Add(current);
                sphereMesh.TriangleIndices.Add(next);
                sphereMesh.TriangleIndices.Add(current + 1);

                sphereMesh.TriangleIndices.Add(next);
                sphereMesh.TriangleIndices.Add(next + 1);
                sphereMesh.TriangleIndices.Add(current + 1);
            }
        }

        // High-visibility cyan/white emissive material
        SolidColorBrush sphereBrush = new SolidColorBrush(Colors.Cyan);
        MaterialGroup matGroup = new MaterialGroup();
        matGroup.Children.Add(new DiffuseMaterial(sphereBrush));
        matGroup.Children.Add(new EmissiveMaterial(sphereBrush)); // Self-illuminated cyan glow

        return new GeometryModel3D { Geometry = sphereMesh, Material = matGroup, BackMaterial = matGroup };
    }

    private GeometryModel3D CreateVerticalTetherMesh(Point3D start, Point3D end, System.Windows.Media.Color color,
        double thickness) {
        MeshGeometry3D tetherMesh = new MeshGeometry3D();

        tetherMesh.Positions.Add(new Point3D(start.X - thickness, start.Y, start.Z));
        tetherMesh.Positions.Add(new Point3D(start.X + thickness, start.Y, start.Z));
        tetherMesh.Positions.Add(new Point3D(end.X + thickness, end.Y, end.Z));
        tetherMesh.Positions.Add(new Point3D(end.X - thickness, end.Y, end.Z));

        tetherMesh.TriangleIndices.Add(0);
        tetherMesh.TriangleIndices.Add(1);
        tetherMesh.TriangleIndices.Add(2);
        tetherMesh.TriangleIndices.Add(0);
        tetherMesh.TriangleIndices.Add(2);
        tetherMesh.TriangleIndices.Add(3);

        MaterialGroup matGroup = new MaterialGroup();
        matGroup.Children.Add(new DiffuseMaterial(new SolidColorBrush(color) { Opacity = 0.75 }));
        matGroup.Children.Add(new EmissiveMaterial(new SolidColorBrush(color) { Opacity = 0.75 }));

        return new GeometryModel3D { Geometry = tetherMesh, Material = matGroup, BackMaterial = matGroup };
    }
#endregion 

// -------------------------------------------------------------------
// DESTINATION MARKER 
// -------------------------------------------------------------------
#region Destination
    //Similar to player but it only updates when a target changes
    public void UpdateDestinationPosition(float x, float y, float z,
        float mapPlaneZ = 0.0f) {
        EnsureInitialized();

        Point3D actualPos = new Point3D(x, y, z);

        // Determine active floor Z (fallback to mapPlaneZ or z)
        float activeFloorZ = mapPlaneZ != 0.0f ? mapPlaneZ : (float)(_mapLayers.Values.FirstOrDefault().ZElevation);

        // // Cache ground position so per-frame OnRenderingTick pulse rings follow the player
        _destinationGroundPos = actualPos; //new Point3D(x, y, activeFloorZ);
         _hasDestinationPosition = true;

        // Fixed world-space dimensions
        //const double destinationRadius = 1.2;

        // Elevation clearance above map quad to allow ambient illumination and prevent Z-fighting
        double destinationElevatedZ = z + 0.35;

        // -------------------------------------------------------------
        // 1. TRUE-Z DIRECTIONAL AVATAR TRIANGLE (+0.35 Z Clearance)
        // -------------------------------------------------------------
        if (_destinationGroundMarkerModel == null) {
            // _destinationGroundMarkerModel =
            //     CreateDestinationMesh(width: destinationRadius, length: destinationRadius * 2, height: 3.0);
            _destinationGroundMarkerModel = CreateFlatMapMarker(_destinationGroundPos, System.Windows.Media.Colors.Yellow );

            //playerGroundMarkerModel = CreatePlayerTriangleAvatarMesh(baseRadius: avatarRadius, height: 3.0);
            _worldGroup.Children.Add(_destinationGroundMarkerModel);
        }

        var layersExist = _mapLayers.Count > 0;
        if (layersExist) {
            // Find the layer closest by Z elevation that actually contains or matches this location, 
            // or fallback safely if none match.
            var closestLayer = _mapLayers.Values
                .OrderBy(l => Math.Abs(l.ZElevation - (float)z))
                .FirstOrDefault(l => {
                    // Assuming you have access to the layer's bounds or settings here.
                    // Replace this with your actual world boundary check for the layer:
                    return IsCoordinateWithinLayerBounds(l, x, y);
                });

            double surfaceOffset = 0.35;

            if (closestLayer.Model != null) {
                _destinationGroundPos.Z = closestLayer.ZElevation;
                actualPos.Z = closestLayer.ZElevation;
                destinationElevatedZ = closestLayer.ZElevation + surfaceOffset;
            }
            else {
                // Fallback to native Z if no map layers exist or match
            }
        }

        Transform3DGroup destinationTransform = new Transform3DGroup();
        //avatarTransform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), -heading)));
        destinationTransform.Children.Add(new TranslateTransform3D(x, y,
            destinationElevatedZ)); // Elevated Z for lighting & visibility
        _destinationGroundMarkerModel.Transform = destinationTransform;


        // -------------------------------------------------------------
        // 2. PROJECTED FLOOR CIRCLES (Sits slightly above each map quad)
        // -------------------------------------------------------------
        // foreach (var layerKvp in _mapLayers) {
        //     string layerId = layerKvp.Key;
        //     float layerZ = layerKvp.Value.ZElevation;
        //
        //     if (!_projectedFloorMarkers.TryGetValue(layerId, out var floorMarker)) {
        //         float deltaZ = Math.Abs(z - layerZ);
        //         System.Windows.Media.Color markerColor = deltaZ < 15.0f
        //             ? Colors.Yellow
        //             : System.Windows.Media.Color.FromArgb(120, 0, 255, 255);
        //
        //         floorMarker = CreateProjectedFloorCircleMesh(markerColor, radius: avatarRadius);
        //         _projectedFloorMarkers[layerId] = floorMarker;
        //         _worldGroup.Children.Add(floorMarker);
        //     }
        //
        //     // Translate to map floor Z + 0.1 clearance offset
        //     Transform3DGroup floorTransform = new Transform3DGroup();
        //     floorTransform.Children.Add(new TranslateTransform3D(x, y, layerZ + 0.1));
        //     floorMarker.Transform = floorTransform;
        // }


        // -------------------------------------------------------------
        // 3. VERTICAL TETHER LINE THROUGH FLOORS
        // -------------------------------------------------------------
        // if (_mapLayers.Count > 0) {
        //     float minLayerZ = _mapLayers.Values.Min(l => l.ZElevation);
        //     float maxLayerZ = _mapLayers.Values.Max(l => l.ZElevation);
        //
        //     Point3D bottomAnchor = new Point3D(x, y, Math.Min(z, minLayerZ));
        //     Point3D topAnchor = new Point3D(x, y, Math.Max(z, maxLayerZ));
        //
        //     if (_playerTetherLineModel != null) {
        //         _worldGroup.Children.Remove(_playerTetherLineModel);
        //     }
        //
        //     _playerTetherLineModel = CreateVerticalTetherMesh(bottomAnchor, topAnchor, Colors.White, thickness: 0.25);
        //     _worldGroup.Children.Add(_playerTetherLineModel);
        // }

        // -------------------------------------------------------------
        // 4. OVERLAYS, RENDER ORDER & CAMERA TRACKING
        // -------------------------------------------------------------
        
        EnforceTransparentRenderOrder();
    }
    
    public void RemoveDestinationMarker() {
        _hasDestinationPosition = false;
    }
    
    private GeometryModel3D CreateFlatMapMarker(Point3D position, System.Windows.Media.Color color)
    {
        MeshGeometry3D quadMesh = new MeshGeometry3D();
        double size = 1.5; // Pin size

        quadMesh.Positions.Add(new Point3D(position.X - size, position.Y - size, position.Z + 0.1));
        quadMesh.Positions.Add(new Point3D(position.X + size, position.Y - size, position.Z + 0.1));
        quadMesh.Positions.Add(new Point3D(position.X + size, position.Y + size, position.Z + 0.1));
        quadMesh.Positions.Add(new Point3D(position.X - size, position.Y + size, position.Z + 0.1));

        quadMesh.TriangleIndices.Add(0); quadMesh.TriangleIndices.Add(1); quadMesh.TriangleIndices.Add(2);
        quadMesh.TriangleIndices.Add(0); quadMesh.TriangleIndices.Add(2); quadMesh.TriangleIndices.Add(3);

        DiffuseMaterial material = new DiffuseMaterial(new SolidColorBrush(color));
        return new GeometryModel3D { Geometry = quadMesh, Material = material, BackMaterial = material };
    }
    
    private GeometryModel3D
        CreateDestinationMesh(double width = 2.0, double length = 3.5, double height = 1.2) {
        MeshGeometry3D arrowMesh = new MeshGeometry3D();

        // -------------------------------------------------------------
        // Vertices defining an Arrowhead/Dart with an indented tail
        // -------------------------------------------------------------
        Point3D nose = new Point3D(0, length * 0.7, 0.2); // Index 0: Front Nose (+Y)
        Point3D leftWing = new Point3D(-width, -length * 0.5, 0); // Index 1: Rear Left Wing
        Point3D rightWing = new Point3D(width, -length * 0.5, 0); // Index 2: Rear Right Wing
        Point3D tailNotch = new Point3D(0, -length * 0.1, 0.2); // Index 3: Indented Rear Tail
        Point3D spinePeak = new Point3D(0, -length * 0.1, height); // Index 4: Raised Center Ridge

        arrowMesh.Positions.Add(nose); // 0
        arrowMesh.Positions.Add(leftWing); // 1
        arrowMesh.Positions.Add(rightWing); // 2
        arrowMesh.Positions.Add(tailNotch); // 3
        arrowMesh.Positions.Add(spinePeak); // 4

        // -------------------------------------------------------------
        // Upper Hull (Sloped 3D Ridge Faces)
        // -------------------------------------------------------------
        // Top-Left Nose Slope
        arrowMesh.TriangleIndices.Add(0);
        arrowMesh.TriangleIndices.Add(4);
        arrowMesh.TriangleIndices.Add(1);

        // Top-Right Nose Slope
        arrowMesh.TriangleIndices.Add(0);
        arrowMesh.TriangleIndices.Add(2);
        arrowMesh.TriangleIndices.Add(4);

        // Rear-Left Wing Slope
        arrowMesh.TriangleIndices.Add(1);
        arrowMesh.TriangleIndices.Add(4);
        arrowMesh.TriangleIndices.Add(3);

        // Rear-Right Wing Slope
        arrowMesh.TriangleIndices.Add(3);
        arrowMesh.TriangleIndices.Add(4);
        arrowMesh.TriangleIndices.Add(2);

        // -------------------------------------------------------------
        // Bottom Cap (Underbelly)
        // -------------------------------------------------------------
        arrowMesh.TriangleIndices.Add(0);
        arrowMesh.TriangleIndices.Add(1);
        arrowMesh.TriangleIndices.Add(3);

        arrowMesh.TriangleIndices.Add(0);
        arrowMesh.TriangleIndices.Add(3);
        arrowMesh.TriangleIndices.Add(2);

        // High-visibility emissive yellow material
        SolidColorBrush yellowBrush = new SolidColorBrush(Colors.Yellow);
        MaterialGroup matGroup = new MaterialGroup();
        matGroup.Children.Add(new DiffuseMaterial(yellowBrush));
        matGroup.Children.Add(new EmissiveMaterial(yellowBrush));

        return new GeometryModel3D {
            Geometry = arrowMesh,
            Material = matGroup,
            BackMaterial = matGroup
        };
    }
    
    /// <summary>
    /// Renders a completely fixed yellow anchor ring (no pulsing, no camera distance scaling).
    /// </summary>
    private void UpdateDestinationPulseRing(Point3D position, double baseScale) {
        if (_destinationPulseRingModel == null) {
            _destinationPulseRingModel = CreatePulseRingMesh(Colors.Yellow);
            _worldGroup.Children.Add(_destinationPulseRingModel);
        }

        // Fixed 1.0 multiplier (ignores camera baseScale and pulse animation)
        double fixedRadius = 1.0;

        Transform3DGroup ringTransform = new Transform3DGroup();
        ringTransform.Children.Add(new ScaleTransform3D(fixedRadius, fixedRadius, 1.0));
        ringTransform.Children.Add(new TranslateTransform3D(position.X, position.Y, position.Z + 0.3));
        _destinationPulseRingModel.Transform = ringTransform;
    }

    private void UpdateDestinationOuterRedPulseRing(Point3D position, double baseScale) {
        if (_destinationOuterPulseRingModel == null) {
            _destinationOuterPulseRingModel = CreateDestinationOuterPulseRingMesh();
            _worldGroup.Children.Add(_destinationOuterPulseRingModel);
        }

        // SLOWER PULSE SPEED: Reduce step from 0.01 to 0.003
        double pulseStep = 0.002;
        // Smooth pulse speed (0.01 step)
        double maxScale = 1.4; // 1.4x maximum diameter
        _outerPulseScale += pulseStep;
        if (_outerPulseScale > maxScale) _outerPulseScale = 1.0;

        double progress = (_outerPulseScale - 1.0) / (maxScale - 1.0); // Range = 0.4

        // Yellow (255, 255, 0) -> Red (255, 0, 0)
        byte red = 255;
        byte green = (byte)(255 * (1.0 - progress));
        byte alpha = (byte)(60 * (1.0 - progress));

        System.Windows.Media.Color dynamicColor = System.Windows.Media.Color.FromArgb(alpha, red, green, 0);

        SolidColorBrush brush = new SolidColorBrush(dynamicColor);
        MaterialGroup matGroup = new MaterialGroup();
        matGroup.Children.Add(new DiffuseMaterial(brush));
        matGroup.Children.Add(new EmissiveMaterial(brush));

        _destinationOuterPulseRingModel.Material = matGroup;
        _destinationOuterPulseRingModel.BackMaterial = matGroup;

        double currentRadius = baseScale * _outerPulseScale;

        Transform3DGroup ringTransform = new Transform3DGroup();
        ringTransform.Children.Add(new ScaleTransform3D(currentRadius, currentRadius, 1.0));
        ringTransform.Children.Add(new TranslateTransform3D(position.X, position.Y, position.Z + 0.4));
        _destinationOuterPulseRingModel.Transform = ringTransform;
    }

    private MeshGeometry3D CreateDestinationPulseRingGeometry() {
        MeshGeometry3D ringMesh = new MeshGeometry3D();
        int segments = 24;
        double innerR = 5.0, outerR = 24;

        for (int i = 0; i < segments; i++) {
            double angle1 = i * 2 * Math.PI / segments;
            double angle2 = (i + 1) * 2 * Math.PI / segments;

            ringMesh.Positions.Add(new Point3D(innerR * Math.Cos(angle1), innerR * Math.Sin(angle1), 0));
            ringMesh.Positions.Add(new Point3D(outerR * Math.Cos(angle1), outerR * Math.Sin(angle1), 0));
            ringMesh.Positions.Add(new Point3D(outerR * Math.Cos(angle2), outerR * Math.Sin(angle2), 0));
            ringMesh.Positions.Add(new Point3D(innerR * Math.Cos(angle2), innerR * Math.Sin(angle2), 0));

            int idx = i * 4;
            // CCW Winding for Top-Down Perspective Camera (Z-up)
            ringMesh.TriangleIndices.Add(idx);
            ringMesh.TriangleIndices.Add(idx + 2);
            ringMesh.TriangleIndices.Add(idx + 1);
            ringMesh.TriangleIndices.Add(idx);
            ringMesh.TriangleIndices.Add(idx + 3);
            ringMesh.TriangleIndices.Add(idx + 2);
        }

        return ringMesh;
    }
    
    private GeometryModel3D CreateDestinationPulseRingMesh(System.Windows.Media.Color color) {
        MeshGeometry3D ringMesh = new MeshGeometry3D();
        int segments = 24;
        double innerR = 2.0, outerR = 2.3;

        for (int i = 0; i < segments; i++) {
            double angle1 = i * 2 * Math.PI / segments;
            double angle2 = (i + 1) * 2 * Math.PI / segments;

            ringMesh.Positions.Add(new Point3D(innerR * Math.Cos(angle1), innerR * Math.Sin(angle1), 0));
            ringMesh.Positions.Add(new Point3D(outerR * Math.Cos(angle1), outerR * Math.Sin(angle1), 0));
            ringMesh.Positions.Add(new Point3D(outerR * Math.Cos(angle2), outerR * Math.Sin(angle2), 0));
            ringMesh.Positions.Add(new Point3D(innerR * Math.Cos(angle2), innerR * Math.Sin(angle2), 0));

            int idx = i * 4;
            // CCW Winding for Top-Down Perspective Camera (Z-up)
            ringMesh.TriangleIndices.Add(idx);
            ringMesh.TriangleIndices.Add(idx + 2);
            ringMesh.TriangleIndices.Add(idx + 1);
            ringMesh.TriangleIndices.Add(idx);
            ringMesh.TriangleIndices.Add(idx + 3);
            ringMesh.TriangleIndices.Add(idx + 2);
        }

        MaterialGroup matGroup = new MaterialGroup();
        matGroup.Children.Add(new DiffuseMaterial(new SolidColorBrush(color) { Opacity = 0.6 }));
        matGroup.Children.Add(new EmissiveMaterial(new SolidColorBrush(color) { Opacity = 0.6 }));

        return new GeometryModel3D { Geometry = ringMesh, Material = matGroup, BackMaterial = matGroup };
    }

    private GeometryModel3D CreateOuterDestinationPulseRingMesh() {
        MeshGeometry3D ringMesh = new MeshGeometry3D();
        int segments = 32;
        double innerR = 2.0, outerR = 2.8;

        for (int i = 0; i < segments; i++) {
            double angle1 = i * 2 * Math.PI / segments;
            double angle2 = (i + 1) * 2 * Math.PI / segments;

            ringMesh.Positions.Add(new Point3D(innerR * Math.Cos(angle1), innerR * Math.Sin(angle1), 0));
            ringMesh.Positions.Add(new Point3D(outerR * Math.Cos(angle1), outerR * Math.Sin(angle1), 0));
            ringMesh.Positions.Add(new Point3D(outerR * Math.Cos(angle2), outerR * Math.Sin(angle2), 0));
            ringMesh.Positions.Add(new Point3D(innerR * Math.Cos(angle2), innerR * Math.Sin(angle2), 0));

            int idx = i * 4;
            // CCW Winding for Top-Down Perspective Camera (Z-up)
            ringMesh.TriangleIndices.Add(idx);
            ringMesh.TriangleIndices.Add(idx + 2);
            ringMesh.TriangleIndices.Add(idx + 1);
            ringMesh.TriangleIndices.Add(idx);
            ringMesh.TriangleIndices.Add(idx + 3);
            ringMesh.TriangleIndices.Add(idx + 2);
        }

        SolidColorBrush brush = new SolidColorBrush(Colors.Yellow);
        MaterialGroup matGroup = new MaterialGroup();
        matGroup.Children.Add(new DiffuseMaterial(brush));
        matGroup.Children.Add(new EmissiveMaterial(brush));

        return new GeometryModel3D { Geometry = ringMesh, Material = matGroup, BackMaterial = matGroup };
    }

    private GeometryModel3D CreateDestinationGroundMarkerMesh() {
        MeshGeometry3D pyramidMesh = new MeshGeometry3D();
        double width = 1.5;
        double length = 2.25;
        double height = 1.8;

        // Base rests strictly at local Z = 0.0
        pyramidMesh.Positions.Add(new Point3D(0, length / 2, 0.0)); // 0: Apex
        pyramidMesh.Positions.Add(new Point3D(-width / 2, -length / 2, 0.0)); // 1: Bottom-Left
        pyramidMesh.Positions.Add(new Point3D(width / 2, -length / 2, 0.0)); // 2: Bottom-Right
        pyramidMesh.Positions.Add(new Point3D(0, 0, height)); // 3: Tip

        // CCW Triangle Winding
        pyramidMesh.TriangleIndices.Add(0);
        pyramidMesh.TriangleIndices.Add(2);
        pyramidMesh.TriangleIndices.Add(1);
        pyramidMesh.TriangleIndices.Add(0);
        pyramidMesh.TriangleIndices.Add(1);
        pyramidMesh.TriangleIndices.Add(3);
        pyramidMesh.TriangleIndices.Add(1);
        pyramidMesh.TriangleIndices.Add(2);
        pyramidMesh.TriangleIndices.Add(3);
        pyramidMesh.TriangleIndices.Add(2);
        pyramidMesh.TriangleIndices.Add(0);
        pyramidMesh.TriangleIndices.Add(3);

        MaterialGroup matGroup = new MaterialGroup();
        matGroup.Children.Add(new DiffuseMaterial(new SolidColorBrush(Colors.Yellow)));
        matGroup.Children.Add(new EmissiveMaterial(new SolidColorBrush(Colors.Yellow))); // Self-illuminat

        return new GeometryModel3D { Geometry = pyramidMesh, Material = matGroup, BackMaterial = matGroup };
    }

    // private GeometryModel3D CreateTruePositionSphereMesh(double radius) {
    //     MeshGeometry3D sphereMesh = new MeshGeometry3D();
    //     int latSegments = 10, lonSegments = 14;
    //
    //     for (int lat = 0; lat <= latSegments; lat++) {
    //         double theta = lat * Math.PI / latSegments;
    //         double sinTheta = Math.Sin(theta);
    //         double cosTheta = Math.Cos(theta);
    //
    //         for (int lon = 0; lon <= lonSegments; lon++) {
    //             double phi = lon * 2 * Math.PI / lonSegments;
    //             double x = radius * sinTheta * Math.Cos(phi);
    //             double y = radius * sinTheta * Math.Sin(phi);
    //             double z = radius * cosTheta;
    //
    //             sphereMesh.Positions.Add(new Point3D(x, y, z));
    //         }
    //     }
    //
    //     for (int lat = 0; lat < latSegments; lat++) {
    //         for (int lon = 0; lon < lonSegments; lon++) {
    //             int current = (lat * (lonSegments + 1)) + lon;
    //             int next = current + lonSegments + 1;
    //
    //             sphereMesh.TriangleIndices.Add(current);
    //             sphereMesh.TriangleIndices.Add(next);
    //             sphereMesh.TriangleIndices.Add(current + 1);
    //
    //             sphereMesh.TriangleIndices.Add(next);
    //             sphereMesh.TriangleIndices.Add(next + 1);
    //             sphereMesh.TriangleIndices.Add(current + 1);
    //         }
    //     }
    //
    //     // High-visibility cyan/white emissive material
    //     SolidColorBrush sphereBrush = new SolidColorBrush(Colors.Cyan);
    //     MaterialGroup matGroup = new MaterialGroup();
    //     matGroup.Children.Add(new DiffuseMaterial(sphereBrush));
    //     matGroup.Children.Add(new EmissiveMaterial(sphereBrush)); // Self-illuminated cyan glow
    //
    //     return new GeometryModel3D { Geometry = sphereMesh, Material = matGroup, BackMaterial = matGroup };
    // }

    // private GeometryModel3D CreateDestinationVerticalTetherMesh(Point3D start, Point3D end, System.Windows.Media.Color color,
    //     double thickness) {
    //     MeshGeometry3D tetherMesh = new MeshGeometry3D();
    //
    //     tetherMesh.Positions.Add(new Point3D(start.X - thickness, start.Y, start.Z));
    //     tetherMesh.Positions.Add(new Point3D(start.X + thickness, start.Y, start.Z));
    //     tetherMesh.Positions.Add(new Point3D(end.X + thickness, end.Y, end.Z));
    //     tetherMesh.Positions.Add(new Point3D(end.X - thickness, end.Y, end.Z));
    //
    //     tetherMesh.TriangleIndices.Add(0);
    //     tetherMesh.TriangleIndices.Add(1);
    //     tetherMesh.TriangleIndices.Add(2);
    //     tetherMesh.TriangleIndices.Add(0);
    //     tetherMesh.TriangleIndices.Add(2);
    //     tetherMesh.TriangleIndices.Add(3);
    //
    //     MaterialGroup matGroup = new MaterialGroup();
    //     matGroup.Children.Add(new DiffuseMaterial(new SolidColorBrush(color) { Opacity = 0.75 }));
    //     matGroup.Children.Add(new EmissiveMaterial(new SolidColorBrush(color) { Opacity = 0.75 }));
    //
    //     return new GeometryModel3D { Geometry = tetherMesh, Material = matGroup, BackMaterial = matGroup };
    // }
    #endregion
    
//     public void UpdatePlayerPosition(float x, float y, float z, float heading, bool syncHeadingCamera, float mapPlaneZ = 0.0f) 
// {
//     EnsureInitialized();
//
//     double baseScale = Math.Max(1.0, _cameraRadius / 100.0);
//     Point3D groundPos = new Point3D(x, y, mapPlaneZ);
//     Point3D actualPos = new Point3D(x, y, z);
//
//     _lastKnownGroundPos = groundPos;
//     _hasPlayerPosition = true;
//
//     // 1. GROUNDED DIRECTIONAL ARROW
//     if (_playerGroundMarkerModel == null) {
//         _playerGroundMarkerModel = CreatePlayerGroundMarkerMesh();
//         _worldGroup.Children.Add(_playerGroundMarkerModel);
//     }
//
//     // Absolute Z clearance (+0.35 units above map floor) prevents Z-fighting completely
//     double elevatedGroundZ = mapPlaneZ + 0.35;
//
//     Transform3DGroup groundTransform = new Transform3DGroup();
//     groundTransform.Children.Add(new ScaleTransform3D(baseScale, baseScale, baseScale));
//     groundTransform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), -heading)));
//     groundTransform.Children.Add(new TranslateTransform3D(x, y, elevatedGroundZ)); 
//     _playerGroundMarkerModel.Transform = groundTransform;
//
//     // 2. FLOATING SPHERE & VERTICAL TETHER
//     double deltaZ = Math.Abs(z - mapPlaneZ);
//
//     if (deltaZ > 0.5) 
//     {
//         if (_playerTrueSphereModel == null) {
//             _playerTrueSphereModel = CreateTruePositionSphereMesh(radius: 1.5);
//             _worldGroup.Children.Add(_playerTrueSphereModel);
//         }
//         else if (!_worldGroup.Children.Contains(_playerTrueSphereModel)) {
//             _worldGroup.Children.Add(_playerTrueSphereModel);
//         }
//
//         _playerTrueSphereModel.Transform = new TranslateTransform3D(x, y, z);
//
//         if (_playerTetherLineModel != null) {
//             _worldGroup.Children.Remove(_playerTetherLineModel);
//         }
//
//         // Anchor tether to the map quad floor (mapPlaneZ)
//         Point3D tetherAnchor = new Point3D(x, y, mapPlaneZ);
//         _playerTetherLineModel = CreateVerticalTetherMesh(tetherAnchor, actualPos, Colors.White, thickness: 0.25);
//         _worldGroup.Children.Add(_playerTetherLineModel);
//     }
//     else {
//         if (_playerTrueSphereModel != null && _worldGroup.Children.Contains(_playerTrueSphereModel)) {
//             _worldGroup.Children.Remove(_playerTrueSphereModel);
//         }
//         if (_playerTetherLineModel != null && _worldGroup.Children.Contains(_playerTetherLineModel)) {
//             _worldGroup.Children.Remove(_playerTetherLineModel);
//         }
//     }
//
//     // 3. TERRAIN & CAMERA UPDATES
//     AddGhostTerrainPoint(actualPos);
//
//     AddBreadcrumbNode(actualPos);
//     
//     EnforceTransparentRenderOrder();
//     
//     if (syncHeadingCamera) {
//         _cameraTarget = groundPos;
//         UpdateCameraPosition();
//     }
//     
// }

    // private void UpdatePulseRing(Point3D position, double baseScale) {
    //     if (_pulseRingModel == null) {
    //         _pulseRingModel = CreatePulseRingMesh();
    //         _worldGroup.Children.Add(_pulseRingModel);
    //     }
    //
    //     // Smooth pulse speed (0.015 step)
    //     _pulseScale += 0.015;
    //     if (_pulseScale > 2.2) _pulseScale = 1.0;
    //
    //     double currentRadius = baseScale * _pulseScale;
    //
    //     Transform3DGroup ringTransform = new Transform3DGroup();
    //     ringTransform.Children.Add(new ScaleTransform3D(currentRadius, currentRadius, 1.0));
    //     ringTransform.Children.Add(new TranslateTransform3D(position.X, position.Y, position.Z + 0.3));
    //     _pulseRingModel.Transform = ringTransform;
    // }

    // /// <summary>
    // /// Renders a fixed (non-pulsing) yellow anchor ring on the ground plane around the player.
    // /// </summary>
    // private void UpdatePulseRing(Point3D position, double baseScale)
    // {
    //     if (_pulseRingModel == null)
    //     {
    //         _pulseRingModel = CreatePulseRingMesh();
    //         _worldGroup.Children.Add(_pulseRingModel);
    //     }
    //
    //     // Fixed scale anchor (no incrementing pulse animation)
    //     double fixedScale = 1.0;
    //     double currentRadius = baseScale * fixedScale;
    //
    //     Transform3DGroup ringTransform = new Transform3DGroup();
    //     ringTransform.Children.Add(new ScaleTransform3D(currentRadius, currentRadius, 1.0));
    //     ringTransform.Children.Add(new TranslateTransform3D(position.X, position.Y, position.Z + 0.3));
    //     _pulseRingModel.Transform = ringTransform;
    // }

    

    // private void AddBreadcrumbNode(Point3D point) {
    //     if (_breadcrumbPoints.Count > 0 && Point3D.Subtract(point, _breadcrumbPoints[^1]).Length < 2.0)
    //         return;
    //
    //     _breadcrumbPoints.Add(point);
    //
    //     if (_breadcrumbLineModel != null)
    //         _worldGroup.Children.Remove(_breadcrumbLineModel);
    //
    //     if (_breadcrumbPoints.Count > 1) {
    //         _breadcrumbLineModel = Create3DLineStripMesh(_breadcrumbPoints, Colors.Cyan, thickness: 0.4);
    //         _worldGroup.Children.Add(_breadcrumbLineModel);
    //     }
    // }

    /// <summary>
    /// Adds a breadcrumb node, maintaining a strict rolling tail cap of 200 points.
    /// </summary>
    private void AddBreadcrumbNode(Point3D point) {
        // Filter minor position jitter
        if (_breadcrumbPoints.Count > 0 && Point3D.Subtract(point, _breadcrumbPoints[^1]).Length < 2.0)
            return;

        _breadcrumbPoints.Add(point);

        // Hard cap at 200 rolling nodes
        if (_breadcrumbPoints.Count > 200) {
            _breadcrumbPoints.RemoveAt(0);
        }

        if (_breadcrumbLineModel != null)
            _worldGroup.Children.Remove(_breadcrumbLineModel);

        if (_breadcrumbPoints.Count > 1) {
            _breadcrumbLineModel = Create3DLineStripMesh(_breadcrumbPoints, Colors.Cyan, thickness: 0.4);
            _worldGroup.Children.Add(_breadcrumbLineModel);
        }
    }

    /// <summary>
    /// Builds a solid, 100% opaque 3D line strip mesh for the rolling breadcrumb trail.
    /// </summary>
    private GeometryModel3D Create3DLineStripMesh(List<Point3D> points, System.Windows.Media.Color color,
        double thickness) {
        MeshGeometry3D lineMesh = new MeshGeometry3D();

        for (int i = 0; i < points.Count - 1; i++) {
            Point3D p1 = points[i];
            Point3D p2 = points[i + 1];
            int baseIdx = lineMesh.Positions.Count;

            // Horizontal Ribbon (XY Plane)
            lineMesh.Positions.Add(new Point3D(p1.X - thickness, p1.Y, p1.Z));
            lineMesh.Positions.Add(new Point3D(p1.X + thickness, p1.Y, p1.Z));
            lineMesh.Positions.Add(new Point3D(p2.X + thickness, p2.Y, p2.Z));
            lineMesh.Positions.Add(new Point3D(p2.X - thickness, p2.Y, p2.Z));

            lineMesh.TriangleIndices.Add(baseIdx);
            lineMesh.TriangleIndices.Add(baseIdx + 2);
            lineMesh.TriangleIndices.Add(baseIdx + 1);
            lineMesh.TriangleIndices.Add(baseIdx);
            lineMesh.TriangleIndices.Add(baseIdx + 3);
            lineMesh.TriangleIndices.Add(baseIdx + 2);

            // Vertical Ribbon Cross (XZ Plane) so the line is visible from edge-on and subterranean angles
            int vertIdx = lineMesh.Positions.Count;
            lineMesh.Positions.Add(new Point3D(p1.X, p1.Y, p1.Z - thickness));
            lineMesh.Positions.Add(new Point3D(p1.X, p1.Y, p1.Z + thickness));
            lineMesh.Positions.Add(new Point3D(p2.X, p2.Y, p2.Z + thickness));
            lineMesh.Positions.Add(new Point3D(p2.X, p2.Y, p2.Z - thickness));

            lineMesh.TriangleIndices.Add(vertIdx);
            lineMesh.TriangleIndices.Add(vertIdx + 2);
            lineMesh.TriangleIndices.Add(vertIdx + 1);
            lineMesh.TriangleIndices.Add(vertIdx);
            lineMesh.TriangleIndices.Add(vertIdx + 3);
            lineMesh.TriangleIndices.Add(vertIdx + 2);
        }

        // Pure Emissive Cyan (Self-Illuminated Glow)
        SolidColorBrush glowBrush = new SolidColorBrush(color);

        MaterialGroup materialGroup = new MaterialGroup();
        materialGroup.Children.Add(new DiffuseMaterial(glowBrush));
        materialGroup.Children.Add(new EmissiveMaterial(glowBrush)); // Guarantees full brightness below map

        return new GeometryModel3D {
            Geometry = lineMesh,
            Material = materialGroup,
            BackMaterial = materialGroup
        };
    }

    // private GeometryModel3D Create3DLineStripMesh(List<Point3D> points, System.Windows.Media.Color color, double thickness) {
    //     MeshGeometry3D lineMesh = new MeshGeometry3D();
    //
    //     for (int i = 0; i < points.Count - 1; i++) {
    //         Point3D p1 = points[i];
    //         Point3D p2 = points[i + 1];
    //         int baseIdx = i * 4;
    //
    //         lineMesh.Positions.Add(new Point3D(p1.X - thickness, p1.Y, p1.Z));
    //         lineMesh.Positions.Add(new Point3D(p1.X + thickness, p1.Y, p1.Z));
    //         lineMesh.Positions.Add(new Point3D(p2.X + thickness, p2.Y, p2.Z));
    //         lineMesh.Positions.Add(new Point3D(p2.X - thickness, p2.Y, p2.Z));
    //
    //         lineMesh.TriangleIndices.Add(baseIdx);
    //         lineMesh.TriangleIndices.Add(baseIdx + 1);
    //         lineMesh.TriangleIndices.Add(baseIdx + 2);
    //
    //         lineMesh.TriangleIndices.Add(baseIdx);
    //         lineMesh.TriangleIndices.Add(baseIdx + 2);
    //         lineMesh.TriangleIndices.Add(baseIdx + 3);
    //     }
    //
    //     DiffuseMaterial material = new DiffuseMaterial(new SolidColorBrush(color) { Opacity = 0.85 });
    //     return new GeometryModel3D { Geometry = lineMesh, Material = material, BackMaterial = material };
    // }

    #region ghost grid

    private GeometryModel3D? _ghostTerrainModel;
    private MeshGeometry3D _ghostMesh = new MeshGeometry3D();
    private readonly TerrainVoxelGrid _voxelGrid = new TerrainVoxelGrid();

    /// <summary>
    /// Adds a node to the ghost terrain mesh if it represents new unique spatial volume.
    /// </summary>
    public void AddGhostTerrainPoint(Point3D newPoint) {
        if (!_voxelGrid.TryAddPoint(newPoint, out Point3D canonicalPoint))
            return; // Skip duplicate / overlapping spatial data

        if (_ghostTerrainModel == null) {
            _ghostTerrainModel = CreateGhostTerrainModel(_ghostMesh);
            _worldGroup.Children.Add(_ghostTerrainModel);
        }

        // Connect new point to nearby existing nodes to form ghost surface quads/triangles
        AppendToGhostMesh(_ghostMesh, canonicalPoint);
    }

    private GeometryModel3D CreateGhostTerrainModel(MeshGeometry3D mesh) {
        // Ghost White Translucent Brush (~25% Opacity White)
        SolidColorBrush ghostBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 240, 245, 255));

        MaterialGroup matGroup = new MaterialGroup();
        matGroup.Children.Add(new DiffuseMaterial(ghostBrush));
        matGroup.Children.Add(
            new EmissiveMaterial(new SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 255, 255, 255))));

        return new GeometryModel3D {
            Geometry = mesh,
            Material = matGroup,
            BackMaterial = matGroup // Double-sided rendering for inner/outer visibility
        };
    }

    private void AppendToGhostMesh(MeshGeometry3D mesh, Point3D point) {
        // Append vertex and construct lightweight ribbon triangle strip
        int vIdx = mesh.Positions.Count;
        double r = 0.75; // 1.5m footprint radius

        mesh.Positions.Add(new Point3D(point.X - r, point.Y - r, point.Z));
        mesh.Positions.Add(new Point3D(point.X + r, point.Y - r, point.Z));
        mesh.Positions.Add(new Point3D(point.X + r, point.Y + r, point.Z));
        mesh.Positions.Add(new Point3D(point.X - r, point.Y + r, point.Z));

        // CCW Winding for Z-up Top-Down Viewport
        mesh.TriangleIndices.Add(vIdx);
        mesh.TriangleIndices.Add(vIdx + 2);
        mesh.TriangleIndices.Add(vIdx + 1);
        mesh.TriangleIndices.Add(vIdx);
        mesh.TriangleIndices.Add(vIdx + 3);
        mesh.TriangleIndices.Add(vIdx + 2);
    }

    #endregion

    #region Location Clicked
    
    public event Action<MapLocation3D>? LocationMarkerClicked;

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        base.OnMouseLeftButtonDown(e);

        System.Windows.Point mousePos = e.GetPosition(_viewport);
        HitTestResult result = VisualTreeHelper.HitTest(_viewport, mousePos);

        if (result is RayMeshGeometry3DHitTestResult meshResult) {
            // Cast the generic Model3D to a GeometryModel3D safely
            if (meshResult.ModelHit is GeometryModel3D hitGeometryModel && 
                _markerToLocationMap.TryGetValue(hitGeometryModel, out MapLocation3D? clickedLocation)) {
            
                LocationMarkerClicked?.Invoke(clickedLocation);
                e.Handled = true;
            }
        }
    }
    
    #endregion
    
    #region Zoom to mouse
    
    public void ZoomCameraToMouse(float deltaRadius, System.Windows.Point mousePos) {
        EnsureInitialized();

        // Define a threshold distance (e.g., if camera radius is greater than 1000.0, use standard center zoom)
        const float distanceThreshold = 4500.0f;

        if (_cameraRadius <= distanceThreshold) {
            // 1. Perform a hit test or unproject the mouse position to find the world point under the cursor
            Point3D? worldTarget = GetWorldPointFromMouse(mousePos);

            if (worldTarget.HasValue && _worldScaleFactor > 0) {
                // 2. Optionally blend the camera target slightly toward the mouse's world point 
                // as you zoom in, making the cursor the center of the zoom action
                double lerpFactor = 0.3; // Adjust pull strength (0.0 to 1.0)
                _cameraTarget = new Point3D(
                    _cameraTarget.X + (worldTarget.Value.X - _cameraTarget.X) * lerpFactor,
                    _cameraTarget.Y + (worldTarget.Value.Y - _cameraTarget.Y) * lerpFactor,
                    _cameraTarget.Z
                );
            }
        }

        // 3. Perform standard zoom distance clamping
        float baseStep = _cameraRadius > 1000.0f ? deltaRadius * 25.0f : deltaRadius * 15.0f;
        float step = (float)(baseStep * _worldScaleFactor);
        double absoluteMin = _worldScaleFactor <= 0.1 ? 0.2 : 10.0;

        _cameraRadius = (float)Math.Clamp(_cameraRadius + step, absoluteMin, _zoomLimit);
        UpdateCameraPosition();
    }
    
    private Point3D? GetWorldPointFromMouse(System.Windows.Point mousePos) {
        HitTestResult result = VisualTreeHelper.HitTest(_viewport, mousePos);
        if (result is RayMeshGeometry3DHitTestResult meshResult) {
            // Returns the exact 3D world coordinate where the ray hit a map mesh or marker
            return meshResult.PointHit;
        }
        return null; // Fallback if pointing at empty space
    }
    
    #endregion
    
    #region Hover Tooltips
    
    private MapLocation3D? _lastHoveredLocation;
    public event Action<MapLocation3D>? LocationMarkerHoverChanged;
    
    private void Viewport_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) {
        System.Windows.Point mousePos = e.GetPosition(_viewport);
        HitTestResult result = VisualTreeHelper.HitTest(_viewport, mousePos);

        MapLocation3D? currentHovered = null;

        if (result is RayMeshGeometry3DHitTestResult meshResult) {
            if (meshResult.ModelHit is GeometryModel3D hitGeometryModel && 
                _markerToLocationMap.TryGetValue(hitGeometryModel, out MapLocation3D? location)) {
                currentHovered = location;
            }
        }

        // If the hovered location changed, trigger your tooltip logic or raise an event
        if (currentHovered != _lastHoveredLocation) {
            _lastHoveredLocation = currentHovered;
        
            if (currentHovered != null) {
                // Show tooltip with the location name or details (e.g., currentHovered.Name or Tooltip)
                //RaiseEvent(new RoutedEventArgs(LocationMarkerHovered, currentHovered));
                LocationMarkerHoverChanged?.Invoke(currentHovered); // Passes the location, or null if left
            } else {
                // Hide tooltip
                //RaiseEvent(new RoutedEventArgs(MarkerHoveredEvent, null));
            }
        }
    }

    public MapLocation3D? GetHoveredLocation(System.Windows.Point mousePos) {
        MapLocation3D? currentHovered = null;
        HitTestResult result = VisualTreeHelper.HitTest(_viewport, mousePos);
        
        if (result is RayMeshGeometry3DHitTestResult meshResult) {
            if (meshResult.ModelHit is GeometryModel3D hitGeometryModel && 
                _markerToLocationMap.TryGetValue(hitGeometryModel, out MapLocation3D? location)) {
                currentHovered = location;
            }
        }

        return currentHovered;
    }
    
    #endregion
    
    #region Map sets

    /// <summary>
    /// Removes a specific map quad layer from the 3D scene.
    /// </summary>
    public void RemoveMapLayer(string layerId) {
        EnsureInitialized();

        if (_mapLayers.TryGetValue(layerId, out var existingLayer)) {
            _worldGroup.Children.Remove(existingLayer.Model);
            _mapLayers.Remove(layerId);
        }
    }

    /// <summary>
    /// Clears all map layers from the 3D scene.
    /// </summary>
    public void ClearAllMapLayers() {
        EnsureInitialized();

        foreach (var layer in _mapLayers.Values) {
            _worldGroup.Children.Remove(layer.Model);
        }

        _mapLayers.Clear();
    }

    /// <summary>
    /// Updates an existing map plane's texture in real-time as live drawing expands the image bitmap.
    /// </summary>
    public void RefreshMapLayerTexture(string layerId, BitmapSource updatedBitmap) {
        if (!_mapLayers.TryGetValue(layerId, out var layer)) return;

        ImageBrush brush = new ImageBrush(updatedBitmap) { Opacity = layer.Brush.Opacity };
        MaterialGroup materialGroup = new MaterialGroup();
        materialGroup.Children.Add(new DiffuseMaterial(brush));
        materialGroup.Children.Add(new EmissiveMaterial(brush));

        layer.Model.Material = materialGroup;
        layer.Model.BackMaterial = materialGroup;
    }

    #endregion

    /// <summary>
    /// Clears all ghost terrain footprint meshes and resets the spatial voxel grid.
    /// </summary>
    public void ClearGhostTerrain() {
        EnsureInitialized();

        // Reset the internal voxel deduplication grid
        _voxelGrid.Clear();

        // Remove the 3D model from the Viewport scene
        if (_ghostTerrainModel != null) {
            _worldGroup.Children.Remove(_ghostTerrainModel);
            _ghostTerrainModel = null;
        }

        // Clear raw vertex and triangle buffers
        _ghostMesh.Positions.Clear();
        _ghostMesh.TriangleIndices.Clear();
        _ghostMesh.TextureCoordinates.Clear();
        _ghostMesh.Normals.Clear();
    }

    #region Support various world space operations

    // Dynamic zoom factor based on world scale
    private double _worldScaleFactor = 1.0;

    public void ConfigureForMapBounds(double worldWidth, double worldHeight) {
        double maxDim = Math.Max(worldWidth, worldHeight);

        // If it's a tiny 50x50 micro-grid, scale down steps and near planes
        if (maxDim <= 100.0) {
            _worldScaleFactor = 0.1;
            _camera.NearPlaneDistance = 0.01; // Closer clipping for micro-grids
            _camera.FarPlaneDistance = 5000.0;
        }
        else {
            _worldScaleFactor = 1.0;
            _camera.NearPlaneDistance = 1.0;
            _camera.FarPlaneDistance = 50000.0;
        }
    }

    // public void ZoomCamera(float deltaRadius) {
    //     EnsureInitialized();
    //     float step = _cameraRadius > 1000.0f ? deltaRadius * 25.0f : deltaRadius * 15.0f;
    //     _cameraRadius = Math.Clamp(_cameraRadius + step, 10.0f, _zoomLimit);
    //     UpdateCameraPosition();
    // }


    public void ZoomCamera(float deltaRadius) {
        EnsureInitialized();
        // Scale zoom step dynamically by world scale factor
        float baseStep = _cameraRadius > 1000.0f ? deltaRadius * 25.0f : deltaRadius * 15.0f;
        float step = (float)(baseStep * _worldScaleFactor);

        // 2. Allow a much tighter minimum radius for micro-grids (e.g., 0.1 instead of 1.0) 
        double absoluteMin = _worldScaleFactor <= 0.1 ? 0.2 : 10.0;

        _cameraRadius = Math.Clamp(_cameraRadius + step, 1.0f * (float)_worldScaleFactor, _zoomLimit);
        _cameraRadius = (float)Math.Clamp(_cameraRadius + step, absoluteMin, _zoomLimit);
        UpdateCameraPosition();
    }

    private GeometryModel3D? _proceduralGridModel;

    public void ToggleProceduralGrid(bool show, Point3D? center = null, double width = 200.0, double height = 200.0) {
        EnsureInitialized();

        if (show) {
            // If dimensions or center changed, recreate or reposition the grid
            if (_proceduralGridModel != null) {
                _worldGroup.Children.Remove(_proceduralGridModel);
                _proceduralGridModel = null;
            }

            double step = Math.Max(10.0, width / 20.0); // Dynamically scale grid line spacing
            _proceduralGridModel = CreateGridMesh(width, height, step, .5);

            // Apply transform to center the grid on the actual world/map center if provided
            if (center.HasValue) {
                _proceduralGridModel.Transform = new TranslateTransform3D(center.Value.X, center.Value.Y, center.Value.Z);
            }

            _worldGroup.Children.Add(_proceduralGridModel);
        } else {
            if (_proceduralGridModel != null && _worldGroup.Children.Contains(_proceduralGridModel)) {
                _worldGroup.Children.Remove(_proceduralGridModel);
            }
        }

        EnforceTransparentRenderOrder();
    }

    private GeometryModel3D CreateGridMesh(double width, double height, double step, double thickness = 0.1) {
        MeshGeometry3D mesh = new MeshGeometry3D();

        double halfW = width / 2.0;
        double halfH = height / 2.0;
        
        // Generate lines along X axis (horizontal bars)
        for (double y = -halfH; y <= halfH; y += step) {
            AddLineSegment(mesh, new Point3D(-halfW, y, 0), new Point3D(halfW, y, 0), thickness);
        }

        // Generate lines along Y axis (vertical bars)
        for (double x = -halfW; x <= halfW; x += step) {
            AddLineSegment(mesh, new Point3D(x, -halfH, 0), new Point3D(x, halfH, 0), thickness);
        }

        SolidColorBrush gridBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 200, 200, 200));
        MaterialGroup matGroup = new MaterialGroup();
        matGroup.Children.Add(new DiffuseMaterial(gridBrush));
        matGroup.Children.Add(new EmissiveMaterial(gridBrush));

        return new GeometryModel3D { Geometry = mesh, Material = matGroup, BackMaterial = matGroup };
    }

    private void AddLineSegment(MeshGeometry3D mesh, Point3D p1, Point3D p2, double thickness) {
        int baseIdx = mesh.Positions.Count;
        double halfT = thickness / 2.0;

        // Build a small flat ribbon quad for the line segment on the XY plane
        mesh.Positions.Add(new Point3D(p1.X - halfT, p1.Y - halfT, p1.Z)); // 0
        mesh.Positions.Add(new Point3D(p1.X + halfT, p1.Y + halfT, p1.Z)); // 1
        mesh.Positions.Add(new Point3D(p2.X + halfT, p2.Y + halfT, p2.Z)); // 2
        mesh.Positions.Add(new Point3D(p2.X - halfT, p2.Y - halfT, p2.Z)); // 3

        // Double-sided triangle winding so it renders cleanly from any pitch angle
        mesh.TriangleIndices.Add(baseIdx);
        mesh.TriangleIndices.Add(baseIdx + 2);
        mesh.TriangleIndices.Add(baseIdx + 1);

        mesh.TriangleIndices.Add(baseIdx);
        mesh.TriangleIndices.Add(baseIdx + 3);
        mesh.TriangleIndices.Add(baseIdx + 2);

        // Backface triangles
        mesh.TriangleIndices.Add(baseIdx);
        mesh.TriangleIndices.Add(baseIdx + 1);
        mesh.TriangleIndices.Add(baseIdx + 2);

        mesh.TriangleIndices.Add(baseIdx);
        mesh.TriangleIndices.Add(baseIdx + 2);
        mesh.TriangleIndices.Add(baseIdx + 3);
    }

    public void TogglePerspectivePreset() {
        if (Math.Abs(_cameraPitch - 89.0f) < 1.0f) {
            _cameraPitch = 45.0f; // Switch to standard isometric
        }
        else {
            _cameraPitch = 89.0f; // Switch to flat top-down overview
        }

        UpdateCameraPosition();
    }

    private Point3D? _targetCameraTarget;
    private float? _targetCameraRadius;
    private bool _isTransitioning;

    public void SmoothRecenterOnPlayer(Point3D playerPos, float targetRadius = -1) {
        EnsureInitialized();
        _targetCameraTarget = new Point3D(playerPos.X, playerPos.Y, 0.0);
        if (targetRadius > 0) {
            _targetCameraRadius = targetRadius;
        }

        _isTransitioning = true;
    }

// Hook this into your existing OnRenderingTick method to animate smoothly each frame
    private void AnimateCameraTransition() {
        if (!_isTransitioning || _targetCameraTarget == null) return;

        // Linear interpolation (Lerp factor 0.1 for smooth glide)
        double lerpFactor = 0.15;

        _cameraTarget = new Point3D(
            _cameraTarget.X + (_targetCameraTarget.Value.X - _cameraTarget.X) * lerpFactor,
            _cameraTarget.Y + (_targetCameraTarget.Value.Y - _cameraTarget.Y) * lerpFactor,
            _cameraTarget.Z + (_targetCameraTarget.Value.Z - _cameraTarget.Z) * lerpFactor
        );

        if (_targetCameraRadius.HasValue) {
            _cameraRadius += (float)((_targetCameraRadius.Value - _cameraRadius) * lerpFactor);
            if (Math.Abs(_targetCameraRadius.Value - _cameraRadius) < 0.1f) {
                _targetCameraRadius = null;
            }
        }

        UpdateCameraPosition();

        // Stop animating once close enough to target
        if (Point3D.Subtract(_cameraTarget, _targetCameraTarget.Value).Length < 0.01) {
            _isTransitioning = false;
            _targetCameraTarget = null;
        }
    }

    #endregion
}

public class TerrainVoxelGrid {
    private readonly double _cellSize = 1.5; // 1.5 meter bucket size
    private readonly Dictionary<(int X, int Y, int Z), Point3D> _grid = new();

    /// <summary>
    /// Clears all stored voxel cells.
    /// </summary>
    public void Clear() {
        _grid.Clear();
    }

    public bool TryAddPoint(Point3D point, out Point3D canonicalPoint) {
        int cellX = (int)Math.Floor(point.X / _cellSize);
        int cellY = (int)Math.Floor(point.Y / _cellSize);
        int cellZ = (int)Math.Floor(point.Z / _cellSize);

        var key = (cellX, cellY, cellZ);

        if (_grid.TryGetValue(key, out var existing)) {
            // Point already exists in this voxel space; ignore duplicates
            canonicalPoint = existing;
            return false;
        }

        _grid[key] = point;
        canonicalPoint = point;
        return true;
    }
}