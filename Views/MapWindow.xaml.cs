using System.ComponentModel;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Controls.Primitives;
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

    public MapWindow(MapViewModel viewModel) {
        InitializeComponent();
        DataContext = viewModel;

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

        Loaded += MapWindow_Loaded;
        SourceInitialized += MapWindow_SourceInitialized;

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _hoverTimer.Tick += HoverTimer_Tick;
        _hoverTimer.Start();
    }

    private void AddPin_Click(object sender, RoutedEventArgs e) {
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

    private void Calibrate_Click(object sender, RoutedEventArgs e) {
        if (_isCalibrating) {
            CancelCalibration();
            return;
        }

        StartCalibration();
    }

    private void CancelCalibration() {
        if (_isCalibrating) {
            if (_savedFogSettings.HasValue) {
                var vm = (MapViewModel)DataContext;
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
        // Use the current scale of the transform
        double currentZoom = vm.Settings.ZoomLevel; // Use the VM value directly

        double targetX = (vm.MarkerX * currentZoom) - (MapScrollViewer.ActualWidth / 2);
        double targetY = (vm.MarkerY * currentZoom) - (MapScrollViewer.ActualHeight / 2);

        MapScrollViewer.ScrollToHorizontalOffset(targetX);
        MapScrollViewer.ScrollToVerticalOffset(targetY);
    }

    public void Cleanup() {
        try {
            SaveWindowPlacement();
        }
        catch {
            //ignore
        }

        try {
            _hoverTimer?.Stop();
            _dragTimer?.Stop();
        }
        catch {
            //ignore
        }

        try {
            SaveCurrentMap();
        }
        catch {
            //ignore
        }
    }

    private void ClearCalibration(MapSettings settings) {
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
        Close();
    }

    private void CreateChallenge_Click(object sender, RoutedEventArgs e) {
        new ChallengeDesignerWindow().Show();
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

        // Move logic
        System.Drawing.Point currentMousePos = System.Windows.Forms.Cursor.Position;

        var source = System.Windows.PresentationSource.FromVisual(this);
        if (source == null) return;

        // 1.0 provides a safe math fallback
        var dpiX = source.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var dpiY = source.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        Left += (currentMousePos.X - _lastMousePos.X) / dpiX;
        Top += (currentMousePos.Y - _lastMousePos.Y) / dpiY;

        _lastMousePos = currentMousePos;
    }

    //to allow click through I changed this to the one below
    // private void HoverTimer_Tick(object sender, EventArgs e) {
    //     if (DataContext is MapViewModel vm) {
    //         System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
    //         var topLeft = this.PointToScreen(new System.Windows.Point(0, 0));
    //         
    //         var bounds = new Rectangle((int)topLeft.X, (int)topLeft.Y, (int)this.ActualWidth, (int)this.ActualHeight);
    //         bool isInside = bounds.Contains(cursor.X, cursor.Y);
    //
    //         if (isInside) {
    //             // Mouse is inside, reset the "outside" timer and set hovered
    //             _lastMouseOutsideTime = DateTime.MinValue;
    //             if (!vm.IsHovered) vm.IsHovered = true;
    //         }
    //         else {
    //             // Mouse is outside
    //             if (_lastMouseOutsideTime == DateTime.MinValue)
    //                 _lastMouseOutsideTime = DateTime.Now;
    //
    //             // Only hide if the mouse has been outside for 300ms
    //             //As you drag around the window, the hover state will be maintained for a short period to avoid flickering
    //             if (DateTime.Now - _lastMouseOutsideTime > TimeSpan.FromMilliseconds(300)) {
    //                 if (vm.IsHovered) vm.IsHovered = false;
    //             }
    //         }
    //     }
    // }


    //with the reactivation taking place via CTRL + LeftClick this is replaced with better performing version blow
// private void HoverTimer_Tick(object sender, EventArgs e) {
//     if (DataContext is MapViewModel vm) {
//         // 1. Get current mouse position in screen coordinates
//         System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
//
//         // 2. Calculate Window Bounds (for Hiding)
//         var windowTopLeft = this.PointToScreen(new System.Windows.Point(0, 0));
//         var windowBounds = new Rectangle((int)windowTopLeft.X, (int)windowTopLeft.Y, (int)this.ActualWidth, (int)this.ActualHeight);
//         
//         // 3. Calculate Map Bounds (for Showing)
//         // 'MapScroller' is the name of your ScrollViewer/Map container
//         // var mapTopLeft = MapScrollViewer.PointToScreen(new System.Windows.Point(0, 0));
//         // var mapBounds = new Rectangle((int)mapTopLeft.X, (int)mapTopLeft.Y, (int)MapScrollViewer.ActualWidth, (int)MapScrollViewer.ActualHeight);
//         
//         Rect rect = MapImageElement.TransformToAncestor(this)
//             .TransformBounds(new Rect(0, 0, MapImageElement.ActualWidth, MapImageElement.ActualHeight));
//
// // Convert that Window-relative Rect to Screen Coordinates
//
//         var mapBounds = new Rectangle(
//             (int)(windowTopLeft.X + rect.X), 
//             (int)(windowTopLeft.Y + rect.Y), 
//             (int)rect.Width, 
//             (int)rect.Height);
//         
//         bool isInsideWindow = windowBounds.Contains(cursor.X, cursor.Y);
//         bool isInsideMap = mapBounds.Contains(cursor.X, cursor.Y);
//
//         if (isInsideMap) {
//             // Mouse is over the MAP: Wake up immediately
//             _lastMouseOutsideTime = DateTime.MinValue;
//             if (!vm.IsHovered) vm.IsHovered = true;
//         }
//         else if (isInsideWindow) {
//             // Mouse is in the Window "Dead Zone" (not over map)
//             if (vm.IsHovered) {
//                 // If UI is ALREADY on, keep it on (ignore the 'outside' timer)
//                 _lastMouseOutsideTime = DateTime.MinValue;
//             }
//             else {
//                 // If UI is OFF, staying in the dead zone keeps it OFF
//                 _lastMouseOutsideTime = DateTime.Now; 
//             }
//         }
//         else {
//             // Mouse is completely outside the Window
//             if (_lastMouseOutsideTime == DateTime.MinValue)
//                 _lastMouseOutsideTime = DateTime.Now;
//
//             // Grace period to prevent flickering during fast movements/drags
//             if (DateTime.Now - _lastMouseOutsideTime > TimeSpan.FromMilliseconds(300)) {
//                 if (vm.IsHovered) vm.IsHovered = false;
//             }
//         }
//     }
// }

    //Give some leeway for people to have their mouse leave the window before hiding portions of it
    private void HoverTimer_Tick(object? sender, EventArgs e) {
        if (DataContext is MapViewModel vm) {
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
            if (NativeMethods.GetCursorPos(out NativeMethods.Win32Point p)) {

                // 1. Convert the physical screen point (Win32) to a WPF Logical Point
                System.Windows.Point mousePoint = this.PointFromScreen(new System.Windows.Point(p.X, p.Y));

                // 2. Check bounds against ActualWidth/Height
                // Note: PointFromScreen handles the DPI scaling math for you.
                bool isInsideWindow = (mousePoint.X >= 0 && 
                                       mousePoint.X <= this.ActualWidth &&
                                       mousePoint.Y >= 0 && 
                                       mousePoint.Y <= this.ActualHeight);
                
                if (isInsideWindow) {
                    // Keep it alive
                    _lastMouseOutsideTime = DateTime.MinValue;
                    //vm.IsHovered = true; only clicking restores so dont set to true here
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
    }

    private void MapImageElement_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        // Handle Double Click
        if (e.ClickCount == 2) {
            if (DataContext is MapViewModel vm) {
                if (sender is IInputElement element) {
                    // Force the window to keep focus even if the 4K scaling 
                    // makes it think the mouse moved slightly off-element
                    Mouse.Capture(element); 
                }
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
        //Handle CTRL+Left click
        if (Keyboard.Modifiers == ModifierKeys.Control) {
            if (DataContext is MapViewModel vm) {
                if (sender is IInputElement element) {
                    // Force the window to keep focus even if the 4K scaling 
                    // makes it think the mouse moved slightly off-element
                    Mouse.Capture(element); 
                }
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
    }
    
    private void MapImageElement_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        Mouse.Capture(null);
    }
    
    private void MapWindow_SourceInitialized(object? sender, EventArgs e) {
        _hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(HwndHandler);

        // Initial application of the style
        UpdateKeyboardClickThrough();

        if (DataContext is MapViewModel vm) {
            // We need to listen to the global KeyboardClickThrough setting
            vm.AppSettings.PropertyChanged += (_, ev) => {
                if (ev.PropertyName == nameof(AppSettings.KeyboardClickThrough)) {
                    UpdateKeyboardClickThrough();
                }

                if (ev.PropertyName == nameof(AppSettings.Opacity)) {
                    // Update opacity if needed (already bound in XAML)
                }
            };
        }
    }

    private void KeepOnTop() {
        if (_hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    //control how the window reacts to being clicked, specifically preventing the window from
    //taking "focus" (becoming the active foreground window) in certain scenarios.
    //I want activity in this window to not steal focus from, say, another program like my gaming application.
    //So I can click but keep typing elsewhere.
    protected override IntPtr HwndHandler(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled) {
        // 1. Run the base logic first (checks for IsDialogActive)
        IntPtr result = base.HwndHandler(hwnd, msg, wparam, lparam, ref handled);

        // 2. If the base class already handled the message, return its result
        if (handled) {
            return result;
        }

        // 3. MapWindow specific logic
        if (msg == NativeMethods.WM_MOUSEACTIVATE) {
            if (DataContext is MapViewModel vm && vm.AppSettings.KeyboardClickThrough) {
                //Don't come to the front, don't make this my focussed window.
                // Return MA_NOACTIVATE and set handled = true to prevent stealing focus.
                //MA_NOACTIVATE tells the operating system to not activate (bring to the foreground/focus) a window when a user
                //clicks inside it, but to still process the mouse click (e.g., clicking a button inside that window still works)
                handled = true;
                return NativeMethods.MA_NOACTIVATE;
            }
        }

        if (msg == NativeMethods.WM_ACTIVATE) {
            //If for some reason I AM brought to the front (activated), immediately find the window that
            //was open before me and force the focus back to it. Conditional
            if ((int)wparam != NativeMethods.WA_INACTIVE) {
                if (DataContext is MapViewModel vm && vm.AppSettings.KeyboardClickThrough) {
                    System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] MapWindow activated. wparam: {wparam}");
                    // If we are being activated and click-through is on, push focus back to previous window.

                    // We try to restore to the window that WAS foreground BEFORE this activation happened.
                    // If we don't have one, we try the window that is currently foreground (if it's not us).
                    var foregroundWindow = NativeMethods.GetForegroundWindow();

                    nint targetWnd; // = IntPtr.Zero
                    if (_preDragForegroundWindow != IntPtr.Zero && _preDragForegroundWindow != _hwnd) {
                        targetWnd = _preDragForegroundWindow;
                    }
                    else if (foregroundWindow != _hwnd && foregroundWindow != IntPtr.Zero) {
                        targetWnd = foregroundWindow;
                    }
                    else {
                        // Fallback: Try to find the next window in the Z-order to give focus to.
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

    private void HideHint_Click(object sender, RoutedEventArgs e) {
        if (DataContext is MapViewModel vm) {
            vm.AppSettings.HideMapClickHint = true;
        }
    }

    private void LoadMap_Click(object sender, RoutedEventArgs e) {
        var vm = (MapViewModel)DataContext;
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
        catch {
            //ignore
        }

        IsDialogActive = true;
        Window? helperWindow = null;
        vm.Settings ??= new MapSettings();

        try {
            ConfigureDialogToHaveAValidOwner(this, out helperWindow);

            var openFileDialog = new Microsoft.Win32.OpenFileDialog {
                InitialDirectory = mapsDir,
                Filter = "Image files (*.png;*.jpeg;*.jpg;*.bmp)|*.png;*.jpeg;*.jpg;*.bmp|All files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog(helperWindow) == true) {
                vm.IsLoadingFile = true;
                var imagePath = openFileDialog.FileName;
                var configPath = Path.ChangeExtension(imagePath, ".json");

                if (File.Exists(configPath)) {
                    try {
                        var calibrated = vm.LoadImageConfig(imagePath);

                        if (calibrated) {
                            vm.IsLoadingFile = false;
                            vm.LoadImage();
                            vm.UpdateMarkers();
                            StatusTextBlock.Text = $"Status: Loaded {Path.GetFileName(imagePath)} with config.";
                        }
                    }
                    catch (Exception ex) {
                        MessageBox.Show($"Error loading config: {ex.Message}. Loading image only.");
                        vm.Settings.ImagePath = imagePath;
                        ClearCalibration(vm.Settings);
                        StatusTextBlock.Text = $"Status: Loaded {Path.GetFileName(imagePath)} (config error).";
                    }
                }
                else {
                    vm.Settings.ImagePath = imagePath;
                    ClearCalibration(vm.Settings);
                    StatusTextBlock.Text = $"Status: Loaded {Path.GetFileName(imagePath)} (no config).";
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

    private void MapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (System.Windows.Application.Current.MainWindow?.DataContext is MainViewModel mainVm &&
            mainVm.Settings.KeyboardClickThrough) {
            _preDragForegroundWindow = NativeMethods.GetForegroundWindow();
            if (_preDragForegroundWindow == _hwnd) _preDragForegroundWindow = IntPtr.Zero;
        }

        if (!_isCalibrating && !_isSettingDestination && !_isAddingPin) {
            _isDragging = true;
            _lastMousePosition = e.GetPosition(MapScrollViewer);
            MapCanvas.CaptureMouse();
            MapCanvas.Cursor = System.Windows.Input.Cursors.Hand;

            e.Handled = true;
            return;
        }

        var vm = (MapViewModel)DataContext;
        vm.Settings ??= new MapSettings();
        System.Windows.Point clickPoint = e.GetPosition(MapCanvas);

        if (_isSettingDestination) {
            var coords = vm.GetCoordinatesFromPixels(clickPoint.X, clickPoint.Y);
            if (coords.HasValue) {
                vm.SelectDestination(coords.Value);
                _isSettingDestination = false;
                SetDestinationMenuItem.IsChecked = false;
                StatusTextBlock.Text = "Status: Destination set";
            }

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

            return;
        }

        if (_calibrationStep == 1) {
            string suggestedCoords = vm.CurrentPosition.HasValue
                ? $"{vm.CurrentPosition.Value.X}, {vm.CurrentPosition.Value.Y}"
                : "0, 0";

            try {
                var inputDialog =
                    new InputDialog("Enter coordinates for Point 1 (x, y):", "Calibration Point 1", suggestedCoords)
                        { Owner = this };
                // Set the owner to the MainWindow BEFORE calling ShowDialog()
                // You can access the MainWindow via Application.Current.MainWindow
                inputDialog.Owner = System.Windows.Application.Current.MainWindow;

                IsDialogActive = true;
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
                        MessageBox.Show("Invalid coordinates format.");
                    }
                }
            }
            finally {
                IsDialogActive = false;
            }
        }
        else if (_calibrationStep == 2) {
            string suggestedCoords = vm.CurrentPosition.HasValue
                ? $"{vm.CurrentPosition.Value.X}, {vm.CurrentPosition.Value.Y}"
                : "0, 0";

            try {
                var inputDialog =
                    new InputDialog("Enter coordinates for Point 2 (x, y):", "Calibration Point 2", suggestedCoords)
                        { Owner = this };
                // Set the owner to the MainWindow BEFORE calling ShowDialog()
                // You can access the MainWindow via Application.Current.MainWindow
                inputDialog.Owner = System.Windows.Application.Current.MainWindow;

                IsDialogActive = true;
                inputDialog.ShowDialog();

                // Check your manual property instead of the built-in DialogResult
                if (inputDialog.ManualDialogResult == true) {
                    if (Scrubber.TryParse(inputDialog.Answer, "x y", out var coords)) {
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

                            var newName = Path.GetFileNameWithoutExtension(vm.MapPath);

                            // Remove invalid characters
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
                        MessageBox.Show("Invalid coordinates format.");
                    }
                }
            }
            finally {
                IsDialogActive = false;
            }
        }
    }

    private void MapCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        if (_isDragging) {
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

            e.Handled = true;
        }
    }

    private void MapCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) {
        var vm = (MapViewModel)DataContext;
        System.Windows.Point currentPoint = e.GetPosition(MapCanvas);
        vm.UpdateHoverCoordinates(currentPoint.X, currentPoint.Y);

        if (_isDragging) {
            System.Windows.Point currentScrollPoint = e.GetPosition(MapScrollViewer);
            var deltaX = currentScrollPoint.X - _lastMousePosition.X;
            var deltaY = currentScrollPoint.Y - _lastMousePosition.Y;

            MapScrollViewer.ScrollToHorizontalOffset(MapScrollViewer.HorizontalOffset - deltaX);
            MapScrollViewer.ScrollToVerticalOffset(MapScrollViewer.VerticalOffset - deltaY);

            _lastMousePosition = currentScrollPoint;
            e.Handled = true; // Added this
        }
    }

    private void MapScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
        var vm = (MapViewModel)DataContext;
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

    private void MapScroll_ScrollChanged(object sender, ScrollChangedEventArgs e) {
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

    private void MapWindow_Loaded(object sender, RoutedEventArgs e) {
        Deactivated += HandleWindowDeactivated;
        Closed += (s, e) => Deactivated -= HandleWindowDeactivated;
    }

    private void HandleWindowDeactivated(object? sender, EventArgs e) {
        if (System.Windows.Application.Current.MainWindow?.DataContext is MainViewModel mainVm &&
            mainVm.Settings.KeyboardClickThrough) {
            KeepOnTop();
        }
    }
    //Normal drag behavior abandoned due to issues related in preventing keys from passing through
    // private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
    //     IntPtr foregroundWindow = IntPtr.Zero;
    //     bool isClickThrough = false;
    //     if (System.Windows.Application.Current.MainWindow?.DataContext is MainViewModel mainVm &&
    //         mainVm.Settings.KeyboardClickThrough) {
    //         isClickThrough = true;
    //         foregroundWindow = Helpers.NativeMethods.GetForegroundWindow();
    //         if (foregroundWindow == _hwnd) foregroundWindow = IntPtr.Zero; // Don't restore to self
    //         _preDragForegroundWindow = foregroundWindow;
    //     }
    //
    //     try {
    //         DragMove();
    //     }
    //     catch (InvalidOperationException) {
    //         // DragMove can fail if mouse button was released too quickly
    //     }
    //
    //     if (isClickThrough) {
    //         IntPtr currentForeground = Helpers.NativeMethods.GetForegroundWindow();
    //         if (currentForeground == _hwnd) {
    //             if (_preDragForegroundWindow != IntPtr.Zero && _preDragForegroundWindow != _hwnd) {
    //                 Helpers.NativeMethods.SetForegroundWindow(_preDragForegroundWindow);
    //             }
    //             else {
    //                 IntPtr nextWnd = NativeMethods.GetWindow(_hwnd, (uint)NativeMethods.GW_HWNDNEXT);
    //                 if (nextWnd != IntPtr.Zero) Helpers.NativeMethods.SetForegroundWindow(nextWnd);
    //             }
    //         }
    //
    //         _preDragForegroundWindow = IntPtr.Zero;
    //     }
    //
    //     e.Handled = true;
    // }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) {
        WindowState = WindowState.Maximized;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) {
        WindowState = WindowState.Minimized;
    }

    private void NormalButton_Click(object sender, RoutedEventArgs e) {
        WindowState = WindowState.Normal;
    }

    protected override void OnBeforeCleanup() {
        Cleanup();
    }

    private void PickImage_Click(object sender, RoutedEventArgs e) {
        var pickVm = (MapViewModel)DataContext;
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
                Filter =
                    "Image files (*.png;*.jpeg;*.jpg;*.bmp;*.tiff;*.svg;*.gif)|*.png;*.jpeg;*.jpg;*.bmp;*.tiff;*.svg;*.gif"
            };

            if (openFileDialog.ShowDialog(helperWindow) == true) {
                var vm = (MapViewModel)DataContext;
                vm.Settings ??= new MapSettings();
                vm.Settings.ImagePath = openFileDialog.FileName;
                vm.LoadImage();
                ClearCalibration(vm.Settings);
                StatusTextBlock.Text = "Status: Image loaded. Please calibrate.";

                const string message =
                    "Image loaded successfully. Please calibrate the map.\r\n\r\nYou will pick two points on the map and identify their coordinates. With that, the map knows where you are and can show destinations you've stored.\r\n\r\nSee status message for guidance.\r\n\r\nDo you want to go ahead and calibrate at this time?";
                const string caption = "Image Loaded";
                const MessageBoxButton buttons = MessageBoxButton.YesNo;
                const MessageBoxImage icon = MessageBoxImage.Question;

                var result = MessageBox.Show(message, caption, buttons, icon);

                // Handle the result
                if (result == MessageBoxResult.Yes) {
                    StartCalibration();
                }
            }
        }
        finally {
            // ALWAYS close the helper to prevent memory leaks
            helperWindow?.Close();
            IsDialogActive = false;
        }
    }

    // Call this whenever the Zoom property changes
    private void RefreshPopup() {
        if (WakeHintPopup.IsOpen) {
            // This 'nudge' forces WPF to re-calculate the placement target's screen position
            var offset = WakeHintPopup.HorizontalOffset;
            WakeHintPopup.HorizontalOffset = offset + 0.01;
            WakeHintPopup.HorizontalOffset = offset;
        }
    }

    private void StartDrawMode_Click(object sender, RoutedEventArgs e) {
        var vm = (MapViewModel)DataContext;
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

    private void StopDrawMode_Click(object sender, RoutedEventArgs e) {
        var vm = (MapViewModel)DataContext;
        vm.StopDrawMode();
        StatusTextBlock.Text = "Status: Drawing stopped. Map saved.";
    }

    private void SaveMap_Click(object sender, RoutedEventArgs e) {
        var vm = (MapViewModel)DataContext;
        vm.Settings ??= new MapSettings();
        if (string.IsNullOrEmpty(vm.Settings.ImagePath) || !File.Exists(vm.Settings.ImagePath)) {
            MessageBox.Show("No image loaded to save.");
            return;
        }

        var currentFileName = Path.GetFileNameWithoutExtension(vm.Settings.ImagePath);
        var inputDialog = new InputDialog("Enter a name for this map:", "Save Map", currentFileName) { Owner = this };
        // Set the owner to the MainWindow BEFORE calling ShowDialog()
        // You can access the MainWindow via Application.Current.MainWindow
        inputDialog.Owner = System.Windows.Application.Current.MainWindow;
        inputDialog.ShowDialog();

        // Check your manual property instead of the built-in DialogResult
        if (inputDialog.ManualDialogResult != true) {
            return;
        }

        var newName = inputDialog.Answer.Trim();
        if (string.IsNullOrEmpty(newName)) {
            MessageBox.Show("Map name cannot be empty.");
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
        try {
            if (vm.Settings.ImagePath != destImagePath) {
                File.Copy(vm.Settings.ImagePath, destImagePath, true);
            }

            var configPath = Path.Combine(mapsDir, newName + ".json");
            var json = JsonSerializer.Serialize(vm.Settings);
            File.WriteAllText(configPath, json);

            vm.Settings.ImagePath = destImagePath;
            StatusTextBlock.Text = $"Status: Map saved as {newName}";
        }
        catch (Exception ex) {
            MessageBox.Show($"Error saving map: {ex.Message}");
        }
    }

    public void SaveWindowPlacement() {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.WINDOWPLACEMENT placement = new NativeMethods.WINDOWPLACEMENT();
        placement.length = Marshal.SizeOf(placement);

        if (NativeMethods.GetWindowPlacement(hwnd, ref placement)) {
            var rect = placement.rcNormalPosition;

            // --- DPI ADJUSTMENT START ---
            // Get the scaling factor (e.g., 1.5 for 150%)
            PresentationSource? source = PresentationSource.FromVisual(this);
            double dpiX = 1.0;
            double dpiY = 1.0;

            if (source?.CompositionTarget != null) {
                dpiX = source.CompositionTarget.TransformToDevice.M11;
                dpiY = source.CompositionTarget.TransformToDevice.M22;
            }
            // --- DPI ADJUSTMENT END ---

            if (DataContext is MapViewModel vm) {
                // Convert Physical Pixels back to WPF DIPs
                if (vm.AppSettings.MapWindowPlacement == null) {
                    vm.AppSettings.MapWindowPlacement = new WindowPlacement();
                    vm.AppSettings.MapWindowPlacement.Width = 800;
                    vm.AppSettings.MapWindowPlacement.Height = 600;
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

    private void SaveCurrentMap() {
        if (DataContext is MapViewModel vm) {
            vm.Settings ??= new MapSettings();
            if (vm.FogImage != null && !string.IsNullOrEmpty(vm.FogOfWarFilePath)) {
                ImageHelpers.SaveWriteableBitMap(vm.FogOfWarFilePath, vm.FogImage.Clone());
            }

            vm.StopFading();

            var mapsDir = Path.Combine(NativeMethods.AppFolder(), "maps");
            if (!Directory.Exists(mapsDir)) {
                Directory.CreateDirectory(mapsDir);
            }

            try {
                if (!string.IsNullOrEmpty(vm.MapPath) && vm.Settings.IsCalibrated && File.Exists(vm.MapPath)) {
                    var configPath = Path.Combine(mapsDir, Path.GetFileNameWithoutExtension(vm.MapPath) + ".json");
                    var json = JsonSerializer.Serialize(vm.Settings);
                    File.WriteAllText(configPath, json);
                }
            }
            catch (Exception ex) {
                MessageBox.Show($"Error saving map: {ex.Message}");
            }
        }
    }

    private void SetDestination_Click(object sender, RoutedEventArgs e) {
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

    private void ShowControls_Click(object sender, RoutedEventArgs e) {
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

    private void StartCalibration() {
        var vm = (MapViewModel)DataContext;
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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        // Start the timer-based drag instead of DragMove()
        StartManualDrag();
        e.Handled = true;
    }

    private void UpdateKeyboardClickThrough() {
        if (_hwnd == IntPtr.Zero) return;

        int extendedStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        if (DataContext is MapViewModel vm && vm.AppSettings.KeyboardClickThrough) {
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE,
                extendedStyle | NativeMethods.WS_EX_NOACTIVATE);
            KeepOnTop();
        }
        else {
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE,
                extendedStyle & ~NativeMethods.WS_EX_NOACTIVATE);
        }
    }

    // private void UpdateSavedPlacement()
    // {
    //     // CRITICAL: Only update the settings if the window is in a "Normal" state.
    //     // This ignores the 28px height that happens during minimization.
    //     if (DataContext is MapViewModel vm) {
    //         if (this.WindowState == WindowState.Normal) {
    //             // Use .Width and .Height to match the coordinate system of your Bindings
    //             // Use .ActualWidth only if .Width is 'NaN' (not set)
    //             vm.AppSettings.MapWindowPlacement.Top = this.Top;
    //             vm.AppSettings.MapWindowPlacement.Left = this.Left;
    //             if (!double.IsNaN(this.Width)) {
    //                 vm.AppSettings.MapWindowPlacement.Width = this.Width; 
    //             }
    //             if (!double.IsNaN(this.Height)) {
    //                 vm.AppSettings.MapWindowPlacement.Height = this.Height; 
    //             }
    //             vm.AppSettings.MapWindowPlacement.State = this.WindowState;
    //         }
    //         else if (this.WindowState == WindowState.Maximized) {
    //             // If maximized, only update the State, don't overwrite the Width/Height
    //             // so that when they "Restore" later, it goes back to the old size.
    //             vm.AppSettings.MapWindowPlacement.State = this.WindowState;
    //         }
    //     }
    // }

    public void ValidateAndSetWindowPosition(Window window, double savedLeft, double savedTop) {
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
            // Fallback: Center on the Primary Monitor
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (DataContext is not MapViewModel vm) return;
        vm.Settings ??= new MapSettings();
        
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
        // Get the handle for the Popup's window
        var popup = (sender as Popup);
        if (popup == null) return;
        double offset = popup.VerticalOffset;
        popup.VerticalOffset = offset + 0.01;
        popup.VerticalOffset = offset;
    }
}