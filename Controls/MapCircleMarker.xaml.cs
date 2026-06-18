using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MMONavigator.Models;
using Color = System.Windows.Media.Color;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;
using MMONavigator.Helpers;


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
        SizeChanged += (_, _) => UpdateHandlePositions();
        Loaded += OnLoaded;
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
    // Visual state builders
    // ═════════════════════════════════════════════════════════════════

    void RebuildBackgroundBrush() {
        var c = CircleBackgroundColor;
        c.A = (byte)Math.Round(Math.Clamp(CircleBackgroundOpacity, 0, 1) * 255);
        BackgroundEllipse.Fill = new SolidColorBrush(c);
    }

    void RebuildBorderBrush() {
        var c = CircleBorderColor;
        c.A = (byte)Math.Round(Math.Clamp(CircleBorderOpacity, 0, 1) * 255);
        BackgroundEllipse.Stroke = new SolidColorBrush(c);
        BackgroundEllipse.StrokeThickness = CircleBorderThickness;
    }
    
    void UpdateSwatchColors() {
        if (!IsLoaded) return;

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
    
    void UpdateInverseScale() {
        double s = 1.0 / Math.Max(0.01, ZoomLevel);
        ToolbarInverseScale.ScaleX = s;
        ToolbarInverseScale.ScaleY = s;
    }
    
    void OnLoaded(object sender, RoutedEventArgs e) {
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
            var window = Window.GetWindow(this);
            if (window != null) {
                var helper = new System.Windows.Interop.WindowInteropHelper(window);
                if (helper.Handle != IntPtr.Zero) {
                    NativeMethods.SetForegroundWindow(helper.Handle);
                }
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    void UpdateHandlePositions() {
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

    static void PlaceHandle(Thumb t, double x, double y) {
        Canvas.SetLeft(t, x);
        Canvas.SetTop(t, y);
    }
    
    void Border_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) return;

        var parent = ParentCanvas;
        if (parent == null) return;

        _isDragging       = true;
        _dragOriginScreen = e.GetPosition(parent);
        _dragOriginLeft   = Canvas.GetLeft(this);
        _dragOriginTop    = Canvas.GetTop(this);
    
        BackgroundEllipse.CaptureMouse();
        e.Handled = true;
    }

    void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        var parent = ParentCanvas;
        if (parent == null) return;

        _isDragging = true;
        _dragOriginScreen = e.GetPosition(parent);
        _dragOriginLeft = Canvas.GetLeft(this);
        _dragOriginTop = Canvas.GetTop(this);
        BackgroundEllipse.CaptureMouse();
        e.Handled = true;
    }

    void Border_MouseMove(object sender, MouseEventArgs e) {
        if (!_isDragging) return;
        var parent = ParentCanvas;
        if (parent == null) return;
        var pos = e.GetPosition(parent);
        Canvas.SetLeft(this, _dragOriginLeft + pos.X - _dragOriginScreen.X);
        Canvas.SetTop(this, _dragOriginTop + pos.Y - _dragOriginScreen.Y);
    }

    void Border_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        if (!_isDragging) return;
        _isDragging = false;
        BackgroundEllipse.ReleaseMouseCapture();
        e.Handled = true;
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

    void RotationHandle_DragStarted(object sender, DragStartedEventArgs e) { }

    void RotationHandle_DragDelta(object sender, DragDeltaEventArgs e) {
        var parent = ParentCanvas;
        if (parent == null) return;
        var center = TransformToAncestor(parent).Transform(new Point(ActualWidth / 2, ActualHeight / 2));
        var mouse = Mouse.GetPosition(parent);
        RotationAngle = Math.Atan2(mouse.X - center.X, -(mouse.Y - center.Y)) * (180.0 / Math.PI);
    }
    
    void BorderThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
        if (!IsLoaded) return;
        CircleBorderThickness = e.NewValue;
    }
    
    void PopulateColorPicker() {
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
            var captured = color;
            cell.MouseLeftButtonDown += (_, _) => ApplyPickedColor(captured);
            ColorSwatchPanel.Children.Add(cell);
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

    void StampButton_Click(object sender, RoutedEventArgs e) {
        ColorPickerPopup.IsOpen = false;
        var parent = ParentCanvas;
        if (parent == null) return;

        var mapImage = TargetImage ?? FindDescendantByName<System.Windows.Controls.Image>(parent, MapImageElementName) ?? FindDescendantByType<System.Windows.Controls.Image>(parent);
        if (mapImage == null || mapImage.Source is not BitmapSource bitmapSource) return;

        double myCanvasX = Canvas.GetLeft(this);
        double myCanvasY = Canvas.GetTop(this);

        double imageCanvasX = Canvas.GetLeft(mapImage);
        double imageCanvasY = Canvas.GetTop(mapImage);

        if (double.IsNaN(myCanvasX)) myCanvasX = this.TransformToAncestor(parent).Transform(new Point(0, 0)).X;
        if (double.IsNaN(myCanvasY)) myCanvasY = this.TransformToAncestor(parent).Transform(new Point(0, 0)).Y;
        if (double.IsNaN(imageCanvasX))
            imageCanvasX = mapImage.TransformToAncestor(parent).Transform(new Point(0, 0)).X;
        if (double.IsNaN(imageCanvasY))
            imageCanvasY = mapImage.TransformToAncestor(parent).Transform(new Point(0, 0)).Y;

        double relativeX = myCanvasX - imageCanvasX;
        double relativeY = myCanvasY - imageCanvasY;

        double pixelWidthRatio = (double)bitmapSource.PixelWidth / mapImage.ActualWidth;
        double pixelHeightRatio = (double)bitmapSource.PixelHeight / mapImage.ActualHeight;

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
            // IsBold = IsBold,
            // IsItalic = IsItalic,
            // IsUnderline = IsUnderline,
            TextColor = textColor,
            // TextOpacity = TextOpacity,
            // TextPadding = TextPadding * pixelWidthRatio,
            // TextAlignment = TextAlign,
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

    private static T? FindDescendantByName<T>(DependencyObject element, string name) where T : DependencyObject {
        int count = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < count; i++) {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child is T tElement && child is FrameworkElement fe && fe.Name == name) {
                return tElement;
            }

            var result = FindDescendantByName<T>(child, name);
            if (result != null) return result;
        }

        return null;
    }
    
    private static T? FindDescendantByType<T>(DependencyObject element) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child is T tElement)
            {
                return tElement;
            }

            var result = FindDescendantByType<T>(child);
            if (result != null) return result;
        }
        return null;
    }


    Canvas? ParentCanvas => VisualTreeHelper.GetParent(this) as Canvas;
    
    public System.Windows.Controls.Image? TargetImage
    {
        get => (System.Windows.Controls.Image?)GetValue(TargetImageProperty);
        set => SetValue(TargetImageProperty, value);
    }

    public string MapImageElementName { get; set; } = "MapImageElement";
}