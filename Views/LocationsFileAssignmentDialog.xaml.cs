using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using MMONavigator.Controls;
using MMONavigator.Models;
using MMONavigator.Services;
using MessageBox = System.Windows.Forms.MessageBox;

namespace MMONavigator.Views;

public partial class LocationsFileAssignmentDialog : ChildWindow {
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

    public GameProfile? Profile { get; set; }

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
        try {
            IsDialogActive = true;
            Window? helperWindow = null;

            try {
                ConfigureDialogToHaveAValidOwner(this, out helperWindow);

                // Configure open file dialog box
                // 1. Configure dialog with clean filter string
                var dialog = new Microsoft.Win32.OpenFileDialog {
                    DefaultExt = ".json",
                    Filter = "Locations (*.json)|*.json|All files (*.*)|*.*", // Fixed trailing semicolon
                    CheckFileExists = true,
                    CheckPathExists = true
                };

                // 2. Safely set initial directory
                try {
                    string appFolder = Helpers.NativeMethods.AppFolder();
                    if (!string.IsNullOrWhiteSpace(appFolder) && Directory.Exists(appFolder)) {
                        dialog.InitialDirectory = appFolder;
                    }
                }
                catch {
                    // Fail silently — dialog defaults to Documents or last used folder
                }

                try {
                    bool? result = null;

                    // 3. Safely handle window attachment
                    if (helperWindow != null) {
                        var helper = new WindowInteropHelper(helperWindow);
                        if (helper.Handle != IntPtr.Zero) {
                            result = dialog.ShowDialog();
                        }
                        else {
                            result = dialog.ShowDialog();
                        }
                    }
                    else {
                        result = dialog.ShowDialog();
                    }

                    // 4. Process user selection
                    if (result == true) {
                        string selectedFilename = dialog.FileName;

                        if (!string.IsNullOrWhiteSpace(selectedFilename) && File.Exists(selectedFilename)) {
                            LocationsPath = selectedFilename;
                        }
                        else {
                            // Fallback path calculation
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

                            // Verify the fallback path actually exists before setting it
                            if (!string.IsNullOrWhiteSpace(targetPath) && File.Exists(targetPath)) {
                                LocationsPath = targetPath;
                            }
                            else {
                                System.Windows.MessageBox.Show(
                                    "The selected locations file could not be found or accessed.",
                                    "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return; // Stop execution safely without attempting to load an invalid file
                            }
                        }

                        // 5. Execute file loading inside a guarded block
                        try {
                            LoadLocations(LocationsPath);
                        }
                        catch (Exception ex) {
                            System.Windows.MessageBox.Show($"Unable to load locations from file:\n{ex.Message}",
                                "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
                catch (InvalidOperationException ex) {
                    System.Windows.MessageBox.Show(
                        $"Unable to open file dialog: Threading or state issue detected.\n\nDetails: {ex.Message}",
                        "Dialog Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (Exception ex) {
                    System.Windows.MessageBox.Show(
                        $"An unexpected error occurred while selecting the locations file:\n{ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally {
                // ALWAYS close the helper to prevent memory leaks
                helperWindow?.Close();
                IsDialogActive = false;
            }
        }
        catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine(
                $"[DEBUG_LOG]OpenFileButton_Click error: {ex.Message}");
        }
    }

    private void SaveAsButton_Click(object sender, RoutedEventArgs e) {
        try {
            if (string.IsNullOrWhiteSpace(LocationsPath)) return;

            IsDialogActive = true;
            Window? helperWindow = null;

            try {
                ConfigureDialogToHaveAValidOwner(this, out helperWindow);

                // 1. Ensure source file exists before bothering the user
                if (string.IsNullOrWhiteSpace(LocationsPath) || !File.Exists(LocationsPath)) {
                    System.Windows.MessageBox.Show("The source locations file could not be found to export.",
                        "Export Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 2. Safe default file name derivation
                string defaultFileName = "locations.json";
                try {
                    string extractedName = Path.GetFileName(LocationsPath);
                    if (!string.IsNullOrWhiteSpace(extractedName)) {
                        defaultFileName = extractedName;
                    }
                }
                catch (ArgumentException) {
                    // Safe fallback if LocationsPath contains invalid path characters
                }

                var dialog = new Microsoft.Win32.SaveFileDialog {
                    Title = "Download Selected File",
                    FileName = defaultFileName,
                    DefaultExt = ".json",
                    Filter = "Locations (*.json)|*.json|All files (*.*)|*.*", // Fixed trailing semicolon
                    OverwritePrompt = true
                };

                // 3. Safely set initial directory to AppData / Documents instead of BaseDirectory
                try {
                    string safeInitialDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    if (Directory.Exists(safeInitialDir)) {
                        dialog.InitialDirectory = safeInitialDir;
                    }
                }
                catch {
                    // Fail gracefully — dialog falls back to Windows default
                }

                // 4. Safely show dialog attached to owner
                bool? result;
                try {
                    result = helperWindow != null
                        ? dialog.ShowDialog(helperWindow)
                        : dialog.ShowDialog();
                }
                catch (Exception ex) {
                    System.Windows.MessageBox.Show($"Unable to display the save dialog:\n{ex.Message}",
                        "Dialog Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 5. Process save result inside defensive I/O block
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
                    catch (UnauthorizedAccessException) {
                        System.Windows.MessageBox.Show(
                            "Access denied. You do not have permission to save to that location.",
                            "Permission Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    catch (IOException ex) {
                        System.Windows.MessageBox.Show(
                            $"Could not save the file. It may be in use by another program.\n\nDetails: {ex.Message}",
                            "File Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    catch (Exception ex) {
                        System.Windows.MessageBox.Show(
                            $"An unexpected error occurred while saving the file:\n{ex.Message}",
                            "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            finally {
                // ALWAYS close the helper to prevent memory leaks
                helperWindow?.Close();
                IsDialogActive = false;
            }
        }
        catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine(
                $"[DEBUG_LOG]SaveAsButton_Click error: {ex.Message}");
        }
    }

    private void SetDefaultButton_Click(object sender, RoutedEventArgs e) {
        try {
            if (Profile == null) {
                throw new Exception("Invalid Profile");
            }

            if (Profile.Name != "Default") {
                LocationsPath = Path.Combine(Helpers.NativeMethods.AppFolder(),
                    MakeValidFileName(Profile.Name) + "_locations.json");
            }
            else {
                LocationsPath = Path.Combine(Helpers.NativeMethods.AppFolder(), "locations.json");
            }

            var message =
                $"Use the locations stored in '{LocationsPath}'.\r\n\r\nDo you want to use the locations stored in this file?";
            const string caption = "Confirm File Assignment";
            const MessageBoxButton buttons = MessageBoxButton.YesNo;
            const MessageBoxImage icon = MessageBoxImage.Question;

            var result = System.Windows.MessageBox.Show(message, caption, buttons, icon);

            // Handle the result
            if (result == MessageBoxResult.Yes) {
                LoadLocations(LocationsPath);
            }
        }
        catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine(
                $"[DEBUG_LOG]SetDefaultButton_Click error: {ex.Message}");
        }
    }

    private static string MakeValidFileName(string name) {
        var invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
        var invalidRegStr = string.Format(@"([{0}]* windfall +$)|([{0}]+)", invalidChars);
        return Regex.Replace(name, invalidRegStr, "_");
    }

    public void LoadLocations(string path) {
        if (string.IsNullOrEmpty(path)) {
            if (Profile == null) {
                throw new Exception("Invalid Profile");
            }

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

        try {
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<LocationItem>>(json);
            if (list == null) {
                list = new List<LocationItem>();
            }

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
        catch (Exception ex) {
            MessageBox.Show(
                $"Error loading locations: {ex.Message}. Make sure you are picking a file that contains locations originally created by this program.");
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) {
        try {
            //See notes.txt
            IsConfirmed = true;
            ManualDialogResult = true;
            Hide();

            // Close the window after a tiny delay so the UI loop finishes 
            // processing the 'Hide' message before the OS-level 'Close' message.
            Dispatcher.BeginInvoke(new Action(() => { Close(); }),
                System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine(
                $"[DEBUG_LOG]OkButton_Click error: {ex.Message}");
        }
    }

    public bool IsConfirmed { get; private set; }

    private void CancelButton_Click(object sender, RoutedEventArgs e) {
        try {
            //See notes.txt
            IsConfirmed = false;
            ManualDialogResult = false;
            Hide();

            // Close the window after a tiny delay so the UI loop finishes 
            // processing the 'Hide' message before the OS-level 'Close' message.
            Dispatcher.BeginInvoke(new Action(() => { Close(); }),
                System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine(
                $"[DEBUG_LOG]CancelButton_Click error: {ex.Message}");
        }
    }
}