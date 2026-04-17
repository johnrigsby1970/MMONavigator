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
    private bool _isCalibrating = false;
    private int _calibrationStep = 0;

    public MapWindow(MapViewModel viewModel) {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) {
        Hide();
    }

    private void PickImage_Click(object sender, RoutedEventArgs e) {
        var openFileDialog = new OpenFileDialog();
        openFileDialog.Filter = "Image files (*.png;*.jpeg;*.jpg;*.bmp)|*.png;*.jpeg;*.jpg;*.bmp|All files (*.*)|*.*";
        if (openFileDialog.ShowDialog() == true) {
            var vm = (MapViewModel)DataContext;
            vm.Settings.ImagePath = openFileDialog.FileName;
            vm.Settings.IsCalibrated = false;
            StatusTextBlock.Text = "Status: Image loaded. Please calibrate.";
        }
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
                        StatusTextBlock.Text = $"Status: Loaded {Path.GetFileName(imagePath)} with config.";
                    }
                } catch (Exception ex) {
                    MessageBox.Show($"Error loading config: {ex.Message}. Loading image only.");
                    vm.Settings.ImagePath = imagePath;
                    vm.Settings.IsCalibrated = false;
                    StatusTextBlock.Text = $"Status: Loaded {Path.GetFileName(imagePath)} (config error).";
                }
            } else {
                vm.Settings.ImagePath = imagePath;
                vm.Settings.IsCalibrated = false;
                StatusTextBlock.Text = $"Status: Loaded {Path.GetFileName(imagePath)} (no config).";
            }
        }
    }

    private void Calibrate_Click(object sender, RoutedEventArgs e) {
        _isCalibrating = true;
        _calibrationStep = 1;
        StatusTextBlock.Text = "Status: Click Point 1 on map";
        CalibMarker1.Visibility = Visibility.Collapsed;
        CalibMarker2.Visibility = Visibility.Collapsed;
    }

    private void MapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (!_isCalibrating) return;

        var vm = (MapViewModel)DataContext;
        Point clickPoint = e.GetPosition(MapCanvas);

        if (_calibrationStep == 1) {
            var inputDialog = new InputDialog("Enter coordinates for Point 1 (x, y):", "Calibration Point 1", $"{vm.CurrentPosition.X}, {vm.CurrentPosition.Y}") { Owner = this };
            if (inputDialog.ShowDialog() == true) {
                if (Scrubber.TryParse(inputDialog.Answer, "x y", out var coords)) {
                    vm.Settings.Point1.X = coords.X;
                    vm.Settings.Point1.Y = coords.Y;
                    vm.Settings.Point1.PixelX = clickPoint.X;
                    vm.Settings.Point1.PixelY = clickPoint.Y;

                    Canvas.SetLeft(CalibMarker1, clickPoint.X);
                    Canvas.SetTop(CalibMarker1, clickPoint.Y);
                    CalibMarker1.Visibility = Visibility.Visible;

                    _calibrationStep = 2;
                    StatusTextBlock.Text = "Status: Click Point 2 on map";
                } else {
                    MessageBox.Show("Invalid coordinates format.");
                }
            }
        } else if (_calibrationStep == 2) {
            var inputDialog = new InputDialog("Enter coordinates for Point 2 (x, y):", "Calibration Point 2", $"{vm.CurrentPosition.X}, {vm.CurrentPosition.Y}") { Owner = this };
            if (inputDialog.ShowDialog() == true) {
                if (Scrubber.TryParse(inputDialog.Answer, "x y", out var coords)) {
                    vm.Settings.Point2.X = coords.X;
                    vm.Settings.Point2.Y = coords.Y;
                    vm.Settings.Point2.PixelX = clickPoint.X;
                    vm.Settings.Point2.PixelY = clickPoint.Y;

                    Canvas.SetLeft(CalibMarker2, clickPoint.X);
                    Canvas.SetTop(CalibMarker2, clickPoint.Y);
                    CalibMarker2.Visibility = Visibility.Visible;

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
        Point clickPoint = e.GetPosition(MapCanvas);
        vm.UpdateHoverCoordinates(clickPoint.X, clickPoint.Y);
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
