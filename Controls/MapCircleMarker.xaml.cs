using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MMONavigator.Helpers;
using MMONavigator.Models;
using Color = System.Windows.Media.Color;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;

namespace MMONavigator.Controls;

public partial class MapCircleMarker : UserControl {
    // ─── layout constants ───────────────────────────────────────────
    const double HandleSize = 10;
    const double HandleHalf = HandleSize / 2;
    const double RotationConnectorLen = 30;
    const double RotationHandleRadius = 7;
    const double StampButtonSize = 24;
    const double MinCircleSize = 4;

    // ─── drag state ─────────────────────────────────────────────────
    bool _isDragging;
    Point _dragOriginScreen;
    double _dragOriginLeft, _dragOriginTop;

    // ─── toolbar color-picker state ──────────────────────────────────
    string _colorTarget = string.Empty;

    public event EventHandler<MapTextStampEventArgs>? Stamped;

    static readonly Color[] ColorPalette = {
        // Neutrals (row 1)
        Color.FromRgb(255, 255, 255), Color.FromRgb(192, 192, 192), Color.FromRgb(128, 128, 128),
        Color.FromRgb(80, 80, 80), Color.FromRgb(40, 40, 40), Color.FromRgb(0, 0, 0),
        // Reds (row 2)
        Color.FromRgb(255, 204, 204), Color.FromRgb(255, 102, 102), Color.FromRgb(255, 0, 0),
        Color.FromRgb(204, 0, 0), Color.FromRgb(128, 0, 0), Color.FromRgb(64, 0, 0),
        // Oranges / yellows (row 3)
        Color.FromRgb(255, 200, 100), Color.FromRgb(255, 165, 0), Color.FromRgb(255, 220, 0),
        Color.FromRgb(255, 255, 0), Color.FromRgb(200, 255, 0), Color.FromRgb(128, 255, 0),
        // Greens (row 4)
        Color.FromRgb(0, 255, 0), Color.FromRgb(0, 180, 0), Color.FromRgb(0, 100, 0),
        Color.FromRgb(0, 128, 128), Color.FromRgb(0, 210, 180), Color.FromRgb(0, 255, 200),
        // Blues / cyans (row 5)
        Color.FromRgb(0, 255, 255), Color.FromRgb(100, 180, 255), Color.FromRgb(65, 105, 225),
        Color.FromRgb(0, 0, 255), Color.FromRgb(0, 0, 160), Color.FromRgb(0, 0, 80),
        // Purples / pinks + Transparent (row 6)
        Color.FromRgb(180, 0, 255), Color.FromRgb(128, 0, 128), Color.FromRgb(255, 0, 255),
        Color.FromRgb(255, 105, 180), Color.FromRgb(255, 182, 193), Color.FromArgb(0, 0, 0, 0),
    };

    public MapCircleMarker() {
        InitializeComponent();
        SizeChanged -= OnControlSizeChanged;
        SizeChanged += OnControlSizeChanged;
        Loaded -= OnLoaded;
        Loaded += OnLoaded;
    }

    private void OnControlSizeChanged(object sender, SizeChangedEventArgs e) {
        UpdateHandlePositions();
    }
    // ═════════════════════════════════════════════════════════════════
    // Dependency Properties
    // ═════════════════════════════════════════════════════════════════

    public static readonly DependencyProperty TargetImageProperty =
        DependencyProperty.Register(
            nameof(TargetImage),
            typeof(System.Windows.Controls.Image),
            typeof(MapCircleMarker),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CircleBackgroundColorProperty =
        DependencyProperty.Register(nameof(CircleBackgroundColor), typeof(Color), typeof(MapCircleMarker),
            new PropertyMetadata(Color.FromRgb(0, 0, 0), OnCircleBrushInvalidated));

    public Color CircleBackgroundColor {
        get => (Color)GetValue(CircleBackgroundColorProperty);
        set => SetValue(CircleBackgroundColorProperty, value);
    }

    public static readonly DependencyProperty CircleBackgroundOpacityProperty =
        DependencyProperty.Register(nameof(CircleBackgroundOpacity), typeof(double), typeof(MapCircleMarker),
            new PropertyMetadata(0.65, OnCircleBrushInvalidated));

    public double CircleBackgroundOpacity {
        get => (double)GetValue(CircleBackgroundOpacityProperty);
        set => SetValue(CircleBackgroundOpacityProperty, value);
    }

    public static readonly DependencyProperty CircleBorderColorProperty =
        DependencyProperty.Register(nameof(CircleBorderColor), typeof(Color), typeof(MapCircleMarker),
            new PropertyMetadata(Color.FromArgb(0, 0, 0, 0), OnCircleBorderInvalidated));

    public Color CircleBorderColor {
        get => (Color)GetValue(CircleBorderColorProperty);
        set => SetValue(CircleBorderColorProperty, value);
    }

    public static readonly DependencyProperty CircleBorderThicknessProperty =
        DependencyProperty.Register(nameof(CircleBorderThickness), typeof(double), typeof(MapCircleMarker),
            new PropertyMetadata(0.0, OnCircleBorderInvalidated));

    public double CircleBorderThickness {
        get => (double)GetValue(CircleBorderThicknessProperty);
        set => SetValue(CircleBorderThicknessProperty, value);
    }

    public static readonly DependencyProperty CircleBorderOpacityProperty =
        DependencyProperty.Register(nameof(CircleBorderOpacity), typeof(double), typeof(MapCircleMarker),
            new PropertyMetadata(1.0, OnCircleBorderInvalidated));

    public double CircleBorderOpacity {
        get => (double)GetValue(CircleBorderOpacityProperty);
        set => SetValue(CircleBorderOpacityProperty, value);
    }

    public static readonly DependencyProperty RotationAngleProperty =
        DependencyProperty.Register(nameof(RotationAngle), typeof(double), typeof(MapCircleMarker),
            new PropertyMetadata(0.0, OnRotationAngleChanged));

    public double RotationAngle {
        get => (double)GetValue(RotationAngleProperty);
        set => SetValue(RotationAngleProperty, value);
    }

    public static readonly DependencyProperty ZoomLevelProperty =
        DependencyProperty.Register(nameof(ZoomLevel), typeof(double), typeof(MapCircleMarker),
            new PropertyMetadata(1.0, OnZoomLevelChanged));

    public double ZoomLevel {
        get => (double)GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    // ═════════════════════════════════════════════════════════════════
    // Property Changed Callbacks
    // ═════════════════════════════════════════════════════════════════

    static void OnCircleBrushInvalidated(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is MapCircleMarker c && c.IsLoaded) {
            c.RebuildBackgroundBrush();
            c.UpdateSwatchColors();
        }
    }

    static void OnCircleBorderInvalidated(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is MapCircleMarker c && c.IsLoaded) {
            c.RebuildBorderBrush();
            c.UpdateSwatchColors();
        }
    }

    static void OnRotationAngleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is MapCircleMarker c) c.MainRotation.Angle = (double)e.NewValue;
    }

    static void OnZoomLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is MapCircleMarker c) c.UpdateInverseScale();
    }

    // ═════════════════════════════════════════════════════════════════
    // Visual State Builders
    // ═════════════════════════════════════════════════════════════════

    void RebuildBackgroundBrush() {
        try {
            var c = CircleBackgroundColor;
            c.A = (byte)Math.Round(Math.Clamp(CircleBackgroundOpacity, 0, 1) * 255);
            BackgroundEllipse.Fill = new SolidColorBrush(c);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error rebuilding background brush in MapCircleMarker.");
        }
    }

    void RebuildBorderBrush() {
        try {
            var c = CircleBorderColor;
            c.A = (byte)Math.Round(Math.Clamp(CircleBorderOpacity, 0, 1) * 255);
            BackgroundEllipse.Stroke = new SolidColorBrush(c);
            BackgroundEllipse.StrokeThickness = CircleBorderThickness;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error rebuilding border brush in MapCircleMarker.");
        }
    }

    void UpdateSwatchColors() {
        if (!IsLoaded) return;

        try {
            var bgOpaque = CircleBackgroundColor;
            CircleBgColorSwatch.Background = bgOpaque.A == 0
                ? CreateCheckerBrush()
                : new SolidColorBrush(bgOpaque);

            if (bgOpaque.A == 0) {
                CircleOpacitySlider.Value = 0;
            }

            BorderColorSwatch.Background = CircleBorderColor.A == 0
                ? CreateCheckerBrush()
                : new SolidColorBrush(CircleBorderColor);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error updating swatch colors in MapCircleMarker.");
        }
    }

    void UpdateInverseScale() {
        try {
            double s = 1.0 / Math.Max(0.01, ZoomLevel);
            ToolbarInverseScale.ScaleX = s;
            ToolbarInverseScale.ScaleY = s;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error updating inverse scale in MapCircleMarker.");
        }
    }

    void OnLoaded(object sender, RoutedEventArgs e) {
        try {
            if (double.IsNaN(Width)) Width = ActualWidth;
            if (double.IsNaN(Height)) Height = ActualHeight;

            PopulateColorPicker();
            RebuildBackgroundBrush();
            RebuildBorderBrush();
            UpdateSwatchColors();
            UpdateInverseScale();
            UpdateHandlePositions();

            BorderThicknessSlider.Value = CircleBorderThickness;
            BorderOpacitySlider.Value = CircleBorderOpacity;

            Dispatcher.BeginInvoke(new Action(() => {
                try {
                    var window = Window.GetWindow(this);
                    if (window != null) {
                        var helper = new System.Windows.Interop.WindowInteropHelper(window);
                        if (helper.Handle != IntPtr.Zero) {
                            NativeMethods.SetForegroundWindow(helper.Handle);
                        }
                    }
                }
                catch (Exception ex) {
                    Log.Warning(ex, "Error setting foreground window in MapCircleMarker OnLoaded callback.");
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling OnLoaded in MapCircleMarker.");
        }
    }

    void UpdateHandlePositions() {
        try {
            double w = ActualWidth;
            double h = ActualHeight;

            PlaceHandle(NwHandle, -HandleHalf, -HandleHalf);
            PlaceHandle(NeHandle, w - HandleHalf, -HandleHalf);
            PlaceHandle(SeHandle, w - HandleHalf, h - HandleHalf);
            PlaceHandle(SwHandle, -HandleHalf, h - HandleHalf);

            RotationConnectorLine.X1 = w / 2;
            RotationConnectorLine.Y1 = 0;
            RotationConnectorLine.X2 = w / 2;
            RotationConnectorLine.Y2 = -RotationConnectorLen;

            Canvas.SetLeft(RotationHandle, w / 2 - RotationHandleRadius);
            Canvas.SetTop(RotationHandle, -RotationConnectorLen - RotationHandleRadius * 2);

            Canvas.SetLeft(StampButton, w + 6);
            Canvas.SetTop(StampButton, h / 2 - StampButtonSize / 2);

            Canvas.SetLeft(ToolbarContainer, 0);
            Canvas.SetTop(ToolbarContainer, h + 6);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error updating handle positions in MapCircleMarker.");
        }
    }

    static void PlaceHandle(Thumb t, double x, double y) {
        Canvas.SetLeft(t, x);
        Canvas.SetTop(t, y);
    }

    void Border_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (e.ClickCount == 2) return;

        var parent = ParentCanvas;
        if (parent == null) return;

        try {
            _isDragging = true;
            _dragOriginScreen = e.GetPosition(parent);
            _dragOriginLeft = Canvas.GetLeft(this);
            _dragOriginTop = Canvas.GetTop(this);

            BackgroundEllipse.CaptureMouse();
            e.Handled = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling Border_PreviewMouseLeftButtonDown.");
        }
    }

    void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        var parent = ParentCanvas;
        if (parent == null) return;

        try {
            _isDragging = true;
            _dragOriginScreen = e.GetPosition(parent);
            _dragOriginLeft = Canvas.GetLeft(this);
            _dragOriginTop = Canvas.GetTop(this);
            BackgroundEllipse.CaptureMouse();
            e.Handled = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling Border_MouseLeftButtonDown.");
        }
    }

    void Border_MouseMove(object sender, MouseEventArgs e) {
        if (!_isDragging) return;
        var parent = ParentCanvas;
        if (parent == null) return;

        try {
            var pos = e.GetPosition(parent);
            Canvas.SetLeft(this, _dragOriginLeft + pos.X - _dragOriginScreen.X);
            Canvas.SetTop(this, _dragOriginTop + pos.Y - _dragOriginScreen.Y);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling Border_MouseMove.");
        }
    }

    void Border_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        if (!_isDragging) return;

        try {
            _isDragging = false;
            BackgroundEllipse.ReleaseMouseCapture();
            e.Handled = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling Border_MouseLeftButtonUp.");
        }
    }

    void NwHandle_DragDelta(object sender, DragDeltaEventArgs e) => ApplyResize(e.HorizontalChange, e.VerticalChange,
        anchorRight: true, anchorBottom: true);

    void NHandle_DragDelta(object sender, DragDeltaEventArgs e) =>
        ApplyResize(0, e.VerticalChange, anchorRight: false, anchorBottom: true);

    void NeHandle_DragDelta(object sender, DragDeltaEventArgs e) => ApplyResize(e.HorizontalChange, e.VerticalChange,
        anchorRight: false, anchorBottom: true);

    void EHandle_DragDelta(object sender, DragDeltaEventArgs e) =>
        ApplyResize(e.HorizontalChange, 0, anchorRight: false, anchorBottom: false);

    void SeHandle_DragDelta(object sender, DragDeltaEventArgs e) => ApplyResize(e.HorizontalChange, e.VerticalChange,
        anchorRight: false, anchorBottom: false);

    void SHandle_DragDelta(object sender, DragDeltaEventArgs e) =>
        ApplyResize(0, e.VerticalChange, anchorRight: false, anchorBottom: false);

    void SwHandle_DragDelta(object sender, DragDeltaEventArgs e) => ApplyResize(e.HorizontalChange, e.VerticalChange,
        anchorRight: true, anchorBottom: false);

    void WHandle_DragDelta(object sender, DragDeltaEventArgs e) =>
        ApplyResize(e.HorizontalChange, 0, anchorRight: true, anchorBottom: false);

    void ApplyResize(double dx, double dy, bool anchorRight, bool anchorBottom) {
        try {
            if (anchorRight) {
                double newW = Math.Max(MinCircleSize, Width - dx);
                double usedDx = Width - newW;
                Width = newW;
                Canvas.SetLeft(this, Canvas.GetLeft(this) + usedDx);
            }
            else if (dx != 0) Width = Math.Max(MinCircleSize, Width + dx);

            if (anchorBottom) {
                double newH = Math.Max(MinCircleSize, Height - dy);
                double usedDy = Height - newH;
                Height = newH;
                Canvas.SetTop(this, Canvas.GetTop(this) + usedDy);
            }
            else if (dy != 0) Height = Math.Max(MinCircleSize, Height + dy);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error applying resize transform in MapCircleMarker.");
        }
    }

    void RotationHandle_DragStarted(object sender, DragStartedEventArgs e) { }

    void RotationHandle_DragDelta(object sender, DragDeltaEventArgs e) {
        var parent = ParentCanvas;
        if (parent == null) return;

        try {
            var center = TransformToAncestor(parent).Transform(new Point(ActualWidth / 2, ActualHeight / 2));
            var mouse = Mouse.GetPosition(parent);
            RotationAngle = Math.Atan2(mouse.X - center.X, -(mouse.Y - center.Y)) * (180.0 / Math.PI);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error updating rotation angle in RotationHandle_DragDelta.");
        }
    }

    void BorderThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
        if (!IsLoaded) return;
        CircleBorderThickness = e.NewValue;
    }

    void PopulateColorPicker() {
        try {
            ColorSwatchPanel.Children.Clear();
            foreach (var color in ColorPalette) {
                bool isTransparent = color.A == 0;
                var cell = new Border {
                    Width = 22,
                    Height = 22,
                    Margin = new Thickness(1),
                    CornerRadius = new CornerRadius(2),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                    BorderThickness = new Thickness(1),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Background = isTransparent
                        ? CreateCheckerBrush()
                        : new SolidColorBrush(color),
                    ToolTip = isTransparent ? "Transparent" : $"#{color.R:X2}{color.G:X2}{color.B:X2}",
                };
                // Inside your loop building the color palette:
                cell.Tag = color; // Store the color directly on the UI element
                cell.MouseLeftButtonDown -= OnColorCellClicked; // Prevent duplicates
                cell.MouseLeftButtonDown += OnColorCellClicked;
                ColorSwatchPanel.Children.Add(cell);
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error populating color picker in MapCircleMarker.");
        }
    }
    
    // Single shared handler outside the loop:
    private void OnColorCellClicked(object sender, MouseButtonEventArgs e) {
        if (sender is FrameworkElement element && element.Tag is Color pickedColor) {
            ApplyPickedColor(pickedColor);
        }
    }
    
    static DrawingBrush CreateCheckerBrush() {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            null,
            new RectangleGeometry(new Rect(0, 0, 10, 10))));
        drawing.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromRgb(120, 120, 120)),
            null,
            new GeometryGroup {
                Children = {
                    new RectangleGeometry(new Rect(0, 0, 5, 5)),
                    new RectangleGeometry(new Rect(5, 5, 5, 5)),
                }
            }));
        return new DrawingBrush(drawing) {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 10, 10),
            ViewportUnits = BrushMappingMode.Absolute,
        };
    }

    void CircleBgColorSwatch_Click(object sender, MouseButtonEventArgs e) {
        _colorTarget = "CircleBg";
        ColorPickerPopup.IsOpen = true;
    }

    void BorderColorSwatch_Click(object sender, MouseButtonEventArgs e) {
        _colorTarget = "Border";
        ColorPickerPopup.IsOpen = true;
    }

    void ApplyPickedColor(Color color) {
        try {
            ColorPickerPopup.IsOpen = false;

            switch (_colorTarget) {
                case "CircleBg":
                    CircleBackgroundColor = color;
                    break;

                case "Border":
                    CircleBorderColor = color;
                    if (CircleBorderThickness < 0.5) {
                        CircleBorderThickness = 1.0;
                        BorderThicknessSlider.Value = 1.0;
                    }
                    break;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error applying picked color in MapCircleMarker.");
        }
    }

    void StampButton_Click(object sender, RoutedEventArgs e) {
        try {
            ColorPickerPopup.IsOpen = false;
            var parent = ParentCanvas;
            if (parent == null) return;

            var mapImage = TargetImage ?? FindDescendantByName<System.Windows.Controls.Image>(parent, MapImageElementName) ?? FindDescendantByType<System.Windows.Controls.Image>(parent);
            if (mapImage == null || mapImage.Source is not BitmapSource bitmapSource) {
                Log.Warning("StampButton_Click: Unable to locate valid target map image or BitmapSource.");
                return;
            }

            double myCanvasX = Canvas.GetLeft(this);
            double myCanvasY = Canvas.GetTop(this);

            double imageCanvasX = Canvas.GetLeft(mapImage);
            double imageCanvasY = Canvas.GetTop(mapImage);

            if (double.IsNaN(myCanvasX)) myCanvasX = TransformToAncestor(parent).Transform(new Point(0, 0)).X;
            if (double.IsNaN(myCanvasY)) myCanvasY = TransformToAncestor(parent).Transform(new Point(0, 0)).Y;
            if (double.IsNaN(imageCanvasX))
                imageCanvasX = mapImage.TransformToAncestor(parent).Transform(new Point(0, 0)).X;
            if (double.IsNaN(imageCanvasY))
                imageCanvasY = mapImage.TransformToAncestor(parent).Transform(new Point(0, 0)).Y;

            double relativeX = myCanvasX - imageCanvasX;
            double relativeY = myCanvasY - imageCanvasY;

            // Prevent division-by-zero if ActualWidth/ActualHeight haven't rendered yet
            double actualImageW = mapImage.ActualWidth > 0 ? mapImage.ActualWidth : 1.0;
            double actualImageH = mapImage.ActualHeight > 0 ? mapImage.ActualHeight : 1.0;

            double pixelWidthRatio = (double)bitmapSource.PixelWidth / actualImageW;
            double pixelHeightRatio = (double)bitmapSource.PixelHeight / actualImageH;

            double burnX = relativeX * pixelWidthRatio;
            double burnY = relativeY * pixelHeightRatio;

            double burnWidth = ActualWidth * pixelWidthRatio;
            double burnHeight = ActualHeight * pixelHeightRatio;
            double burnFontSize = FontSize * pixelWidthRatio;

            var textColor = (Foreground as SolidColorBrush)?.Color ?? Color.FromRgb(255, 255, 255);

            var args = new MapTextStampEventArgs {
                Text = string.Empty,
                X = burnX,
                Y = burnY,
                Width = burnWidth,
                Height = burnHeight,
                RotationAngle = RotationAngle,
                FontFamilyName = FontFamily?.Source ?? "Segoe UI",
                FontSize = burnFontSize,
                TextColor = textColor,
                BackgroundColor = CircleBackgroundColor,
                BackgroundOpacity = CircleBackgroundOpacity,
                BoxBorderColor = CircleBorderColor,
                BoxBorderOpacity = CircleBorderOpacity,
                BoxBorderThickness = CircleBorderThickness * pixelWidthRatio,
                CornerRadius = new CornerRadius(0),
                IsEllipse = true,
                EllipseMargin = 15 * pixelWidthRatio
            };

            Stamped?.Invoke(this, args);
            parent.Children.Remove(this);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling StampButton_Click in MapCircleMarker.");
        }
    }

    private static T? FindDescendantByName<T>(DependencyObject element, string name) where T : DependencyObject {
        if (element == null) return null;

        try {
            int count = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < count; i++) {
                var child = VisualTreeHelper.GetChild(element, i);
                if (child is T tElement && child is FrameworkElement fe && fe.Name == name) {
                    return tElement;
                }

                var result = FindDescendantByName<T>(child, name);
                if (result != null) return result;
            }
        }
        catch (Exception ex) {
            Log.Debug(ex, "Error in FindDescendantByName while searching for '{Name}'.", name);
        }

        return null;
    }

    private static T? FindDescendantByType<T>(DependencyObject element) where T : DependencyObject {
        if (element == null) return null;

        try {
            int count = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < count; i++) {
                var child = VisualTreeHelper.GetChild(element, i);
                if (child is T tElement) {
                    return tElement;
                }

                var result = FindDescendantByType<T>(child);
                if (result != null) return result;
            }
        }
        catch (Exception ex) {
            Log.Debug(ex, "Error in FindDescendantByType.");
        }

        return null;
    }

    Canvas? ParentCanvas => VisualTreeHelper.GetParent(this) as Canvas;

    public System.Windows.Controls.Image? TargetImage {
        get => (System.Windows.Controls.Image?)GetValue(TargetImageProperty);
        set => SetValue(TargetImageProperty, value);
    }

    public string MapImageElementName { get; set; } = "MapImageElement";
}