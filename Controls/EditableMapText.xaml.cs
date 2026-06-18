using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MMONavigator.Models;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;
using FontFamily = System.Windows.Media.FontFamily;
using MMONavigator.Helpers;

namespace MMONavigator.Controls;

public enum EditableMapTextState {
    Edit,
    Display
}

public partial class EditableMapText : UserControl {
    // ─── layout constants ───────────────────────────────────────────
    const double HandleSize = 10;
    const double HandleHalf = HandleSize / 2;
    const double RotationConnectorLen = 30;
    const double RotationHandleRadius = 7;
    const double StampButtonSize = 24;
    const double MinBoxSize = 40;

    // ─── drag state ─────────────────────────────────────────────────
    bool _isDragging;
    Point _dragOriginScreen;
    double _dragOriginLeft, _dragOriginTop;

    // ─── toolbar color-picker state ──────────────────────────────────
    string _colorTarget = string.Empty;

    EditableMapTextState _state = EditableMapTextState.Edit;

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

    static readonly string[] FontNames = {
        "Segoe UI", "Arial", "Calibri", "Cambria", "Century Gothic", "Comic Sans MS",
        "Consolas", "Courier New", "Franklin Gothic Medium", "Georgia",
        "Impact", "Lucida Console", "Lucida Sans Unicode", "Microsoft Sans Serif",
        "Palatino Linotype", "Tahoma", "Times New Roman", "Trebuchet MS", "Verdana",
    };

    public EditableMapText() {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateHandlePositions();
        Loaded += OnLoaded;
    }

    // ═════════════════════════════════════════════════════════════════
    // Dependency Properties
    // ═════════════════════════════════════════════════════════════════

    public static readonly DependencyProperty InitialTextProperty =
        DependencyProperty.Register(nameof(InitialText), typeof(string), typeof(EditableMapText),
            new PropertyMetadata(string.Empty));

    public string? InitialText {
        get => (string?)GetValue(InitialTextProperty);
        set => SetValue(InitialTextProperty, value);
    }

    public static readonly DependencyProperty TargetImageProperty =
        DependencyProperty.Register(
            nameof(TargetImage), 
            typeof(System.Windows.Controls.Image), 
            typeof(EditableMapText), 
            new PropertyMetadata(null));
    
    public static readonly DependencyProperty TextAlignProperty =
        DependencyProperty.Register(nameof(TextAlign), typeof(TextAlignment), typeof(EditableMapText),
            new PropertyMetadata(TextAlignment.Left, OnTextAlignChanged));

    public TextAlignment TextAlign {
        get => (TextAlignment)GetValue(TextAlignProperty);
        set => SetValue(TextAlignProperty, value);
    }

    public static readonly DependencyProperty TextOpacityProperty =
        DependencyProperty.Register(nameof(TextOpacity), typeof(double), typeof(EditableMapText),
            new PropertyMetadata(1.0, OnTextOpacityChanged)); // <-- Add callback

    public double TextOpacity {
        get => (double)GetValue(TextOpacityProperty);
        set => SetValue(TextOpacityProperty, value);
    }

    public static readonly DependencyProperty IsBoldProperty =
        DependencyProperty.Register(nameof(IsBold), typeof(bool), typeof(EditableMapText),
            new PropertyMetadata(false, OnFontDecorationChanged));

    public bool IsBold {
        get => (bool)GetValue(IsBoldProperty);
        set => SetValue(IsBoldProperty, value);
    }

    public static readonly DependencyProperty IsItalicProperty =
        DependencyProperty.Register(nameof(IsItalic), typeof(bool), typeof(EditableMapText),
            new PropertyMetadata(false, OnFontDecorationChanged));

    public bool IsItalic {
        get => (bool)GetValue(IsItalicProperty);
        set => SetValue(IsItalicProperty, value);
    }

    public static readonly DependencyProperty IsUnderlineProperty =
        DependencyProperty.Register(nameof(IsUnderline), typeof(bool), typeof(EditableMapText),
            new PropertyMetadata(false, OnFontDecorationChanged));

    public bool IsUnderline {
        get => (bool)GetValue(IsUnderlineProperty);
        set => SetValue(IsUnderlineProperty, value);
    }

    public static readonly DependencyProperty TextPaddingProperty =
        DependencyProperty.Register(nameof(TextPadding), typeof(double), typeof(EditableMapText),
            new PropertyMetadata(4.0, OnFontDecorationChanged));

    public double TextPadding {
        get => (double)GetValue(TextPaddingProperty);
        set => SetValue(TextPaddingProperty, value);
    }

    public static readonly DependencyProperty BoxBackgroundColorProperty =
        DependencyProperty.Register(nameof(BoxBackgroundColor), typeof(Color), typeof(EditableMapText),
            new PropertyMetadata(Color.FromRgb(0, 0, 0), OnBoxBrushInvalidated));

    public Color BoxBackgroundColor {
        get => (Color)GetValue(BoxBackgroundColorProperty);
        set => SetValue(BoxBackgroundColorProperty, value);
    }

    public static readonly DependencyProperty BoxBackgroundOpacityProperty =
        DependencyProperty.Register(nameof(BoxBackgroundOpacity), typeof(double), typeof(EditableMapText),
            new PropertyMetadata(0.65, OnBoxBrushInvalidated));

    public double BoxBackgroundOpacity {
        get => (double)GetValue(BoxBackgroundOpacityProperty);
        set => SetValue(BoxBackgroundOpacityProperty, value);
    }

    public static readonly DependencyProperty BoxBorderColorProperty =
        DependencyProperty.Register(nameof(BoxBorderColor), typeof(Color), typeof(EditableMapText),
            new PropertyMetadata(Color.FromArgb(0, 0, 0, 0), OnBoxBorderInvalidated));

    public Color BoxBorderColor {
        get => (Color)GetValue(BoxBorderColorProperty);
        set => SetValue(BoxBorderColorProperty, value);
    }

    public static readonly DependencyProperty BoxBorderThicknessProperty =
        DependencyProperty.Register(nameof(BoxBorderThickness), typeof(double), typeof(EditableMapText),
            new PropertyMetadata(0.0, OnBoxBorderInvalidated));

    public double BoxBorderThickness {
        get => (double)GetValue(BoxBorderThicknessProperty);
        set => SetValue(BoxBorderThicknessProperty, value);
    }

    public static readonly DependencyProperty BoxBorderOpacityProperty =
        DependencyProperty.Register(nameof(BoxBorderOpacity), typeof(double), typeof(EditableMapText),
            new PropertyMetadata(1.0, OnBoxBorderInvalidated));

    public double BoxBorderOpacity {
        get => (double)GetValue(BoxBorderOpacityProperty);
        set => SetValue(BoxBorderOpacityProperty, value);
    }

    public static readonly DependencyProperty BoxCornerRadiusProperty =
        DependencyProperty.Register(nameof(BoxCornerRadius), typeof(CornerRadius), typeof(EditableMapText),
            new PropertyMetadata(new CornerRadius(4)));

    public CornerRadius BoxCornerRadius {
        get => (CornerRadius)GetValue(BoxCornerRadiusProperty);
        set => SetValue(BoxCornerRadiusProperty, value);
    }

    public static readonly DependencyProperty RotationAngleProperty =
        DependencyProperty.Register(nameof(RotationAngle), typeof(double), typeof(EditableMapText),
            new PropertyMetadata(0.0, OnRotationAngleChanged));

    public double RotationAngle {
        get => (double)GetValue(RotationAngleProperty);
        set => SetValue(RotationAngleProperty, value);
    }

    public static readonly DependencyProperty ZoomLevelProperty =
        DependencyProperty.Register(nameof(ZoomLevel), typeof(double), typeof(EditableMapText),
            new PropertyMetadata(1.0, OnZoomLevelChanged));

    public double ZoomLevel {
        get => (double)GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    // ═════════════════════════════════════════════════════════════════
    // Property Changed Callbacks
    // ═════════════════════════════════════════════════════════════════

    static void OnBoxBrushInvalidated(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is EditableMapText c && c.IsLoaded) {
            c.RebuildBackgroundBrush();
            c.UpdateSwatchColors();
        }
    }

    static void OnBoxBorderInvalidated(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is EditableMapText c && c.IsLoaded) {
            c.RebuildBorderBrush();
            c.UpdateSwatchColors();
        }
    }

    static void OnFontDecorationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is EditableMapText c && c.IsLoaded) c.ApplyFontProperties();
    }

    static void OnTextAlignChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is EditableMapText c && c.IsLoaded) c.UpdateAlignmentButtons();
    }

    static void OnTextOpacityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is EditableMapText c && c.IsLoaded) {
            // Forces the color picker swatches to update their layout transparency displays
            c.UpdateSwatchColors(); 
        }
    }
    
    static void OnRotationAngleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is EditableMapText c) c.MainRotation.Angle = (double)e.NewValue;
    }

    static void OnZoomLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is EditableMapText c) c.UpdateInverseScale();
    }

    // ═════════════════════════════════════════════════════════════════
    // Visual state builders
    // ═════════════════════════════════════════════════════════════════

    void RebuildBackgroundBrush() {
        var c = BoxBackgroundColor;
        c.A = (byte)Math.Round(Math.Clamp(BoxBackgroundOpacity, 0, 1) * 255);
        BackgroundBorder.Background = new SolidColorBrush(c);
    }

    void RebuildBorderBrush() {
        var c = BoxBorderColor;
        c.A = (byte)Math.Round(Math.Clamp(BoxBorderOpacity, 0, 1) * 255);
        BackgroundBorder.BorderBrush = new SolidColorBrush(c);
        BackgroundBorder.BorderThickness = new Thickness(BoxBorderThickness);
    }

    void ApplyFontProperties() {
        FontWeight = IsBold ? FontWeights.Bold : FontWeights.Normal;
        FontStyle = IsItalic ? FontStyles.Italic : FontStyles.Normal;

        var pad = new Thickness(TextPadding);
        var deco = IsUnderline ? TextDecorations.Underline : null;

        EditBox.Padding = pad;
        EditBox.TextDecorations = deco;
        DisplayBlock.Padding = pad;
        DisplayBlock.TextDecorations = deco;
    }

    void UpdateSwatchColors() {
        if (!IsLoaded) return;
        var textColor = (Foreground as SolidColorBrush)?.Color ?? Color.FromRgb(255, 255, 255);
        TextColorSwatch.Background = new SolidColorBrush(textColor);

        var bgOpaque = BoxBackgroundColor;
        //bgOpaque.A = 255;
        BoxBgColorSwatch.Background = new SolidColorBrush(bgOpaque);

        BoxBgColorSwatch.Background = bgOpaque.A == 0
            ? CreateCheckerBrush()
            : new SolidColorBrush(bgOpaque);

        if (bgOpaque.A == 0) {
            BoxOpacitySlider.Value = 0;
        }

        var borderColor = BoxBorderColor;
        borderColor.A = Math.Max((byte)180, borderColor.A);
        // BorderColorSwatch.Background = new SolidColorBrush(BoxBorderColor.A == 0
        //     ? Color.FromRgb(80, 80, 80)
        //     : BoxBorderColor);

        BorderColorSwatch.Background = BoxBorderColor.A == 0
            ? CreateCheckerBrush()
            : new SolidColorBrush(BoxBorderColor);
    }

    void UpdateAlignmentButtons() {
        if (!IsLoaded) return;
        AlignLeftBtn.IsChecked = TextAlign == TextAlignment.Left;
        AlignCenterBtn.IsChecked = TextAlign == TextAlignment.Center;
        AlignRightBtn.IsChecked = TextAlign == TextAlignment.Right;
        AlignJustifyBtn.IsChecked = TextAlign == TextAlignment.Justify;
    }

    void UpdateInverseScale() {
        double s = 1.0 / Math.Max(0.01, ZoomLevel);
        ToolbarInverseScale.ScaleX = s;
        ToolbarInverseScale.ScaleY = s;
    }

    // ═════════════════════════════════════════════════════════════════
    // State Focus Strategy (Fix Implemented Here)
    // ═════════════════════════════════════════════════════════════════

    public EditableMapTextState State {
        get => _state;
        set {
            _state = value;
            if (value == EditableMapTextState.Edit) {
                DisplayBlock.Visibility = Visibility.Collapsed;
                EditBox.Visibility = Visibility.Visible;
                ToolbarContainer.Visibility = Visibility.Visible;

                // Force focus safely after layout is confirmed stable
                ForceFocusOnTextBox();
            }
            else {
                DisplayBlock.Text = EditBox.Text;
                EditBox.Visibility = Visibility.Collapsed;
                DisplayBlock.Visibility = Visibility.Visible;
                ToolbarContainer.Visibility = Visibility.Collapsed;
                ColorPickerPopup.IsOpen = false;
            }
        }
    }

    private void ForceFocusOnTextBox() {
        // DispatcherPriority.Loaded executes at the absolute bottom of the initialization sequence,
        // right AFTER LayoutUpdated completes its operations.
        Dispatcher.BeginInvoke(new Action(() => {
            // 1. Force the parent window to active state so the IDE releases focus hooks
            var window = Window.GetWindow(this);
            if (window != null && !window.IsActive) {
                window.Activate();
            }

            // 2. Set structural logical focus scope
            var scope = FocusManager.GetFocusScope(this);
            FocusManager.SetFocusedElement(scope, EditBox);

            // 3. Command physical system keyboard focus
            EditBox.Focus();
            bool success = Keyboard.Focus(EditBox) == EditBox;

            // 4. Fallback safeguard: If focus failed because layout was still transitioning,
            // retry once on the next tick loop.
            if (!success) {
                Dispatcher.BeginInvoke(new Action(() => {
                    EditBox.Focus();
                    Keyboard.Focus(EditBox);
                    EditBox.SelectAll();
                }), System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
            else {
                EditBox.SelectAll();
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded); // <-- Use Loaded instead of Background
    }

    // ═════════════════════════════════════════════════════════════════
    // Loaded
    // ═════════════════════════════════════════════════════════════════

    void OnLoaded(object sender, RoutedEventArgs e) {
        if (double.IsNaN(Width)) Width = ActualWidth;
        if (double.IsNaN(Height)) Height = ActualHeight;

        if (!string.IsNullOrEmpty(InitialText))
            EditBox.Text = InitialText;

        foreach (var name in FontNames)
            FontFamilyCombo.Items.Add(name);

        var currentFont = FontFamily?.Source ?? "Segoe UI";
        var fi = FontNames.ToList().FindIndex(n =>
            string.Equals(n, currentFont, StringComparison.OrdinalIgnoreCase));
        FontFamilyCombo.SelectedIndex = fi >= 0 ? fi : 0;

        FontSizeBox.Text = ((int)FontSize).ToString();

        // Use Tunneling Events directly to ensure text boxes grab clicks before the parent border can look at them
        FontSizeBox.PreviewMouseLeftButtonDown += TextBox_PreviewMouseLeftButtonDown;
        EditBox.PreviewMouseLeftButtonDown += TextBox_PreviewMouseLeftButtonDown;

        PopulateColorPicker();
        RebuildBackgroundBrush();
        RebuildBorderBrush();
        ApplyFontProperties();
        UpdateSwatchColors();
        UpdateAlignmentButtons();
        UpdateInverseScale();
        UpdateHandlePositions();

        TextPaddingSlider.Value = TextPadding;
        BorderThicknessSlider.Value = BoxBorderThickness;
        CornerRadiusSlider.Value = BoxCornerRadius.TopLeft;

        Dispatcher.BeginInvoke(new Action(() => {
            // Force the parent window to capture foreground typing layouts
            var window = Window.GetWindow(this);
            if (window != null) {
                var helper = new System.Windows.Interop.WindowInteropHelper(window);
                if (helper.Handle != IntPtr.Zero) {
                    // Force windows to prioritize your app over the game thread during typing
                    NativeMethods.SetForegroundWindow(helper.Handle);
                }
            }

            var scope = FocusManager.GetFocusScope(this);
            FocusManager.SetFocusedElement(scope, EditBox);
            EditBox.Focus();
            Keyboard.Focus(EditBox);
            EditBox.SelectAll();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// Prevents parent canvas elements from stealing mouse click events and 
    /// explicitly forces input focus safely onto text fields.
    /// </summary>
    void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (sender is System.Windows.Controls.TextBox textBox) {
            // 1. Forcefully break any rogue mouse captures sitting on the parent canvas or image element
            if (Mouse.Captured != null) {
                Mouse.Capture(null);
            }

            if (BackgroundBorder.IsMouseCaptured) {
                BackgroundBorder.ReleaseMouseCapture();
            }

            // 2. Pass focus cleanly to the clicked input box
            textBox.Focus();
            Keyboard.Focus(textBox);

            // If this is the first click inside the box, select the text fields automatically
            if (textBox == EditBox && string.Equals(EditBox.Text, "New Text")) {
                EditBox.SelectAll();
                e.Handled = true; // Eat the click ONLY when auto-selecting everything
                return;
            }
            // 3. Mark the event as handled so the parent canvas doesn't see a 
            // bubbling click phase and try to process it as a map navigation/drag action!
            //e.Handled = true;
            // CRUCIAL: Do NOT set e.Handled = true here for normal clicks!
            // By leaving e.Handled alone, the mouse click is allowed to natively pass 
            // straight into the TextBox's text layout engine. This allows WPF to 
            // map your pixel coordinate click directly between 'a' and 'b'!
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // Handle Layout
    // ═════════════════════════════════════════════════════════════════

    void UpdateHandlePositions() {
        double w = ActualWidth;
        double h = ActualHeight;

        PlaceHandle(NwHandle, -HandleHalf, -HandleHalf);
        PlaceHandle(NHandle, w / 2 - HandleHalf, -HandleHalf);
        PlaceHandle(NeHandle, w - HandleHalf, -HandleHalf);
        PlaceHandle(EHandle, w - HandleHalf, h / 2 - HandleHalf);
        PlaceHandle(SeHandle, w - HandleHalf, h - HandleHalf);
        PlaceHandle(SHandle, w / 2 - HandleHalf, h - HandleHalf);
        PlaceHandle(SwHandle, -HandleHalf, h - HandleHalf);
        PlaceHandle(WHandle, -HandleHalf, h / 2 - HandleHalf);

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

    // ═════════════════════════════════════════════════════════════════
    // Drag / Move
    // ═════════════════════════════════════════════════════════════════
    
    void Border_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Double click handles switching to Edit mode natively; let it pass
        if (e.ClickCount == 2) return;

        // If we are in Edit mode and the user explicitly clicked the scrollbar or text caret handling area,
        // let the TextBox keep native control.
        if (_state == EditableMapTextState.Edit && e.OriginalSource is ScrollViewer)
        {
            return;
        }

        // CRUCIAL: If the user clicks down on the border background while the crosshair is showing,
        // initiate the drag sequence immediately and bypass TextBox interception.
        var parent = ParentCanvas;
        if (parent == null) return;

        _isDragging       = true;
        _dragOriginScreen = e.GetPosition(parent);
        _dragOriginLeft   = Canvas.GetLeft(this);
        _dragOriginTop    = Canvas.GetTop(this);
    
        // Explicitly lock mouse tracking to this border so it tracks even outside the window bounds
        BackgroundBorder.CaptureMouse();
    
        // Mark the event as handled ONLY if we want to drag, which prevents the TextBox from freezing the capture loop
        e.Handled = true;
    }

    void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (e.ClickCount == 2 && _state == EditableMapTextState.Display) {
            State = EditableMapTextState.Edit;
            e.Handled = true;
            return;
        }

        // CRUCIAL: Explicitly guard against swallowing clicks meant for input text boxes
        if (_state == EditableMapTextState.Edit || e.OriginalSource is System.Windows.Controls.TextBox)
            return;

        var parent = ParentCanvas;
        if (parent == null) return;

        _isDragging = true;
        _dragOriginScreen = e.GetPosition(parent);
        _dragOriginLeft = Canvas.GetLeft(this);
        _dragOriginTop = Canvas.GetTop(this);
        BackgroundBorder.CaptureMouse();
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
        BackgroundBorder.ReleaseMouseCapture();
        e.Handled = true; // Clean up event bubble path
    }

    // ═════════════════════════════════════════════════════════════════
    // TextBox / TextBlock handlers
    // ═════════════════════════════════════════════════════════════════

    void EditBox_KeyDown(object sender, KeyEventArgs e) {
        if (e.Key == Key.Escape) {
            State = EditableMapTextState.Display;

            // Clear focus backward so the main map canvas can regain control gracefully
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    void DisplayBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (e.ClickCount == 2) {
            State = EditableMapTextState.Edit;
            e.Handled = true;
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // Resize — 8 handles
    // ═════════════════════════════════════════════════════════════════

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
            double newW = Math.Max(MinBoxSize, Width - dx);
            double usedDx = Width - newW;
            Width = newW;
            Canvas.SetLeft(this, Canvas.GetLeft(this) + usedDx);
        }
        else if (dx != 0) Width = Math.Max(MinBoxSize, Width + dx);

        if (anchorBottom) {
            double newH = Math.Max(MinBoxSize, Height - dy);
            double usedDy = Height - newH;
            Height = newH;
            Canvas.SetTop(this, Canvas.GetTop(this) + usedDy);
        }
        else if (dy != 0) Height = Math.Max(MinBoxSize, Height + dy);
    }

    // ═════════════════════════════════════════════════════════════════
    // Rotation
    // ═════════════════════════════════════════════════════════════════

    void RotationHandle_DragStarted(object sender, DragStartedEventArgs e) { }

    void RotationHandle_DragDelta(object sender, DragDeltaEventArgs e) {
        var parent = ParentCanvas;
        if (parent == null) return;
        var center = TransformToAncestor(parent).Transform(new Point(ActualWidth / 2, ActualHeight / 2));
        var mouse = Mouse.GetPosition(parent);
        RotationAngle = Math.Atan2(mouse.X - center.X, -(mouse.Y - center.Y)) * (180.0 / Math.PI);
    }

    // ═════════════════════════════════════════════════════════════════
    // Toolbar — font family / size
    // ═════════════════════════════════════════════════════════════════

    void FontFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (!IsLoaded || FontFamilyCombo.SelectedItem is not string name) return;
        try {
            FontFamily = new FontFamily(name);
        }
        catch {
            /* ignore unknown font */
        }
    }

    void FontSizeBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e) {
        e.Handled = !e.Text.All(c => char.IsDigit(c) || c == '.');
    }

    void FontSizeBox_KeyDown(object sender, KeyEventArgs e) {
        if (e.Key == Key.Enter) {
            ApplyFontSizeInput();

            // Force the text box to give up keyboard focus completely
            Keyboard.ClearFocus();

            // Hand focus back to the parent control container so mouse actions restore safely
            this.Focus();
            e.Handled = true;
        }
    }

    void FontSizeBox_LostFocus(object sender, RoutedEventArgs e) {
        ApplyFontSizeInput();

        // Clear any active text selection highlight that could freeze cursor rendering
        if (sender is System.Windows.Controls.TextBox tb) {
            tb.SelectionLength = 0;
        }
    }

    void ApplyFontSizeInput() {
        if (double.TryParse(FontSizeBox.Text, out double v) && v > 0)
            FontSize = Math.Clamp(v, 6, 144);
        FontSizeBox.Text = ((int)FontSize).ToString();
    }

    // ═════════════════════════════════════════════════════════════════
    // Toolbar — alignment buttons
    // ═════════════════════════════════════════════════════════════════

    void AlignBtn_Click(object sender, RoutedEventArgs e) {
        if (sender is ToggleButton btn && btn.Tag is string tag) {
            TextAlign = tag switch {
                "Center" => TextAlignment.Center,
                "Right" => TextAlignment.Right,
                "Justify" => TextAlignment.Justify,
                _ => TextAlignment.Left,
            };
            UpdateAlignmentButtons();
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // Toolbar — slider handlers
    // ═════════════════════════════════════════════════════════════════

    void BorderThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
        if (!IsLoaded) return;
        BoxBorderThickness = e.NewValue;
    }

    void CornerRadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
        if (!IsLoaded) return;
        BoxCornerRadius = new CornerRadius(e.NewValue);
    }

    void TextPaddingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
        if (!IsLoaded) return;
        TextPadding = e.NewValue;
    }

    // ═════════════════════════════════════════════════════════════════
    // Toolbar — color picker
    // ═════════════════════════════════════════════════════════════════

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

    void TextColorSwatch_Click(object sender, MouseButtonEventArgs e) {
        _colorTarget = "Text";
        ColorPickerPopup.IsOpen = true;
    }

    void BoxBgColorSwatch_Click(object sender, MouseButtonEventArgs e) {
        _colorTarget = "BoxBg";
        ColorPickerPopup.IsOpen = true;
    }

    void BorderColorSwatch_Click(object sender, MouseButtonEventArgs e) {
        _colorTarget = "Border";
        ColorPickerPopup.IsOpen = true;
    }

    void ApplyPickedColor(Color color) {
        ColorPickerPopup.IsOpen = false;

        switch (_colorTarget) {
            case "Text":
                Foreground = new SolidColorBrush(color);
                UpdateSwatchColors();
                break;

            case "BoxBg":
                BoxBackgroundColor = color;
                break;

            case "Border":
                BoxBorderColor = color;
                if (BoxBorderThickness < 0.5) {
                    BoxBorderThickness = 1.0;
                    BorderThicknessSlider.Value = 1.0;
                }

                break;
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // Stamp
    // ═════════════════════════════════════════════════════════════════

    //We need to identify an appropriate location to stamp the text to the map image based on rotation of the text box
    //in consideration for the image resolution.
    void StampButton_Click(object sender, RoutedEventArgs e) {
        ColorPickerPopup.IsOpen = false;
        var parent = ParentCanvas;
        if (parent == null) return;

        // 1. Locate the MapImageElement down the tree safely
        var mapImage = TargetImage ?? FindDescendantByName<System.Windows.Controls.Image>(parent, MapImageElementName) ?? FindDescendantByType<System.Windows.Controls.Image>(parent);
        if (mapImage == null || mapImage.Source is not BitmapSource bitmapSource) return;

        // 2. Get the RAW, completely unrotated layout positions from the Canvas properties.
        // Canvas.GetLeft/Top return the exact steady coordinate before any visual transforms happen.
        double myCanvasX = Canvas.GetLeft(this);
        double myCanvasY = Canvas.GetTop(this);

        double imageCanvasX = Canvas.GetLeft(mapImage);
        double imageCanvasY = Canvas.GetTop(mapImage);

        // Fallback if the elements aren't positioned directly via explicit canvas properties
        if (double.IsNaN(myCanvasX)) myCanvasX = this.TransformToAncestor(parent).Transform(new Point(0, 0)).X;
        if (double.IsNaN(myCanvasY)) myCanvasY = this.TransformToAncestor(parent).Transform(new Point(0, 0)).Y;
        if (double.IsNaN(imageCanvasX))
            imageCanvasX = mapImage.TransformToAncestor(parent).Transform(new Point(0, 0)).X;
        if (double.IsNaN(imageCanvasY))
            imageCanvasY = mapImage.TransformToAncestor(parent).Transform(new Point(0, 0)).Y;

        // 3. Compute the pure layout distance delta
        double relativeX = myCanvasX - imageCanvasX;
        double relativeY = myCanvasY - imageCanvasY;

        // 4. Calculate scaling ratios using absolute physical bitmap dimensions
        double pixelWidthRatio = (double)bitmapSource.PixelWidth / mapImage.ActualWidth;
        double pixelHeightRatio = (double)bitmapSource.PixelHeight / mapImage.ActualHeight;

        // 5. Map properties into raw pixel values
        double burnX = relativeX * pixelWidthRatio;
        double burnY = relativeY * pixelHeightRatio;

        double burnWidth = ActualWidth * pixelWidthRatio;
        double burnHeight = ActualHeight * pixelHeightRatio;
        double burnFontSize = FontSize * pixelWidthRatio;

        var textColor = (Foreground as SolidColorBrush)?.Color ?? Color.FromRgb(255, 255, 255);

        var args = new MapTextStampEventArgs {
            Text = EditBox.Text,
            X = burnX,
            Y = burnY,
            Width = burnWidth,
            Height = burnHeight,
            RotationAngle = RotationAngle,
            FontFamilyName = FontFamily?.Source ?? "Segoe UI",
            FontSize = burnFontSize,
            IsBold = IsBold,
            IsItalic = IsItalic,
            IsUnderline = IsUnderline,
            TextColor = textColor,
            TextOpacity = TextOpacity,
            TextPadding = TextPadding * pixelWidthRatio,
            TextAlignment = TextAlign,
            BackgroundColor = BoxBackgroundColor,
            BackgroundOpacity = BoxBackgroundOpacity,
            BoxBorderColor = BoxBorderColor,
            BoxBorderOpacity = BoxBorderOpacity,
            BoxBorderThickness = BoxBorderThickness * pixelWidthRatio,
            CornerRadius = new CornerRadius(BoxCornerRadius.TopLeft * pixelWidthRatio),
            IsEllipse = false
        };

        Stamped?.Invoke(this, args);
        parent.Children.Remove(this);
    }

    // Helper method to find a named descendant anywhere down the visual tree hierarchy
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