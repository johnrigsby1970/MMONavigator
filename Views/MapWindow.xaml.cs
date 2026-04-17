using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
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
