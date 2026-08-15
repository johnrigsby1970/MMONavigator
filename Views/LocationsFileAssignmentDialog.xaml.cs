using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using MMONavigator.Controls;
using MMONavigator.Models;
using MMONavigator.Services;

namespace MMONavigator.Views;

public partial class LocationsFileAssignmentDialog : ChildWindow {
    public LocationsFileAssignmentDialog(GameProfile profile) {
        InitializeComponent();
        DataContext = this;

        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
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

    public GameProfile? Profile { get; set; }

    private bool _isSelected;

    public bool IsSelected {
        get => _isSelected;
        set {
            if (_isSelected != value) {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
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
            }
        }
    }

    private void OpenFileButton_Click(object sender, RoutedEventArgs e) {
        try {
            IsDialogActive = true;
            Window? helperWindow = null;

            try {
                ConfigureDialogToHaveAValidOwner(this, out helperWindow);

                var dialog = new Microsoft.Win32.OpenFileDialog {
                    DefaultExt = ".json",
                    Filter = "Locations (*.json)|*.json|All files (*.*)|*.*",
                    CheckFileExists = true,
                    CheckPathExists = true
                };

                try {
                    string appFolder = Helpers.NativeMethods.AppFolder();
                    if (!string.IsNullOrWhiteSpace(appFolder) && Directory.Exists(appFolder)) {
                        dialog.InitialDirectory = appFolder;
                    }
                }
                catch (Exception ex) {
                    Log.Debug(ex, "Could not set initial directory for Locations open file dialog.");
                }

                try {
                    bool? result;

                    if (helperWindow != null) {
                        var helper = new WindowInteropHelper(helperWindow);
                        result = helper.Handle != IntPtr.Zero
                            ? dialog.ShowDialog(helperWindow)
                            : dialog.ShowDialog();
                    }
                    else {
                        result = dialog.ShowDialog();
                    }

                    if (result == true) {
                        string selectedFilename = dialog.FileName;

                        if (!string.IsNullOrWhiteSpace(selectedFilename) && File.Exists(selectedFilename)) {
                            LocationsPath = selectedFilename;
                        }
                        else {
                            string? targetPath = null;

                            if (Profile != null && string.IsNullOrEmpty(Profile.LastLocationsFile)) {
                                string profileName = Profile.Name ?? "Default";
                                if (!string.Equals(profileName, "Default", StringComparison.OrdinalIgnoreCase)) {
                                    string safeName = MakeValidFileName(profileName);
                                    targetPath = Path.Combine(Helpers.NativeMethods.AppFolder(),
                                        $"{safeName}_locations.json");
                                }
                                else {
                                    targetPath = Path.Combine(Helpers.NativeMethods.AppFolder(), "locations.json");
                                }
                            }
                            else if (Profile != null) {
                                targetPath = Profile.LastLocationsFile;
                            }

                            if (!string.IsNullOrWhiteSpace(targetPath) && File.Exists(targetPath)) {
                                LocationsPath = targetPath;
                            }
                            else {
                                System.Windows.MessageBox.Show(
                                    "The selected locations file could not be found or accessed.",
                                    "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }
                        }

                        try {
                            LoadLocations(LocationsPath);
                        }
                        catch (Exception ex) {
                            Log.Error(ex, "Failed to load selected locations file '{FilePath}'.", LocationsPath);
                            System.Windows.MessageBox.Show($"Unable to load locations from file:\n{ex.Message}",
                                "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
                catch (InvalidOperationException ex) {
                    Log.Error(ex, "Threading or state issue in OpenFileButton_Click dialog execution.");
                    System.Windows.MessageBox.Show(
                        $"Unable to open file dialog: Threading or state issue detected.\n\nDetails: {ex.Message}",
                        "Dialog Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (Exception ex) {
                    Log.Error(ex, "Unexpected error selecting locations file.");
                    System.Windows.MessageBox.Show(
                        $"An unexpected error occurred while selecting the locations file:\n{ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally {
                helperWindow?.Close();
                IsDialogActive = false;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling OpenFileButton_Click.");
        }
    }

    private void SaveAsButton_Click(object sender, RoutedEventArgs e) {
        try {
            if (string.IsNullOrWhiteSpace(LocationsPath)) return;

            IsDialogActive = true;
            Window? helperWindow = null;

            try {
                ConfigureDialogToHaveAValidOwner(this, out helperWindow);

                if (string.IsNullOrWhiteSpace(LocationsPath) || !File.Exists(LocationsPath)) {
                    System.Windows.MessageBox.Show("The source locations file could not be found to export.",
                        "Export Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string defaultFileName = "locations.json";
                try {
                    string extractedName = Path.GetFileName(LocationsPath);
                    if (!string.IsNullOrWhiteSpace(extractedName)) {
                        defaultFileName = extractedName;
                    }
                }
                catch (ArgumentException) {
                    // Safe fallback
                }

                var dialog = new Microsoft.Win32.SaveFileDialog {
                    Title = "Export Selected File",
                    FileName = defaultFileName,
                    DefaultExt = ".json",
                    Filter = "Locations (*.json)|*.json|All files (*.*)|*.*",
                    OverwritePrompt = true
                };

                try {
                    string safeInitialDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    if (Directory.Exists(safeInitialDir)) {
                        dialog.InitialDirectory = safeInitialDir;
                    }
                }
                catch (Exception ex) {
                    Log.Debug(ex, "Could not set initial directory for SaveAs dialog.");
                }

                bool? result;
                try {
                    result = helperWindow != null
                        ? dialog.ShowDialog(helperWindow)
                        : dialog.ShowDialog();
                }
                catch (Exception ex) {
                    Log.Error(ex, "Error opening save file dialog.");
                    System.Windows.MessageBox.Show($"Unable to display the save dialog:\n{ex.Message}",
                        "Dialog Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (result == true) {
                    string targetFilename = dialog.FileName;

                    if (string.IsNullOrWhiteSpace(targetFilename)) {
                        return;
                    }

                    try {
                        File.Copy(LocationsPath, targetFilename, overwrite: true);

                        System.Windows.MessageBox.Show("File exported successfully.",
                            "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (UnauthorizedAccessException ex) {
                        Log.Error(ex, "Access denied exporting locations file to '{TargetPath}'.", targetFilename);
                        System.Windows.MessageBox.Show(
                            "Access denied. You do not have permission to save to that location.",
                            "Permission Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    catch (IOException ex) {
                        Log.Error(ex, "IO Error exporting locations file to '{TargetPath}'.", targetFilename);
                        System.Windows.MessageBox.Show(
                            $"Could not save the file. It may be in use by another program.\n\nDetails: {ex.Message}",
                            "File Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    catch (Exception ex) {
                        Log.Error(ex, "Unexpected error saving exported file to '{TargetPath}'.", targetFilename);
                        System.Windows.MessageBox.Show(
                            $"An unexpected error occurred while saving the file:\n{ex.Message}",
                            "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            finally {
                helperWindow?.Close();
                IsDialogActive = false;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling SaveAsButton_Click.");
        }
    }

    private void SetDefaultButton_Click(object sender, RoutedEventArgs e) {
        try {
            if (Profile == null) {
                Log.Warning("Attempted to set default locations file, but Profile was null.");
                System.Windows.MessageBox.Show("No active profile loaded.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!string.Equals(Profile.Name, "Default", StringComparison.OrdinalIgnoreCase)) {
                LocationsPath = Path.Combine(Helpers.NativeMethods.AppFolder(),
                    MakeValidFileName(Profile.Name) + "_locations.json");
            }
            else {
                LocationsPath = Path.Combine(Helpers.NativeMethods.AppFolder(), "locations.json");
            }

            var message = $"Use the locations stored in '{LocationsPath}'?\r\n\r\nDo you want to assign this file?";
            const string caption = "Confirm File Assignment";

            var result = System.Windows.MessageBox.Show(message, caption, MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes) {
                LoadLocations(LocationsPath);
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling SetDefaultButton_Click.");
        }
    }

    private static string MakeValidFileName(string name) {
        if (string.IsNullOrWhiteSpace(name)) return "profile";
        var invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
        var invalidRegStr = string.Format(@"([{0}]* windfall +$)|([{0}]+)", invalidChars);
        return Regex.Replace(name, invalidRegStr, "_");
    }

    public void LoadLocations(string? path) {
        try {
            if (string.IsNullOrEmpty(path)) {
                if (Profile == null) {
                    Log.Warning("Cannot load default locations because Profile is null.");
                    return;
                }

                if (!string.Equals(Profile.Name, "Default", StringComparison.OrdinalIgnoreCase)) {
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
                    var existingHeaderGroup = Locations.FirstOrDefault(l => l.Header == item.Header);
                    if (existingHeaderGroup != null) {
                        existingHeaderGroup.Items ??= new List<LocationItem>();
                        existingHeaderGroup.Items.Add(item);
                    }
                    else {
                        Locations.Add(new LocationItem {
                            Header = item.Header,
                            Name = item.Header,
                            Items = new List<LocationItem> { item }
                        });
                    }
                }
                else {
                    Locations.Add(item);
                }
            }

            OnPropertyChanged(nameof(LocationsPath));
            OnPropertyChanged(nameof(Locations));
        }
        catch (JsonException ex) {
            Log.Error(ex, "JSON Deserialization error loading locations file from '{Path}'.", path);
            System.Windows.MessageBox.Show(
                $"The selected file is not a valid JSON locations file:\n{ex.Message}",
                "Invalid File Format", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error loading locations from '{Path}'.", path);
            System.Windows.MessageBox.Show(
                $"Error loading locations: {ex.Message}. Make sure you are picking a file that contains locations originally created by this program.",
                "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) {
        try {
            IsConfirmed = true;
            ManualDialogResult = true;
                
            // Inherited from ChildWindow
            SafeCloseDialog();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing OkButton_Click.");
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) {
        try {
            //See notes.txt
            IsConfirmed = false;
            ManualDialogResult = false;
                
            // Inherited from ChildWindow
            SafeCloseDialog();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing CancelButton_Click.");
        }
    }
}