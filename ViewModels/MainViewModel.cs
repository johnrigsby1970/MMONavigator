using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.ObjectModel;
using Microsoft.VisualBasic.CompilerServices;
using MMONavigator.Helpers;
using MMONavigator.Interfaces;
using MMONavigator.Models;
using MMONavigator.Services;
using MMONavigator.Views;

namespace MMONavigator.ViewModels;

public class MainViewModel : INotifyPropertyChanged {
    private readonly ISettingsService _settingsService;
    private readonly IWatcherService _watcherService;
    private const double ProximityDistanceThreshold = 100;
    private const double ArrivalDistance = 10;
    private const double CircleRadius = 40;
    private const double HeadingTolerancePerfect = 2.0;
    private const double HeadingToleranceGood = 4.0;
    private const double HeadingToleranceFair = 6.0;
    private const double MovementThreshold = 1.0;
    private CoordinateData? _lastCoordinateData;
    
    public ObservableCollection<LocationItem> Locations { get; set; } = new();

    public TimerController Timer5 { get; } = new(5);
    public TimerController Timer10 { get; } = new(10);
    public TimerController Timer15 { get; } = new(15);
    public TimerController Timer20 { get; } = new(20);

    private LocationItem? _selectedLocation;
    public LocationItem? SelectedLocation {
        get => _selectedLocation;
        set {
            if (SetField(ref _selectedLocation, value)) {
                SyncLocationAndCoordinates(true);
            }
        }
    }

    private bool _isItemInList;
    public bool IsItemInList {
        get => _isItemInList;
        set {
            if (SetField(ref _isItemInList, value)) {
                OnPropertyChanged(nameof(IsItemNotInList));
            }
        }
    }
    public bool IsItemNotInList => !IsItemInList;

    private void SyncLocationAndCoordinates(bool isSelectionSource) {
        //An AI suggested fix to move logic out of setter and prevent ping pong.
        if (isSelectionSource) {
            if (_selectedLocation != null) {
                _targetCoordinates = _selectedLocation.DisplayName;
                OnPropertyChanged(nameof(TargetCoordinates));
            }
        } else {
            string currentInput = _targetCoordinates ?? string.Empty;
            var scrubbed = Scrubber.ScrubEntry(currentInput);

            var matchingItem = Locations.FirstOrDefault(l =>
                l.ScrubbedCoordinates == scrubbed || currentInput == l.DisplayName);

            if (_selectedLocation != matchingItem) {
                _selectedLocation = matchingItem;
                OnPropertyChanged(nameof(SelectedLocation));
            }
        }

        ShowDirection();
        UpdateListStatus();
    }

    private void UpdateListStatus() {
        IsItemInList = SelectedLocation != null || Locations.Any(l => {
            string scrubbedT;
            if (SelectedLocation != null && TargetCoordinates == SelectedLocation.DisplayName) {
                scrubbedT = SelectedLocation.ScrubbedCoordinates ?? "";
            } else {
                scrubbedT = Scrubber.ScrubEntry(TargetCoordinates) ?? "";
            }
            return !string.IsNullOrEmpty(l.ScrubbedCoordinates) && l.ScrubbedCoordinates == scrubbedT;
        });
    }

    private AppSettings _settings = new();
    public AppSettings Settings {
        get => _settings;
        set {
            AppSettings oldSettings = _settings; // Explicitly capture the current backing field
            if (SetField(ref _settings, value)) {
                SwapSettingsSubscriptions(oldSettings, value);
            }
        }
    }

    private void SwapSettingsSubscriptions(AppSettings? oldSettings, AppSettings? newSettings) {
        if (oldSettings != null) {
            oldSettings.PropertyChanged -= Settings_PropertyChanged;
            foreach (var profile in oldSettings.Profiles) {
                profile.PropertyChanged -= Profile_PropertyChanged;
            }
        }
        if (newSettings != null) {
            newSettings.PropertyChanged += Settings_PropertyChanged;
            foreach (var profile in newSettings.Profiles) {
                profile.PropertyChanged -= Profile_PropertyChanged; // Prevent double subscription
                profile.PropertyChanged += Profile_PropertyChanged;
            }
        }
    }

    private void Profile_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (sender is GameProfile profile && profile.Name == Settings.LastSelectedProfileName) {
            ShowDirection();
            if (e.PropertyName == nameof(GameProfile.WatchMode) || e.PropertyName == nameof(GameProfile.LogFilePath)) {
                if (_lastWindowHandle != IntPtr.Zero) {
                    StartWatcher(_lastWindowHandle);
                }
            }
            SaveSettings();
        }
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(AppSettings.LastSelectedProfileName)) {
            // Re-subscribe to the new SelectedProfile if needed, but since we use the property directly,
            // we just need to trigger a refresh of things that depend on it.
            if (_lastWindowHandle != IntPtr.Zero) {
                StartWatcher(_lastWindowHandle);
            }
            ShowDirection();
            SaveSettings();
        } else if (e.PropertyName == nameof(AppSettings.ShowSettings) || e.PropertyName == nameof(AppSettings.ShowTimers)) {
            SaveSettings();
        }
    }

    private IntPtr _lastWindowHandle;

    private string? _currentCoordinates = "";
    public string? CurrentCoordinates {
        get => _currentCoordinates;
        set {
            if (SetField(ref _currentCoordinates, value)) {
                ShowDirection();
            }
        }
    }

    private string? _targetCoordinates = string.Empty;
    public string? TargetCoordinates {
        get => _targetCoordinates;
        set {
            if (SetField(ref _targetCoordinates, value ?? string.Empty)) {
                SyncLocationAndCoordinates(false);
            }
        }
    }

    private string _correctionDirection = "";
    public string CorrectionDirection {
        get => _correctionDirection;
        set => SetField(ref _correctionDirection, value);
    }

    private string? _tX;
    public string? Tx {
        get => _tX;
        set => SetField(ref _tX, value);
    }

    private string? _tY;
    public string? Ty {
        get => _tY;
        set => SetField(ref _tY, value);
    }

    private string? _cX;
    public string? Cx {
        get => _cX;
        set => SetField(ref _cX, value);
    }

    private string? _cY;
    public string? Cy {
        get => _cY;
        set => SetField(ref _cY, value);
    }

    private double _currentHeading;
    public double CurrentHeading {
        get => _currentHeading;
        set => SetField(ref _currentHeading, value);
    }

    private double _targetHeading;
    public double TargetHeading {
        get => _targetHeading;
        set => SetField(ref _targetHeading, value);
    }

    private string? _goDirection;
    public string? GoDirection {
        get => _goDirection;
        set => SetField(ref _goDirection, value);
    }

    private int _distanceInt;
    public int DistanceInt {
        get => _distanceInt;
        set => SetField(ref _distanceInt, value);
    }

    private Brush _compassBrush = Brushes.Gold;
    public Brush CompassBrush {
        get => _compassBrush;
        set => SetField(ref _compassBrush, value);
    }

    private double _northRotation;
    public double NorthRotation {
        get => _northRotation;
        set => SetField(ref _northRotation, value);
    }

    private double _destinationRotation;
    public double DestinationRotation {
        get => _destinationRotation;
        set => SetField(ref _destinationRotation, value);
    }

    private double _destinationOffset = -CircleRadius;
    public double DestinationOffset {
        get => _destinationOffset;
        set => SetField(ref _destinationOffset, value);
    }

    private Visibility _destinationVisibility = Visibility.Hidden;
    public Visibility DestinationVisibility {
        get => _destinationVisibility;
        set => SetField(ref _destinationVisibility, value);
    }

    private Visibility _leftButtonVisibility = Visibility.Hidden;
    public Visibility LeftButtonVisibility {
        get => _leftButtonVisibility;
        set => SetField(ref _leftButtonVisibility, value);
    }

    private Visibility _rightButtonVisibility = Visibility.Hidden;
    public Visibility RightButtonVisibility {
        get => _rightButtonVisibility;
        set => SetField(ref _rightButtonVisibility, value);
    }

    private Brush _labelDirectionFill = Brushes.White;
    public Brush LabelDirectionFill {
        get => _labelDirectionFill;
        set => SetField(ref _labelDirectionFill, value);
    }

    public ICommand CopyLocationToDestinationCommand { get; }
    public ICommand AddLocationCommand { get; }
    public ICommand RemoveLocationCommand { get; }
    public ICommand TimerCommand { get; }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(Settings)) {
            if (_lastWindowHandle != IntPtr.Zero) {
                StartWatcher(_lastWindowHandle);
            }
        }
    }

    public MainViewModel(ISettingsService settingsService, IWatcherService watcherService) {
        _settingsService = settingsService;
        _watcherService = watcherService;
        _watcherService.LocationUpdated += (s, coords) => CurrentCoordinates = coords;
        this.PropertyChanged += ViewModel_PropertyChanged;
        SwapSettingsSubscriptions(null, _settings);
        LoadSettings();
        LoadLocations();
        UpdateListStatus();

        CopyLocationToDestinationCommand = new RelayCommand(_ => TargetCoordinates = CurrentCoordinates ?? string.Empty);
        AddLocationCommand = new RelayCommand(_ => AddLocation());
        RemoveLocationCommand = new RelayCommand(_ => RemoveLocation());
        TimerCommand = new RelayCommand(p => {
            if (p is TimerController timer) timer.Toggle();
        });
    }

    public MainViewModel() : this(new SettingsService(), new WatcherService()) { }

    public void StartWatcher(IntPtr windowHandle) {
        _lastWindowHandle = windowHandle;
        _watcherService.Start(Settings, windowHandle);
    }

    public void StopWatcher() {
        _watcherService.Stop();
    }

    public void HandleClipboardUpdate() {
        if (_watcherService is WatcherService ws) {
            ws.HandleClipboardUpdate();
        }
    }

    public void LoadSettings() {
        Settings = _settingsService.LoadSettings();
    }

    public void SaveSettings() {
        _settingsService.SaveSettings(Settings);
    }

    public void LoadLocations() {
        var list = _settingsService.LoadLocations();
        Locations.Clear();
        foreach (var item in list) {
            item.ScrubbedCoordinates = Scrubber.ScrubEntry(item.Coordinates);
            Locations.Add(item);
        }
    }

    public void SaveLocations() {
        _settingsService.SaveLocations(Locations);
    }

    private void AddLocation() {
        var scrubbedTarget = Scrubber.ScrubEntry(TargetCoordinates);
        if (string.IsNullOrWhiteSpace(scrubbedTarget)) return;

        string name = string.Empty;
        var dialog = new InputDialog("Enter a name for this location:", "Add Location", "");
        dialog.Owner = Application.Current.MainWindow;
        if (dialog.ShowDialog() == true) {
            name = dialog.Answer;
        } else {
            return;
        }

        var item = new LocationItem {
            Name = string.IsNullOrWhiteSpace(name) ? null : name,
            Coordinates = scrubbedTarget,
            ScrubbedCoordinates = scrubbedTarget
        };
        Locations.Add(item);
        SelectedLocation = item;
        SaveLocations();
    }
    
    
    private void RemoveLocation() {
        var item = SelectedLocation;
        if (item == null) {
            var scrubbedTarget = Scrubber.ScrubEntry(TargetCoordinates);
            item = Locations.FirstOrDefault(l => l.ScrubbedCoordinates == scrubbedTarget);
        }

        if (item != null) {
            var result = MessageBox.Show($"Are you sure you want to remove '{item.DisplayName}'?", "Remove Location", MessageBoxButton.YesNo);
            if (result == MessageBoxResult.Yes) {
                Locations.Remove(item);
                SelectedLocation = null;
                OnPropertyChanged(nameof(TargetCoordinates));
                SaveLocations();
            }
        }
    }

    private string? _locationTooltip;
    public string? LocationTooltip {
        get => _locationTooltip;
        set => SetField(ref _locationTooltip, value);
    }

    private string? _destinationTooltip;
    public string? DestinationTooltip {
        get => _destinationTooltip;
        set => SetField(ref _destinationTooltip, value);
    }

    public void ShowDirection() {
        try {
            LeftButtonVisibility = Visibility.Hidden;
            RightButtonVisibility = Visibility.Hidden;
            DestinationVisibility = Visibility.Hidden;
            GoDirection = string.Empty;
            LocationTooltip = null;
            DestinationTooltip = null;

            if (Scrubber.TryParse(CurrentCoordinates, Settings.SelectedProfile.CoordinateOrder, out var current)) {
                LocationTooltip = FormatTooltip(current);
                
                if (!current.Heading.HasValue && _lastCoordinateData.HasValue) {
                    double moveDistance = Math.Sqrt(Math.Pow(current.X - _lastCoordinateData.Value.X, 2) + Math.Pow(current.Y - _lastCoordinateData.Value.Y, 2));
                    if (moveDistance >= MovementThreshold) {
                        double movementHeading = NavigationCalculator.GetDirection(_lastCoordinateData.Value.X, _lastCoordinateData.Value.Y, current.X, current.Y, Settings.SelectedProfile.CoordinateSystem);
                        current = current with { Heading = movementHeading };
                    }
                    else {
                        // If not moving, maintain last heading if it existed
                        current = current with { Heading = _lastCoordinateData.Value.Heading };
                    }
                }
                _lastCoordinateData = current;
            } else {
                return;
            }

            string targetInput = TargetCoordinates ?? string.Empty;
            string? coordinatesToParse = targetInput;
            if (SelectedLocation != null && targetInput == SelectedLocation.DisplayName) {
                coordinatesToParse = SelectedLocation.Coordinates;
            }
            
            if (Scrubber.TryParse(coordinatesToParse, Settings.SelectedProfile.CoordinateOrder, out var target)) {
                DestinationTooltip = FormatTooltip(target);
            } else {
                return;
            }

            DestinationVisibility = Visibility.Visible;

            Tx = $"x:{target.X}";
            Ty = $"y:{target.Y}";
            Cx = $"x:{current.X}";
            Cy = $"y:{current.Y}";

            var direction = NavigationCalculator.GetDirection(current.X, current.Y, target.X, target.Y, Settings.SelectedProfile.CoordinateSystem);
            double distance = Math.Sqrt(Math.Pow(target.X - current.X, 2) + Math.Pow(target.Y - current.Y, 2));

            UpdateDirectionUI(current, target, direction, distance);
            GoDirection = "Go " + NavigationCalculator.GetCompassDirection(direction) + $" {Convert.ToInt32(distance)}m";
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"Error showing direction: {ex.Message}");
            GoDirection = string.Empty;
        }
    }

    private string FormatTooltip(CoordinateData data) {
        string zPart = data.Z.HasValue ? $", Z: {data.Z}" : "";
        string direction = data.Heading.HasValue ? $", Facing: {NavigationCalculator.GetCompassDirection(data.Heading.Value)} ({data.Heading.Value:F1}°)" : "";
        return $"X: {data.X}, Y: {data.Y}{zPart}{direction}";
    }

    private void UpdateDirectionUI(CoordinateData current, CoordinateData target, double direction, double distance) {
        LabelDirectionFill = Brushes.White;

        DistanceInt = (int)distance;
        CompassBrush = distance <= ProximityDistanceThreshold ? Brushes.DodgerBlue : Brushes.Gold;

        if (current.Heading.HasValue) {
            double h = current.Heading.Value;
            if (h >= direction - HeadingTolerancePerfect && h <= direction + HeadingTolerancePerfect) LabelDirectionFill = Brushes.Green;
            else if (h >= direction - HeadingToleranceGood && h <= direction + HeadingToleranceGood) LabelDirectionFill = Brushes.YellowGreen;
            else if (h >= direction - HeadingToleranceFair && h <= direction + HeadingToleranceFair) LabelDirectionFill = Brushes.Yellow;
        }

        if (distance <= ArrivalDistance) {
            LabelDirectionFill = Brushes.Green;
        }

        if (target.Heading.HasValue) {
            TargetHeading = direction;
        }

        NorthRotation = current.Heading.HasValue ? -current.Heading.Value : 0;
        DestinationRotation = current.Heading.HasValue ? direction - current.Heading.Value : direction;

        if (distance <= ProximityDistanceThreshold) {
            // Linear from CircleRadius to 0 as distance goes from GettingCloseDistance (initially determined as 100) to 0
            // We use -CircleRadius (or initially -40) because we want to move up (Top)
            DestinationOffset = -(distance / ProximityDistanceThreshold * CircleRadius);
        }
        else {
            DestinationOffset = -CircleRadius;
        }

        if (current.Heading.HasValue) {
            CurrentHeading = direction;
            TargetHeading = current.Heading.Value;
            CorrectionDirection = NavigationCalculator.DetermineDirection(TargetHeading, CurrentHeading);

            if (CorrectionDirection == "Left") RightButtonVisibility = Visibility.Visible;
            if (CorrectionDirection == "Right") LeftButtonVisibility = Visibility.Visible;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
