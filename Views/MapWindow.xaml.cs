using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using MMONavigator.Models;
using MMONavigator.Services;
using MMONavigator.ViewModels;

namespace MMONavigator.Views;

public partial class MapWindow : Window {
    private bool _isCalibrating;
    private bool _isSettingDestination;
    private bool _isAddingPin;
    private int _calibrationStep;
    private bool _isDragging;
    private Point _lastMousePosition;
    private IntPtr _preDragForegroundWindow;

    public MapWindow(MapViewModel viewModel) {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += MapWindow_Loaded;
        SourceInitialized += MapWindow_SourceInitialized;
    }

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
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, extendedStyle | NativeMethods.WS_EX_NOACTIVATE);
            KeepOnTop();
        } else {
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, extendedStyle & ~NativeMethods.WS_EX_NOACTIVATE);
        }
    }

    private void KeepOnTop() {
        if (_hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, 
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 3;
    private const int WM_ACTIVATE = 0x0006;
    private const int WA_INACTIVE = 0;

    private IntPtr HwndHandler(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled) {
        if (msg == WM_MOUSEACTIVATE) {
            if (DataContext is MapViewModel vm && vm.AppSettings.KeyboardClickThrough) {
                // Return MA_NOACTIVATE and set handled = true to prevent stealing focus.
                handled = true;
                return (IntPtr)MA_NOACTIVATE;
            }
        }
        
        if (msg == WM_ACTIVATE) {
            if ((int)wparam != WA_INACTIVE) {
                if (DataContext is MapViewModel vm && vm.AppSettings.KeyboardClickThrough) {
                    System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] MapWindow activated. wparam: {wparam}");
                    // If we are being activated and click-through is on, push focus back to previous window.
                    
                    // We try to restore to the window that WAS foreground BEFORE this activation happened.
                    // If we don't have one, we try the window that is currently foreground (if it's not us).
                    IntPtr foregroundWindow = GetForegroundWindow();
                    
                    IntPtr targetWnd = IntPtr.Zero;
                    if (_preDragForegroundWindow != IntPtr.Zero && _preDragForegroundWindow != _hwnd) {
                        targetWnd = _preDragForegroundWindow;
                    } else if (foregroundWindow != _hwnd && foregroundWindow != IntPtr.Zero) {
                        targetWnd = foregroundWindow;
                    } else {
                        // Fallback: Try to find the next window in the Z-order to give focus to.
                        targetWnd = NativeMethods.GetWindow(_hwnd, (uint)NativeMethods.GW_HWNDNEXT);
                    }

                    if (targetWnd != IntPtr.Zero && targetWnd != _hwnd) {
                        SetForegroundWindow(targetWnd);
                    }
                    
                    handled = true;
                }
            }
        }

        return IntPtr.Zero;
    }

    private static class NativeMethods {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int GW_HWNDNEXT = 2;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
    }

    private void MapWindow_Loaded(object sender, RoutedEventArgs e) {
        Deactivated += (s, ev) => {
            if (Application.Current.MainWindow?.DataContext is MainViewModel mainVm && mainVm.Settings.KeyboardClickThrough) {
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

    private void Window_MouseEnter(object sender, MouseEventArgs e) {
        if (DataContext is MapViewModel vm) {
            vm.IsHovered = true;
        }
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e) {
        if (DataContext is MapViewModel vm) {
            vm.IsHovered = false;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        IntPtr foregroundWindow = IntPtr.Zero;
        bool isClickThrough = false;
        if (Application.Current.MainWindow?.DataContext is MainViewModel mainVm && mainVm.Settings.KeyboardClickThrough) {
            isClickThrough = true;
            foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == _hwnd) foregroundWindow = IntPtr.Zero; // Don't restore to self
            _preDragForegroundWindow = foregroundWindow;
        }
        
        try {
            DragMove();
        } catch (InvalidOperationException) {
            // DragMove can fail if mouse button was released too quickly
        }

        if (isClickThrough) {
            IntPtr currentForeground = GetForegroundWindow();
            if (currentForeground == _hwnd) {
                if (_preDragForegroundWindow != IntPtr.Zero && _preDragForegroundWindow != _hwnd) {
                    SetForegroundWindow(_preDragForegroundWindow);
                } else {
                    IntPtr nextWnd = NativeMethods.GetWindow(_hwnd, (uint)NativeMethods.GW_HWNDNEXT);
                    if (nextWnd != IntPtr.Zero) SetForegroundWindow(nextWnd);
                }
            }
            _preDragForegroundWindow = IntPtr.Zero;
        }
        e.Handled = true;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) {
        Close();
    }

    private void PickImage_Click(object sender, RoutedEventArgs e) {
        var openFileDialog = new OpenFileDialog();
        openFileDialog.Filter = "Image files (*.png;*.jpeg;*.jpg;*.bmp)|*.png;*.jpeg;*.jpg;*.bmp|All files (*.*)|*.*";
        if (openFileDialog.ShowDialog() == true) {
            var vm = (MapViewModel)DataContext;
            vm.Settings.ImagePath = openFileDialog.FileName;
            ClearCalibration(vm.Settings);
            StatusTextBlock.Text = "Status: Image loaded. Please calibrate.";
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
            MessageBox.Show("No image loaded to save.");
            return;
        }

        var currentFileName = Path.GetFileNameWithoutExtension(vm.Settings.ImagePath);
        var inputDialog = new InputDialog("Enter a name for this map:", "Save Map", currentFileName) { Owner = this };
        if (inputDialog.ShowDialog() != true) {
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
        var mapsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maps");
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
        } catch (Exception ex) {
            MessageBox.Show($"Error saving map: {ex.Message}");
        }
    }

    private void LoadMap_Click(object sender, RoutedEventArgs e) {
        var mapsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maps");
        if (!Directory.Exists(mapsDir)) {
            Directory.CreateDirectory(mapsDir);
        }

        var openFileDialog = new OpenFileDialog();
        openFileDialog.InitialDirectory = mapsDir;
        openFileDialog.Filter = "Image files (*.png;*.jpeg;*.jpg;*.bmp)|*.png;*.jpeg;*.jpg;*.bmp|All files (*.*)|*.*";
        if (openFileDialog.ShowDialog() == true) {
            var vm = (MapViewModel)DataContext;
            var imagePath = openFileDialog.FileName;
            var configPath = Path.ChangeExtension(imagePath, ".json");

            if (File.Exists(configPath)) {
                try {
                    var json = File.ReadAllText(configPath);
                    var savedSettings = JsonSerializer.Deserialize<MapSettings>(json);
                    if (savedSettings != null) {
                        vm.Settings.ImagePath = imagePath;
                        vm.Settings.Point1.X = savedSettings.Point1.X;
                        vm.Settings.Point1.Y = savedSettings.Point1.Y;
                        vm.Settings.Point1.PixelX = savedSettings.Point1.PixelX;
                        vm.Settings.Point1.PixelY = savedSettings.Point1.PixelY;
                        vm.Settings.Point2.X = savedSettings.Point2.X;
                        vm.Settings.Point2.Y = savedSettings.Point2.Y;
                        vm.Settings.Point2.PixelX = savedSettings.Point2.PixelX;
                        vm.Settings.Point2.PixelY = savedSettings.Point2.PixelY;
                        vm.Settings.IsCalibrated = savedSettings.IsCalibrated;
                        vm.Settings.ZoomLevel = savedSettings.ZoomLevel;
                        vm.Settings.ShowLocations = savedSettings.ShowLocations;
                        vm.Settings.ShowCalibrationMarkers = savedSettings.ShowCalibrationMarkers;
                        
                        Canvas.SetLeft(CalibMarker1, vm.Settings.Point1.PixelX);
                        Canvas.SetTop(CalibMarker1, vm.Settings.Point1.PixelY);

                        Canvas.SetLeft(CalibMarker2, vm.Settings.Point2.PixelX);
                        Canvas.SetTop(CalibMarker2, vm.Settings.Point2.PixelY);

                        vm.UpdateMarkers();
                        StatusTextBlock.Text = $"Status: Loaded {Path.GetFileName(imagePath)} with config.";
                    }
                } catch (Exception ex) {
                    MessageBox.Show($"Error loading config: {ex.Message}. Loading image only.");
                    vm.Settings.ImagePath = imagePath;
                    ClearCalibration(vm.Settings);
                    StatusTextBlock.Text = $"Status: Loaded {Path.GetFileName(imagePath)} (config error).";
                }
            } else {
                vm.Settings.ImagePath = imagePath;
                ClearCalibration(vm.Settings);
                StatusTextBlock.Text = $"Status: Loaded {Path.GetFileName(imagePath)} (no config).";
            }
        }
    }

    private void Calibrate_Click(object sender, RoutedEventArgs e) {
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
        } else {
            StatusTextBlock.Text = "Status: Ready";
        }
    }

    private void AddPin_Click(object sender, RoutedEventArgs e) {
        _isAddingPin = AddPinMenuItem.IsChecked == true;
        if (_isAddingPin) {
            _isCalibrating = false;
            _isSettingDestination = false;
            SetDestinationMenuItem.IsChecked = false;
            StatusTextBlock.Text = "Status: Click on map to add pin";
        } else {
            StatusTextBlock.Text = "Status: Ready";
        }
    }

    private void MapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (Application.Current.MainWindow?.DataContext is MainViewModel mainVm && mainVm.Settings.KeyboardClickThrough) {
            _preDragForegroundWindow = GetForegroundWindow();
            if (_preDragForegroundWindow == _hwnd) _preDragForegroundWindow = IntPtr.Zero;
        }

        if (!_isCalibrating && !_isSettingDestination && !_isAddingPin) {
            _isDragging = true;
            _lastMousePosition = e.GetPosition(MapScrollViewer);
            MapCanvas.CaptureMouse();
            MapCanvas.Cursor = Cursors.Hand;
            
            e.Handled = true;
            return;
        }

        var vm = (MapViewModel)DataContext;
        Point clickPoint = e.GetPosition(MapCanvas);

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
            var inputDialog = new InputDialog("Enter coordinates for Point 1 (x, y):", "Calibration Point 1", suggestedCoords) { Owner = this };
            if (inputDialog.ShowDialog() == true) {
                if (Scrubber.TryParse(inputDialog.Answer, "x y", out var coords)) {
                    vm.Settings.Point1.X = coords.X;
                    vm.Settings.Point1.Y = coords.Y;
                    vm.Settings.Point1.PixelX = clickPoint.X;
                    vm.Settings.Point1.PixelY = clickPoint.Y;

                    Canvas.SetLeft(CalibMarker1, clickPoint.X);
                    Canvas.SetTop(CalibMarker1, clickPoint.Y);

                    _calibrationStep = 2;
                    StatusTextBlock.Text = "Status: Click Point 2 on map";
                } else {
                    MessageBox.Show("Invalid coordinates format.");
                }
            }
        } else if (_calibrationStep == 2) {
            string suggestedCoords = vm.CurrentPosition.HasValue 
                ? $"{vm.CurrentPosition.Value.X}, {vm.CurrentPosition.Value.Y}" 
                : "0, 0";
            var inputDialog = new InputDialog("Enter coordinates for Point 2 (x, y):", "Calibration Point 2", suggestedCoords) { Owner = this };
            if (inputDialog.ShowDialog() == true) {
                if (Scrubber.TryParse(inputDialog.Answer, "x y", out var coords)) {
                    vm.Settings.Point2.X = coords.X;
                    vm.Settings.Point2.Y = coords.Y;
                    vm.Settings.Point2.PixelX = clickPoint.X;
                    vm.Settings.Point2.PixelY = clickPoint.Y;

                    Canvas.SetLeft(CalibMarker2, clickPoint.X);
                    Canvas.SetTop(CalibMarker2, clickPoint.Y);

                    vm.Settings.IsCalibrated = true;
                    vm.UpdateMarkers();
                    
                    _isCalibrating = false;
                    _calibrationStep = 0;
                    StatusTextBlock.Text = "Status: Calibrated";
                } else {
                    MessageBox.Show("Invalid coordinates format.");
                }
            }
        }
    }

    private void MapCanvas_MouseMove(object sender, MouseEventArgs e) {
        var vm = (MapViewModel)DataContext;
        Point currentPoint = e.GetPosition(MapCanvas);
        vm.UpdateHoverCoordinates(currentPoint.X, currentPoint.Y);

        if (_isDragging) {
            Point currentScrollPoint = e.GetPosition(MapScrollViewer);
            double deltaX = currentScrollPoint.X - _lastMousePosition.X;
            double deltaY = currentScrollPoint.Y - _lastMousePosition.Y;

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
            MapCanvas.Cursor = Cursors.Arrow;

            if (Application.Current.MainWindow?.DataContext is MainViewModel mainVm && mainVm.Settings.KeyboardClickThrough) {
                IntPtr currentForeground = GetForegroundWindow();
                if (currentForeground == _hwnd) {
                    if (_preDragForegroundWindow != IntPtr.Zero && _preDragForegroundWindow != _hwnd) {
                        SetForegroundWindow(_preDragForegroundWindow);
                    } else {
                        IntPtr nextWnd = NativeMethods.GetWindow(_hwnd, (uint)NativeMethods.GW_HWNDNEXT);
                        if (nextWnd != IntPtr.Zero) SetForegroundWindow(nextWnd);
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

        Point mousePos = e.GetPosition(MapScrollViewer);

        double relativeMouseX = (mousePos.X + MapScrollViewer.HorizontalOffset) / vm.Settings.ZoomLevel;
        double relativeMouseY = (mousePos.Y + MapScrollViewer.VerticalOffset) / vm.Settings.ZoomLevel;

        vm.Settings.ZoomLevel = newScale;

        MapScrollViewer.ScrollToHorizontalOffset(relativeMouseX * vm.Settings.ZoomLevel - mousePos.X);
        MapScrollViewer.ScrollToVerticalOffset(relativeMouseY * vm.Settings.ZoomLevel - mousePos.Y);

        e.Handled = true;
    }
}
