using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;
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

    private bool _hasLocations;

    public bool HasLocations {
        get => _hasLocations;
        set => SetField(ref _hasLocations, value);
    }

    public void UpdateHasLocations() {
        HasLocations = Locations.Any();
    }

    private ObservableCollection<LocationItem> _locations = new();
    public ObservableCollection<LocationItem> Locations {
        get => _locations;
        set => SetField(ref _locations, value);
    }

    public TimerController Timer5 { get; } = new(5);
    public TimerController Timer10 { get; } = new(10);
    public TimerController Timer15 { get; } = new(15);
    public TimerController Timer20 { get; } = new(20);

    private bool _mainContentVisibility = true;

    public bool MainContentVisibility {
        get => _mainContentVisibility;
        set => SetField(ref _mainContentVisibility, value);
    }

    private bool _showSettings;

    public bool ShowSettings {
        get => _showSettings;
        set => SetField(ref _showSettings, value);
    }

    private bool _showTimers;

    public bool ShowTimers {
        get => _showTimers;
        set => SetField(ref _showTimers, value);
    }

    private LocationItem? _selectedLocation;

    public LocationItem? SelectedLocation {
        get => _selectedLocation;
        set {
            if (value != null) {
                if (value.Items == null) {
                    if (SetField(ref _selectedLocation, value)) {
                        TargetCoordinates = value?.DisplayName;
                        SyncLocationAndCoordinates(true);
                    }
                }
            }
        }
    }

    // private string? _destinationLocation;
    // public string? DestinationLocation
    // {
    //     get => _destinationLocation;
    //     set
    //     {
    //         //if (SelectedLocation != null && SelectedLocation.DisplayName == value) {
    //             TargetCoordinates = value;
    //         //}
    //         if (_destinationLocation != value)
    //         {
    //             _destinationLocation = value;
    //             OnPropertyChanged(nameof(DestinationLocation));
    //             // Add logic here to track the single selected item in the main ViewModel
    //         }
    //     }
    // }

    private bool _isSelected;

    public bool IsSelected {
        get => _isSelected;
        set {
            if (_isSelected != value) {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
                // Add logic here to track the single selected item in the main ViewModel
            }
        }
    }

    private bool _isExpanded;

    public bool IsExpanded {
        get => _isExpanded;
        set {
            if (_isExpanded != value) {
                _isExpanded = value;
                OnPropertyChanged(nameof(IsExpanded));
                // Add logic here to track the single selected item in the main ViewModel
            }
        }
    }

    private bool _isItemInListAndHasValue;

    public bool IsItemInListAndHasValue => IsItemNotInList && !string.IsNullOrWhiteSpace(TargetCoordinates);
    
    private bool _isItemInList;

    public bool IsItemInList {
        get => _isItemInList;
        set {
            if (SetField(ref _isItemInList, value)) {
                OnPropertyChanged(nameof(IsItemNotInList));
                OnPropertyChanged(nameof(IsItemInListAndHasValue));
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
        }
        else {
            string currentInput = _targetCoordinates ?? string.Empty;
            var scrubbed = Scrubber.ScrubEntry(currentInput);

            var temp = Locations.Where(l => l.Items == null).ToList();
            temp.AddRange(Locations.Where(l => l.Items != null).SelectMany(y => y.Items!));
            // var matchingItem = Locations.FirstOrDefault(l =>
            //     l.ScrubbedCoordinates == scrubbed || currentInput == l.DisplayName);
            var matchingItem = temp.FirstOrDefault(l =>
                l.ScrubbedCoordinates == scrubbed || currentInput == l.DisplayName);
            if (_selectedLocation != matchingItem) {
                _selectedLocation = matchingItem;
                OnPropertyChanged(nameof(SelectedLocation));
            }
        }

        ShowDirection();
        UpdateListStatus();
    }

    public void UpdateListStatus() {
        string scrubbedT;
        if (SelectedLocation != null && TargetCoordinates == SelectedLocation.DisplayName) {
            scrubbedT = SelectedLocation.ScrubbedCoordinates ?? "";
        }
        else {
            scrubbedT = Scrubber.ScrubEntry(TargetCoordinates) ?? "";
        }

        if (string.IsNullOrEmpty(TargetCoordinates)) {
            IsItemInList = false;
            OnPropertyChanged(nameof(IsItemInList));
            return;
        }
        var found = !string.IsNullOrEmpty(scrubbedT) && (Locations.Where(x => x.Items == null).Any(l => {
            // string scrubbedT;
            // if (SelectedLocation != null && TargetCoordinates == SelectedLocation.DisplayName) {
            //     scrubbedT = SelectedLocation.ScrubbedCoordinates ?? "";
            // } else {
            //     scrubbedT = Scrubber.ScrubEntry(TargetCoordinates) ?? "";
            // }
            return !string.IsNullOrEmpty(l.ScrubbedCoordinates) && l.ScrubbedCoordinates == scrubbedT;
        }) || Locations.Where(x => x.Items != null).SelectMany(y => y.Items!).Any(l => {
            return !string.IsNullOrEmpty(l.ScrubbedCoordinates) && l.ScrubbedCoordinates == scrubbedT;
        }));

        IsItemInList = found;
        OnPropertyChanged(nameof(IsItemInList));
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
                profile.MapSettings.PropertyChanged -= MapSettings_PropertyChanged;
                profile.MapSettings.Point1.PropertyChanged -= MapSettings_PropertyChanged;
                profile.MapSettings.Point2.PropertyChanged -= MapSettings_PropertyChanged;
            }
        }

        if (newSettings != null) {
            newSettings.PropertyChanged += Settings_PropertyChanged;
            foreach (var profile in newSettings.Profiles) {
                profile.PropertyChanged -= Profile_PropertyChanged; // Prevent double subscription
                profile.PropertyChanged += Profile_PropertyChanged;
                
                profile.MapSettings.PropertyChanged -= MapSettings_PropertyChanged;
                profile.MapSettings.PropertyChanged += MapSettings_PropertyChanged;
                profile.MapSettings.Point1.PropertyChanged -= MapSettings_PropertyChanged;
                profile.MapSettings.Point1.PropertyChanged += MapSettings_PropertyChanged;
                profile.MapSettings.Point2.PropertyChanged -= MapSettings_PropertyChanged;
                profile.MapSettings.Point2.PropertyChanged += MapSettings_PropertyChanged;
            }
        }
    }

    private void MapSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        SaveSettings();
    }

    private void Profile_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (sender is GameProfile profile && profile.Name == Settings.LastSelectedProfileName) {
            ShowDirection();
            if (e.PropertyName == nameof(GameProfile.MapSettings)) {
                if (_mapViewModel != null) {
                    _mapViewModel.Settings = profile.MapSettings;
                }
                profile.MapSettings.PropertyChanged -= MapSettings_PropertyChanged;
                profile.MapSettings.PropertyChanged += MapSettings_PropertyChanged;
                profile.MapSettings.Point1.PropertyChanged -= MapSettings_PropertyChanged;
                profile.MapSettings.Point1.PropertyChanged += MapSettings_PropertyChanged;
                profile.MapSettings.Point2.PropertyChanged -= MapSettings_PropertyChanged;
                profile.MapSettings.Point2.PropertyChanged += MapSettings_PropertyChanged;
            }
            if (e.PropertyName == nameof(GameProfile.CoordinateSystem)) {
                if (_mapViewModel != null) {
                    _mapViewModel.CoordinateSystem = profile.CoordinateSystem;
                }
            }
            if (e.PropertyName == nameof(GameProfile.WatchMode) || e.PropertyName == nameof(GameProfile.LogFilePath)) {
                if (_lastWindowHandle != IntPtr.Zero) {
                    StartWatcher(_lastWindowHandle);
                }
            }

            if (e.PropertyName == nameof(GameProfile.LastLocationsFile)) {
                LoadLocations();
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

            LoadLocations();
            ShowDirection();
            SaveSettings();
        }
        else if (e.PropertyName == nameof(AppSettings.ShowSettings) ||
                 e.PropertyName == nameof(AppSettings.ShowTimers)) {
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
                if (_mapViewModel != null) {
                    _mapViewModel.CurrentCoordinatesLabel = value;
                    if (Scrubber.TryParse(_currentCoordinates, Settings.SelectedProfile.CoordinateOrder, out var current)) {
                        _mapViewModel.CurrentPosition = current;
                    }
                }
            }
        }
    }

    private string? _targetCoordinates = string.Empty;

    public string? TargetCoordinates {
        get => _targetCoordinates;
        set {
            if (SetField(ref _targetCoordinates, value ?? string.Empty)) {
                OnPropertyChanged(nameof(IsItemInListAndHasValue));
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

    private Visibility _showCoffeeIcon = Visibility.Collapsed;

    public Visibility ShowCoffeeIcon {
        get => _showCoffeeIcon;
        set => SetField(ref _showCoffeeIcon, value);
    }

    public ICommand CopyLocationToDestinationCommand { get; }
    public ICommand AddLocationCommand { get; }
    public ICommand EditLocationCommand { get; }
    public ICommand RemoveLocationCommand { get; }
    public ICommand SelectLocationFileCommand { get; }
    public ICommand OpenMapCommand { get; }
    public ICommand TimerCommand { get; }
    public ICommand BuyMeACoffeeCommand { get; }

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

        CopyLocationToDestinationCommand =
            new RelayCommand(_ => TargetCoordinates = CurrentCoordinates ?? string.Empty);
        AddLocationCommand = new RelayCommand(_ => AddLocation());
        EditLocationCommand = new RelayCommand(_ => EditLocation());
        SelectLocationFileCommand = new RelayCommand(_ => SelectLocationFile());
        OpenMapCommand = new RelayCommand(_ => OpenMap());
        RemoveLocationCommand = new RelayCommand(_ => RemoveLocation());
        TimerCommand = new RelayCommand(p => {
            if (p is TimerController timer) timer.Toggle();
        });
        BuyMeACoffeeCommand = new RelayCommand(_ => {
            try {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                    FileName = "https://buymeacoffee.com/johnrigsby",
                    UseShellExecute = true
                });
            }
            catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Error opening URL: {ex.Message}");
            }
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
        var list = _settingsService.LoadLocations(Settings.SelectedProfile);
        Locations.Clear();
        foreach (var item in list) {
            item.ScrubbedCoordinates = Scrubber.ScrubEntry(item.Coordinates);
            if (!string.IsNullOrWhiteSpace(item.Header)) {
                if (Locations.Any(l => l.Header == item.Header)) {
                    Locations.Single(l => l.Header == item.Header).Items.Add(item);
                }
                else {
                    Locations.Add(new LocationItem
                        { Header = item.Header, Name = item.Header, Items = new List<LocationItem>() { item } });
                }
            }
            else {
                Locations.Add(item);
            }
        }
        var sorted = Locations.OrderBy(l => l.Name).ToList();
        foreach (var group in sorted.Where(x => x.Items != null).ToList()) {
            group.Items = group.Items!.OrderBy(l => l.Name).ToList();
        }
        Locations = new ObservableCollection<LocationItem>(sorted);
        UpdateHasLocations();
        UpdateMapLocations();
    }

    public void SaveLocations() {
        var temp = new List<LocationItem>();
        foreach (var location in Locations) {
            if (location.Items == null) {
                temp.Add(location);
            }
            else {
                foreach (var item in location.Items) {
                    temp.Add(item);
                }
            }
        }

        _settingsService.SaveLocations(temp, Settings.SelectedProfile);
    }

    private void AddLocation() {
        AddLocation(null);
    }

    private void AddLocation(CoordinateData? customCoords) {
        string scrubbedTarget;
        if (customCoords.HasValue) {
            var coords = customCoords.Value;
            if (Settings.SelectedProfile.CoordinateOrder == "y x") {
                scrubbedTarget = $"{coords.Y:F1} {coords.X:F1}";
            } else if (Settings.SelectedProfile.CoordinateOrder == "y x z") {
                scrubbedTarget = $"{coords.Y:F1} {coords.X:F1} {coords.Z ?? 0:F1}";
            } else if (Settings.SelectedProfile.CoordinateOrder == "x y") {
                scrubbedTarget = $"{coords.X:F1} {coords.Y:F1}";
            } else {
                // Default x z y d
                scrubbedTarget = $"{coords.X:F1} {coords.Z ?? 0:F1} {coords.Y:F1}";
            }
        }
        else {
            scrubbedTarget = Scrubber.ScrubEntry(TargetCoordinates);
        }
        
        if (string.IsNullOrWhiteSpace(scrubbedTarget)) return;

        string? name = string.Empty;
        string? group = string.Empty;
        List<string> groups = Locations.Where(x => x.Items != null).Select(l => l.Header).ToList();
        var dialog = new DestinationDialog("", "", groups);
        dialog.Owner = Application.Current.MainWindow;
        if (dialog.ShowDialog() == true) {
            name = dialog.Answer;
            group = dialog.Group;
        }
        else {
            return;
        }

        var item = new LocationItem {
            Name = string.IsNullOrWhiteSpace(name) ? null : name,
            Coordinates = scrubbedTarget,
            ScrubbedCoordinates = scrubbedTarget,
            Header = string.IsNullOrWhiteSpace(group) ? null : group,
        };
        if (!string.IsNullOrWhiteSpace(item.Header)) {
            if (Locations.Any(l => l.Header == item.Header)) {
                if (Locations.Single(l => l.Header == item.Header).Items == null) {
                    Locations.Single(l => l.Header == item.Header).Items = [];
                }
                Locations.Single(l => l.Header == item.Header).Items!.Add(item);
            }
            else {
                Locations.Add(new LocationItem
                    { Header = item.Header, Name = item.Name, Items = new List<LocationItem> { item } });
            }
        }
        else {
            Locations.Add(item);
        }

        SelectedLocation = item;
        SaveLocations();
        LoadLocations();
        UpdateHasLocations();
        UpdateListStatus();
    }

    private void EditLocation() {
        if (SelectedLocation == null) return;

        var name = SelectedLocation.Name;
        var group = SelectedLocation.Header;
        var groups = Locations.Where(x => x.Items != null).Select(l => l.Header).ToList();
        var dialog = new DestinationDialog(name, group, groups) {
            Owner = Application.Current.MainWindow
        };
        if (dialog.ShowDialog() == true) {
            SelectedLocation.Name = dialog.Answer;
            SelectedLocation.Header = dialog.Group;

            OnPropertyChanged(nameof(SelectedLocation));
            OnPropertyChanged(nameof(Locations));
            SaveLocations();
            LoadLocations();
            
            TargetCoordinates = SelectedLocation.DisplayName;
            OnPropertyChanged(nameof(TargetCoordinates));
            
            UpdateListStatus();
        }
    }
    
    private void SelectLocationFile() {
        var dialog = new LocationsFileAssignmentDialog(_settings.SelectedProfile) {
            Owner = Application.Current.MainWindow
        };
        if (dialog.ShowDialog() == true) {
            _settings.SelectedProfile.LastLocationsFile = dialog.LocationsPath??"";
            OnPropertyChanged(nameof(SelectedLocation));
            OnPropertyChanged(nameof(Locations));
            OnPropertyChanged(nameof(TargetCoordinates));
            SaveSettings();
            LoadLocations();
            UpdateHasLocations();
            UpdateListStatus();
        }
    }
    
    

    private void RemoveLocation() {
        var item = SelectedLocation;
        var scrubbedTarget = string.Empty;

        if (item == null) {
            scrubbedTarget = Scrubber.ScrubEntry(TargetCoordinates);
        }
        else {
            scrubbedTarget = item.ScrubbedCoordinates;
        }

        if (item != null) {
            item = Locations.Where(x => x.Items == null).FirstOrDefault(l => l.ScrubbedCoordinates == scrubbedTarget);

            if (item != null) {
                var result = MessageBox.Show($"Are you sure you want to remove '{item.DisplayName}'?",
                    "Remove Location", MessageBoxButton.YesNo);
                if (result == MessageBoxResult.Yes) {
                    Locations.Remove(item);
                    SelectedLocation = null;
                    TargetCoordinates = "";
                    OnPropertyChanged(nameof(SelectedLocation));
                    OnPropertyChanged(nameof(TargetCoordinates));
                    SaveLocations();
                    LoadLocations();
                    UpdateHasLocations();
                    UpdateListStatus();
                }

                return;
            }

            item = Locations.Where(x => x.Items != null).SelectMany(y => y.Items!)
                .FirstOrDefault(l => l.ScrubbedCoordinates == scrubbedTarget);

            if (item != null) {
                var result = MessageBox.Show($"Are you sure you want to remove '{item.DisplayName}'?",
                    "Remove Location", MessageBoxButton.YesNo);
                if (result == MessageBoxResult.Yes) {
                    foreach (var location in Locations.Where(x => x.Items == null)) {
                        if (location == item) {
                            Locations.Where(x => x.Items == null).ToList().Remove(location);
                        }
                    }

                    foreach (var group in Locations.Where(x => x.Items != null).ToList()) {
                        foreach (var location in group.Items!.ToList()) {
                            if (item == location) {
                                group.Items!.Remove(location);
                            }
                        }

                        if (group.Items!.Count == 0) {
                            Locations.Where(x => x.Items != null).ToList().Remove(group);
                        }
                    }

                    SelectedLocation = null;
                    TargetCoordinates = "";
                    OnPropertyChanged(nameof(SelectedLocation));
                    OnPropertyChanged(nameof(TargetCoordinates));
                    SaveLocations();
                    LoadLocations();
                    UpdateHasLocations();
                    UpdateListStatus();
                }
            }
            //item = Locations.FirstOrDefault(l => l.ScrubbedCoordinates == scrubbedTarget);
        }

        // if (item != null) {
        //     var result = MessageBox.Show($"Are you sure you want to remove '{item.DisplayName}'?", "Remove Location", MessageBoxButton.YesNo);
        //     if (result == MessageBoxResult.Yes) {
        //         Locations.Remove(item);
        //         SelectedLocation = null;
        //         OnPropertyChanged(nameof(TargetCoordinates));
        //         SaveLocations();
        //     }
        // }
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
                    double moveDistance = Math.Sqrt(Math.Pow(current.X - _lastCoordinateData.Value.X, 2) +
                                                    Math.Pow(current.Y - _lastCoordinateData.Value.Y, 2));
                    if (moveDistance >= MovementThreshold) {
                        double movementHeading = NavigationCalculator.GetDirection(_lastCoordinateData.Value.X,
                            _lastCoordinateData.Value.Y, current.X, current.Y,
                            Settings.SelectedProfile.CoordinateSystem);
                        current = current with { Heading = movementHeading };
                    }
                    else {
                        // If not moving, maintain last heading if it existed
                        current = current with { Heading = _lastCoordinateData.Value.Heading };
                    }
                }

                _lastCoordinateData = current;
            }
            else {
                return;
            }

            string targetInput = TargetCoordinates ?? string.Empty;
            string? coordinatesToParse = targetInput;
            if (SelectedLocation != null && targetInput == SelectedLocation.DisplayName) {
                coordinatesToParse = SelectedLocation.Coordinates;
            }

            if (Scrubber.TryParse(coordinatesToParse, Settings.SelectedProfile.CoordinateOrder, out var target)) {
                DestinationTooltip = FormatTooltip(target);
                DestinationVisibility = Visibility.Visible;
            }
            else {
                DestinationVisibility = Visibility.Hidden;
                if (_mapViewModel != null) {
                    _mapViewModel.TargetPosition = null;
                }
                return;
            }

            Tx = $"x:{target.X}";
            Ty = $"y:{target.Y}";
            Cx = $"x:{current.X}";
            Cy = $"y:{current.Y}";

            var direction = NavigationCalculator.GetDirection(current.X, current.Y, target.X, target.Y,
                Settings.SelectedProfile.CoordinateSystem);
            double distance = Math.Sqrt(Math.Pow(target.X - current.X, 2) + Math.Pow(target.Y - current.Y, 2));

            UpdateDirectionUI(current, target, direction, distance);
            if (_mapViewModel != null) {
                _mapViewModel.CurrentPosition = current;
                _mapViewModel.TargetPosition = target;
            }
            if (distance == 0) {
                GoDirection = "You have arrived";
                return;
            }
            GoDirection = "Go " + NavigationCalculator.GetCompassDirection(direction) +
                          $" {Convert.ToInt32(distance)}m";
        }
        catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"Error showing direction: {ex.Message}");
            GoDirection = string.Empty;
        }
    }

    private string FormatTooltip(CoordinateData data) {
        string zPart = data.Z.HasValue ? $", Z: {data.Z}" : "";
        string direction = data.Heading.HasValue
            ? $", Facing: {NavigationCalculator.GetCompassDirection(data.Heading.Value)} ({data.Heading.Value:F1}°)"
            : "";
        return $"X: {data.X}, Y: {data.Y}{zPart}{direction}";
    }

    private void UpdateDirectionUI(CoordinateData current, CoordinateData target, double direction, double distance) {
        LabelDirectionFill = Brushes.White;

        DistanceInt = (int)distance;
        CompassBrush = distance <= ProximityDistanceThreshold ? Brushes.DodgerBlue : Brushes.Gold;
        ShowCoffeeIcon = distance <= ArrivalDistance ? Visibility.Visible : Visibility.Collapsed;

        if (current.Heading.HasValue) {
            double h = current.Heading.Value;
            if (h >= direction - HeadingTolerancePerfect && h <= direction + HeadingTolerancePerfect)
                LabelDirectionFill = Brushes.Green;
            else if (h >= direction - HeadingToleranceGood && h <= direction + HeadingToleranceGood)
                LabelDirectionFill = Brushes.YellowGreen;
            else if (h >= direction - HeadingToleranceFair && h <= direction + HeadingToleranceFair)
                LabelDirectionFill = Brushes.Yellow;
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

    private MapWindow? _mapWindow;
    private MapViewModel? _mapViewModel;

    private void UpdateMapLocations() {
        if (_mapViewModel == null) return;

        var existingLocations = _mapViewModel.Locations.ToList();
        var newFlattened = new List<MapLocation>();
        foreach (var loc in Locations) {
            AddLocationToMap(loc, newFlattened);
        }

        // If the count is different, it's easier to just reset (this happens when adding/loading)
        if (existingLocations.Count != newFlattened.Count) {
            _mapViewModel.Locations = new ObservableCollection<MapLocation>(newFlattened);
        }
        else {
            // Try to update in place to preserve UI elements and ToolTips
            bool changed = false;
            for (int i = 0; i < newFlattened.Count; i++) {
                if (existingLocations[i].Coordinates != newFlattened[i].Coordinates ||
                    existingLocations[i].DisplayName != newFlattened[i].DisplayName) {
                    changed = true;
                    break;
                }
            }

            if (changed) {
                _mapViewModel.Locations = new ObservableCollection<MapLocation>(newFlattened);
            }
        }
        
        _mapViewModel.UpdateMarkers();
    }

    private void AddLocationToMap(LocationItem item, List<MapLocation> flattened) {
        if (item.Items != null) {
            foreach (var subItem in item.Items) {
                AddLocationToMap(subItem, flattened);
            }
        }
        else {
            flattened.Add(new MapLocation {
                DisplayName = item.Name ?? string.Empty,
                Tooltip = item.DisplayName, // Contains Name and Coordinates
                Coordinates = item.ScrubbedCoordinates ?? string.Empty
            });
        }
    }

    private void OpenMap() {
        if (_mapWindow == null || !Application.Current.Windows.OfType<MapWindow>().Any()) {
            _mapViewModel = new MapViewModel(Settings.SelectedProfile.MapSettings, Settings);
            _mapViewModel.CoordinateSystem = Settings.SelectedProfile.CoordinateSystem;
            _mapViewModel.CurrentCoordinatesLabel = CurrentCoordinates;
            if (Scrubber.TryParse(CurrentCoordinates, Settings.SelectedProfile.CoordinateOrder, out var currentPos)) {
                _mapViewModel.CurrentPosition = currentPos;
            } else {
                _mapViewModel.CurrentPosition = null;
            }

            string targetInput = TargetCoordinates ?? string.Empty;
            string? coordinatesToParse = targetInput;
            if (SelectedLocation != null && targetInput == SelectedLocation.DisplayName) {
                coordinatesToParse = SelectedLocation.Coordinates;
            }

            if (Scrubber.TryParse(coordinatesToParse, Settings.SelectedProfile.CoordinateOrder, out var targetPos)) {
                _mapViewModel.TargetPosition = targetPos;
            }
            else {
                _mapViewModel.TargetPosition = null;
            }
            UpdateMapLocations();
            _mapWindow = new MapWindow(_mapViewModel);
            _mapViewModel.DestinationSelected += coords => {
                string formatted;
                if (Settings.SelectedProfile.CoordinateOrder == "y x") {
                    formatted = $"{coords.Y:F1}, {coords.X:F1}";
                } else if (Settings.SelectedProfile.CoordinateOrder == "y x z") {
                    formatted = $"{coords.Y:F1}, {coords.X:F1}, {coords.Z ?? 0:F1}";
                } else if (Settings.SelectedProfile.CoordinateOrder == "x y") {
                    formatted = $"{coords.X:F1}, {coords.Y:F1}";
                } else {
                    // Default x z y d
                    formatted = $"{coords.X:F1}, {coords.Z ?? 0:F1}, {coords.Y:F1}";
                }
                SelectedLocation = null;
                TargetCoordinates = formatted;
                ShowDirection();
            };
            _mapViewModel.PinRequested += coords => {
                Application.Current.Dispatcher.Invoke(() => {
                    AddLocation(coords);
                    UpdateMapLocations();
                });
            };
            // _mapWindow.Owner = Application.Current.MainWindow;
            _mapWindow.Closed += (s, e) => {
                _mapWindow = null;
                _mapViewModel = null;
            };
            _mapWindow.Show();
        } else {
            if (_mapWindow.WindowState == WindowState.Minimized) {
                _mapWindow.WindowState = WindowState.Normal;
            }
            if (!Settings.KeyboardClickThrough) {
                _mapWindow.Activate();
            }
            _mapWindow.Show();
        }
    }

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