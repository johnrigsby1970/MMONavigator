// MMONavigator 
// Copyright (C) 2026 John Rigsby
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Windows.Services.Store;
using MMONavigator.Helpers;
using MMONavigator.Interfaces;
using MMONavigator.Models;
using MMONavigator.Services;
using MMONavigator.Views;

namespace MMONavigator.ViewModels;

public class MainViewModel : INotifyPropertyChanged, IDisposable {
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
    private DispatcherTimer? _saveDebounceTimer;
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
                        TargetCoordinates = value.DisplayName;
                        SyncLocationAndCoordinates(true);
                    }
                }
            }
        }
    }

    private bool _isSelected;

    public bool IsSelected {
        get => _isSelected;
        set {
            if (_isSelected != value) {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isExpanded;

    public bool IsExpanded {
        get => _isExpanded;
        set {
            if (_isExpanded != value) {
                _isExpanded = value;
                OnPropertyChanged();
                Log.Debug("Popup expanded state changed: {IsExpanded}", _isExpanded);
            }
        }
    }

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
            AppSettings oldSettings = _settings;
            if (SetField(ref _settings, value)) {
                SwapSettingsSubscriptions(oldSettings, value);
            }
        }
    }

    private void SwapSettingsSubscriptions(AppSettings? oldSettings, AppSettings? newSettings) {
        if (oldSettings?.Profiles != null) {
            oldSettings.PropertyChanged -= Settings_PropertyChanged;

            foreach (var profile in oldSettings.Profiles) {
                profile.PropertyChanged -= Profile_PropertyChanged;

                profile.MapSettings.PropertyChanged -= MapSettings_PropertyChanged;
                profile.MapSettings.Point1.PropertyChanged -= MapSettings_PropertyChanged;
                profile.MapSettings.Point2.PropertyChanged -= MapSettings_PropertyChanged;
            }
        }

        if (newSettings?.Profiles != null) {
            newSettings.PropertyChanged -= Settings_PropertyChanged;
            newSettings.PropertyChanged += Settings_PropertyChanged;

            foreach (var profile in newSettings.Profiles) {
                profile.PropertyChanged -= Profile_PropertyChanged;
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
                    // If the profile has settings, use them; otherwise, provide a safe fallback or new instance
                    _mapViewModel.Settings = profile.MapSettings ?? new MapSettings();
                }

                // Safe unhook/re-hook with null checks
                if (profile.MapSettings != null) {
                    profile.MapSettings.PropertyChanged -= MapSettings_PropertyChanged;
                    profile.MapSettings.PropertyChanged += MapSettings_PropertyChanged;

                    profile.MapSettings.Point1.PropertyChanged -= MapSettings_PropertyChanged;
                    profile.MapSettings.Point1.PropertyChanged += MapSettings_PropertyChanged;

                    profile.MapSettings.Point2.PropertyChanged -= MapSettings_PropertyChanged;
                    profile.MapSettings.Point2.PropertyChanged += MapSettings_PropertyChanged;
                }
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

    public bool HasCoordinates => !string.IsNullOrEmpty(CurrentCoordinates) && !string.IsNullOrEmpty(TargetCoordinates);
    public bool HasEitherCoordinates => !string.IsNullOrEmpty(CurrentCoordinates) || !string.IsNullOrEmpty(TargetCoordinates);
    
    private bool _hideLocHint;

    public  bool HideLocHint {
        get => _hideLocHint;
        set {
            if (SetField(ref _hideLocHint, value)) {
            }
        }
    }
    
    private string? _currentCoordinates = "";

    public string? CurrentCoordinates {
        get => _currentCoordinates;
        set {
            if (SetField(ref _currentCoordinates, value)) {
                ShowDirection();
                if (_mapViewModel != null || _threeDMapViewModel != null) {
                    if (_mapViewModel != null) _mapViewModel.CurrentCoordinatesLabel = value;
                    if (_threeDMapViewModel != null) _threeDMapViewModel.CurrentCoordinatesLabel = value;
                    if (Scrubber.TryParse(_currentCoordinates, Settings.SelectedProfile.CoordinateOrder,
                            out var current)) {
                        if (_mapViewModel != null) _mapViewModel.CurrentPosition = current;
                        if (_threeDMapViewModel != null) _threeDMapViewModel.CurrentPosition = current;
                    }
                }
                OnPropertyChanged(nameof(HasCoordinates));
                OnPropertyChanged(nameof(HasEitherCoordinates));
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
                OnPropertyChanged(nameof(HasCoordinates));
                OnPropertyChanged(nameof(HasEitherCoordinates));
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

    private System.Windows.Media.Brush _compassBrush = System.Windows.Media.Brushes.Gold;

    public System.Windows.Media.Brush CompassBrush {
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

    private System.Windows.Media.Brush _labelDirectionFill = System.Windows.Media.Brushes.White;

    public System.Windows.Media.Brush LabelDirectionFill {
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
    public ICommand OpenAboutCommand { get; }
    public ICommand OpenMapCommand { get; }

    public ICommand Open3DMapCommand { get; }
    public ICommand TimerCommand { get; }
    public ICommand BuyMeACoffeeCommand { get; }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(Settings)) {
            if (_lastWindowHandle != IntPtr.Zero) {
                StartWatcher(_lastWindowHandle);
            }
        }
    }

    private void OnLocationUpdated(object? sender, string coords) {
        CurrentCoordinates = coords;
    }

    public void Dispose() {
        FlushPendingSave();
        if (_watcherService != null) {
            _watcherService.LocationUpdated -= OnLocationUpdated;
        }
    }

    public void FlushPendingSave() {
        // If a save was debounced/pending, stop the timer cleanly and save synchronously on exit
        if (_saveDebounceTimer != null && _saveDebounceTimer.IsEnabled) {
            StopSaveTimer();
            try {
                _settingsService.SaveSettings(Settings);
            }
            catch (Exception ex) {
                Log.Error(ex, "Error flushing settings during cleanup.");
            }
        }
    }

    public MainViewModel(ISettingsService settingsService, IWatcherService watcherService) {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _watcherService = watcherService ?? throw new ArgumentNullException(nameof(watcherService));

        // Prevent duplicate subscriptions before subscribing
        _watcherService.LocationUpdated -= OnLocationUpdated;
        _watcherService.LocationUpdated += OnLocationUpdated;

        this.PropertyChanged -= ViewModel_PropertyChanged;
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
        OpenAboutCommand = new RelayCommand(_ => OpenAbout());
        OpenMapCommand = new RelayCommand(_ => OpenMap());
        Open3DMapCommand = new RelayCommand(_ => OpenThreeDMap());
        RemoveLocationCommand = new RelayCommand(_ => RemoveLocation());
        TimerCommand = new RelayCommand(p => {
            if (p is TimerController timer) timer.Toggle();
        });
        BuyMeACoffeeCommand = new RelayCommand(_ => {
            try {
                Process.Start(new ProcessStartInfo {
                    FileName = "https://buymeacoffee.com/johnrigsby",
                    UseShellExecute = true
                });
            }
            catch (Exception ex) {
                Log.Error(ex, "Error launching BuyMeACoffee URL.");
            }
        });
    }
    
    
    #region Premium Feature
    
    private bool _isPremiumOverrideValid;
    public bool IsPremiumOverrideValid
    {
        get => _isPremiumOverrideValid;
        set { _isPremiumOverrideValid = value; OnPropertyChanged(); }
    }

    public bool ValidateAndApplyOverrideCode(string inputCode)
    {
        if (string.IsNullOrWhiteSpace(inputCode)) return false;

        // This is the SHA-256 hash of your secret code (e.g., "ISA-IT-WASSECRET")
        // Generating it once and saving only the hash keeps your actual code out of GitHub entirely.
        string expectedHash = "60017641e902b42b99c63f1ba5d48e9581833478483115348d85865fcf394610"; 

        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(inputCode.Trim()));
            string inputHash = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();

            if (inputHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                IsPremiumOverrideValid = true;
                MMONavigator.Properties.Settings.Default.ActivationCode = inputCode.Trim();
                MMONavigator.Properties.Settings.Default.Save();
                return true;
            }
        }
    
        return false;
    }
    
    private async Task<bool> CheckStoreLicenseAsync()
    {
        try
        {
            // Standard Windows Store Context check for MSIX packaged desktop apps
            StoreContext context = StoreContext.GetDefault();
            StoreAppLicense license = await context.GetAppLicenseAsync();
        
            // Check if the specific 3D Map add-on / dlc product ID is active
            if (license.AddOnLicenses.TryGetValue("3DMap", out StoreLicense? addOnLicense))
            {
                return addOnLicense.IsActive;
            }
        }
        catch 
        {
            // Fallback if running unpackaged/debug outside the store environment
        }
        return false;
    }
    
    #endregion

    public MainViewModel() : this(new SettingsService(), new WatcherService()) { }

    public void StartWatcher(IntPtr windowHandle) {
        _lastWindowHandle = windowHandle;
        try {
            _watcherService.Start(Settings, windowHandle);
        }
        catch (Exception ex) {
            Log.Error(ex, "Failed to start WatcherService.");
        }
    }

    public void StopWatcher() {
        try {
            _watcherService.Stop();
        }
        catch (Exception ex) {
            Log.Error(ex, "Failed to stop WatcherService.");
        }
    }

    public void HandleClipboardUpdate() {
        if (_watcherService is WatcherService ws) {
            ws.HandleClipboardUpdate();
        }
    }

    public void LoadSettings() {
        try {
            Settings = _settingsService.LoadSettings();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error loading settings in MainViewModel.");
        }
    }

    public void SaveSettings() {
        try {
            // Must be called on the UI thread to manage the DispatcherTimer safely
            // If we are not on the UI thread, asynchronously dispatch back to it instead of blocking
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess()) {
                dispatcher.BeginInvoke(new Action(SaveSettings));
                return;
            }

            // 1. Unhook and stop any existing timer
            StopSaveTimer();

            // 2. Safely initialize and subscribe with a named handler
            _saveDebounceTimer = new DispatcherTimer {
                Interval = TimeSpan.FromMilliseconds(500)
            };

            _saveDebounceTimer.Tick += SaveDebounceTimer_Tick;
            _saveDebounceTimer.Start();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error initializing debounced SaveSettings.");
        }
    }

    private void SaveDebounceTimer_Tick(object? sender, EventArgs e) {
        // Cleanly unhook immediately so a queued tick can never re-enter
        StopSaveTimer();

        // Snapshot settings on UI thread
        var settingsSnapshot = Settings;

        // Offload disk IO to background thread
        Task.Run(() => {
            try {
                _settingsService.SaveSettings(settingsSnapshot);
            }
            catch (Exception ex) {
                Log.Error(ex, "Error saving settings in debounced background task.");
            }
        });
    }

    private void StopSaveTimer() {
        if (_saveDebounceTimer != null) {
            _saveDebounceTimer.Stop();
            _saveDebounceTimer.Tick -= SaveDebounceTimer_Tick;
            _saveDebounceTimer = null;
        }
    }

    public void LoadLocations() {
        try {
            var list = _settingsService.LoadLocations(Settings.SelectedProfile);
            Locations.Clear();

            void ProcessItems(List<LocationItem> items) {
                foreach (var item in items) {
                    item.ScrubbedCoordinates = Scrubber.ScrubEntry(item.Coordinates);
                    if (item.Items != null) {
                        ProcessItems(item.Items);
                    }
                }
            }

            ProcessItems(list);

            // 2. Flatten the list to get only 'leaf' locations (those that aren't folder nodes themselves)
            // Folder nodes in the saved file are recognized by having an Items collection.
            // We want to rebuild the hierarchy from scratch to avoid redundancy.
            List<LocationItem> GetAllLeafLocations(IEnumerable<LocationItem> items) {
                var leafItems = new List<LocationItem>();
                foreach (var item in items) {
                    if (item.Items == null || item.Items.Count == 0) {
                        // It's a location item
                        leafItems.Add(item);
                    }
                    else {
                        // It's a folder node, get its children
                        leafItems.AddRange(GetAllLeafLocations(item.Items));
                    }
                }

                return leafItems;
            }

            var flattenedLeafs = GetAllLeafLocations(list);

            // 3. Rebuild the hierarchy based on the Header property of each leaf
            foreach (var item in flattenedLeafs) {
                if (!string.IsNullOrWhiteSpace(item.Header)) {
                    var group = Locations.FirstOrDefault(l => l.Header == item.Header);
                    if (group != null) {
                        group.Items ??= new List<LocationItem>();
                        group.Items.Add(item);
                    }
                    else {
                        Locations.Add(new LocationItem
                            { Header = item.Header, Name = item.Header, Items = new List<LocationItem> { item } });
                    }
                }
                else {
                    Locations.Add(item);
                }
            }

            // 4. Sort recursively
            void SortItems(List<LocationItem> items) {
                var sortedList = items.OrderBy(l => l.Name).ToList();
                items.Clear();
                items.AddRange(sortedList);
                foreach (var item in items) {
                    if (item.Items != null) {
                        SortItems(item.Items);
                    }
                }
            }

            var rootSorted = Locations.OrderBy(l => l.Name).ToList();
            foreach (var group in rootSorted.Where(x => x.Items != null)) {
                SortItems(group.Items!);
            }

            Locations = new ObservableCollection<LocationItem>(rootSorted);
            UpdateHasLocations();
            UpdateMapLocations();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error loading locations in MainViewModel.");
        }
    }

    public void SaveLocations() {
        try {
            // Flatten the locations to save only 'leaf' items.
            // The hierarchy will be rebuilt from the 'Header' property during LoadLocations.
            List<LocationItem> GetLeafLocations(IEnumerable<LocationItem> items) {
                var leafs = new List<LocationItem>();
                foreach (var item in items) {
                    if (item.Items == null || item.Items.Count == 0) {
                        leafs.Add(item);
                    }
                    else {
                        leafs.AddRange(GetLeafLocations(item.Items));
                    }
                }

                return leafs;
            }

            var flattened = GetLeafLocations(Locations);
            _settingsService.SaveLocations(flattened, Settings.SelectedProfile);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error saving locations in MainViewModel.");
        }
    }

    private void AddLocation() {
        AddLocation(null);
    }

    private void AddLocation(CoordinateData? customCoords) {
        try {
            string? scrubbedTarget;
            if (customCoords.HasValue) {
                var coords = customCoords.Value;
                if (Settings.SelectedProfile.CoordinateOrder == "y x") {
                    scrubbedTarget = $"{coords.Y:F1} {coords.X:F1}";
                }
                else if (Settings.SelectedProfile.CoordinateOrder == "y x z") {
                    scrubbedTarget = $"{coords.Y:F1} {coords.X:F1} {coords.Z ?? 0:F1}";
                }
                else if (Settings.SelectedProfile.CoordinateOrder == "x y") {
                    scrubbedTarget = $"{coords.X:F1} {coords.Y:F1}";
                }
                else {
                    // Default x z y d
                    scrubbedTarget = $"{coords.X:F1} {coords.Z ?? 0:F1} {coords.Y:F1}";
                }
            }
            else {
                scrubbedTarget = string.IsNullOrWhiteSpace(TargetCoordinates)
                    ? ""
                    : Scrubber.ScrubEntry(TargetCoordinates);
            }

            if (string.IsNullOrWhiteSpace(scrubbedTarget)) return;

            var name = string.Empty;
            var group = string.Empty;

            List<string> GetAllGroups(IEnumerable<LocationItem> items) {
                var result = new List<string>();
                foreach (var item in items) {
                    if (item.Items != null && !string.IsNullOrWhiteSpace(item.Header)) {
                        result.Add(item.Header);
                        result.AddRange(GetAllGroups(item.Items));
                    }
                }

                return result;
            }

            List<string> groups = GetAllGroups(Locations).Distinct().ToList();
            var dialog = new DestinationDialog("", "", groups) {
                Owner = System.Windows.Application.Current.MainWindow
            };
            dialog.ShowDialog();

            // Check your manual property instead of the built-in DialogResult
            if (dialog.ManualDialogResult == true) {
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
                LocationItem? FindGroup(IEnumerable<LocationItem> items, string header) {
                    foreach (var g in items) {
                        if (g.Header == header) return g;
                        if (g.Items != null) {
                            var found = FindGroup(g.Items, header);
                            if (found != null) return found;
                        }
                    }

                    return null;
                }

                var groupItem = FindGroup(Locations, item.Header);
                if (groupItem != null) {
                    groupItem.Items ??= new List<LocationItem>();
                    groupItem.Items.Add(item);
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

            // After LoadLocations, SelectedLocation reference is stale. Re-identify it.
            LocationItem? FindSame(IEnumerable<LocationItem> items, LocationItem target) {
                foreach (var i in items) {
                    if (i.Name == target.Name && i.ScrubbedCoordinates == target.ScrubbedCoordinates &&
                        i.Header == target.Header) return i;
                    if (i.Items != null) {
                        var found = FindSame(i.Items, target);
                        if (found != null) return found;
                    }
                }

                return null;
            }

            SelectedLocation = FindSame(Locations, item);

            UpdateHasLocations();
            UpdateListStatus();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error adding location.");
        }
    }

    private void EditLocation() {
        if (SelectedLocation == null) return;

        try {
            var name = SelectedLocation.Name;
            var group = SelectedLocation.Header;
            var groups = Locations.Where(x => x.Items != null && !string.IsNullOrWhiteSpace(x.Header))
                .Select(l => l.Header!).ToList();

            var dialog = new DestinationDialog(name, group, groups) {
                Owner = System.Windows.Application.Current.MainWindow
            };
            dialog.ShowDialog();

            // Check your manual property instead of the built-in DialogResult
            if (dialog.ManualDialogResult == true) {
                SelectedLocation.Name = dialog.Answer;
                SelectedLocation.Header = dialog.Group;

                OnPropertyChanged(nameof(SelectedLocation));
                OnPropertyChanged(nameof(Locations));
                SaveLocations();
                LoadLocations();

                // After LoadLocations, SelectedLocation reference is stale. Re-identify it.
                LocationItem? FindSame(IEnumerable<LocationItem> items, string? name, string? coords, string? header) {
                    foreach (var i in items) {
                        if (i.Name == name && i.ScrubbedCoordinates == coords && i.Header == header) return i;
                        if (i.Items != null) {
                            var found = FindSame(i.Items, name, coords, header);
                            if (found != null) return found;
                        }
                    }

                    return null;
                }

                SelectedLocation = FindSame(Locations, dialog.Answer, SelectedLocation.ScrubbedCoordinates,
                    dialog.Group);

                TargetCoordinates = SelectedLocation?.DisplayName ?? "";
                OnPropertyChanged(nameof(TargetCoordinates));

                UpdateListStatus();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error editing location.");
        }
    }

    private void SelectLocationFile() {
        try {
            var dialog = new LocationsFileAssignmentDialog(_settings.SelectedProfile) {
                Owner = System.Windows.Application.Current.MainWindow
            };
            dialog.ShowDialog();

            // Check your manual property instead of the built-in DialogResult
            if (dialog.ManualDialogResult == true) {
                _settings.SelectedProfile.LastLocationsFile = dialog.LocationsPath ?? "";
                OnPropertyChanged(nameof(SelectedLocation));
                OnPropertyChanged(nameof(Locations));
                OnPropertyChanged(nameof(TargetCoordinates));
                SaveSettings();
                LoadLocations();
                UpdateHasLocations();
                UpdateListStatus();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error selecting location file.");
        }
    }

    private void RemoveLocation() {
        if (SelectedLocation == null) return;

        try {
            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to remove '{SelectedLocation.DisplayName}'?",
                "Remove Location", MessageBoxButton.YesNo);
            if (result != MessageBoxResult.Yes) return;

            bool RemoveRecursive(IList<LocationItem> items, LocationItem target) {
                if (items.Remove(target)) return true;
                foreach (var item in items) {
                    if (item.Items != null && RemoveRecursive(item.Items, target)) {
                        if (item.Items.Count == 0) {
                            items.Remove(item);
                        }

                        return true;
                    }
                }

                return false;
            }

            if (RemoveRecursive(Locations, SelectedLocation)) {
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
        catch (Exception ex) {
            Log.Error(ex, "Error removing location.");
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
            var determineDirection = true;

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
                determineDirection = false;
            }

            var targetInput = TargetCoordinates ?? string.Empty;
            var coordinatesToParse = targetInput;
            if (SelectedLocation != null && targetInput == SelectedLocation.DisplayName) {
                coordinatesToParse = SelectedLocation.Coordinates;
            }

            if (Scrubber.TryParse(coordinatesToParse, Settings.SelectedProfile.CoordinateOrder, out var target)) {
                DestinationTooltip = FormatTooltip(target);
                DestinationVisibility = Visibility.Visible;
                if (_mapViewModel != null && !Nullable.Equals(_mapViewModel.TargetPosition, target)) {
                    _mapViewModel.TargetPosition = target;
                }

                if (_threeDMapViewModel != null && !Nullable.Equals(_threeDMapViewModel.TargetPosition, target)) {
                    _threeDMapViewModel.TargetPosition = target;
                }
            }
            else {
                DestinationVisibility = Visibility.Hidden;
                if (_mapViewModel != null && _mapViewModel.TargetPosition != null) {
                    _mapViewModel.TargetPosition = null;
                }

                if (_threeDMapViewModel != null && _threeDMapViewModel.TargetPosition != null) {
                    _threeDMapViewModel.TargetPosition = null;
                }

                return;
            }

            if (!determineDirection) return;

            Tx = $"x:{target.X}";
            Ty = $"y:{target.Y}";
            Cx = $"x:{current.X}";
            Cy = $"y:{current.Y}";

            var direction = NavigationCalculator.GetDirection(current.X, current.Y, target.X, target.Y,
                Settings.SelectedProfile.CoordinateSystem);
            var distance = Math.Sqrt(Math.Pow(target.X - current.X, 2) + Math.Pow(target.Y - current.Y, 2));

            UpdateDirectionUI(current, target, direction, distance);
            if (_mapViewModel != null) {
                _mapViewModel.CurrentPosition = current;
                _mapViewModel.TargetPosition = target;
            }

            if (_threeDMapViewModel != null) {
                _threeDMapViewModel.CurrentPosition = current;
                _threeDMapViewModel.TargetPosition = target;
            }

            if (distance == 0) {
                GoDirection = "You have arrived";
                return;
            }

            GoDirection = "Go " + NavigationCalculator.GetCompassDirection(direction) +
                          $" {Convert.ToInt32(distance)}m";
        }
        catch (Exception ex) {
            Log.Error(ex, "Error calculating direction in ShowDirection.");
            GoDirection = string.Empty;
        }
    }

    private string FormatTooltip(CoordinateData data) {
        var zPart = data.Z.HasValue ? $", Z: {data.Z}" : "";
        var direction = data.Heading.HasValue
            ? $", Facing: {NavigationCalculator.GetCompassDirection(data.Heading.Value)} ({data.Heading.Value:F1}°)"
            : "";
        return $"X: {data.X}, Y: {data.Y}{zPart}{direction}";
    }

    private void UpdateDirectionUI(CoordinateData current, CoordinateData target, double direction, double distance) {
        LabelDirectionFill = System.Windows.Media.Brushes.White;

        DistanceInt = (int)distance;
        CompassBrush = distance <= ProximityDistanceThreshold
            ? System.Windows.Media.Brushes.DodgerBlue
            : System.Windows.Media.Brushes.Gold;
        ShowCoffeeIcon = distance <= ArrivalDistance ? Visibility.Visible : Visibility.Collapsed;

        if (current.Heading.HasValue) {
            double h = current.Heading.Value;
            if (h >= direction - HeadingTolerancePerfect && h <= direction + HeadingTolerancePerfect)
                LabelDirectionFill = System.Windows.Media.Brushes.Green;
            else if (h >= direction - HeadingToleranceGood && h <= direction + HeadingToleranceGood)
                LabelDirectionFill = System.Windows.Media.Brushes.YellowGreen;
            else if (h >= direction - HeadingToleranceFair && h <= direction + HeadingToleranceFair)
                LabelDirectionFill = System.Windows.Media.Brushes.Yellow;
        }

        if (distance <= ArrivalDistance) {
            LabelDirectionFill = System.Windows.Media.Brushes.Green;
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

    private ThreeDMapWindow? _threeDMapWindow;
    private ThreeDMapViewModel? _threeDMapViewModel;

    private void UpdateMapLocations() {
        if (_mapViewModel == null) return;

        try {
            var existingLocations = _mapViewModel.Locations.ToList();
            var newFlattened = new List<MapLocation>();
            foreach (var loc in Locations) {
                AddLocationToMap(loc, newFlattened);
            }

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
        catch (Exception ex) {
            Log.Error(ex, "Error updating map locations.");
        }
    }

    private void UpdateThreeDMapLocations() {
        if (_threeDMapViewModel == null) return;

        try {
            var existingLocations = _threeDMapViewModel.Locations.ToList();
            var newFlattened = new List<MapLocation>();
            foreach (var loc in Locations) {
                AddLocationToMap(loc, newFlattened);
            }

            if (existingLocations.Count != newFlattened.Count) {
                _threeDMapViewModel.Locations = new ObservableCollection<MapLocation>(newFlattened);
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
                    _threeDMapViewModel.Locations = new ObservableCollection<MapLocation>(newFlattened);
                }
            }

            _threeDMapViewModel.UpdateMarkers();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error updating map locations.");
        }
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
                Tooltip = item.DisplayName,
                Coordinates = item.ScrubbedCoordinates ?? string.Empty
            });
        }
    }

    private void OpenAbout() {
        try {
            var aboutWindow = new About();
            aboutWindow.Show();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error opening About window.");
        }
    }

    private void OpenMap() {
        try {
            if (_mapWindow == null || !System.Windows.Application.Current.Windows.OfType<MapWindow>().Any()) {
                _mapViewModel = new MapViewModel(Settings.SelectedProfile.MapSettings, Settings) {
                    CoordinateSystem = Settings.SelectedProfile.CoordinateSystem,
                    CurrentCoordinatesLabel = CurrentCoordinates
                };
                if (Scrubber.TryParse(CurrentCoordinates, Settings.SelectedProfile.CoordinateOrder,
                        out var currentPos)) {
                    _mapViewModel.CurrentPosition = currentPos;
                }
                else {
                    _mapViewModel.CurrentPosition = null;
                }

                var targetInput = TargetCoordinates ?? string.Empty;
                var coordinatesToParse = targetInput;
                if (SelectedLocation != null && targetInput == SelectedLocation.DisplayName) {
                    coordinatesToParse = SelectedLocation.Coordinates;
                }

                if (Scrubber.TryParse(coordinatesToParse, Settings.SelectedProfile.CoordinateOrder,
                        out var targetPos)) {
                    _mapViewModel.TargetPosition = targetPos;
                }
                else {
                    _mapViewModel.TargetPosition = null;
                }

                UpdateMapLocations();
                _mapWindow = new MapWindow(_mapViewModel);
                _mapWindow.Closed += (s, e) => {
                    _mapViewModel?.Dispose();
                    _mapWindow = null;
                    _mapViewModel = null;
                };
                _mapViewModel.DestinationSelected += coords => {
                    string formatted;
                    if (Settings.SelectedProfile.CoordinateOrder == "y x") {
                        formatted = $"{coords.Y:F1}, {coords.X:F1}";
                    }
                    else if (Settings.SelectedProfile.CoordinateOrder == "y x z") {
                        formatted = $"{coords.Y:F1}, {coords.X:F1}, {coords.Z ?? 0:F1}";
                    }
                    else if (Settings.SelectedProfile.CoordinateOrder == "x y") {
                        formatted = $"{coords.X:F1}, {coords.Y:F1}";
                    }
                    else {
                        // Default x z y d
                        formatted = $"{coords.X:F1}, {coords.Z ?? 0:F1}, {coords.Y:F1}";
                    }

                    // Wrap in Dispatcher to prevent cross-thread UI exceptions
                    System.Windows.Application.Current.Dispatcher.Invoke(() => {
                        SelectedLocation = null;
                        TargetCoordinates = formatted;
                        ShowDirection();
                    });
                };
                _mapViewModel.PinRequested += coords => {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => {
                        AddLocation(coords);
                        UpdateMapLocations();
                    });
                };
                _mapWindow.Show();
            }
            else {
                // If the local reference is null or the window isn't actually in the collection,
                // clear everything out safely so a fresh click will work normally next time.
                if (_mapWindow == null ||
                    !System.Windows.Application.Current.Windows.OfType<MapWindow>().Contains(_mapWindow)) {
                    _mapWindow = null;
                    _mapViewModel = null;
                    return; // Exit cleanly instead of looping!
                }

                if (_mapWindow.WindowState == WindowState.Minimized) {
                    _mapWindow.WindowState = WindowState.Normal;
                }

                if (!Settings.KeyboardClickThrough) {
                    _mapWindow.Activate();
                }

                _mapWindow.Show();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error opening or activating MapWindow.");
        }
    }
    
    private async void OpenThreeDMap() {
        try {
            var code = MMONavigator.Properties.Settings.Default.ActivationCode;
            if (!string.IsNullOrWhiteSpace(code)) {
                if (ValidateAndApplyOverrideCode(code)) {
                    IsPremiumOverrideValid = true;
                }
            }

            if (IsPremiumOverrideValid || await CheckStoreLicenseAsync()) {
                if (_threeDMapWindow == null ||
                    !System.Windows.Application.Current.Windows.OfType<ThreeDMapWindow>().Any()) {
                    _threeDMapViewModel = new ThreeDMapViewModel(Settings.SelectedProfile.MapSettings, Settings) {
                        CoordinateSystem = Settings.SelectedProfile.CoordinateSystem,
                        CurrentCoordinatesLabel = CurrentCoordinates
                    };
                    if (Scrubber.TryParse(CurrentCoordinates, Settings.SelectedProfile.CoordinateOrder,
                            out var currentPos)) {
                        _threeDMapViewModel.CurrentPosition = currentPos;
                    }
                    else {
                        _threeDMapViewModel.CurrentPosition = null;
                    }

                    var targetInput = TargetCoordinates ?? string.Empty;
                    var coordinatesToParse = targetInput;
                    if (SelectedLocation != null && targetInput == SelectedLocation.DisplayName) {
                        coordinatesToParse = SelectedLocation.Coordinates;
                    }

                    if (Scrubber.TryParse(coordinatesToParse, Settings.SelectedProfile.CoordinateOrder,
                            out var targetPos)) {
                        _threeDMapViewModel.TargetPosition = targetPos;
                    }
                    else {
                        _threeDMapViewModel.TargetPosition = null;
                    }

                    UpdateThreeDMapLocations();
                    _threeDMapWindow = new ThreeDMapWindow(_threeDMapViewModel);
                    _threeDMapWindow.Closed += (s, e) => {
                        _threeDMapWindow = null;
                        _threeDMapViewModel = null;
                    };
                    _threeDMapViewModel.DestinationSelected += coords => {
                        string formatted;
                        if (Settings.SelectedProfile.CoordinateOrder == "y x") {
                            formatted = $"{coords.Y:F1}, {coords.X:F1}";
                        }
                        else if (Settings.SelectedProfile.CoordinateOrder == "y x z") {
                            formatted = $"{coords.Y:F1}, {coords.X:F1}, {coords.Z ?? 0:F1}";
                        }
                        else if (Settings.SelectedProfile.CoordinateOrder == "x y") {
                            formatted = $"{coords.X:F1}, {coords.Y:F1}";
                        }
                        else {
                            // Default x z y d
                            formatted = $"{coords.X:F1}, {coords.Z ?? 0:F1}, {coords.Y:F1}";
                        }

                        // Wrap in Dispatcher to prevent cross-thread UI exceptions
                        System.Windows.Application.Current.Dispatcher.Invoke(() => {
                            SelectedLocation = null;
                            TargetCoordinates = formatted;
                            ShowDirection();
                        });
                    };
                    _threeDMapViewModel.PinRequested += coords => {
                        System.Windows.Application.Current.Dispatcher.Invoke(() => {
                            AddLocation(coords);
                            UpdateThreeDMapLocations();
                        });
                    };
                    _threeDMapWindow.Show();
                }
                else {
                    // If the local reference is null or the window isn't actually in the collection,
                    // clear everything out safely so a fresh click will work normally next time.
                    if (_threeDMapWindow == null || !System.Windows.Application.Current.Windows
                            .OfType<ThreeDMapWindow>()
                            .Contains(_threeDMapWindow)) {
                        _threeDMapWindow = null;
                        _threeDMapViewModel = null;
                        return; // Exit cleanly instead of looping!
                    }

                    if (_threeDMapWindow.WindowState == WindowState.Minimized) {
                        _threeDMapWindow.WindowState = WindowState.Normal;
                    }

                    if (!Settings.KeyboardClickThrough) {
                        _threeDMapWindow.Activate();
                    }

                    _threeDMapWindow.Show();
                }
            }
            else
            {
                // Prompt user to buy or enter an unlock code
                ShowActivationPrompt();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error opening or activating ThreeDMapWindow.");
        }
    }

    private void ShowActivationPrompt()
    {
        var mainWindow = System.Windows.Application.Current.MainWindow;
    
       
        
        // Safely configure a helper owner window if transparency/window style causes issues
        if (mainWindow != null)
        {
            
            Window? helperWindow = null;
            
            ConfigureDialogToHaveAValidOwner(mainWindow, out helperWindow);
            
            var prompt = new ActivationPromptWindow(this);
            prompt.WindowStyle = WindowStyle.None;
            prompt.Owner = helperWindow;
            prompt.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            
            try
            {
                prompt.ShowDialog();
                
                if (prompt.IsUnlocked)
                {
                    // Immediately open the 3D Map window since they just unlocked it!
                    OpenThreeDMap();
                }
            }
            finally
            {
                helperWindow.Close();
            }
        }
    }
    
    private void ConfigureDialogToHaveAValidOwner(Window owner, out Window helperWindow) {
        if (owner == null) {
            throw new ArgumentNullException(nameof(owner));
        }

        try {
            helperWindow = new Window {
                Width = 0,
                Height = 0,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                Opacity = 0,
                Topmost = owner.Topmost,
                Left = owner.Left,
                Top = owner.Top
            };
            helperWindow.Show();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in ConfigureDialogToHaveAValidOwner.");
            throw;
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

    public void InitializeWindow(IntPtr windowHandle) {
        _lastWindowHandle = windowHandle;
    }
}