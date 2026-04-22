using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using MMONavigator.Controls;
using MMONavigator.Models;
using MMONavigator.Services;

namespace MMONavigator.Views;

public partial class LocationsFileAssignmentDialog : ChildWindow, INotifyPropertyChanged {
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
        IsDialogActive = true;
        Window helperWindow = null;

        try {
            ConfigureDialogToHaveAValidOwner(this, out helperWindow);

            // Configure open file dialog box
            var dialog = new Microsoft.Win32.OpenFileDialog {
                //dialog.FileName = "locations.json"; // Default file name
                DefaultDirectory = Helpers.NativeMethods.AppFolder(),
                DefaultExt = ".json", // Default file extension
                Filter = "Locations (*.json)|*.json;|All files (*.*)|*.*" // Filter files by extension
            };
            
            // Use the native interop handle to ensure the dialog 
            // feels 'attached' to the helper
            var helper = new WindowInteropHelper(helperWindow);

            // Show the open file dialog box
            var result = dialog.ShowDialog();

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
                            LocationsPath = Path.Combine(Helpers.NativeMethods.AppFolder(),
                                MakeValidFileName(Profile.Name) + "_locations.json");
                        }
                        else {
                            LocationsPath = Path.Combine(Helpers.NativeMethods.AppFolder(), "locations.json");
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
        finally {
            // ALWAYS close the helper to prevent memory leaks
            helperWindow?.Close();
            IsDialogActive = false;
        }
    }

    private void SaveAsButton_Click(object sender, RoutedEventArgs e) {
        if (string.IsNullOrWhiteSpace(LocationsPath)) return;

        IsDialogActive = true;
        Window helperWindow = null;
        
        try {
            ConfigureDialogToHaveAValidOwner(this, out helperWindow);
            
            // Configure open file dialog box
            var dialog = new Microsoft.Win32.SaveFileDialog {
                Title = "Download Selected File",
                FileName = Path.GetFileName(LocationsPath), // Default file name
                DefaultDirectory = AppDomain.CurrentDomain.BaseDirectory,
                DefaultExt = ".json", // Default file extension
                Filter = "Locations (*.json)|*.json;|All files (*.*)|*.*" // Filter files by extension
            };

            // Use the native interop handle to ensure the dialog 
            // feels 'attached' to the helper
            var helper = new WindowInteropHelper(helperWindow);
            
            // Show the open file dialog box
            var result = dialog.ShowDialog();

            // Process open file dialog box results
            if (result == true) {
                // Open document - get the name of the selected file
                var filename = dialog.FileName;

                if (File.Exists(LocationsPath)) {
                    File.Copy(LocationsPath, filename, true);
                }
            }
        }
        finally {
            // ALWAYS close the helper to prevent memory leaks
            helperWindow?.Close();
            IsDialogActive = false;
        }
    }

    private void SetDefaultButton_Click(object sender, RoutedEventArgs e) {
        if (Profile.Name != "Default") {
            LocationsPath = Path.Combine(Helpers.NativeMethods.AppFolder(),
                MakeValidFileName(Profile.Name) + "_locations.json");
        }
        else {
            LocationsPath = Path.Combine(Helpers.NativeMethods.AppFolder(), "locations.json");
        }

        var message = $"Use the locations stored in '{LocationsPath}'.\r\n\r\nDo you want to use the locations stored in this file?";
        const string caption = "Confirm File Assignment";
        const MessageBoxButton buttons = MessageBoxButton.YesNo;
        const MessageBoxImage icon = MessageBoxImage.Question;

        var result = System.Windows.MessageBox.Show(message, caption, buttons, icon);

        // Handle the result
        if (result == MessageBoxResult.Yes) {
            LoadLocations(LocationsPath);
        }
    }

    private static string MakeValidFileName(string name) {
        var invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
        var invalidRegStr = string.Format(@"([{0}]* windfall +$)|([{0}]+)", invalidChars);
        return Regex.Replace(name, invalidRegStr, "_");
    }

    public void LoadLocations(string path) {
        if (string.IsNullOrEmpty(path)) {
            if (Profile.Name != "Default") {
                path = Path.Combine(Helpers.NativeMethods.AppFolder(),
                    MakeValidFileName(Profile.Name) + "_locations.json");
            }
            else {
                path = Path.Combine(Helpers.NativeMethods.AppFolder(), "locations.json");
            }
        }

        LocationsPath = path;

        if (!File.Exists(path)) return;
        
        var json = File.ReadAllText(path);
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
        //See notes.txt
        IsConfirmed = true;
        ManualDialogResult = true;
        Hide(); 
        
        // Close the window after a tiny delay so the UI loop finishes 
        // processing the 'Hide' message before the OS-level 'Close' message.
        Dispatcher.BeginInvoke(new Action(() => {
            Close();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    public bool IsConfirmed { get; private set; }

    private void CancelButton_Click(object sender, RoutedEventArgs e) {
        //See notes.txt
        IsConfirmed = false;
        ManualDialogResult = false;
        Hide(); 

        // Close the window after a tiny delay so the UI loop finishes 
        // processing the 'Hide' message before the OS-level 'Close' message.
        Dispatcher.BeginInvoke(new Action(() => {
            Close();
        }), System.Windows.Threading.DispatcherPriority.Background);
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