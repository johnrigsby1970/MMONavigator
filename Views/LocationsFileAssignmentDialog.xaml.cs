using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using MMONavigator.Models;
using MMONavigator.Services;

namespace MMONavigator.Views;

public partial class LocationsFileAssignmentDialog : Window, INotifyPropertyChanged {
    public LocationsFileAssignmentDialog(GameProfile profile) {
        InitializeComponent();
        DataContext = this;
        
        Profile = profile;
        LocationsPath = profile.LastLocationsFile;
        LoadLocations(profile.LastLocationsFile);
    }

    private string? _locationsPath;

    public string? LocationsPath {
        get => _locationsPath;
        set {
            _locationsPath = value;
            OnPropertyChanged(nameof(LocationsPath));
        }
    }

    private ObservableCollection<LocationItem> _locations = new();

    public ObservableCollection<LocationItem> Locations {
        get => _locations;
        set {
            _locations = value;
            OnPropertyChanged(nameof(Locations));
        }
    }

    public GameProfile Profile { get; set; }

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
    
    private void OpenFileButton_Click(object sender, RoutedEventArgs e) {
        // Configure open file dialog box
        var dialog = new Microsoft.Win32.OpenFileDialog {
            //dialog.FileName = "locations.json"; // Default file name
            DefaultDirectory = AppDomain.CurrentDomain.BaseDirectory,
            DefaultExt = ".json", // Default file extension
            Filter = "Locations (*.json)|*.json;|All files (*.*)|*.*" // Filter files by extension
        };

        // Show the open file dialog box
        bool? result = dialog.ShowDialog();

        // Process open file dialog box results
        if (result == true) {
            // Open document - get the name of the selected file
            string filename = dialog.FileName;

            if (File.Exists(filename)) {
                LocationsPath = filename;
            }
            else {
                if (string.IsNullOrEmpty(Profile.LastLocationsFile)) {
                    if (Profile.Name != "Default") {
                        LocationsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                            MakeValidFileName(Profile.Name) + "_locations.json");
                    }
                    else {
                        LocationsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locations.json");
                    }
                }
                else {
                    LocationsPath = Profile.LastLocationsFile;
                }
            }

            LoadLocations(LocationsPath);
            // You can use the filename to read the file or display it in a TextBox
            // e.g., FileNameTextBox.Text = filename;
        }
    }

    private void SaveAsButton_Click(object sender, RoutedEventArgs e) {
        if(string.IsNullOrWhiteSpace(LocationsPath)) return;
        // Configure open file dialog box
        var dialog = new Microsoft.Win32.SaveFileDialog {
            Title = "Download Selected File",
            FileName = Path.GetFileName(LocationsPath),// Default file name
            DefaultDirectory = AppDomain.CurrentDomain.BaseDirectory,
            DefaultExt = ".json", // Default file extension
            Filter = "Locations (*.json)|*.json;|All files (*.*)|*.*" // Filter files by extension
        };

        // Show the open file dialog box
        bool? result = dialog.ShowDialog();

        // Process open file dialog box results
        if (result == true) {
            // Open document - get the name of the selected file
            string filename = dialog.FileName;

            if (File.Exists(LocationsPath)) {
                File.Copy(LocationsPath, filename, true);
            }
        }
    }
    
    private void SetDefaultButton_Click(object sender, RoutedEventArgs e) {
        if (Profile.Name != "Default") {
            LocationsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                MakeValidFileName(Profile.Name) + "_locations.json");
        }
        else {
            LocationsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locations.json");
        }

        LoadLocations(LocationsPath);
    }

    private static string MakeValidFileName(string name) {
        string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
        string invalidRegStr = string.Format(@"([{0}]* windfall +$)|([{0}]+)", invalidChars);
        return Regex.Replace(name, invalidRegStr, "_");
    }

    public void LoadLocations(string path) {
        if (string.IsNullOrEmpty(path)) {
            if (Profile.Name != "Default") {
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    MakeValidFileName(Profile.Name) + "_locations.json");
            }
            else {
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locations.json");
            }
        }

        LocationsPath = path;
        
        if (!File.Exists(path)) return;
        
        string json = File.ReadAllText(path);
        var list = JsonSerializer.Deserialize<List<LocationItem>>(json) ?? new List<LocationItem>();

        Locations.Clear();
        foreach (var item in list) {
            item.ScrubbedCoordinates = Scrubber.ScrubEntry(item.Coordinates);
            if (!string.IsNullOrWhiteSpace(item.Header)) {
                if (Locations.Any(l => l.Header == item.Header)) {
                    if (Locations.Single(l => l.Header == item.Header).Items == null) {
                        Locations.Single(l => l.Header == item.Header).Items = new List<LocationItem>();
                    }

                    Locations.Single(l => l.Header == item.Header).Items!.Add(item);
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
        OnPropertyChanged(nameof(LocationsPath));
        OnPropertyChanged(nameof(Locations));
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) {
        DialogResult = false;
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