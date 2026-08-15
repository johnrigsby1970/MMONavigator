using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using MMONavigator.Controls;
using MMONavigator.Helpers;
using MMONavigator.Models;

namespace MMONavigator.Views;

public partial class WatcherConfigurationDialog : ChildWindow {
    private readonly AppSettings _settings;
    private GameProfile? _currentProfile;
    private bool _isUpdatingUI;

    public WatchMode WatchMode => ClipboardRadio.IsChecked == true ? WatchMode.Clipboard : WatchMode.File;
    public string LogFilePath => FilePathTextBox.Text;
    public string LogFileRegex => RegexTextBox.Text;
    public string CoordinateOrder => OrderComboBox.SelectedItem?.ToString() ?? "x z y d";

    private CoordinateSystem _currentCoordinateSystem;

    public CoordinateSystem CurrentCoordinateSystem {
        get => _currentCoordinateSystem;
        set {
            if (_currentCoordinateSystem != value) {
                _currentCoordinateSystem = value;
                OnPropertyChanged();
            }
        }
    }

    public List<CoordinateItem> Items { get; } = Enum.GetValues(typeof(CoordinateSystem))
        .Cast<CoordinateSystem>()
        .Select(e => new CoordinateItem {
            Value = e,
            Label = Methods.GetDisplayName(e)
        })
        .ToList();

    public WatcherConfigurationDialog(AppSettings settings) {
        InitializeComponent();
        DataContext = this;

        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        try {
            SystemComboBox.ItemsSource = Items;
            ProfileComboBox.ItemsSource = _settings.Profiles;
            ProfileComboBox.DisplayMemberPath = "Name";

            var selectedProfile = _settings.Profiles.FirstOrDefault(p => p.Name == _settings.LastSelectedProfileName)
                                  ?? _settings.Profiles.FirstOrDefault();

            ProfileComboBox.SelectedItem = selectedProfile;
            OrderComboBox.ItemsSource = Constants.AvailableCoordinateOrders;

            ClipboardRadio.Checked -= WatchMode_Checked;
            FileRadio.Checked -= WatchMode_Checked;
            ClipboardRadio.Checked += WatchMode_Checked;
            FileRadio.Checked += WatchMode_Checked;

            ProfileComboBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler(ProfileComboBox_TextChanged));

            LoadProfile(selectedProfile);
            UpdateProfileButtons();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error initializing WatcherConfigurationDialog.");
        }
    }

    private bool _readMore;

    public bool ReadMore {
        get => _readMore;
        set => SetField(ref _readMore, value);
    }

    private void ReadMore_Click(object sender, RoutedEventArgs e) {
        try {
            ReadMore = !ReadMore;
            if (ReadMore) {
                ExtraContent.Visibility = Visibility.Visible;
                ReadMoreBtn.Content = "Read Less";
            }
            else {
                ExtraContent.Visibility = Visibility.Collapsed;
                ReadMoreBtn.Content = "Read More";
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error toggling Read More section.");
        }
    }

    private void ProfileComboBox_TextChanged(object sender, TextChangedEventArgs e) {
        UpdateProfileButtons();
    }

    private void UpdateProfileButtons() {
        try {
            var currentText = ProfileComboBox.Text.Trim();
            var isDefault = currentText.Equals("Default", StringComparison.OrdinalIgnoreCase);
            var isEmpty = string.IsNullOrWhiteSpace(currentText);
            var exists = _settings.Profiles.Any(p => p.Name.Trim().Equals(currentText, StringComparison.OrdinalIgnoreCase));

            if (isDefault || isEmpty) {
                AddProfileButton.Visibility = Visibility.Visible;
                DuplicateProfileButton.Visibility = isDefault && exists ? Visibility.Visible : Visibility.Collapsed;
                RemoveProfileButton.Visibility = isDefault && exists
                    ? Visibility.Collapsed
                    : (exists ? Visibility.Visible : Visibility.Collapsed);
            }
            else {
                AddProfileButton.Visibility = exists ? Visibility.Collapsed : Visibility.Visible;
                DuplicateProfileButton.Visibility = exists ? Visibility.Visible : Visibility.Collapsed;
                RemoveProfileButton.Visibility = exists ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        catch (Exception ex) {
            Log.Warning(ex, "Error updating profile buttons state.");
        }
    }

    private void WatchMode_Checked(object sender, RoutedEventArgs e) {
        if (_isUpdatingUI || _currentProfile == null) return;

        try {
            // If switching to File mode and it looks like a "fresh" profile for File mode
            // (i.e. path is empty and it was using default RightHanded/x z y d)
            // then apply the suggested defaults for Log File games.
            if (FileRadio.IsChecked == true && string.IsNullOrEmpty(FilePathTextBox.Text)) {
                if (CurrentCoordinateSystem == CoordinateSystem.RightHanded) {
                    var leftHandedItem = Items.FirstOrDefault(i => i.Value == CoordinateSystem.LeftHanded);
                    if (leftHandedItem != null) {
                        CurrentCoordinateSystem = leftHandedItem.Value;
                    }
                }

                if (OrderComboBox.SelectedItem?.ToString() == "x z y d") {
                    OrderComboBox.SelectedItem = "y x z";
                }

                if (string.IsNullOrEmpty(RegexTextBox.Text) || RegexTextBox.Text == Constants.EQLocationRegex) {
                    RegexTextBox.Text = Constants.EQLocationRegex;
                }
            }
            // If switching to Clipboard mode and it looks like a "fresh" profile for Clipboard mode
            // (i.e. it was using Log File defaults y x and LeftHanded)
            // then apply the suggested defaults for Clipboard (Pantheon-style) games.
            else if (ClipboardRadio.IsChecked == true) {
                if (CurrentCoordinateSystem == CoordinateSystem.LeftHanded) {
                    var rightHandedItem = Items.FirstOrDefault(i => i.Value == CoordinateSystem.RightHanded);
                    if (rightHandedItem != null) {
                        // Only switch if it matches the "Log File" default we might have just set or was there
                        //SystemComboBox.SelectedItem = CoordinateSystem.RightHanded;
                        CurrentCoordinateSystem = rightHandedItem.Value;
                    }
                }

                if (OrderComboBox.SelectedItem?.ToString() == "y x" || OrderComboBox.SelectedItem?.ToString() == "y x z") {
                    OrderComboBox.SelectedItem = "x z y d";
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing WatchMode_Checked logic.");
        }
    }

    private void LoadProfile(GameProfile? profile) {
        if (profile == null) return;

        _isUpdatingUI = true;
        try {
            _currentProfile = profile;

            if (profile.WatchMode == WatchMode.Clipboard) {
                ClipboardRadio.IsChecked = true;
            }
            else {
                FileRadio.IsChecked = true;
            }

            FilePathTextBox.Text = profile.LogFilePath ?? string.Empty;
            RegexTextBox.Text = profile.LogFileRegex ?? string.Empty;

            var coordItem = Items.FirstOrDefault(i => i.Value == profile.CoordinateSystem) ?? Items.FirstOrDefault();
            if (coordItem != null) {
                CurrentCoordinateSystem = coordItem.Value;
            }

            OrderComboBox.SelectedItem = profile.CoordinateOrder;
            KeyboardClickThroughCheckBox.IsChecked = _settings.KeyboardClickThrough;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error loading profile '{ProfileName}'.", profile.Name);
        }
        finally {
            _isUpdatingUI = false;
        }
    }

    private void SaveToCurrentProfile() {
        if (_currentProfile == null || _isUpdatingUI) return;

        try {
            // Only save back to the current profile if the name in the combo box matches it.
            // If the user has typed a new name, we don't want to overwrite the old profile's settings
            // with whatever they are currently changing in the UI.
            var currentText = ProfileComboBox.Text.Trim();
            if (!currentText.Equals(_currentProfile.Name, StringComparison.OrdinalIgnoreCase)) {
                return;
            }

            _currentProfile.WatchMode = WatchMode;
            _currentProfile.LogFilePath = LogFilePath;
            _currentProfile.LogFileRegex = LogFileRegex;
            _currentProfile.CoordinateSystem = CurrentCoordinateSystem;
            _currentProfile.CoordinateOrder = CoordinateOrder;
            _settings.KeyboardClickThrough = KeyboardClickThroughCheckBox.IsChecked == true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error saving settings to current profile '{ProfileName}'.", _currentProfile.Name);
        }
    }

    private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (_isUpdatingUI) return;

        try {
            SaveToCurrentProfile();
            if (ProfileComboBox.SelectedItem is GameProfile selectedProfile) {
                LoadProfile(selectedProfile);
            }

            UpdateProfileButtons();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error changing profile selection.");
        }
    }

    private void AddNewProfile(string name) {
        try {
            var trimmedName = name.Trim();
            // We DON'T call SaveToCurrentProfile here because if we are adding a new profile,
            // it means the name in the combo box is already different from _currentProfile.Name,
            // so SaveToCurrentProfile wouldn't do anything anyway.
            // Plus, we want to preserve the _currentProfile as it was before the user started typing.

            // Clone the PREVIOUSLY LOADED profile to get its baseline settings
            var newProfile = _currentProfile != null
                ? _currentProfile.Clone(trimmedName)
                : new GameProfile { Name = trimmedName };

            // Apply current UI settings to the new profile.
            // This makes sure both '+' (with typed settings) and Duplicate (cloning current state)
            // result in a profile that matches what the user sees in the UI.
            newProfile.WatchMode = WatchMode;
            newProfile.LogFilePath = LogFilePath;
            newProfile.LogFileRegex = LogFileRegex;
            newProfile.CoordinateSystem = CurrentCoordinateSystem;
            newProfile.CoordinateOrder = CoordinateOrder;

            _settings.Profiles.Add(newProfile);
            ProfileComboBox.SelectedItem = newProfile;
            LoadProfile(newProfile);
            UpdateProfileButtons();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error adding new profile '{ProfileName}'.", name);
        }
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e) {
        try {
            string newName = ProfileComboBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(newName)) {
                System.Windows.MessageBox.Show("Please enter a name for the new profile.", "New Profile",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_settings.Profiles.Any(p => p.Name.Trim().Equals(newName, StringComparison.OrdinalIgnoreCase))) {
                System.Windows.MessageBox.Show("A profile with this name already exists.", "New Profile",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AddNewProfile(newName);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling AddProfile_Click.");
        }
    }

    private void DuplicateProfile_Click(object sender, RoutedEventArgs e) {
        try {
            if (ProfileComboBox.SelectedItem is GameProfile profileToDuplicate) {
                var dialog = new InputDialog("Enter a name for the duplicated profile:", "Duplicate Profile",
                    $"{profileToDuplicate.Name} - Copy") { Owner = this };
                dialog.ShowDialog();

                if (dialog.ManualDialogResult == true) {
                    string newName = dialog.Answer.Trim();
                    if (string.IsNullOrWhiteSpace(newName)) {
                        System.Windows.MessageBox.Show("Please enter a name for the new profile.", "Duplicate Profile",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (_settings.Profiles.Any(p => p.Name.Trim().Equals(newName, StringComparison.OrdinalIgnoreCase))) {
                        System.Windows.MessageBox.Show("A profile with this name already exists.", "Duplicate Profile",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    AddNewProfile(newName);
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling DuplicateProfile_Click.");
        }
    }

    private void RemoveProfile_Click(object sender, RoutedEventArgs e) {
        try {
            if (ProfileComboBox.SelectedItem is GameProfile profileToRemove) {
                if (_settings.Profiles.Count <= 1) {
                    System.Windows.MessageBox.Show("Cannot remove the last profile.", "Remove Profile", MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var result = System.Windows.MessageBox.Show(
                    $"Are you sure you want to remove the profile '{profileToRemove.Name}'?",
                    "Remove Profile", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes) {
                    _settings.Profiles.Remove(profileToRemove);
                    var nextProfile = _settings.Profiles.FirstOrDefault();
                    ProfileComboBox.SelectedItem = nextProfile;
                    LoadProfile(nextProfile);
                    UpdateProfileButtons();
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling RemoveProfile_Click.");
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e) {
        IsDialogActive = true;
        Window? helperWindow = null;

        try {
            ConfigureDialogToHaveAValidOwner(this, out helperWindow);

            var openFileDialog = new Microsoft.Win32.OpenFileDialog {
                Filter = "Log files (*.log;*.txt)|*.log;*.txt|All files (*.*)|*.*",
                CheckFileExists = true,
                CheckPathExists = true
            };

            bool? result = null;
            // 1. Safely handle owner window handle attachment
            if (helperWindow != null) {
                var helper = new WindowInteropHelper(helperWindow);
                IntPtr ownerHandle = helper.Handle;

                // Pass the handle directly to attach the dialog modally
                result = ownerHandle != IntPtr.Zero
                    ? openFileDialog.ShowDialog(helperWindow)
                    : openFileDialog.ShowDialog();
            }
            else {
                // Fallback if helperWindow is null
                result = openFileDialog.ShowDialog();
            }

            if (result == true && !string.IsNullOrWhiteSpace(openFileDialog.FileName)) {
                FilePathTextBox.Text = openFileDialog.FileName;
            }
        }
        catch (InvalidOperationException ex) {
            Log.Error(ex, "Threading issue opening file dialog in BrowseButton_Click.");
            System.Windows.MessageBox.Show(
                $"Unable to open file dialog: Threading issue detected.\n\nDetails: {ex.Message}",
                "Dialog Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex) {
            Log.Error(ex, "Unexpected error in BrowseButton_Click.");
            System.Windows.MessageBox.Show($"An error occurred while opening the file dialog:\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally {
            // ALWAYS close the helper to prevent memory leaks
            helperWindow?.Close();
            IsDialogActive = false;
        }
    }
    
    private void OkButton_Click(object sender, RoutedEventArgs e) {
        try {
            var currentProfileName = ProfileComboBox.Text.Trim();
            var profileExists = _settings.Profiles.Any(p =>
                p.Name.Trim().Equals(currentProfileName, StringComparison.OrdinalIgnoreCase));

            if (!profileExists && !string.IsNullOrWhiteSpace(currentProfileName) &&
                !currentProfileName.Equals("Default", StringComparison.OrdinalIgnoreCase)) {
                var result = System.Windows.MessageBox.Show(
                    $"The profile '{currentProfileName}' does not exist. Would you like to add it?",
                    "Add New Profile", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes) {
                    AddNewProfile(currentProfileName);
                }
            }

            SaveToCurrentProfile();

            if (WatchMode == WatchMode.File && string.IsNullOrWhiteSpace(LogFilePath)) {
                var result = System.Windows.MessageBox.Show(
                    "No log file has been selected. The program will not be able to watch for location changes until a log file is defined.\n\nDo you want to continue?",
                    "No Log File Selected", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.No) {
                    return;
                }
            }

            if (ProfileComboBox.SelectedItem is GameProfile selectedProfile) {
                _settings.LastSelectedProfileName = selectedProfile.Name;
            }

            IsConfirmed = true;
            ManualDialogResult = true;
                
            // Inherited from ChildWindow
            SafeCloseDialog();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error confirming WatcherConfigurationDialog in OkButton_Click.");
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) {
        try {
            IsConfirmed = false;
            ManualDialogResult = false;
            
            // Inherited from ChildWindow
            SafeCloseDialog();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error canceling WatcherConfigurationDialog.");
        }
    }
}