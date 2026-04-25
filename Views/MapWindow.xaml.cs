using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using MMONavigator.Controls;
using MMONavigator.Helpers;
using MMONavigator.Models;
using MMONavigator.Services;
using MMONavigator.ViewModels;
using MessageBox = System.Windows.MessageBox;
using System.Drawing;

namespace MMONavigator.Views;

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
    private System.Windows.Threading.DispatcherTimer? _dragTimer;
    private DispatcherTimer _hoverTimer;

    public MapWindow(MapViewModel viewModel) {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += MapWindow_Loaded;
        SourceInitialized += MapWindow_SourceInitialized;
        // Forcefully attach to the main grid
        // myMapGrid.MouseLeave += Root_MouseLeave;

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _hoverTimer.Tick += HoverTimer_Tick;
        _hoverTimer.Start();
    }

    private DateTime _lastMouseOutsideTime = DateTime.MinValue;

    private void HoverTimer_Tick(object sender, EventArgs e) {
        if (DataContext is MapViewModel vm) {
            System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
            var topLeft = this.PointToScreen(new System.Windows.Point(0, 0));
            var bounds = new Rectangle((int)topLeft.X, (int)topLeft.Y, (int)this.ActualWidth, (int)this.ActualHeight);

            bool isInside = bounds.Contains(cursor.X, cursor.Y);

            if (isInside) {
                // Mouse is inside, reset the "outside" timer and set hovered
                _lastMouseOutsideTime = DateTime.MinValue;
                if (!vm.IsHovered) vm.IsHovered = true;
            }
            else {
                // Mouse is outside
                if (_lastMouseOutsideTime == DateTime.MinValue)
                    _lastMouseOutsideTime = DateTime.Now;

                // Only hide if the mouse has been outside for 300ms
                //As you drag around the window, the hover state will be maintained for a short period to avoid flickering
                if (DateTime.Now - _lastMouseOutsideTime > TimeSpan.FromMilliseconds(300)) {
                    if (vm.IsHovered) vm.IsHovered = false;
                }
            }
        }
    }

    // private void HoverTimer_Tick(object sender, EventArgs e)
    // {
    //     if (DataContext is MapViewModel vm)
    //     {
    //         // 1. Get global screen position of the mouse
    //         System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
    //         
    //         // 2. Get the screen bounds of this window
    //         var topLeft = this.PointToScreen(new System.Windows.Point(0, 0));
    //         var bounds = new Rectangle((int)topLeft.X, (int)topLeft.Y, (int)this.ActualWidth, (int)this.ActualHeight);
    //         
    //         // 3. Check if the global cursor is inside the window rectangle
    //         bool isInside = bounds.Contains(cursor.X, cursor.Y);
    //         
    //         // 4. Update VM without relying on events
    //         if (vm.IsHovered != isInside)
    //         {
    //             vm.IsHovered = isInside;
    //         }
    //     }
    // }
    private IntPtr _hwnd;

    private void MapWindow_SourceInitialized(object? sender, EventArgs e) {
        _hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(HwndHandler);

        // Initial application of the style
        UpdateKeyboardClickThrough();

        if (DataContext is MapViewModel vm) {
            vm.Settings.PropertyChanged += (s, ev) => {
                if (ev.PropertyName == nameof(MapSettings.Opacity)) {
                    // Update opacity if needed (already bound in XAML)
                }
            };

            // We need to listen to the global KeyboardClickThrough setting
            vm.AppSettings.PropertyChanged += (s, ev) => {
                if (ev.PropertyName == nameof(AppSettings.KeyboardClickThrough)) {
                    UpdateKeyboardClickThrough();
                }
            };
        }
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

    private void KeepOnTop() {
        if (_hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private IntPtr HwndHandler(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled) {
        if (msg == NativeMethods.WM_MOUSEACTIVATE) {
            if (DataContext is MapViewModel vm && vm.AppSettings.KeyboardClickThrough) {
                // Return MA_NOACTIVATE and set handled = true to prevent stealing focus.
                handled = true;
                return (IntPtr)NativeMethods.MA_NOACTIVATE;
            }
        }

        if (msg == NativeMethods.WM_ACTIVATE) {
            if ((int)wparam != NativeMethods.WA_INACTIVE) {
                if (DataContext is MapViewModel vm && vm.AppSettings.KeyboardClickThrough) {
                    System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] MapWindow activated. wparam: {wparam}");
                    // If we are being activated and click-through is on, push focus back to previous window.

                    // We try to restore to the window that WAS foreground BEFORE this activation happened.
                    // If we don't have one, we try the window that is currently foreground (if it's not us).
                    var foregroundWindow = NativeMethods.GetForegroundWindow();

                    var targetWnd = IntPtr.Zero;
                    if (_preDragForegroundWindow != IntPtr.Zero && _preDragForegroundWindow != _hwnd) {
                        targetWnd = _preDragForegroundWindow;
                    }
                    else if (foregroundWindow != _hwnd && foregroundWindow != IntPtr.Zero) {
                        targetWnd = foregroundWindow;
                    }
                    else {
                        // Fallback: Try to find the next window in the Z-order to give focus to.
                        targetWnd = NativeMethods.GetWindow(_hwnd, (uint)NativeMethods.GW_HWNDNEXT);
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

    private void MapWindow_Loaded(object sender, RoutedEventArgs e) {
        Deactivated += (s, ev) => {
            if (System.Windows.Application.Current.MainWindow?.DataContext is MainViewModel mainVm &&
                mainVm.Settings.KeyboardClickThrough) {
                KeepOnTop();
            }
        };

        if (DataContext is MapViewModel vm) {
            Canvas.SetLeft(CalibMarker1, vm.Settings.Point1.PixelX);
            Canvas.SetTop(CalibMarker1, vm.Settings.Point1.PixelY);
            Canvas.SetLeft(CalibMarker2, vm.Settings.Point2.PixelX);
            Canvas.SetTop(CalibMarker2, vm.Settings.Point2.PixelY);
        }
    }

    // private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) {
    //     if (DataContext is MapViewModel vm) {
    //         vm.IsHovered = true;
    //     }
    // }
    //
    // private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) {
    //     if (DataContext is MapViewModel vm) {
    //         vm.IsHovered = false;
    //     }
    // }

    // private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    // {
    //     if (DataContext is MapViewModel vm)
    //     {
    //         // Get the current position relative to the Window
    //         var pos = e.GetPosition(this);
    //     
    //         // Only set to false if the mouse is ACTUALLY outside the window boundaries
    //         // This prevents the ContextMenu/Drag glitches
    //         bool isOutside = pos.X <= 0 || pos.Y <= 0 || pos.X >= ActualWidth || pos.Y >= ActualHeight;
    //     
    //         // Add a small buffer or check if a context menu is open
    //         if (isOutside)
    //         {
    //             vm.IsHovered = false;
    //         }
    //     }
    // }
    //
    // private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    // {
    //     if (DataContext is MapViewModel vm && !vm.IsHovered)
    //     {
    //         vm.IsHovered = true;
    //     }
    //     // if (DataContext is MapViewModel vm)
    //     // {
    //     //     // Check if mouse is actually inside the window boundaries
    //     //     var pos = e.GetPosition(this);
    //     //     bool isInside = pos.X >= 0 && pos.Y >= 0 && pos.X <= ActualWidth && pos.Y <= ActualHeight;
    //     //
    //     //     // Only update if the state has changed
    //     //     if (vm.IsHovered != isInside)
    //     //     {
    //     //         vm.IsHovered = isInside;
    //     //     }
    //     // }
    // }
    // private void Root_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    // {
    //     // The visual check is now unnecessary if we are sure we are 
    //     // attached to the actual container covering the whole window
    //     if (DataContext is MapViewModel vm)
    //     {
    //         vm.IsHovered = false;
    //     }
    // }
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        // Start the timer-based drag instead of DragMove()
        StartManualDrag();
        e.Handled = true;
    }

    private void StartManualDrag() {
        // Capture the window we want to return focus to
        if (System.Windows.Application.Current.MainWindow?.DataContext is MainViewModel mainVm &&
            mainVm.Settings.KeyboardClickThrough) {
            _preDragForegroundWindow = NativeMethods.GetForegroundWindow();
        }

        _lastMousePos = System.Windows.Forms.Cursor.Position;

        _dragTimer = new System.Windows.Threading.DispatcherTimer();
        _dragTimer.Interval = TimeSpan.FromMilliseconds(1);
        _dragTimer.Tick += DragTimer_Tick;
        _dragTimer.Start();
    }

    private void DragTimer_Tick(object? sender, EventArgs e) {
        // If user releases mouse, stop dragging
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

        var dpiX = source.CompositionTarget.TransformToDevice.M11;
        var dpiY = source.CompositionTarget.TransformToDevice.M22;

        Left += (currentMousePos.X - _lastMousePos.X) / dpiX;
        Top += (currentMousePos.Y - _lastMousePos.Y) / dpiY;

        _lastMousePos = currentMousePos;
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

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) {
        _hoverTimer.Stop();
        if (DataContext is MapViewModel vm) {
            if (vm.FogImage != null && !string.IsNullOrEmpty(vm.FogOfWarFilePath)) {
                ImageHelpers.SaveWriteableBitMap(vm.FogOfWarFilePath, vm.FogImage.Clone());
            }
            vm.StopFading();
        }

        Close();
    }

    private void PickImage_Click(object sender, RoutedEventArgs e) {
        IsDialogActive = true;
        Window helperWindow = null;

        try {
            ConfigureDialogToHaveAValidOwner(this, out helperWindow);

            var openFileDialog = new Microsoft.Win32.OpenFileDialog {
                Filter =
                    "Image files (*.png;*.jpeg;*.jpg;*.bmp;*.tiff;*.svg;*.gif)|*.png;*.jpeg;*.jpg;*.bmp;*.tiff;*.svg;*.gif"
            };

            // Use the native interop handle to ensure the dialog 
            // feels 'attached' to the helper
            var helper = new WindowInteropHelper(helperWindow);

            if (openFileDialog.ShowDialog() == true) {
                var vm = (MapViewModel)DataContext;
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

    private void SaveMap_Click(object sender, RoutedEventArgs e) {
        var vm = (MapViewModel)DataContext;
        if (string.IsNullOrEmpty(vm.Settings.ImagePath) || !File.Exists(vm.Settings.ImagePath)) {
            System.Windows.MessageBox.Show("No image loaded to save.");
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
            System.Windows.MessageBox.Show("Map name cannot be empty.");
            return;
        }

        // Remove invalid characters
        foreach (char c in Path.GetInvalidFileNameChars()) {
            newName = newName.Replace(c, '_');
        }

        var extension = Path.GetExtension(vm.Settings.ImagePath);
        var mapsDir = Path.Combine(Helpers.NativeMethods.AppFolder(), "maps");
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
            System.Windows.MessageBox.Show($"Error saving map: {ex.Message}");
        }
    }

    private void LoadMap_Click(object sender, RoutedEventArgs e) {
        var mapsDir = Path.Combine(Helpers.NativeMethods.AppFolder(), "maps");
        if (!Directory.Exists(mapsDir)) {
            Directory.CreateDirectory(mapsDir);
        }

        IsDialogActive = true;
        Window helperWindow = null;
        var vm = (MapViewModel)DataContext;

        try {
            ConfigureDialogToHaveAValidOwner(this, out helperWindow);

            var openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.InitialDirectory = mapsDir;
            openFileDialog.Filter =
                "Image files (*.png;*.jpeg;*.jpg;*.bmp)|*.png;*.jpeg;*.jpg;*.bmp|All files (*.*)|*.*";

            // Use the native interop handle to ensure the dialog 
            // feels 'attached' to the helper
            var helper = new WindowInteropHelper(helperWindow);

            if (openFileDialog.ShowDialog() == true) {
                vm.IsLoadingFile = true;
                var imagePath = openFileDialog.FileName;
                var configPath = Path.ChangeExtension(imagePath, ".json");

                if (File.Exists(configPath)) {
                    try {
                        var calibrated = vm.LoadImageConfig(imagePath);
                        
                        if (calibrated) {
                            Canvas.SetLeft(CalibMarker1, vm.Settings.Point1.PixelX);
                            Canvas.SetTop(CalibMarker1, vm.Settings.Point1.PixelY);

                            Canvas.SetLeft(CalibMarker2, vm.Settings.Point2.PixelX);
                            Canvas.SetTop(CalibMarker2, vm.Settings.Point2.PixelY);
                            vm.IsLoadingFile = false;
                            vm.LoadImage();
                            vm.UpdateMarkers();
                            StatusTextBlock.Text = $"Status: Loaded {Path.GetFileName(imagePath)} with config.";
                        }
                    }
                    catch (Exception ex) {
                        System.Windows.MessageBox.Show($"Error loading config: {ex.Message}. Loading image only.");
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

    private void SetDestination_Click(object sender, RoutedEventArgs e) {
        _isSettingDestination = SetDestinationMenuItem.IsChecked == true;
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

    private void CreateChallenge_Click(object sender, RoutedEventArgs e) {
        new ChallengeDesignerWindow().Show();
    }

    private void AddPin_Click(object sender, RoutedEventArgs e) {
        _isAddingPin = AddPinMenuItem.IsChecked == true;
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

    private void MapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (System.Windows.Application.Current.MainWindow?.DataContext is MainViewModel mainVm &&
            mainVm.Settings.KeyboardClickThrough) {
            _preDragForegroundWindow = Helpers.NativeMethods.GetForegroundWindow();
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
                vm.RequestPin(coords.Value);
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
            var inputDialog =
                new InputDialog("Enter coordinates for Point 1 (x, y):", "Calibration Point 1", suggestedCoords)
                    { Owner = this };
            // Set the owner to the MainWindow BEFORE calling ShowDialog()
            // You can access the MainWindow via Application.Current.MainWindow
            inputDialog.Owner = System.Windows.Application.Current.MainWindow;
            inputDialog.ShowDialog();

            // Check your manual property instead of the built-in DialogResult
            if (inputDialog.ManualDialogResult == true) {
                if (Scrubber.TryParse(inputDialog.Answer, "x y", out var coords)) {
                    vm.Settings.Point1.X = coords.X;
                    vm.Settings.Point1.Y = coords.Y;
                    vm.Settings.Point1.PixelX = clickPoint.X;
                    vm.Settings.Point1.PixelY = clickPoint.Y;

                    Canvas.SetLeft(CalibMarker1, clickPoint.X);
                    Canvas.SetTop(CalibMarker1, clickPoint.Y);

                    _calibrationStep = 2;
                    StatusTextBlock.Text = "Status: Click Point 2 on map";
                }
                else {
                    System.Windows.MessageBox.Show("Invalid coordinates format.");
                }
            }
        }
        else if (_calibrationStep == 2) {
            string suggestedCoords = vm.CurrentPosition.HasValue
                ? $"{vm.CurrentPosition.Value.X}, {vm.CurrentPosition.Value.Y}"
                : "0, 0";
            var inputDialog =
                new InputDialog("Enter coordinates for Point 2 (x, y):", "Calibration Point 2", suggestedCoords)
                    { Owner = this };
            // Set the owner to the MainWindow BEFORE calling ShowDialog()
            // You can access the MainWindow via Application.Current.MainWindow
            inputDialog.Owner = System.Windows.Application.Current.MainWindow;
            inputDialog.ShowDialog();

            // Check your manual property instead of the built-in DialogResult
            if (inputDialog.ManualDialogResult == true) {
                if (Scrubber.TryParse(inputDialog.Answer, "x y", out var coords)) {
                    vm.Settings.Point2.X = coords.X;
                    vm.Settings.Point2.Y = coords.Y;
                    vm.Settings.Point2.PixelX = clickPoint.X;
                    vm.Settings.Point2.PixelY = clickPoint.Y;

                    Canvas.SetLeft(CalibMarker2, clickPoint.X);
                    Canvas.SetTop(CalibMarker2, clickPoint.Y);

                    vm.Settings.IsCalibrated = true;
                    vm.UpdateMarkers();

                    if (_savedFogSettings.HasValue) {
                        vm.ShowFogOfWar = _savedFogSettings.Value;
                        _savedFogSettings = null;
                    }
                    _isCalibrating = false;
                    _calibrationStep = 0;

                    if (vm.MapImage != null && !string.IsNullOrEmpty(vm.MapPath)) {
                        var mapsDir = Path.Combine(Helpers.NativeMethods.AppFolder(), "maps");
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
                    System.Windows.MessageBox.Show("Invalid coordinates format.");
                }
            }
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

    private void MapCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        if (_isDragging) {
            _isDragging = false;
            MapCanvas.ReleaseMouseCapture();
            MapCanvas.Cursor = System.Windows.Input.Cursors.Arrow;

            if (System.Windows.Application.Current.MainWindow?.DataContext is MainViewModel mainVm &&
                mainVm.Settings.KeyboardClickThrough) {
                IntPtr currentForeground = Helpers.NativeMethods.GetForegroundWindow();
                if (currentForeground == _hwnd) {
                    if (_preDragForegroundWindow != IntPtr.Zero && _preDragForegroundWindow != _hwnd) {
                        Helpers.NativeMethods.SetForegroundWindow(_preDragForegroundWindow);
                    }
                    else {
                        IntPtr nextWnd = NativeMethods.GetWindow(_hwnd, (uint)NativeMethods.GW_HWNDNEXT);
                        if (nextWnd != IntPtr.Zero) Helpers.NativeMethods.SetForegroundWindow(nextWnd);
                    }
                }

                _preDragForegroundWindow = IntPtr.Zero;
            }

            e.Handled = true;
        }
    }

    private void MapScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
        var vm = (MapViewModel)DataContext;
        double zoom = e.Delta > 0 ? 1.1 : 0.9;
        double newScale = vm.Settings.ZoomLevel * zoom;

        // Limit zoom
        if (newScale < 0.1) newScale = 0.1;
        if (newScale > 10) newScale = 10;

        zoom = newScale / vm.Settings.ZoomLevel;

        System.Windows.Point mousePos = e.GetPosition(MapScrollViewer);

        var relativeMouseX = (mousePos.X + MapScrollViewer.HorizontalOffset) / vm.Settings.ZoomLevel;
        var relativeMouseY = (mousePos.Y + MapScrollViewer.VerticalOffset) / vm.Settings.ZoomLevel;

        vm.Settings.ZoomLevel = newScale;

        MapScrollViewer.ScrollToHorizontalOffset(relativeMouseX * vm.Settings.ZoomLevel - mousePos.X);
        MapScrollViewer.ScrollToVerticalOffset(relativeMouseY * vm.Settings.ZoomLevel - mousePos.Y);

        e.Handled = true;
    }
}