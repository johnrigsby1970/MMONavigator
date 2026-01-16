using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MMONavigator.Helpers;
using MMONavigator.Models;
using MMONavigator.ViewModels;

namespace MMONavigator.Views;

public partial class WatcherConfigurationDialog : Window {
    private readonly AppSettings _settings;
    private GameProfile? _currentProfile;
    private bool _isUpdatingUI = false;

    public WatchMode WatchMode => ClipboardRadio.IsChecked == true ? WatchMode.Clipboard : WatchMode.File;
    public string LogFilePath => FilePathTextBox.Text;
    public string LogFileRegex => RegexTextBox.Text;
    public string CoordinateOrder => OrderComboBox.SelectedItem?.ToString() ?? "x z y d";
    public CoordinateSystem CoordinateSystem => (CoordinateSystem)SystemComboBox.SelectedItem;

    public WatcherConfigurationDialog(AppSettings settings) {
        InitializeComponent();
        _settings = settings;

        ProfileComboBox.ItemsSource = _settings.Profiles;
        ProfileComboBox.DisplayMemberPath = "Name";
        
        var selectedProfile = _settings.Profiles.FirstOrDefault(p => p.Name == _settings.LastSelectedProfileName) 
                             ?? _settings.Profiles.FirstOrDefault();
        
        ProfileComboBox.SelectedItem = selectedProfile;

        SystemComboBox.ItemsSource = Enum.GetValues(typeof(CoordinateSystem));
        OrderComboBox.ItemsSource = Constants.AvailableCoordinateOrders;

        ClipboardRadio.Checked += WatchMode_Checked;
        FileRadio.Checked += WatchMode_Checked;

        ProfileComboBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler(ProfileComboBox_TextChanged));

        LoadProfile(selectedProfile);
        UpdateProfileButtons();
    }

    private void ProfileComboBox_TextChanged(object sender, TextChangedEventArgs e) {
        UpdateProfileButtons();
    }

    private void UpdateProfileButtons() {
        string currentText = ProfileComboBox.Text.Trim();
        bool isDefault = currentText.Equals("Default", StringComparison.OrdinalIgnoreCase);
        bool isEmpty = string.IsNullOrWhiteSpace(currentText);
        bool exists = _settings.Profiles.Any(p => p.Name.Trim().Equals(currentText, StringComparison.OrdinalIgnoreCase));

        if (isDefault || isEmpty) {
            AddProfileButton.Visibility = Visibility.Collapsed;
            DuplicateProfileButton.Visibility = isDefault && exists ? Visibility.Visible : Visibility.Collapsed;
            RemoveProfileButton.Visibility = isDefault && exists ? Visibility.Collapsed : (exists ? Visibility.Visible : Visibility.Collapsed);
        } else {
            AddProfileButton.Visibility = exists ? Visibility.Collapsed : Visibility.Visible;
            DuplicateProfileButton.Visibility = exists ? Visibility.Visible : Visibility.Collapsed;
            RemoveProfileButton.Visibility = exists ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void WatchMode_Checked(object sender, RoutedEventArgs e) {
        if (_isUpdatingUI || _currentProfile == null) return;
        
        // If switching to File mode and it looks like a "fresh" profile for File mode
        // (i.e. path is empty and it was using default RightHanded/x z y d)
        // then apply the suggested defaults for Log File games.
        if (FileRadio.IsChecked == true && string.IsNullOrEmpty(FilePathTextBox.Text)) {
            if (SystemComboBox.SelectedItem is CoordinateSystem cs && cs == CoordinateSystem.RightHanded) {
                SystemComboBox.SelectedItem = CoordinateSystem.LeftHanded;
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
            if (SystemComboBox.SelectedItem is CoordinateSystem cs && cs == CoordinateSystem.LeftHanded) {
                // Only switch if it matches the "Log File" default we might have just set or was there
                SystemComboBox.SelectedItem = CoordinateSystem.RightHanded;
            }
            if (OrderComboBox.SelectedItem?.ToString() == "y x" || OrderComboBox.SelectedItem?.ToString() == "y x z") {
                OrderComboBox.SelectedItem = "x z y d";
            }
        }
    }

    private void LoadProfile(GameProfile? profile) {
        if (profile == null) return;
        _isUpdatingUI = true;
        _currentProfile = profile;

        if (profile.WatchMode == WatchMode.Clipboard) {
            ClipboardRadio.IsChecked = true;
        } else {
            FileRadio.IsChecked = true;
        }

        FilePathTextBox.Text = profile.LogFilePath;
        RegexTextBox.Text = profile.LogFileRegex;
        SystemComboBox.SelectedItem = profile.CoordinateSystem;
        OrderComboBox.SelectedItem = profile.CoordinateOrder;
        _isUpdatingUI = false;
    }

    private void SaveToCurrentProfile() {
        if (_currentProfile == null || _isUpdatingUI) return;

        // Only save back to the current profile if the name in the combo box matches it.
        // If the user has typed a new name, we don't want to overwrite the old profile's settings
        // with whatever they are currently changing in the UI.
        string currentText = ProfileComboBox.Text.Trim();
        if (!currentText.Equals(_currentProfile.Name, StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        _currentProfile.WatchMode = WatchMode;
        _currentProfile.LogFilePath = LogFilePath;
        _currentProfile.LogFileRegex = LogFileRegex;
        _currentProfile.CoordinateSystem = CoordinateSystem;
        _currentProfile.CoordinateOrder = CoordinateOrder;
    }

    private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (_isUpdatingUI) return;
        
        SaveToCurrentProfile();
        if (ProfileComboBox.SelectedItem is GameProfile selectedProfile) {
            LoadProfile(selectedProfile);
        }
        UpdateProfileButtons();
    }

    private void AddNewProfile(string name) {
        string trimmedName = name.Trim();
        // We DON'T call SaveToCurrentProfile here because if we are adding a new profile,
        // it means the name in the combo box is already different from _currentProfile.Name,
        // so SaveToCurrentProfile wouldn't do anything anyway.
        // Plus, we want to preserve the _currentProfile as it was before the user started typing.

        var newProfile = new GameProfile { Name = trimmedName };
        if (_currentProfile != null) {
            // Clone the PREVIOUSLY LOADED profile to get its baseline settings
            newProfile = _currentProfile.Clone(trimmedName);
        }

        // Apply current UI settings to the new profile.
        // This makes sure both '+' (with typed settings) and Duplicate (cloning current state)
        // result in a profile that matches what the user sees in the UI.
        newProfile.WatchMode = WatchMode;
        newProfile.LogFilePath = LogFilePath;
        newProfile.LogFileRegex = LogFileRegex;
        newProfile.CoordinateSystem = CoordinateSystem;
        newProfile.CoordinateOrder = CoordinateOrder;

        _settings.Profiles.Add(newProfile);
        ProfileComboBox.SelectedItem = newProfile;
        LoadProfile(newProfile);
        UpdateProfileButtons();
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e) {
        string newName = ProfileComboBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName)) {
            MessageBox.Show("Please enter a name for the new profile.", "New Profile", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_settings.Profiles.Any(p => p.Name.Trim().Equals(newName, StringComparison.OrdinalIgnoreCase))) {
            MessageBox.Show("A profile with this name already exists.", "New Profile", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AddNewProfile(newName);
    }

    private void DuplicateProfile_Click(object sender, RoutedEventArgs e) {
        if (ProfileComboBox.SelectedItem is GameProfile profileToDuplicate) {
            var dialog = new InputDialog("Enter a name for the duplicated profile:", "Duplicate Profile", $"{profileToDuplicate.Name} - Copy");
            dialog.Owner = this;
            if (dialog.ShowDialog() == true) {
                string newName = dialog.Answer.Trim();
                if (string.IsNullOrWhiteSpace(newName)) {
                    MessageBox.Show("Please enter a name for the new profile.", "Duplicate Profile", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_settings.Profiles.Any(p => p.Name.Trim().Equals(newName, StringComparison.OrdinalIgnoreCase))) {
                    MessageBox.Show("A profile with this name already exists.", "Duplicate Profile", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                AddNewProfile(newName);
            }
        }
    }

    private void RemoveProfile_Click(object sender, RoutedEventArgs e) {
        if (ProfileComboBox.SelectedItem is GameProfile profileToRemove) {
            if (_settings.Profiles.Count <= 1) {
                MessageBox.Show("Cannot remove the last profile.", "Remove Profile", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show($"Are you sure you want to remove the profile '{profileToRemove.Name}'?", "Remove Profile", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes) {
                _settings.Profiles.Remove(profileToRemove);
                var nextProfile = _settings.Profiles.FirstOrDefault();
                ProfileComboBox.SelectedItem = nextProfile;
                LoadProfile(nextProfile);
                UpdateProfileButtons();
            }
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e) {
        OpenFileDialog openFileDialog = new OpenFileDialog();
        openFileDialog.Filter = "Log files (*.log;*.txt)|*.log;*.txt|All files (*.*)|*.*";
        if (openFileDialog.ShowDialog() == true) {
            FilePathTextBox.Text = openFileDialog.FileName;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) {
        string currentProfileName = ProfileComboBox.Text.Trim();
        bool profileExists = _settings.Profiles.Any(p => p.Name.Trim().Equals(currentProfileName, StringComparison.OrdinalIgnoreCase));

        if (!profileExists && !string.IsNullOrWhiteSpace(currentProfileName) && !currentProfileName.Equals("Default", StringComparison.OrdinalIgnoreCase)) {
            var result = MessageBox.Show($"The profile '{currentProfileName}' does not exist. Would you like to add it?", "Add New Profile", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes) {
                AddNewProfile(currentProfileName);
            }
        }

        SaveToCurrentProfile();
        if (WatchMode == WatchMode.File && string.IsNullOrWhiteSpace(LogFilePath)) {
            var result = MessageBox.Show("No log file has been selected. The program will not be able to watch for location changes until a log file is defined.\n\nDo you want to continue?", "No Log File Selected", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.No) {
                return;
            }
        }
        if (ProfileComboBox.SelectedItem is GameProfile selectedProfile) {
            _settings.LastSelectedProfileName = selectedProfile.Name;
        }
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) {
        DialogResult = false;
    }
}
