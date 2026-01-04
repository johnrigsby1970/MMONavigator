using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace MMONavigator;

//https://stackoverflow.com/questions/21461017/wpf-window-with-transparent-background-containing-opaque-controls
//https://stackoverflow.com/questions/55447212/how-do-i-make-a-transparent-wpf-window-with-the-default-title-bar-functionality
//https://corey255a1.wixsite.com/wundervision/single-post/simple-wpf-compass-control
//https://learn.microsoft.com/en-us/windows/apps/design/style/segoe-ui-symbol-font
//https://stackoverflow.com/questions/2842667/how-to-create-a-semi-transparent-window-in-wpf-that-allows-mouse-events-to-pass

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged {
    public class LocationItem {
        public string? Name { get; set; }
        public string? Coordinates { get; set; }

        public string DisplayName {
            get {
                if (string.IsNullOrEmpty(Name)) return Coordinates ?? "";
                return $"{Name} ({Coordinates})";
            }
        }
    }
    public ObservableCollection<LocationItem> Locations { get; set; } = new();

    private LocationItem? _selectedLocation;
    public LocationItem? SelectedLocation {
        get => _selectedLocation;
        set {
            if (SetField(ref _selectedLocation, value)) {
                if (value != null) {
                    TargetCoordinates = value.DisplayName;
                }
                OnPropertyChanged(nameof(IsItemInList));
                OnPropertyChanged(nameof(IsItemNotInList));
            }
        }
    }

    public bool IsItemInList => SelectedLocation != null || Locations.Any(l => {
        var scrubbedL = ScrubEntry(l.Coordinates);
        string scrubbedT;
        if (SelectedLocation != null && TargetCoordinates == SelectedLocation.DisplayName) {
            scrubbedT = ScrubEntry(SelectedLocation.Coordinates) ?? "";
        } else {
            scrubbedT = ScrubEntry(TargetCoordinates) ?? "";
        }
        return !string.IsNullOrEmpty(scrubbedL) && scrubbedL == scrubbedT;
    });
    public bool IsItemNotInList => !IsItemInList;

    private AppSettings _settings = new();
    public AppSettings Settings {
        get => _settings;
        set {
            if (_settings != null) _settings.PropertyChanged -= Settings_PropertyChanged;
            if (SetField(ref _settings, value)) {
                if (_settings != null) _settings.PropertyChanged += Settings_PropertyChanged;
            }
        }
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(AppSettings.CoordinateOrder)) {
            ShowDirection();
        }
    }

    private void LoadSettings() {
        try {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
            if (File.Exists(path)) {
                string json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null) {
                    Settings = settings;
                }
            }
        } catch { }
    }

    private void SaveSettings() {
        try {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
            string json = JsonSerializer.Serialize(Settings);
            File.WriteAllText(path, json);
        } catch { }
    }

    private void LoadLocations() {
        try {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locations.json");
            if (File.Exists(path)) {
                string json = File.ReadAllText(path);
                var list = JsonSerializer.Deserialize<List<LocationItem>>(json);
                if (list != null) {
                    Locations.Clear();
                    foreach (var item in list) Locations.Add(item);
                }
            }
        } catch { }
    }

    private void SaveLocations() {
        try {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locations.json");
            string json = JsonSerializer.Serialize(Locations.ToList());
            File.WriteAllText(path, json);
        } catch { }
    }

    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private readonly IntPtr _windowHandle;

    public event EventHandler? ClipboardUpdate;

    public MainWindow() {
        InitializeComponent();
        _settings.PropertyChanged += Settings_PropertyChanged;
        LoadSettings();
        LoadLocations();
        myGrid.DataContext = this;
        Topmost = true;
        _windowHandle = new WindowInteropHelper(this).EnsureHandle();
        HwndSource.FromHwnd(_windowHandle)?.AddHook(HwndHandler);
        
        Top = 0; //SystemParameters.PrimaryScreenHeight;
        Left = (SystemParameters.PrimaryScreenWidth / 2) - (Width / 2);
        leftbutton.Visibility = Visibility.Hidden;
        rightbutton.Visibility = Visibility.Hidden;
        _showSettings = true;
        ToggleSettings();
        Start();
    }
    
    // //https://stackoverflow.com/questions/34510416/making-a-wpf-window-click-through-but-not-its-controls
    // protected override void OnSourceInitialized(EventArgs e)
    // {
    //     base.OnSourceInitialized(e);
    //     var hwnd = new WindowInteropHelper(this).Handle;
    //     WindowHelper.SetWindowExTransparent(hwnd);
    // }
    
    private bool _showSettings;
    public bool ShowSettings {
        get => _showSettings;
        set {
            _showSettings = value;
            OnPropertyChanged();
        }
    }
    
    private string? _currentCoordinates = "";

    public string? CurrentCoordinates {
        get => _currentCoordinates;
        set {
            _currentCoordinates = value;
            ShowDirection();
            OnPropertyChanged();
        }
    }

    private string? _targetCoordinates = "";

    public string? TargetCoordinates {
        get => _targetCoordinates;
        set {
            if (SetField(ref _targetCoordinates, value)) {
                ShowDirection();
                // If the user typed/pasted something, try to find a matching location item
                var scrubbed = ScrubEntry(value);
                var matchingItem = Locations.FirstOrDefault(l => ScrubEntry(l.Coordinates) == scrubbed || value == l.DisplayName);
                if (matchingItem != null) {
                    if (_selectedLocation != matchingItem) {
                        _selectedLocation = matchingItem;
                        OnPropertyChanged(nameof(SelectedLocation));
                    }
                } else {
                    if (_selectedLocation != null) {
                        _selectedLocation = null;
                        OnPropertyChanged(nameof(SelectedLocation));
                    }
                }
                OnPropertyChanged(nameof(IsItemInList));
                OnPropertyChanged(nameof(IsItemNotInList));
            }
        }
    }

    private string _correctionDirection = "";

    public string CorrectionDirection {
        get => _correctionDirection;
        set {
            _correctionDirection = value;
            OnPropertyChanged();
        }
    }
    
    private string? _tX;

    public string? Tx {
        get => _tX;
        set {
            _tX = value;
            OnPropertyChanged();
        }
    }

    private string? _tY;

    public string? Ty {
        get => _tY;
        set {
            _tY = value;
            OnPropertyChanged();
        }
    }

    private string? _cX;

    public string? Cx {
        get => _cX;
        set {
            _cX = value;
            OnPropertyChanged();
        }
    }

    private string? _cY;

    public string? Cy {
        get => _cY;
        set {
            _cY = value;
            OnPropertyChanged();
        }
    }

    private double _currentHeading;

    public double CurrentHeading {
        get => _currentHeading;
        set {
            _currentHeading = value;
            OnPropertyChanged();
        }
    }
    
    private double _targetHeading;

    public double TargetHeading {
        get => _targetHeading;
        set {
            _targetHeading = value;
            OnPropertyChanged();
        }
    }
    
    private string? _goDirection;

    public string? GoDirection {
        get => _goDirection;
        set {
            _goDirection = value;
            OnPropertyChanged();
        }
    }

    public static readonly DependencyProperty ClipboardUpdateCommandProperty =
        DependencyProperty.Register("ClipboardUpdateCommand", typeof(ICommand), typeof(MainWindow), new FrameworkPropertyMetadata(null));

    public ICommand ClipboardUpdateCommand {
        get { return (ICommand)GetValue(ClipboardUpdateCommandProperty); }
        set { SetValue(ClipboardUpdateCommandProperty, value); }
    }

    protected virtual void OnClipboardUpdate() {
        try {
            var text = Clipboard.GetText();
            var value = ScrubEntry(text);
            if (string.IsNullOrWhiteSpace(value)) return;

            var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return;

            foreach (var part in parts) {
                if (!double.TryParse(part, out _)) {
                    return;
                }
            }

            CurrentCoordinates = value;
        }
        catch {
            GoDirection = string.Empty;
        }
    }

    private string? ScrubEntry(string? value) => Scrubber.ScrubEntry(value);
  
    private void CopyLocationToDesination_Click(object sender, RoutedEventArgs e) {
        TargetCoordinates = CurrentCoordinates;
    }
    
    protected virtual void ShowDirection() {
        try {
            //coordinates can be "x y" or "x z y" or "x z y direction"
            leftbutton.Visibility = Visibility.Hidden;
            rightbutton.Visibility = Visibility.Hidden;
            GoDirection = string.Empty;
            var s = ScrubEntry(CurrentCoordinates);//may have been user entered

            if (!string.IsNullOrWhiteSpace(TargetCoordinates)) {
                string scrubbedTarget;
                if (SelectedLocation != null && TargetCoordinates == SelectedLocation.DisplayName) {
                    scrubbedTarget = ScrubEntry(SelectedLocation.Coordinates) ?? "";
                } else {
                    scrubbedTarget = ScrubEntry(TargetCoordinates) ?? "";
                }
                
                var target = scrubbedTarget.Split(' ');
                
                var targetValid = true;
                foreach (var item in target) {
                    if (!decimal.TryParse(item, out decimal _)) {
                        targetValid = false;
                        //return;
                    }
                }

                if (!targetValid) return;
                if (target.Length <= 1) return;

                 if (!string.IsNullOrWhiteSpace(s)) {
                    var coordinates = s.Split(' ');
                    var currentValid = true;
                    foreach (var item in coordinates) {
                        if (!decimal.TryParse(item, out decimal _)) {
                            currentValid = false;
                            //continue;
                        }
                    }

                    if (!currentValid) return;
                    if (coordinates.Length < 2) return;

                    double x1, y1, x2, y2;
                    if (Settings.CoordinateOrder == "y x") {
                        x1 = double.Parse(coordinates[1]);
                        y1 = double.Parse(coordinates[0]);
                        x2 = double.Parse(target[1]);
                        y2 = double.Parse(target[0]);
                    } else {
                        // Default "x z y d"
                        x1 = double.Parse(coordinates[0]);
                        x2 = double.Parse(target[0]);
                        y1 = double.Parse(coordinates.Length == 2 ? coordinates[1] : coordinates[2]);
                        y2 = (target.Length > 2) ? double.Parse(target[2]) : double.Parse(target[1]);
                    }

                    Tx = $"x:{x2}";
                    Ty = $"y:{y2}";

                    Cx = $"x:{x1}";
                    Cy = $"y:{y1}";

                    var direction = GetDirection(x1, y1, x2, y2);

                    try {
                        //labelDirection.Foreground = Brushes.White;
                        labelDirection.Fill = Brushes.White;
                        if (coordinates.Length > 3) {
                            //4th number is facing in compass degrees
                            if (double.Parse(coordinates[3]) >= direction - 2 && double.Parse(coordinates[3]) <= direction + 2) {
                                labelDirection.Fill = Brushes.Green;
                            }
                            else if (double.Parse(coordinates[3]) >= direction - 4 && double.Parse(coordinates[3]) <= direction + 4) {
                                labelDirection.Fill = Brushes.YellowGreen;
                            }
                            else if (double.Parse(coordinates[3]) >= direction - 6 && double.Parse(coordinates[3]) <= direction + 6) {
                                labelDirection.Fill = Brushes.Yellow;
                            }
                        }
                    }
                    catch {
                        //ignore
                    }

                    double distance = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
                    if (distance <= 10) {
                        labelDirection.Fill = Brushes.Green;
                    }

                    if (target.Length > 3) {
                        TargetHeading = direction;
                    }
                    

                    if (coordinates.Length > 3) {
                        CurrentHeading = direction;
                        TargetHeading = double.Parse(coordinates[3]);
                        CorrectionDirection = DetermineDirection();
                        
                        if (CorrectionDirection == "Left") {
                            rightbutton.Visibility = Visibility.Visible;
                        }
                        if (CorrectionDirection == "Right") {
                            leftbutton.Visibility = Visibility.Visible;
                        }
                    }
                    
                    GoDirection = GetCompassDirection(direction) + $" {Convert.ToInt32(direction)}\u00b0 {Convert.ToInt32(distance)}";
                }
            }
        }
        catch {
            GoDirection = string.Empty;
        }
    }

    //https://stackoverflow.com/questions/60237767/get-left-or-right-position-from-heading-in-degrees
    private string DetermineDirection() {
        var diff = (TargetHeading - CurrentHeading + 360) % 360; // Calculate relative difference
        return diff switch {
            > 355 or < 5 => "",
            < 180 => "Right",
            _ => "Left"
        };
    }
    
    private static string GetCompassDirection(double angle) {
        // Normalize the angle to the range [0, 360)
        angle = (angle % 360 + 360) % 360;

        return angle switch {
            >= 0 and < 22.5 => "N",
            >= 22.5 and < 67.5 => "NE",
            >= 67.5 and < 112.5 => "E",
            >= 112.5 and < 157.5 => "SE",
            >= 157.5 and < 202.5 => "S",
            >= 202.5 and < 247.5 => "SW",
            >= 247.5 and < 292.5 => "W",
            >= 292.5 and < 337.5 => "NW",
            _ => "N"
        };
    }

    private static double GetDirection(double x1, double y1, double x2, double y2) {
        // Calculate the difference in x and y coordinates
        var dx = x2 - x1;
        var dy = y2 - y1;

        // Use Atan2 to calculate the angle in radians
        var angleRad = Math.Atan2(dy, dx);

        // Convert the angle from radians to degrees
        var angleDeg = angleRad * 180 / Math.PI;

        // Normalize the angle to be between 0 and 360 degrees
        while (angleDeg < 0) {
            angleDeg += 360;
        }

        while (angleDeg >= 360) {
            angleDeg -= 360;
        }

        return ReverseAngle(angleDeg);
    }

    private static double ReverseAngle(double angleInDegrees) {
        // Ensure the angle is within the 0-360 degree range
        var normalizedAngle = angleInDegrees % 360;

        // If the normalized angle is negative, add 360 to make it positive
        if (normalizedAngle < 0) {
            normalizedAngle += 360;
        }

        // Calculate the reversed angle (180 degrees difference)
        var reversed = (180 - normalizedAngle) % 360;
        reversed = reversed - 90;
        if (reversed < 0) {
            reversed += 360;
        }

        return reversed;
    }

    private void Start() {
        NativeMethods.AddClipboardFormatListener(_windowHandle);
    }

    private void Stop() {
        NativeMethods.RemoveClipboardFormatListener(_windowHandle);
    }

    private IntPtr HwndHandler(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled) {
        if (msg == WM_CLIPBOARDUPDATE) {
            // fire event
            ClipboardUpdate?.Invoke(this, new EventArgs());
            // execute command
            if (ClipboardUpdateCommand?.CanExecute(null) ?? false) {
                ClipboardUpdateCommand?.Execute(null);
            }

            // call virtual method
            OnClipboardUpdate();
        }

        handled = false;
        return IntPtr.Zero;
    }


    private static class NativeMethods {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    }

    #region Title Bar

    //https://stackoverflow.com/questions/55447212/how-do-i-make-a-transparent-wpf-window-with-the-default-title-bar-functionality
    
    private void HideShowSettings_Click(object sender, RoutedEventArgs e) {
        ToggleSettings();
    }

    private void ToggleSettings() {
        ShowSettings = !ShowSettings;
        if (ShowSettings) {
            destinationRow.Height = new GridLength(0);
            targetRow.Height = new GridLength(0);
            settingsRow.Height = new GridLength(0);
        }
        else {
            destinationRow.Height = new GridLength(30);
            targetRow.Height = new GridLength(30);
            settingsRow.Height = new GridLength(30);
        }
    }

    private void TitleBar_MouseEnter(object sender, MouseEventArgs e) {
        settingsbutton.Visibility = Visibility.Visible;
        minbutton.Visibility = Visibility.Visible;
        closebutton.Visibility = Visibility.Visible;
    }

    private void TitleBar_MouseLeave(object sender, MouseEventArgs e) {
        settingsbutton.Visibility = Visibility.Collapsed;
        minbutton.Visibility = Visibility.Collapsed;
        closebutton.Visibility = Visibility.Collapsed;
    }
    
    /// <summary>
    /// Min
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) {
        if (WindowState != WindowState.Minimized) {
            WindowState = WindowState.Minimized;
        }
    }
    
    /// <summary>
    /// Max
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) {
        if (WindowState != WindowState.Maximized) {
            WindowState = WindowState.Normal;
        }
    }

    /// <summary>
    /// Close
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void CloseButton_Click(object sender, RoutedEventArgs e) {
        SaveSettings();
        Stop();
        Close();
    }

    /// <summary>
    /// DragMove
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void AddLocation_Click(object sender, RoutedEventArgs e) {
        var scrubbedTarget = ScrubEntry(TargetCoordinates);
        if (string.IsNullOrWhiteSpace(scrubbedTarget)) return;
        
        string name = Microsoft.VisualBasic.Interaction.InputBox("Enter a name for this location:", "Add Location", "");
        
        var item = new LocationItem {
            Name = string.IsNullOrWhiteSpace(name) ? null : name,
            Coordinates = scrubbedTarget
        };
        Locations.Add(item);
        SelectedLocation = item;
        SaveLocations();
    }

    private void RemoveLocation_Click(object sender, RoutedEventArgs e) {
        var item = SelectedLocation;
        if (item == null) {
            var scrubbedTarget = ScrubEntry(TargetCoordinates);
            item = Locations.FirstOrDefault(l => ScrubEntry(l.Coordinates) == scrubbedTarget);
        }

        if (item != null) {
            var result = MessageBox.Show($"Are you sure you want to remove '{item.DisplayName}'?", "Remove Location", MessageBoxButton.YesNo);
            if (result == MessageBoxResult.Yes) {
                Locations.Remove(item);
                SelectedLocation = null;
                // Force update of TargetCoordinates to refresh IsItemInList/IsItemNotInList
                OnPropertyChanged(nameof(TargetCoordinates));
                OnPropertyChanged(nameof(IsItemInList));
                OnPropertyChanged(nameof(IsItemNotInList));
                SaveLocations();
            }
        }
    }

    private void Rectangle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        this.DragMove();
    }

    #endregion

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