using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MMONavigator.Controls;
using MMONavigator.Helpers;
using MMONavigator.Models;
using MMONavigator.Services;
using MMONavigator.ViewModels;
using MessageBox = System.Windows.MessageBox;

// ReSharper disable RedundantNameQualifier

namespace MMONavigator.Views;

// ReSharper disable once RedundantExtendsListEntry
public partial class MapWindow : ChildWindow {
    private bool _isCalibrating;
    private bool? _savedFogSettings;
    private bool _isSettingDestination;
    private bool _isPickingTextLocation;
    private bool _isPickingCircleLocation;
    private bool _isPickingEllipseLocation;
    private bool _isAddingPin;
    private int _calibrationStep;
    private bool _isDragging;
    private System.Windows.Point _lastMousePosition;
    private IntPtr _preDragForegroundWindow;
    private System.Drawing.Point _lastMousePos;
    private DispatcherTimer? _dragTimer;
    private DispatcherTimer? _hoverTimer;
    private DateTime _lastMouseOutsideTime = DateTime.MinValue;
    private IntPtr _hwnd;
    private AppSettings? _subscribedAppSettings;

    public MapWindow(MapViewModel viewModel) {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;

        if (viewModel.AppSettings.MapWindowPlacement == null) {
            viewModel.AppSettings.MapWindowPlacement = new WindowPlacement {
                Left = 100,
                Top = 100,
                Width = 800,
                Height = 600
            };
        }

        double lastX = viewModel.AppSettings.MapWindowPlacement.Left;
        double lastY = viewModel.AppSettings.MapWindowPlacement.Top;

        ValidateAndSetWindowPosition(this, lastX, lastY);

        Loaded -= MapWindow_Loaded;
        SourceInitialized -= MapWindow_SourceInitialized;
        Loaded += MapWindow_Loaded;
        SourceInitialized += MapWindow_SourceInitialized;

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _hoverTimer.Tick += HoverTimer_Tick;
        _hoverTimer.Start();
    }

    private void AddPin_Click(object sender, RoutedEventArgs e) {
        try {
            _isAddingPin = AddPinMenuItem.IsChecked;
            if (_isAddingPin) {
                _isCalibrating = false;
                _isSettingDestination = false;
                SetDestinationMenuItem.IsChecked = false;
                StatusTextBlock.Text = "Status: Click on map to add pin";
            }
            else {
                StatusTextBlock.Text = "Status: Ready";
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error toggling Add Pin mode.");
            MessageBox.Show("Could not add pin. Please try again.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Calibrate_Click(object sender, RoutedEventArgs e) {
        try {
            if (_isCalibrating) {
                CancelCalibration();
                return;
            }

            StartCalibration();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error initiating map calibration.");
            MessageBox.Show("Could not calibrate. Please try again.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void CancelActiveModes() {
        if (_isSettingDestination || _isAddingPin || _isPickingTextLocation || _isPickingCircleLocation ||
            _isPickingEllipseLocation || _isCalibrating) {
            _isSettingDestination = false;
            _isAddingPin = false;
            _isPickingTextLocation = false;
            _isPickingCircleLocation = false;
            _isPickingEllipseLocation = false;

            if (_isCalibrating) {
                if (_savedFogSettings.HasValue && DataContext is MapViewModel vm) {
                    vm.ShowFogOfWar = _savedFogSettings.Value;
                    _savedFogSettings = null;
                }

                _isCalibrating = false;
                _calibrationStep = 0;
            }

            // Reset UI Elements
            SetDestinationMenuItem.IsChecked = false;
            AddPinMenuItem.IsChecked = false;
            MapCanvas.Cursor = System.Windows.Input.Cursors.Arrow;
            StatusTextBlock.Text = "Status: Action cancelled.";
        }
    }

    private void CancelCalibration() {
        if (_isCalibrating) {
            if (_savedFogSettings.HasValue && DataContext is MapViewModel vm) {
                vm.ShowFogOfWar = _savedFogSettings.Value;
                _savedFogSettings = null;
            }

            _isCalibrating = false;
            _isSettingDestination = false;
            SetDestinationMenuItem.IsChecked = false;
            _isAddingPin = false;
            AddPinMenuItem.IsChecked = false;
            _calibrationStep = 0;
            StatusTextBlock.Text = "Status: Calibration Cancelled";
        }
    }

    private void CenterMapOnMarker() {
        if (DataContext is not MapViewModel vm) return;
        vm.Settings ??= new MapSettings();

        double currentZoom = vm.Settings.ZoomLevel;

        double targetX = (vm.MarkerX * currentZoom) - (MapScrollViewer.ActualWidth / 2);
        double targetY = (vm.MarkerY * currentZoom) - (MapScrollViewer.ActualHeight / 2);

        MapScrollViewer.ScrollToHorizontalOffset(targetX);
        MapScrollViewer.ScrollToVerticalOffset(targetY);
    }

    public void Cleanup() {
        if (_subscribedAppSettings != null) {
            _subscribedAppSettings.PropertyChanged -= OnAppSettingsPropertyChanged;
            _subscribedAppSettings = null;
        }

        try {
            SaveWindowPlacement();
        }
        catch (Exception ex) {
            Log.Warning(ex, "Error saving window placement during Cleanup.");
        }

        try {
            _hoverTimer?.Stop();
            _dragTimer?.Stop();
        }
        catch (Exception ex) {
            Log.Warning(ex, "Error stopping timers during Cleanup.");
        }

        try {
            SaveCurrentMap();
        }
        catch (Exception ex) {
            Log.Warning(ex, "Error saving current map during Cleanup.");
        }
    }

    private void ClearCalibration(MapSettings settings) {
        if (settings == null) return;
        settings.IsCalibrated = false;
        settings.Point1.X = 0;
        settings.Point1.Y = 0;
        settings.Point1.PixelX = 0;
        settings.Point1.PixelY = 0;
        settings.Point2.X = 0;
        settings.Point2.Y = 0;
        settings.Point2.PixelX = 0;
        settings.Point2.PixelY = 0;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) {
        try {
            Close();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error closing MapWindow.");
        }
    }

    private void CreateChallenge_Click(object sender, RoutedEventArgs e) {
        try {
            new ChallengeDesignerWindow().Show();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error opening ChallengeDesignerWindow.");
        }
    }

    private void DragTimer_Tick(object? sender, EventArgs e) {
        // If user releases mouse, stop dragging
        // ReSharper disable once RedundantNameQualifier
        if (System.Windows.Forms.Control.MouseButtons != System.Windows.Forms.MouseButtons.Left) {
            _dragTimer?.Stop();

            // Return focus to the background app
            if (_preDragForegroundWindow != IntPtr.Zero && _preDragForegroundWindow != _hwnd) {
                NativeMethods.SetForegroundWindow(_preDragForegroundWindow);
            }

            return;
        }

        System.Drawing.Point currentMousePos = System.Windows.Forms.Cursor.Position;

        var source = PresentationSource.FromVisual(this);
        if (source == null) return;

        var dpiX = source.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var dpiY = source.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        Left += (currentMousePos.X - _lastMousePos.X) / dpiX;
        Top += (currentMousePos.Y - _lastMousePos.Y) / dpiY;

        _lastMousePos = currentMousePos;
    }

    //Give some leeway for people to have their mouse leave the window before hiding portions of it
    private void HoverTimer_Tick(object? sender, EventArgs e) {
        if (DataContext is not MapViewModel vm) return;

        //If we just used a dialog, then a timer will be in play to assume we are hovering
        //to give the user time to return to the map, given the dialog may be positioned
        //over the screen and not include the map window.
        if (IsDialogActive || _isAddingPin || _isSettingDestination || HoverTrackDisabled) return;

        if (Height <= 28 || WindowState == WindowState.Minimized) {
            vm.Opacity = 1;
            vm.IsHovered = true;
            return;
        }

        // If it's already off, we don't need to do coordinate math to turn it on!
        if (!vm.IsHovered) {
            // OPTIONAL: Stop the timer to save CPU cycles
            // _hoverTimer.Stop(); 
            return;
        }

        //if opacity is 1, it doesn't matter what the mouse is doing, it will stay on
        if (Math.Abs(vm.Opacity - 1) < MapViewModel.Tolerance) {
            return;
        }

        try {
            //code works on slower or lower res machines
            // System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
            // var windowTopLeft = this.PointToScreen(new System.Windows.Point(0, 0));
            // var windowBounds = new Rectangle((int)windowTopLeft.X, (int)windowTopLeft.Y, (int)this.ActualWidth,
            //     (int)this.ActualHeight);
            //
            // bool isInsideWindow = windowBounds.Contains(cursor.X, cursor.Y);

            //code does not work on higher res machines
            // Get the mouse position relative to the current Window
            // System.Windows.Point relativeMousePos = Mouse.GetPosition(this);
            //
            // // In WPF units, if the mouse is inside the window, 
            // // X will be between 0 and ActualWidth, Y between 0 and ActualHeight.
            // bool isInsideWindow = (relativeMousePos.X >= 0 && 
            //                        relativeMousePos.X <= this.ActualWidth &&
            //                        relativeMousePos.Y >= 0 && 
            //                        relativeMousePos.Y <= this.ActualHeight);

            //This method works on higher resolution / faster machines as well as slower
            if (NativeMethods.GetCursorPos(out NativeMethods.Win32Point p) &&
                PresentationSource.FromVisual(this) != null) {
                // 1. Convert the physical screen point (Win32) to a WPF Logical Point
                System.Windows.Point mousePoint = PointFromScreen(new System.Windows.Point(p.X, p.Y));

                // 2. Check bounds against ActualWidth/Height
                // Note: PointFromScreen handles the DPI scaling math for you.
                bool isInsideWindow = (mousePoint.X >= 0 &&
                                       mousePoint.X <= ActualWidth &&
                                       mousePoint.Y >= 0 &&
                                       mousePoint.Y <= ActualHeight);

                if (isInsideWindow) {
                    // Keep it alive
                    _lastMouseOutsideTime = DateTime.MinValue;
                }
                else {
                    // Start the exit countdown
                    if (_lastMouseOutsideTime == DateTime.MinValue)
                        _lastMouseOutsideTime = DateTime.Now;

                    if (DateTime.Now - _lastMouseOutsideTime > TimeSpan.FromMilliseconds(300)) {
                        vm.IsHovered = false;
                        // UI is now hidden; the timer will hit the 'if (!vm.IsHovered)' block next time
                    }
                }
            }
        }
        catch (InvalidOperationException ex) {
            Log.Debug(ex, "PointFromScreen call bypassed because MapWindow is not attached to a visual source.");
        }
        catch (Exception ex) {
            Log.Warning(ex, "Error checking cursor position in HoverTimer_Tick.");
        }
    }

    private void MapImageElement_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        try {
            if (DataContext is not MapViewModel vm) return;

            // Wake UI via Double-Click OR Control + Left-Click
            if (e.ClickCount == 2 || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) {
                if (sender is IInputElement element) {
                    // Force the window to keep focus even if the 4K scaling 
                    // makes it think the mouse moved slightly off-element
                    Mouse.Capture(element);
                }

                PauseHoverTracking();
                bool wasHidden = !vm.IsHovered;

                // Wake up controls and disable transparent click-through
                vm.IsHovered = true;
                UpdateKeyboardClickThrough();

                // 2. Edge Case: Should we drop a marker?
                // If the UI was hidden, maybe the user just wanted to see the map.
                // You can "eat" the click so a marker isn't accidentally placed 
                // the moment the UI appears.
                if (wasHidden) {
                    e.Handled = true; // Consume activation click so pins aren't placed accidentally
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling PreviewMouseLeftButtonDown on MapImageElement.");
        }
    }

    private void MapImageElement_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        try {
            Mouse.Capture(null);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error releasing mouse capture in MapImageElement_PreviewMouseLeftButtonUp.");
        }
    }

    private void MapWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) {
        try {
            if (e.Key == Key.Escape) {
                // Only mark handled if we actually cancelled something, 
                // so Esc can still close dialogs or do other native tasks if we're idle.
                if (_isSettingDestination || _isAddingPin || _isPickingTextLocation || _isPickingCircleLocation ||
                    _isPickingEllipseLocation || _isCalibrating) {
                    CancelActiveModes();
                    e.Handled = true;
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling PreviewKeyDown in MapWindow.");
        }
    }

    private void MapWindow_SourceInitialized(object? sender, EventArgs e) {
        try {
            _hwnd = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(_hwnd);
            source?.AddHook(HwndHandler);

            // Initial application of the style
            UpdateKeyboardClickThrough();

            // Subscribe cleanly to AppSettings
            SubscribeToAppSettings();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error initializing HwndSource hook in MapWindow_SourceInitialized.");
        }
    }

    private void SubscribeToAppSettings() {
        if (DataContext is not MapViewModel vm) return;

        // Unhook prior instance if present
        if (_subscribedAppSettings != null) {
            _subscribedAppSettings.PropertyChanged -= OnAppSettingsPropertyChanged;
        }

        _subscribedAppSettings = vm.AppSettings;
        if (_subscribedAppSettings != null) {
            _subscribedAppSettings.PropertyChanged += OnAppSettingsPropertyChanged;
        }
    }

    private void OnAppSettingsPropertyChanged(object? sender, PropertyChangedEventArgs ev) {
        if (ev.PropertyName == nameof(AppSettings.KeyboardClickThrough)) {
            UpdateKeyboardClickThrough();
        }
    }

    private void KeepOnTop() {
        try {
            if (_hwnd == IntPtr.Zero) return;
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
        catch (Exception ex) {
            Log.Warning(ex, "Failed to apply SetWindowPos in KeepOnTop.");
        }
    }

    protected override IntPtr HwndHandler(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled) {
        IntPtr result = base.HwndHandler(hwnd, msg, wparam, lparam, ref handled);
        if (handled) return result;

        if (DataContext is not MapViewModel vm) return IntPtr.Zero;

        // Helper flag: Are we actively interacting with ANY part of the text tool?
        bool isInteractingWithTextTool = _isPickingTextLocation || _isPickingCircleLocation ||
                                         _isPickingEllipseLocation || vm.IsDrawModeActive ||
                                         Keyboard.FocusedElement is System.Windows.Controls.TextBox ||
                                         IsMouseOverTextControl();

        if (isInteractingWithTextTool) {
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_MOUSEACTIVATE) {
            if (vm.AppSettings.KeyboardClickThrough) {
                // EXCEPTION: If interacting with text or its handles/toolbars, fully activate!
                if (isInteractingWithTextTool) {
                    return IntPtr.Zero; // Let Windows activate our app normally
                }

                handled = true;
                return NativeMethods.MA_NOACTIVATE;
            }
        }

        if (msg == NativeMethods.WM_ACTIVATE) {
            if ((int)wparam != NativeMethods.WA_INACTIVE) {
                if (vm.AppSettings.KeyboardClickThrough) {
                    // EXCEPTION: Keep focus here if we are editing or transforming text
                    if (isInteractingWithTextTool) {
                        return IntPtr.Zero;
                    }

                    var foregroundWindow = NativeMethods.GetForegroundWindow();
                    nint targetWnd;

                    if (_preDragForegroundWindow != IntPtr.Zero && _preDragForegroundWindow != _hwnd) {
                        targetWnd = _preDragForegroundWindow;
                    }
                    else if (foregroundWindow != _hwnd && foregroundWindow != IntPtr.Zero) {
                        targetWnd = foregroundWindow;
                    }
                    else {
                        targetWnd = NativeMethods.GetWindow(_hwnd, NativeMethods.GW_HWNDNEXT);
                    }

                    if (targetWnd != IntPtr.Zero && targetWnd != _hwnd) {
                        NativeMethods.SetForegroundWindow(targetWnd);
                    }

                    handled = true;
                }
            }
        }

        return IntPtr.Zero;
    }

    private bool IsMouseOverTextControl() {
        bool hitTextControl = false;

        try {
            // Get current mouse position relative to the MapCanvas
            System.Windows.Point mousePos = Mouse.GetPosition(MapCanvas);

            VisualTreeHelper.HitTest(MapCanvas,
                null,
                new HitTestResultCallback(result => {
                    if (result.VisualHit is DependencyObject hitObj) {
                        // Check if the hit element belongs to our EditableMapText control
                        var parent = FindParent<EditableMapText>(hitObj);
                        if (parent != null) {
                            hitTextControl = true;
                            return HitTestResultBehavior.Stop; // Found it, stop searching
                        }
                    }

                    return HitTestResultBehavior.Continue;
                }),
                new PointHitTestParameters(mousePos));
        }
        catch (Exception ex) {
            Log.Debug(ex, "Error performing visual tree hit test for text controls.");
        }

        return hitTextControl;
    }

    private void HideHint_Click(object sender, RoutedEventArgs e) {
        try {
            if (DataContext is MapViewModel vm) {
                vm.AppSettings.HideMapClickHint = true;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error setting HideMapClickHint.");
        }
    }

    private void Menu_SubmenuOpened(object sender, RoutedEventArgs e) {
        // Stop HoverTimer and MouseHook processing while navigating top-level menus
        IsDialogActive = true;
    }

    private void Menu_SubmenuClosed(object sender, RoutedEventArgs e) {
        // Re-enable tracking after menu closes (if no modal dialog is active)100, 100
        IsDialogActive = false;
    }

    private void LoadMap_Click(object sender, RoutedEventArgs e) {
        if (DataContext is not MapViewModel vm) return;

        // Yield control so WPF closes the MenuItem layout cleanly before launching the dialog
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { ExecuteLoadMap(vm); }));
    }

    private void ExecuteLoadMap(MapViewModel vm) {
        try {
            if (vm.IsDrawModeActive) {
                vm.StopDrawMode();
                StatusTextBlock.Text = "Status: Drawing stopped.";
            }

            var mapsDir = Path.Combine(NativeMethods.AppFolder(), "maps");
            if (!Directory.Exists(mapsDir)) {
                Directory.CreateDirectory(mapsDir);
            }

            try {
                SaveCurrentMap();
            }
            catch (Exception ex) {
                Log.Warning(ex, "Non-critical exception during map auto-save prior to loading new map.");
            }

            IsDialogActive = true;
            Window? helperWindow = null;
            vm.Settings ??= new MapSettings();

            try {
                ConfigureDialogToHaveAValidOwner(this, out helperWindow);

                var openFileDialog = new Microsoft.Win32.OpenFileDialog {
                    Filter = "Image files (*.png;*.jpeg;*.jpg;*.bmp)|*.png;*.jpeg;*.jpg;*.bmp|All files (*.*)|*.*",
                    CheckFileExists = true,
                    CheckPathExists = true
                };

                try {
                    if (!string.IsNullOrWhiteSpace(mapsDir) && Directory.Exists(mapsDir)) {
                        openFileDialog.InitialDirectory = mapsDir;
                    }
                }
                catch (Exception ex) {
                    Log.Debug(ex, "Could not set initial directory for map open dialog.");
                }

                bool? dialogResult;
                try {
                    dialogResult = helperWindow != null
                        ? openFileDialog.ShowDialog(helperWindow)
                        : openFileDialog.ShowDialog();
                }
                catch (Exception ex) {
                    Log.Error(ex, "Error displaying open file dialog in LoadMap_Click.");
                    MessageBox.Show($"Unable to display the file picker:\n{ex.Message}",
                        "Dialog Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (dialogResult == true) {
                    vm.IsLoadingFile = true;
                    string imagePath = openFileDialog.FileName;

                    if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) {
                        MessageBox.Show("The selected image file could not be found or accessed.",
                            "File Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // Guarantee Settings exists
                    vm.Settings ??= new MapSettings();

                    // 2. Safely derive config JSON path
                    string? configPath = null;
                    try {
                        configPath = Path.ChangeExtension(imagePath, ".json");
                    }
                    catch (ArgumentException) {
                        Log.Warning("Invalid characters in image path when attempting to change extension to .json");
                    }

                    bool configLoaded = false;

                    // 3. Attempt JSON Config Load if present
                    if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath)) {
                        try {
                            bool calibrated = vm.LoadImageConfig(imagePath);

                            if (calibrated) {
                                configLoaded = true;
                            }
                        }
                        catch (Exception ex) {
                            Log.Error(ex, "Error loading map configuration file '{ConfigPath}'.", configPath);
                            MessageBox.Show(
                                $"Error loading map configuration file:\n{ex.Message}\n\nLoading image only.",
                                "Config Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }

                    // 4. Load the Image safely (Common to both calibrated and non-calibrated paths)
                    try {
                        vm.Settings.ImagePath = imagePath;
                        vm.LoadImage();

                        if (configLoaded) {
                            vm.UpdateMarkers();
                            if (StatusTextBlock != null) {
                                StatusTextBlock.Text = $"Status: Loaded {Path.GetFileName(imagePath)} with config.";
                            }
                        }
                        else {
                            ClearCalibration(vm.Settings);
                            if (StatusTextBlock != null) {
                                string suffix = !string.IsNullOrEmpty(configPath) && File.Exists(configPath)
                                    ? "(config error)"
                                    : "(no config)";
                                StatusTextBlock.Text = $"Status: Loaded {Path.GetFileName(imagePath)} {suffix}.";
                            }
                        }
                    }
                    catch (OutOfMemoryException ex) {
                        Log.Error(ex, "Image file too large or corrupted: {ImagePath}", imagePath);
                        MessageBox.Show("The selected image is too large or corrupt to decode.",
                            "Memory / Image Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    catch (FileFormatException ex) {
                        Log.Error(ex, "Invalid image format: {ImagePath}", imagePath);
                        MessageBox.Show("The selected file is not a valid or supported image format.",
                            "Unsupported Format", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    catch (UnauthorizedAccessException ex) {
                        Log.Error(ex, "Access denied reading image file: {ImagePath}", imagePath);
                        MessageBox.Show("Access denied. You do not have permission to read this file.",
                            "Permission Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    catch (Exception ex) {
                        Log.Error(ex, "Unexpected error loading image: {ImagePath}", imagePath);
                        MessageBox.Show($"An unexpected error occurred while loading the image:\n{ex.Message}",
                            "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            finally {
                // ALWAYS close the helper to prevent memory leaks
                helperWindow?.Close();
                IsDialogActive = false;
                vm.IsLoadingFile = false;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing LoadMap_Click handler.");
        }
    }

    private void MapCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e) {
        try {
            // Only intercept the right-click if the user is actually in a special mode
            if (_isSettingDestination || _isAddingPin || _isPickingTextLocation || _isPickingCircleLocation ||
                _isPickingEllipseLocation || _isCalibrating) {
                CancelActiveModes();
                // CRITICAL: Tell WPF we consumed this click to cancel the tool.
                // This stops a standard context menu from opening on top of our canvas.
                e.Handled = true;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling MouseRightButtonDown on MapCanvas.");
        }
    }

    EventHandler<MapTextStampEventArgs> labelHandler = null!;
    EventHandler<MapTextStampEventArgs> handler = null!;

    private void MapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        // // If the user is actively clicking inside our formatting toolbar or handles canvas,
        // // let the event route natively to those buttons and sliders instead of triggering map actions!
        // if (e.OriginalSource is DependencyObject clickedObj) {
        //     if (VisualTreeHelper.GetParent(clickedObj) is FrameworkElement parent && 
        //         (parent.Name == "ToolbarContainer" || parent.Name == "HandleCanvas")) {
        //         return; // Exit early, do not set e.Handled = true so the buttons can process the click!
        //     }
        // }
        // If the click originated from inside a TextBox or the text toolbar layout, 
        // immediately mark it handled at the canvas layer so the canvas stops processing it!

        try {
            if (DataContext is not MapViewModel vm) return;

            if (e.OriginalSource is DependencyObject clickedObj) {
                // Check if the click is inside our custom EditableMapText control tree
                var parentTextControl = FindParent<EditableMapText>(clickedObj);
                if (parentTextControl != null || e.OriginalSource is System.Windows.Controls.TextBox) {
                    e.Handled = true; // Stop the map window from running click-through or drag rules!
                    return;
                }
            }

            if (!_isPickingTextLocation && !_isPickingCircleLocation && !_isPickingEllipseLocation) {
                if (System.Windows.Application.Current.MainWindow?.DataContext is MainViewModel mainVm &&
                    mainVm.Settings.KeyboardClickThrough) {
                    _preDragForegroundWindow = NativeMethods.GetForegroundWindow();
                    if (_preDragForegroundWindow == _hwnd) _preDragForegroundWindow = IntPtr.Zero;
                }
            }
            else {
                // Force your WPF window handle to explicitly command focus from the OS
                NativeMethods.SetForegroundWindow(_hwnd);
            }

            if (!_isCalibrating && !_isSettingDestination && !_isAddingPin && !_isPickingTextLocation &&
                !_isPickingCircleLocation && !_isPickingEllipseLocation) {
                _isDragging = true;
                _lastMousePosition = e.GetPosition(MapScrollViewer);
                MapCanvas.CaptureMouse();
                MapCanvas.Cursor = System.Windows.Input.Cursors.Hand;

                e.Handled = true;
                return;
            }

            vm.Settings ??= new MapSettings();
            System.Windows.Point clickPoint = e.GetPosition(MapCanvas);

            if (_isPickingTextLocation) {
                //We aere in draw mode and have selected to add text to the map
                var coords = vm.GetCoordinatesFromPixels(clickPoint.X, clickPoint.Y);
                if (coords.HasValue) {
                    // 1. Temporarily flip click-through window properties OFF 
                    // so the OS can safely bind standard keyboard streams
                    var oldClickThroughSetting = vm.AppSettings.KeyboardClickThrough;
                    vm.AppSettings.KeyboardClickThrough = false;
                    UpdateKeyboardClickThrough();

                    var label = new EditableMapText {
                        InitialText = "New Text",
                        BoxBackgroundColor = Colors.Black,
                        Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 14,
                        Width = 180,
                        Height = 80,
                        TargetImage = MapImageElement
                    };

                    labelHandler = (s, args) => {
                        // 1. Immediately detach so it only runs ONCE and releases memory
                        label.Stamped -= handler;

                        // 2. Perform stamp work
                        OnLabelStamped(s, args);
                        vm.AppSettings.KeyboardClickThrough = oldClickThroughSetting;
                        UpdateKeyboardClickThrough();
                    };

                    // When the user stamps the text, restore original background tracking parameters automatically
                    label.Stamped += labelHandler;

                    Canvas.SetLeft(label, clickPoint.X - (label.Width / 2));
                    Canvas.SetTop(label, clickPoint.Y - (label.Height / 2));

                    MapCanvas.Children.Add(label);

                    _isPickingTextLocation = false;
                    StatusTextBlock.Text = "Status: When done adding text, save it to the image";
                }

                e.Handled = true;
                return;
            }

            if (_isPickingCircleLocation) {
                //We aere in draw mode and have selected to add text to the map
                var coords = vm.GetCoordinatesFromPixels(clickPoint.X, clickPoint.Y);
                if (coords.HasValue) {
                    // 1. Temporarily flip click-through window properties OFF 
                    // so the OS can safely bind standard keyboard streams
                    var oldClickThroughSetting = vm.AppSettings.KeyboardClickThrough;
                    vm.AppSettings.KeyboardClickThrough = false;
                    UpdateKeyboardClickThrough();

                    var label = new EditableMapEllipse {
                        InitialText = "New Text",
                        BoxBackgroundColor = Colors.Black,
                        Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 14,
                        Width = 180,
                        Height = 80,
                        TargetImage = MapImageElement
                    };

                    handler = (s, args) => {
                        // 1. Immediately detach so it only runs ONCE and releases memory
                        label.Stamped -= handler;

                        // 2. Perform stamp work
                        OnCircleStamped(s, args);
                        vm.AppSettings.KeyboardClickThrough = oldClickThroughSetting;
                        UpdateKeyboardClickThrough();
                    };

                    // When the user stamps the text, restore original background tracking parameters automatically
                    label.Stamped += handler;

                    Canvas.SetLeft(label, clickPoint.X - (label.Width / 2));
                    Canvas.SetTop(label, clickPoint.Y - (label.Height / 2));

                    MapCanvas.Children.Add(label);

                    _isPickingCircleLocation = false;
                    StatusTextBlock.Text = "Status: When done adding text, save it to the image";
                }

                e.Handled = true;
                return;
            }

            if (_isPickingEllipseLocation) {
                //We aere in draw mode and have selected to add text to the map
                var coords = vm.GetCoordinatesFromPixels(clickPoint.X, clickPoint.Y);
                if (coords.HasValue) {
                    // 1. Temporarily flip click-through window properties OFF 
                    // so the OS can safely bind standard keyboard streams
                    var oldClickThroughSetting = vm.AppSettings.KeyboardClickThrough;
                    vm.AppSettings.KeyboardClickThrough = false;
                    UpdateKeyboardClickThrough();

                    var label = new MapCircleMarker {
                        CircleBackgroundColor = Colors.Black,
                        Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 14,
                        Width = 180,
                        Height = 80,
                        TargetImage = MapImageElement
                    };

                    // When the user stamps the text, restore original background tracking parameters automatically
                    label.Stamped += (s, args) => {
                        OnCircleMarkerStamped(s, args);
                        vm.AppSettings.KeyboardClickThrough = oldClickThroughSetting;
                        UpdateKeyboardClickThrough();
                    };

                    Canvas.SetLeft(label, clickPoint.X - (label.Width / 2));
                    Canvas.SetTop(label, clickPoint.Y - (label.Height / 2));

                    MapCanvas.Children.Add(label);

                    _isPickingEllipseLocation = false;
                    StatusTextBlock.Text = "Status: When done adding ellipse, save it to the image";
                }

                e.Handled = true;
                return;
            }

            if (_isSettingDestination) {
                var coords = vm.GetCoordinatesFromPixels(clickPoint.X, clickPoint.Y);
                if (coords.HasValue) {
                    vm.SelectDestination(coords.Value);
                    _isSettingDestination = false;
                    SetDestinationMenuItem.IsChecked = false;
                    StatusTextBlock.Text = "Status: Destination set";
                }

                // Mark handled so parent controls ignore the click, 
                // though native game passthrough is handled via window styles/win32 flags.
                e.Handled = true;
                return;
            }

            if (_isAddingPin) {
                var coords = vm.GetCoordinatesFromPixels(clickPoint.X, clickPoint.Y);
                if (coords.HasValue) {
                    IsDialogActive = true;
                    vm.RequestPin(coords.Value);
                    IsDialogActive = false;
                    _isAddingPin = false;
                    AddPinMenuItem.IsChecked = false;
                    StatusTextBlock.Text = "Status: Pin requested";
                }

                // Mark handled so parent controls ignore the click, 
                // though native game passthrough is handled via window styles/win32 flags.
                e.Handled = true;
                return;
            }

            if (_calibrationStep == 1) {
                string suggestedCoords = vm.CurrentPosition.HasValue
                    ? $"{vm.CurrentPosition.Value.X}, {vm.CurrentPosition.Value.Y}"
                    : "0, 0";

                try {
                    var inputDialog =
                        new InputDialog("Enter coordinates for Point 1 (x, y):", "Calibration Point 1", suggestedCoords)
                            { Owner = System.Windows.Application.Current.MainWindow };

                    IsDialogActive = true;
                    // Set the owner to the MainWindow BEFORE calling ShowDialog()
                    // You can access the MainWindow via Application.Current.MainWindow
                    inputDialog.ShowDialog();

                    // Check your manual property instead of the built-in DialogResult
                    if (inputDialog.ManualDialogResult == true) {
                        if (Scrubber.TryParse(inputDialog.Answer, "x y", out var coords)) {
                            vm.Settings.Point1.X = coords.X;
                            vm.Settings.Point1.Y = coords.Y;
                            vm.Settings.Point1.PixelX = clickPoint.X;
                            vm.Settings.Point1.PixelY = clickPoint.Y;

                            _calibrationStep = 2;
                            StatusTextBlock.Text = "Status: Click Point 2 on map";
                        }
                        else {
                            MessageBox.Show("Invalid coordinates format.", "Calibration Error", MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                    }
                }
                finally {
                    IsDialogActive = false;
                }

                // Mark handled so parent controls ignore the click, 
                // though native game passthrough is handled via window styles/win32 flags.
                e.Handled = true;
                return;
            }
            else if (_calibrationStep == 2) {
                string suggestedCoords = vm.CurrentPosition.HasValue
                    ? $"{vm.CurrentPosition.Value.X}, {vm.CurrentPosition.Value.Y}"
                    : "0, 0";

                try {
                    var inputDialog =
                        new InputDialog("Enter coordinates for Point 2 (x, y):", "Calibration Point 2", suggestedCoords)
                            { Owner = System.Windows.Application.Current.MainWindow };

                    // Set the owner to the MainWindow BEFORE calling ShowDialog()
                    // You can access the MainWindow via Application.Current.MainWindow
                    IsDialogActive = true;
                    inputDialog.ShowDialog();

                    // Check your manual property instead of the built-in DialogResult
                    if (inputDialog.ManualDialogResult == true) {
                        if (Scrubber.TryParse(inputDialog.Answer, "x y", out var coords)) {
                            // Validate that Point 2 is distinct enough from Point 1
                            if (Math.Abs(coords.X - vm.Settings.Point1.X) <= 10 ||
                                Math.Abs(coords.Y - vm.Settings.Point1.Y) <= 10) {
                                MessageBox.Show(
                                    "Calibration points are too close together. Please choose points that differ by more than 10 units in both X and Y for accurate mapping.",
                                    "Invalid Calibration", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }

                            vm.Settings.Point2.X = coords.X;
                            vm.Settings.Point2.Y = coords.Y;
                            vm.Settings.Point2.PixelX = clickPoint.X;
                            vm.Settings.Point2.PixelY = clickPoint.Y;

                            vm.Settings.IsCalibrated = true;
                            vm.UpdateMarkers();

                            if (_savedFogSettings.HasValue) {
                                vm.ShowFogOfWar = _savedFogSettings.Value;
                                _savedFogSettings = null;
                            }

                            _isCalibrating = false;
                            _calibrationStep = 0;

                            if (vm.MapImage != null && !string.IsNullOrEmpty(vm.MapPath)) {
                                var mapsDir = Path.Combine(NativeMethods.AppFolder(), "maps");
                                if (!Directory.Exists(mapsDir)) {
                                    Directory.CreateDirectory(mapsDir);
                                }

                                // Remove invalid characters
                                var newName = Path.GetFileNameWithoutExtension(vm.MapPath);
                                foreach (char c in Path.GetInvalidFileNameChars()) {
                                    newName = newName.Replace(c, '_');
                                }

                                var configPath = Path.Combine(mapsDir, newName + ".json");
                                var json = JsonSerializer.Serialize(vm.Settings);
                                File.WriteAllText(configPath, json);
                            }

                            StatusTextBlock.Text = "Status: Calibrated";
                        }
                        else {
                            MessageBox.Show("Invalid coordinates format.", "Calibration Error", MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                    }
                }
                finally {
                    IsDialogActive = false;
                }

                // Mark handled so parent controls ignore the click, 
                // though native game passthrough is handled via window styles/win32 flags.
                e.Handled = true;
                return;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling MouseLeftButtonDown on MapCanvas.");
        }
    }

    private void MapCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        try {
            // If we are actively editing text or just finished dropping a control,
            // prevent the canvas from running global map focus resets!
            // If we are actively editing text, we still need to make sure 
            // we don't accidentally leave a dangling mouse capture on the canvas!
            if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) {
                _isDragging = false;

                if (MapCanvas.IsMouseCaptured) {
                    MapCanvas.ReleaseMouseCapture();
                    MapCanvas.Cursor = System.Windows.Input.Cursors.Arrow;
                }

                // If they clicked the empty canvas, clear focus from the textbox 
                // so the user can use hotkeys/interact with the map normally again.
                if (e.OriginalSource == MapCanvas || e.OriginalSource == MapImageElement) {
                    FocusManager.SetFocusedElement(this, null);
                    Keyboard.ClearFocus();
                }

                e.Handled = true;
                return;
            }

            if (e.OriginalSource is DependencyObject clickedObj) {
                // Check if the click is inside our custom EditableMapText control tree
                var parentTextControl = FindParent<EditableMapText>(clickedObj);
                if (parentTextControl != null || e.OriginalSource is System.Windows.Controls.TextBox) {
                    _isDragging = false;
                    e.Handled = true; // Stop the map window from running click-through or drag rules!
                    return;
                }
            }

            if (_isDragging) {
                try {
                    _isDragging = false;
                    MapCanvas.ReleaseMouseCapture();
                    MapCanvas.Cursor = System.Windows.Input.Cursors.Arrow;

                    if (System.Windows.Application.Current.MainWindow?.DataContext is MainViewModel mainVm &&
                        mainVm.Settings.KeyboardClickThrough) {
                        IntPtr currentForeground = NativeMethods.GetForegroundWindow();
                        if (currentForeground == _hwnd) {
                            if (_preDragForegroundWindow != IntPtr.Zero && _preDragForegroundWindow != _hwnd) {
                                NativeMethods.SetForegroundWindow(_preDragForegroundWindow);
                            }
                            else {
                                IntPtr nextWnd = NativeMethods.GetWindow(_hwnd, NativeMethods.GW_HWNDNEXT);
                                if (nextWnd != IntPtr.Zero) NativeMethods.SetForegroundWindow(nextWnd);
                            }
                        }

                        _preDragForegroundWindow = IntPtr.Zero;
                    }
                }
                finally {
                    e.Handled = true;
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling MouseLeftButtonUp on MapCanvas.");
        }
    }

    private void MapCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) {
        try {
            if (DataContext is not MapViewModel vm) return;
            System.Windows.Point currentPoint = e.GetPosition(MapCanvas);
            vm.UpdateHoverCoordinates(currentPoint.X, currentPoint.Y);

            if (_isDragging) {
                System.Windows.Point currentScrollPoint = e.GetPosition(MapScrollViewer);
                var deltaX = currentScrollPoint.X - _lastMousePosition.X;
                var deltaY = currentScrollPoint.Y - _lastMousePosition.Y;

                MapScrollViewer.ScrollToHorizontalOffset(MapScrollViewer.HorizontalOffset - deltaX);
                MapScrollViewer.ScrollToVerticalOffset(MapScrollViewer.VerticalOffset - deltaY);

                _lastMousePosition = currentScrollPoint;
                e.Handled = true;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling MouseMove on MapCanvas.");
        }
    }

    private void MapScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
        try {
            if (DataContext is not MapViewModel vm) return;
            vm.Settings ??= new MapSettings();

            // 1. Calculate the new scale
            double zoomFactor = e.Delta > 0 ? 1.1 : 0.9;
            double newScale = vm.Settings.ZoomLevel * zoomFactor;

            // Limit zoom level
            if (newScale < 0.1) newScale = 0.1;
            if (newScale > 10) newScale = 10;

            // 2. Apply the new scale to the ViewModel/Settings
            vm.Settings.ZoomLevel = newScale;
            RefreshPopup();

            // 3. Handle Viewport Positioning
            if (vm.IsFollowModeActive) {
                // If following, ignore mouse position and force center on the marker
                CenterMapOnMarker();
            }
            else {
                // Standard "Zoom to Mouse" logic
                System.Windows.Point mousePos = e.GetPosition(MapScrollViewer);

                var relativeMouseX = (mousePos.X + MapScrollViewer.HorizontalOffset) / (newScale / zoomFactor);
                var relativeMouseY = (mousePos.Y + MapScrollViewer.VerticalOffset) / (newScale / zoomFactor);

                MapScrollViewer.ScrollToHorizontalOffset(relativeMouseX * newScale - mousePos.X);
                MapScrollViewer.ScrollToVerticalOffset(relativeMouseY * newScale - mousePos.Y);
            }

            e.Handled = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling PreviewMouseWheel on MapScrollViewer.");
        }
    }

    private void MapScroll_ScrollChanged(object sender, ScrollChangedEventArgs e) {
        try {
            if (DataContext is MapViewModel vm) {
                // We track these now so the HUD knows the dimensions of the visible "window" 
                // into the map, allowing us to keep the text centered.
                vm.ViewportWidth = e.ViewportWidth;
                vm.ViewportHeight = e.ViewportHeight;
                // If you still want the text to "hide" when the map is small and centered:
                vm.HorizontalScrollOffset = e.HorizontalOffset;
                vm.VerticalScrollOffset = e.VerticalOffset;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling ScrollChanged on MapScrollViewer.");
        }
    }

    private void MapWindow_Loaded(object sender, RoutedEventArgs e) {
        Deactivated += HandleWindowDeactivated;
        Closed += (s, ev) => {
            Deactivated -= HandleWindowDeactivated;
            MouseHook.Stop(); // Clean up hook
        };

        // Listen for low-level double-click or Ctrl+click events
        MouseHook.DoubleClickedOrCtrlClicked -= OnGlobalMouseTrigger;
        MouseHook.DoubleClickedOrCtrlClicked += OnGlobalMouseTrigger;
        MouseHook.Start();
    }

    private bool _isUpdatingState = false;

    private void OnGlobalMouseTrigger(NativeMethods.Win32Point pt) {
        // Prevent re-entry if we are already processing a state change
        if (_isUpdatingState) return;

        Dispatcher.Invoke(() => {
            if (DataContext is not MapViewModel vm) return;
            if (!IsLoaded) return;

            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd)) return;

            if (NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect)) {
                bool isInsideWindow = (pt.X >= rect.Left && pt.X <= rect.Right &&
                                       pt.Y >= rect.Top && pt.Y <= rect.Bottom);

                // Only act if the state actually needs to change
                if (isInsideWindow && !vm.IsHovered) {
                    try {
                        _isUpdatingState = true;

                        PauseHoverTracking();
                        vm.IsHovered = true;
                        UpdateKeyboardClickThrough();
                    }
                    finally {
                        _isUpdatingState = false;
                    }
                }
            }
        });
    }

    #region Stamp Helpers

    private void OnLabelStamped(object? s, MapTextStampEventArgs a) {
        try {
            if (DataContext is not MapViewModel vm || vm.MapImage == null) return;

            WriteableBitmap? targetWriteableBmp = vm.MapImage as WriteableBitmap;

            // 1. If it's already a WriteableBitmap, we can use it directly
            if (targetWriteableBmp == null) {
                BitmapSource sourceToUse = vm.MapImage;

                // 2. Check if the format is standard 32-bit. If not, normalize it.
                if (vm.MapImage.Format != PixelFormats.Pbgra32 && vm.MapImage.Format != PixelFormats.Bgra32) {
                    FormatConvertedBitmap convertedBmp = new FormatConvertedBitmap();
                    convertedBmp.BeginInit();
                    convertedBmp.Source = vm.MapImage;
                    convertedBmp.DestinationFormat = PixelFormats.Pbgra32;
                    convertedBmp.EndInit();

                    sourceToUse = convertedBmp;
                }

                // 3. Initialize the mutable WriteableBitmap with our safely formatted source
                targetWriteableBmp = new WriteableBitmap(sourceToUse);
            }

            // 4. Render the stamp
            DrawMapHelpers.BurnTextToBitmap(targetWriteableBmp, a);

            // 5. Update UI and VM
            vm.MapImage = targetWriteableBmp;
            MapImageElement.Source = vm.MapImage;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing OnLabelStamped callback.");
        }
    }

    private void OnCircleStamped(object? s, MapTextStampEventArgs a) {
        try {
            if (DataContext is not MapViewModel vm || vm.MapImage == null) return;

            WriteableBitmap? targetWriteableBmp = vm.MapImage as WriteableBitmap;

            // 1. If it's already a WriteableBitmap, we can use it directly
            if (targetWriteableBmp == null) {
                BitmapSource sourceToUse = vm.MapImage;

                // 2. Check if the format is standard 32-bit. If not, normalize it.
                if (vm.MapImage.Format != PixelFormats.Pbgra32 && vm.MapImage.Format != PixelFormats.Bgra32) {
                    FormatConvertedBitmap convertedBmp = new FormatConvertedBitmap();
                    convertedBmp.BeginInit();
                    convertedBmp.Source = vm.MapImage;
                    convertedBmp.DestinationFormat = PixelFormats.Pbgra32;
                    convertedBmp.EndInit();

                    sourceToUse = convertedBmp;
                }

                // 3. Initialize the mutable WriteableBitmap with our safely formatted source
                targetWriteableBmp = new WriteableBitmap(sourceToUse);
            }

            // 4. Render the stamp
            DrawMapHelpers.BurnCircleToBitmap(targetWriteableBmp, a);

            // 5. Update UI and VM
            vm.MapImage = targetWriteableBmp;
            MapImageElement.Source = vm.MapImage;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing OnCircleStamped callback.");
        }
    }

    private void OnCircleMarkerStamped(object? s, MapTextStampEventArgs a) {
        try {
            if (DataContext is not MapViewModel vm || vm.MapImage == null) return;

            WriteableBitmap? targetWriteableBmp = vm.MapImage as WriteableBitmap;

            // 1. If it's already a WriteableBitmap, we can use it directly
            if (targetWriteableBmp == null) {
                BitmapSource sourceToUse = vm.MapImage;

                // 2. Check if the format is standard 32-bit. If not, normalize it.
                if (vm.MapImage.Format != PixelFormats.Pbgra32 && vm.MapImage.Format != PixelFormats.Bgra32) {
                    FormatConvertedBitmap convertedBmp = new FormatConvertedBitmap();
                    convertedBmp.BeginInit();
                    convertedBmp.Source = vm.MapImage;
                    convertedBmp.DestinationFormat = PixelFormats.Pbgra32;
                    convertedBmp.EndInit();

                    sourceToUse = convertedBmp;
                }

                // 3. Initialize the mutable WriteableBitmap with our safely formatted source
                targetWriteableBmp = new WriteableBitmap(sourceToUse);
            }

            // 4. Render the stamp
            DrawMapHelpers.BurnCircleMarkerToBitmap(targetWriteableBmp, a);

            // 5. Update UI and VM
            vm.MapImage = targetWriteableBmp;
            MapImageElement.Source = vm.MapImage;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing OnCircleMarkerStamped callback.");
        }
    }

    private T? FindParent<T>(DependencyObject child) where T : DependencyObject {
        // Get the immediate visual parent of the clicked element
        DependencyObject parentObject = VisualTreeHelper.GetParent(child);
        // If we hit the top of the tree without finding it, return null
        if (parentObject == null) return null;
        // If the parent matches the type we are looking for (EditableMapText), return it!
        if (parentObject is T parent) return parent;
        // Otherwise, recursively move up to the next parent level
        return FindParent<T>(parentObject);
    }

    #endregion

    private void OnResetMapClicked(object sender, RoutedEventArgs e) {
        try {
            if (DataContext is MapViewModel vm) {
                // Restore the pristine original image
                vm.MapImage = vm.OriginalMapImage;
                MapImageElement.Source = vm.MapImage;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing OnResetMapClicked.");
        }
    }

    private void HandleWindowDeactivated(object? sender, EventArgs e) {
        try {
            if (System.Windows.Application.Current.MainWindow?.DataContext is MainViewModel mainVm &&
                mainVm.Settings.KeyboardClickThrough) {
                KeepOnTop();
            }
        }
        catch (Exception ex) {
            Log.Warning(ex, "Error handling window deactivation in MapWindow.");
        }
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) {
        try {
            WindowState = WindowState.Maximized;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error maximizing MapWindow.");
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) {
        try {
            WindowState = WindowState.Minimized;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error minimizing MapWindow.");
        }
    }

    private void NormalButton_Click(object sender, RoutedEventArgs e) {
        try {
            WindowState = WindowState.Normal;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error restoring MapWindow size.");
        }
    }

    protected override void OnBeforeCleanup() {
        Cleanup();
    }

    private void PickImage_Click(object sender, RoutedEventArgs e) {
        try {
            if (DataContext is not MapViewModel pickVm) return;

            if (pickVm.IsDrawModeActive) {
                pickVm.StopDrawMode();
                StatusTextBlock.Text = "Status: Drawing stopped.";
            }

            IsDialogActive = true;
            Window? helperWindow = null;

            //Note: We are dealing with focus issues, preventing the app from stealing
            //focus from the underlying game for as long as possible.
            //IF they press cancel, they never have to leave their keyboard.
            try {
                ConfigureDialogToHaveAValidOwner(this, out helperWindow);

                var openFileDialog = new Microsoft.Win32.OpenFileDialog {
                    Filter = "Image files (*.png;*.jpeg;*.jpg;*.bmp)|*.png;*.jpeg;*.jpg;*.bmp|All files (*.*)|*.*",
                    CheckFileExists = true,
                    CheckPathExists = true
                };

                bool? dialogResult;
                try {
                    dialogResult = helperWindow != null
                        ? openFileDialog.ShowDialog(helperWindow)
                        : openFileDialog.ShowDialog();
                }
                catch (Exception ex) {
                    Log.Error(ex, "Error displaying file picker in PickImage_Click.");
                    MessageBox.Show($"Unable to display the file picker:\n{ex.Message}",
                        "Dialog Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (dialogResult == true) {
                    string selectedPath = openFileDialog.FileName;

                    if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath)) {
                        MessageBox.Show("The selected image file could not be found or accessed.",
                            "File Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    try {
                        // Backup old path in case loading fails so we can revert cleanly
                        pickVm.Settings ??= new MapSettings();
                        pickVm.Settings.ImagePath = selectedPath;

                        pickVm.LoadImage();
                        // Perform state updates only after LoadImage succeeds
                        ClearCalibration(pickVm.Settings);

                        if (StatusTextBlock != null) {
                            StatusTextBlock.Text = "Status: Image loaded. Please calibrate.";
                        }

                        // 3. Prompt user for calibration
                        const string message =
                            "Image loaded successfully. Please calibrate the map.\r\n\r\n" +
                            "You will pick two points on the map and identify their coordinates. " +
                            "With that, the map knows where you are and can show destinations you've stored.\r\n\r\n" +
                            "See status message for guidance.\r\n\r\n" +
                            "Do you want to go ahead and calibrate at this time?";
                        const string caption = "Image Loaded";

                        var result = MessageBox.Show(message, caption, MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes) {
                            StartCalibration();
                        }
                    }
                    catch (OutOfMemoryException ex) {
                        Log.Error(ex, "Selected image is too large: {Path}", selectedPath);
                        MessageBox.Show("The selected image is too large or corrupt to decode.",
                            "Memory / Image Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    catch (FileFormatException ex) {
                        Log.Error(ex, "Unsupported file format: {Path}", selectedPath);
                        MessageBox.Show("The selected file is not a valid or supported image format.",
                            "Unsupported Format", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    catch (UnauthorizedAccessException ex) {
                        Log.Error(ex, "Access denied reading image file: {Path}", selectedPath);
                        MessageBox.Show("Access denied. You do not have permission to read this file.",
                            "Permission Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    catch (Exception ex) {
                        Log.Error(ex, "Unexpected error loading image in PickImage_Click.");
                        MessageBox.Show($"An unexpected error occurred while loading the image:\n{ex.Message}",
                            "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            finally {
                // ALWAYS close the helper to prevent memory leaks
                helperWindow?.Close();
                IsDialogActive = false;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling PickImage_Click.");
        }
    }

    // Call this whenever the Zoom property changes
    private void RefreshPopup() {
        try {
            if (WakeHintPopup.IsOpen) {
                var offset = WakeHintPopup.HorizontalOffset;
                WakeHintPopup.HorizontalOffset = offset + 0.01;
                WakeHintPopup.HorizontalOffset = offset;
            }
        }
        catch (Exception ex) {
            Log.Debug(ex, "Error refreshing WakeHintPopup position.");
        }
    }

    private void StartDrawMode_Click(object sender, RoutedEventArgs e) {
        try {
            if (DataContext is not MapViewModel vm) return;
            IsDialogActive = true;
            try {
                var inputDialog = new InputDialog("Enter a name for the draw map:", "Start Drawing", "") {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                inputDialog.ShowDialog();
                if (inputDialog.ManualDialogResult != true) return;

                var mapName = inputDialog.Answer.Trim();
                if (string.IsNullOrEmpty(mapName)) return;

                foreach (char c in Path.GetInvalidFileNameChars())
                    mapName = mapName.Replace(c, '_');

                vm.StartDrawMode(mapName);
                StatusTextBlock.Text = $"Status: Drawing mode active — {mapName}";
            }
            finally {
                IsDialogActive = false;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in StartDrawMode_Click.");
        }
    }

    private void ToggleDrawMode_Click(object sender, RoutedEventArgs e) {
        try {
            if (DataContext is not MapViewModel vm) return;

            if (string.IsNullOrWhiteSpace(vm.MapName)) return;
            if (!vm.IsDrawModeActive) {
                var mapName = Path.GetFileNameWithoutExtension(vm.MapName);
                vm.StartDrawMode(mapName);
                StatusTextBlock.Text = $"Status: Drawing mode active — {mapName}";
            }
            else {
                vm.StopDrawMode();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in ToggleDrawMode_Click.");
        }
    }

    private void DrawModeAddCircle_Click(object sender, RoutedEventArgs e) {
        try {
            if (DataContext is not MapViewModel vm) return;
            if (string.IsNullOrWhiteSpace(vm.MapName) || !vm.IsDrawModeActive) return;

            StatusTextBlock.Text = "Status: Pick a point to add circle.";
            _isPickingCircleLocation = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in DrawModeAddCircle_Click.");
        }
    }

    private void DrawModeAddEllipse_Click(object sender, RoutedEventArgs e) {
        try {
            if (DataContext is not MapViewModel vm) return;
            if (string.IsNullOrWhiteSpace(vm.MapName) || !vm.IsDrawModeActive) return;

            StatusTextBlock.Text = "Status: Pick a point to add ellipse.";
            _isPickingEllipseLocation = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in DrawModeAddEllipse_Click.");
        }
    }

    private void DrawModeAddText_Click(object sender, RoutedEventArgs e) {
        try {
            if (DataContext is not MapViewModel vm) return;
            if (string.IsNullOrWhiteSpace(vm.MapName) || !vm.IsDrawModeActive) return;

            StatusTextBlock.Text = "Status: Pick a point to add text.";
            _isPickingTextLocation = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in DrawModeAddText_Click.");
        }
    }

    private void StopDrawMode_Click(object sender, RoutedEventArgs e) {
        try {
            if (DataContext is not MapViewModel vm) return;
            vm.StopDrawMode();
            StatusTextBlock.Text = "Status: Drawing stopped. Map saved.";
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in StopDrawMode_Click.");
        }
    }

    private void SaveMap_Click(object sender, RoutedEventArgs e) {
        try {
            if (DataContext is not MapViewModel vm) return;
            vm.Settings ??= new MapSettings();

            if (string.IsNullOrEmpty(vm.Settings.ImagePath) || !File.Exists(vm.Settings.ImagePath)) {
                MessageBox.Show("No image loaded to save.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var currentFileName = Path.GetFileNameWithoutExtension(vm.Settings.ImagePath);
            // Set the owner to the MainWindow BEFORE calling ShowDialog()
            // You can access the MainWindow via Application.Current.MainWindow
            var inputDialog = new InputDialog("Enter a name for this map:", "Save Map", currentFileName)
                { Owner = System.Windows.Application.Current.MainWindow };

            inputDialog.ShowDialog();

            // Check your manual property instead of the built-in DialogResult
            if (inputDialog.ManualDialogResult != true) {
                return;
            }

            var newName = inputDialog.Answer.Trim();
            if (string.IsNullOrEmpty(newName)) {
                MessageBox.Show("Map name cannot be empty.", "Save Error", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (newName.Length > 100) {
                MessageBox.Show("Map name is too long. Please use a shorter name (under 100 characters).", "Save Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Remove invalid characters
            foreach (char c in Path.GetInvalidFileNameChars()) {
                newName = newName.Replace(c, '_');
            }

            var extension = Path.GetExtension(vm.Settings.ImagePath);
            var mapsDir = Path.Combine(NativeMethods.AppFolder(), "maps");
            if (!Directory.Exists(mapsDir)) {
                Directory.CreateDirectory(mapsDir);
            }

            var destImagePath = Path.Combine(mapsDir, newName + extension);

            // Final path length check
            if (destImagePath.Length >= 255) {
                MessageBox.Show("The resulting file path is too long for Windows. Please use a shorter name.",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try {
                if (vm.Settings.ImagePath != destImagePath) {
                    File.Copy(vm.Settings.ImagePath, destImagePath, true);
                }

                var configPath = Path.Combine(mapsDir, newName + ".json");
                var json = JsonSerializer.Serialize(vm.Settings, new JsonSerializerOptions { WriteIndented = true });

                // Atomic write for map configuration
                var tempPath = configPath + ".tmp";
                File.WriteAllText(tempPath, json);
                if (File.Exists(configPath)) {
                    File.Replace(tempPath, configPath, configPath + ".old");
                    try {
                        File.Delete(configPath + ".old");
                    }
                    catch (Exception ex) {
                        Log.Warning(ex, "Could not delete temporary backup config file: {Path}", configPath + ".old");
                    }
                }
                else {
                    File.Move(tempPath, configPath);
                }

                vm.Settings.ImagePath = destImagePath;
                StatusTextBlock.Text = $"Status: Map saved as {newName}";
            }
            catch (UnauthorizedAccessException ex) {
                Log.Error(ex, "Access denied saving map to '{Path}'.", destImagePath);
                MessageBox.Show("Access denied. Please ensure you have permission to write to the application folder.",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (IOException ioex) {
                Log.Error(ioex, "IO Error saving map to '{Path}'.", destImagePath);
                MessageBox.Show($"IO Error saving map: {ioex.Message}", "Save Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in SaveMap_Click.");
        }
    }

    public void SaveWindowPlacement() {
        try {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.WINDOWPLACEMENT placement = new NativeMethods.WINDOWPLACEMENT();
            placement.length = Marshal.SizeOf(placement);

            if (NativeMethods.GetWindowPlacement(hwnd, ref placement)) {
                var rect = placement.rcNormalPosition;

                // --- DPI ADJUSTMENT START ---
                // Get the scaling factor (e.g., 1.5 for 150%)
                PresentationSource? source = PresentationSource.FromVisual(this);
                double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

                if (DataContext is MapViewModel vm) {
                    // Convert Physical Pixels back to WPF DIPs
                    if (vm.AppSettings.MapWindowPlacement == null) {
                        vm.AppSettings.MapWindowPlacement = new WindowPlacement {
                            Width = 800,
                            Height = 600
                        };
                    }

                    if (vm.AppSettings.MapWindowPlacement.State == WindowState.Minimized) {
                        vm.AppSettings.MapWindowPlacement.Top = rect.Top / dpiY;
                        vm.AppSettings.MapWindowPlacement.Left = rect.Left / dpiX;
                        vm.AppSettings.MapWindowPlacement.Width = (rect.Right - rect.Left) / dpiX;
                        vm.AppSettings.MapWindowPlacement.Height = (rect.Bottom - rect.Top) / dpiY;
                        vm.AppSettings.MapWindowPlacement.State = WindowState.Normal;
                    }

                    vm.SaveSettings();
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error saving window placement in MapWindow.");
        }
    }

    private void SaveCurrentMap() {
        if (DataContext is not MapViewModel vm) return;

        try {
            vm.Settings ??= new MapSettings();
            if (vm.FogImage != null && !string.IsNullOrEmpty(vm.FogOfWarFilePath)) {
                ImageHelpers.SaveWriteableBitMap(vm.FogOfWarFilePath, vm.FogImage.Clone());
            }

            vm.StopFading();

            var mapsDir = Path.Combine(NativeMethods.AppFolder(), "maps");
            if (!Directory.Exists(mapsDir)) {
                Directory.CreateDirectory(mapsDir);
            }

            if (!string.IsNullOrEmpty(vm.MapPath) && vm.Settings.IsCalibrated && File.Exists(vm.MapPath)) {
                var configPath = Path.Combine(mapsDir, Path.GetFileNameWithoutExtension(vm.MapPath) + ".json");
                var json = JsonSerializer.Serialize(vm.Settings, new JsonSerializerOptions { WriteIndented = true });

                // Atomic write for map configuration
                var tempPath = configPath + ".tmp";
                File.WriteAllText(tempPath, json);
                if (File.Exists(configPath)) {
                    File.Replace(tempPath, configPath, configPath + ".old");
                    try {
                        File.Delete(configPath + ".old");
                    }
                    catch (Exception ex) {
                        Log.Warning(ex, "Could not delete temporary backup config file: {Path}", configPath + ".old");
                    }
                }
                else {
                    File.Move(tempPath, configPath);
                }
            }
        }
        catch (UnauthorizedAccessException ex) {
            Log.Warning(ex, "Access denied saving current map configuration.");
        }
        catch (IOException ioex) {
            Log.Warning(ioex, "IO Exception saving current map configuration.");
        }
        catch (Exception ex) {
            Log.Error(ex, "Unexpected error saving current map.");
        }
    }

    private void SetDestination_Click(object sender, RoutedEventArgs e) {
        try {
            _isSettingDestination = SetDestinationMenuItem.IsChecked;
            if (_isSettingDestination) {
                _isCalibrating = false;
                _isAddingPin = false;
                AddPinMenuItem.IsChecked = false;
                StatusTextBlock.Text = "Status: Click on map to set destination";
            }
            else {
                StatusTextBlock.Text = "Status: Ready";
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing SetDestination_Click.");
        }
    }

    private void ShowControls_Click(object sender, RoutedEventArgs e) {
        try {
            if (DataContext is MapViewModel vm) {
                PauseHoverTracking();
                // 1. Wake the UI
                bool wasHidden = !vm.IsHovered;
                vm.IsHovered = true;

                // 2. Edge Case: Should we drop a marker?
                // If the UI was hidden, maybe the user just wanted to see the map.
                // You can "eat" the click so a marker isn't accidentally placed 
                // the moment the UI appears.
                if (wasHidden) {
                    e.Handled = true;
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing ShowControls_Click.");
        }
    }

    private void StartCalibration() {
        if (DataContext is not MapViewModel vm) return;
        _savedFogSettings = vm.ShowFogOfWar;
        vm.ShowFogOfWar = false;
        _isCalibrating = true;
        _isSettingDestination = false;
        SetDestinationMenuItem.IsChecked = false;
        _isAddingPin = false;
        AddPinMenuItem.IsChecked = false;
        _calibrationStep = 1;
        StatusTextBlock.Text = "Status: Click Point 1 on map";
    }

    private void StartManualDrag() {
        // Capture the window we want to return focus to
        try {
            if (System.Windows.Application.Current.MainWindow?.DataContext is MainViewModel mainVm &&
                mainVm.Settings.KeyboardClickThrough) {
                _preDragForegroundWindow = NativeMethods.GetForegroundWindow();
            }

            _lastMousePos = System.Windows.Forms.Cursor.Position;

            _dragTimer = new DispatcherTimer {
                Interval = TimeSpan.FromMilliseconds(1)
            };
            _dragTimer.Tick += DragTimer_Tick;
            _dragTimer.Start();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error initiating manual drag sequence.");
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        try {
            // Start the timer-based drag instead of DragMove()
            StartManualDrag();
            e.Handled = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling TitleBar_MouseLeftButtonDown.");
        }
    }

    private void UpdateKeyboardClickThrough() {
        try {
            if (_hwnd == IntPtr.Zero) return;

            int extendedStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);

            if (DataContext is MapViewModel vm && vm.AppSettings.KeyboardClickThrough) {
                // Keep NOACTIVATE so clicking controls doesn't steal game focus
                extendedStyle |= NativeMethods.WS_EX_NOACTIVATE;

                // When controls are hidden, set WS_EX_TRANSPARENT so ALL right/left clicks
                // pass straight through to the mobs in game
                if (!vm.IsHovered) {
                    extendedStyle |= NativeMethods.WS_EX_TRANSPARENT;
                }
                else {
                    extendedStyle &= ~NativeMethods.WS_EX_TRANSPARENT;
                }

                NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, extendedStyle);
                KeepOnTop();
            }
            else {
                NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE,
                    extendedStyle & ~NativeMethods.WS_EX_NOACTIVATE & ~NativeMethods.WS_EX_TRANSPARENT);
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error applying extended window styles in UpdateKeyboardClickThrough.");
        }
    }

    public void ValidateAndSetWindowPosition(Window window, double savedLeft, double savedTop) {
        try {
            // 1. Get the total bounds of all monitors combined
            double virtualLeft = SystemParameters.VirtualScreenLeft;
            double virtualTop = SystemParameters.VirtualScreenTop;
            double virtualWidth = SystemParameters.VirtualScreenWidth;
            double virtualHeight = SystemParameters.VirtualScreenHeight;

            // 2. Check if the saved position is completely outside the virtual screen
            // We add a small buffer (like 50px) so the title bar is always reachable
            bool isVisible = (savedLeft >= virtualLeft && savedLeft < (virtualLeft + virtualWidth - 50)) &&
                             (savedTop >= virtualTop && savedTop < (virtualTop + virtualHeight - 50));

            if (isVisible) {
                window.Left = savedLeft;
                window.Top = savedTop;
            }
            else {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }
        catch (Exception ex) {
            // Fallback: Center on the Primary Monitor
            Log.Warning(ex, "Error validating window bounds; defaulting to CenterScreen.");
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void DrawColor_Click(object sender, MouseButtonEventArgs e) {
        try {
            if (sender is not Border border || !int.TryParse(border.Tag?.ToString(), out int index)) return;
            if (DataContext is MapViewModel vm) {
                vm.SetDrawColor(index);
                UpdateDrawColorBoxes(index);
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing DrawColor_Click.");
        }
    }

    private void UpdateDrawColorBoxes(int selectedIndex) {
        Border[] boxes = [
            DrawColorBox0, DrawColorBox1, DrawColorBox2, DrawColorBox3,
            DrawColorBox4, DrawColorBox5, DrawColorBox6, DrawColorBox7,
            DrawColorBox8, DrawColorBox9, DrawColorBox10, DrawColorBox11,
            DrawColorBox12
        ];
        foreach (var box in boxes) {
            if (int.TryParse(box.Tag?.ToString(), out int idx))
                box.BorderBrush = idx == selectedIndex
                    ? System.Windows.Media.Brushes.White
                    : System.Windows.Media.Brushes.Transparent;
        }
    }

    private void DrawSize_Click(object sender, MouseButtonEventArgs e) {
        try {
            if (sender is not Border border || !int.TryParse(border.Tag?.ToString(), out int mode)) return;
            if (DataContext is MapViewModel vm) {
                vm.DrawSizeMode = mode;
                UpdateDrawSizeBoxes(mode);
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing DrawSize_Click.");
        }
    }

    private void UpdateDrawSizeBoxes(int selectedMode) {
        Border[] boxes = [DrawSizeBoxSmall, DrawSizeBoxDefault, DrawSizeBoxPlus3, DrawSizeBoxPlus5, DrawSizeBoxPlus10];
        foreach (var box in boxes) {
            if (int.TryParse(box.Tag?.ToString(), out int mode))
                box.BorderBrush = mode == selectedMode
                    ? System.Windows.Media.Brushes.White
                    : System.Windows.Media.Brushes.Transparent;
        }
    }

    private void DrawAntiAlias_Click(object sender, MouseButtonEventArgs e) {
        try {
            if (DataContext is MapViewModel vm) {
                vm.DrawAntiAlias = !vm.DrawAntiAlias;
                DrawAntiAliasBox.BorderBrush = vm.DrawAntiAlias
                    ? System.Windows.Media.Brushes.White
                    : System.Windows.Media.Brushes.Transparent;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing DrawAntiAlias_Click.");
        }
    }

    private void DrawLineMode_Click(object sender, MouseButtonEventArgs e) {
        try {
            if (DataContext is MapViewModel vm) {
                vm.DrawLineMode = !vm.DrawLineMode;
                DrawLineModeBox.BorderBrush = vm.DrawLineMode
                    ? System.Windows.Media.Brushes.White
                    : System.Windows.Media.Brushes.Transparent;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing DrawLineMode_Click.");
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (DataContext is not MapViewModel vm) return;
        vm.Settings ??= new MapSettings();

        // Dynamically toggle WS_EX_TRANSPARENT when mouse enters/exits or controls are toggled
        if (e.PropertyName == nameof(MapViewModel.IsHovered) || e.PropertyName == nameof(MapViewModel.UIVisibility)) {
            UpdateKeyboardClickThrough();
        }

        if (e.PropertyName == nameof(MapViewModel.IsDrawModeActive) && vm.IsDrawModeActive) {
            UpdateDrawColorBoxes(0);
            UpdateDrawSizeBoxes(0);
            DrawLineModeBox.BorderBrush = System.Windows.Media.Brushes.Transparent;
            DrawAntiAliasBox.BorderBrush = System.Windows.Media.Brushes.White;
        }

        if (e.PropertyName == nameof(MapViewModel.IsFollowModeActive)) {
            if (vm.IsFollowModeActive) {
                // Turning ON: Save current zoom before jumping to FollowZoom
                // vm.PreviousZoom = MapScaleTransform.ScaleX;
                // Entering Follow Mode: Save what we had
                vm.PreviousZoom = vm.Settings.ZoomLevel;
                CenterMapOnMarker();
            }
            else {
                // Exiting Follow Mode: Restore the previous zoom
                vm.Settings.ZoomLevel = vm.PreviousZoom;
                // Optional: If you want to stop the "jump," do NOT call CenterMapOnMarker here.
                // Just let the map stay where it was at the normal zoom level.
            }
        }
        else if (vm.IsFollowModeActive && (e.PropertyName == "MarkerX" || e.PropertyName == "MarkerY")) {
            CenterMapOnMarker();
        }
    }

    private void WakeHintPopup_Opened(object sender, EventArgs e) {
        try {
            // Get the handle for the Popup's window
            if (sender is Popup popup) {
                double offset = popup.VerticalOffset;
                popup.VerticalOffset = offset + 0.01;
                popup.VerticalOffset = offset;
            }
        }
        catch (Exception ex) {
            Log.Debug(ex, "Error executing WakeHintPopup_Opened.");
        }
    }
}