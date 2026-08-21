using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using MMONavigator.Models;

namespace MMONavigator.Services;

public interface ISettingsService {
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);
    List<LocationItem> LoadLocations(GameProfile profile);
    void SaveLocations(IEnumerable<LocationItem> locations, GameProfile profile);
}

public class SettingsService : ISettingsService {
    private readonly string _settingsPath;

    public SettingsService() {
        var appFolder = Helpers.NativeMethods.AppFolder();
        _settingsPath = Path.Combine(appFolder, "settings.json");
    }

    public AppSettings LoadSettings() {
        try {
            if (File.Exists(_settingsPath)) {
                var json = File.ReadAllText(_settingsPath);
                try {
                    var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                    settings.MigrateLegacySettings();
                    return settings;
                }
                catch (JsonException ex) {
                    Log.Error(ex, "JSON deserialization error loading settings from '{Path}'.", _settingsPath);
                    
                    System.Windows.MessageBox.Show(
                        $"The settings file appears to be corrupted and could not be loaded. Default settings will be used.\n\nError: {ex.Message}", 
                        "Settings Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    
                    try {
                        File.Move(_settingsPath, _settingsPath + ".bak", overwrite: true);
                        Log.Information("Corrupted settings file moved to '{Path}.bak'.", _settingsPath);
                    } 
                    catch (Exception moveEx) {
                        Log.Warning(moveEx, "Failed to move corrupted settings file to backup.");
                    }
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Unexpected error loading settings from '{Path}'.", _settingsPath);
        }

        var newSettings = new AppSettings();
        newSettings.MigrateLegacySettings();
        return newSettings;
    }

    public void SaveSettings(AppSettings settings) {
        if (settings == null) {
            Log.Warning("Attempted to call SaveSettings with a null AppSettings instance.");
            return;
        }

        try {
            // Ensure window positions don't save in a minimized state
            if (settings.MainWindowPlacement?.State == WindowState.Minimized) {
                settings.MainWindowPlacement.State = WindowState.Normal;
            }

            if (settings.MapWindowPlacement?.State == WindowState.Minimized) {
                settings.MapWindowPlacement.State = WindowState.Normal;
            }
            
            if (settings.ThreeDMapWindowPlacement?.State == WindowState.Minimized) {
                settings.ThreeDMapWindowPlacement.State = WindowState.Normal;
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(settings, options);

            var tempPath = _settingsPath + ".tmp";
            var backupPath = _settingsPath + ".old";

            // Atomic write sequence
            File.WriteAllText(tempPath, json);

            if (File.Exists(_settingsPath)) {
                File.Replace(tempPath, _settingsPath, backupPath);
                try {
                    if (File.Exists(backupPath)) {
                        File.Delete(backupPath);
                    }
                }
                catch (Exception delEx) {
                    Log.Debug(delEx, "Could not remove temporary backup settings file '{BackupPath}'.", backupPath);
                }
            } 
            else {
                File.Move(tempPath, _settingsPath);
            }

            Log.Information("AppSettings saved successfully.");
        }
        catch (Exception ex) {
            Log.Error(ex, "Error saving settings to '{Path}'.", _settingsPath);
        }
    }

    public List<LocationItem> LoadLocations(GameProfile profile) {
        if (profile == null) {
            Log.Warning("Attempted to call LoadLocations with a null GameProfile.");
            return new List<LocationItem>();
        }

        string locationsPath = GetLocationsFilePath(profile);

        try {
            if (File.Exists(locationsPath)) {
                var json = File.ReadAllText(locationsPath);
                try {
                    return JsonSerializer.Deserialize<List<LocationItem>>(json) ?? new List<LocationItem>();
                }
                catch (JsonException ex) {
                    Log.Error(ex, "JSON deserialization error loading locations from '{Path}'.", locationsPath);

                    System.Windows.MessageBox.Show(
                        $"The locations file '{Path.GetFileName(locationsPath)}' is corrupted and could not be loaded.\n\nError: {ex.Message}", 
                        "Locations Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    
                    try {
                        File.Move(locationsPath, locationsPath + ".bak", overwrite: true);
                        Log.Information("Corrupted locations file moved to '{Path}.bak'.", locationsPath);
                    } 
                    catch (Exception moveEx) {
                        Log.Warning(moveEx, "Failed to move corrupted locations file to backup.");
                    }
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Unexpected error loading locations from '{Path}'.", locationsPath);
        }

        return new List<LocationItem>();
    }

    public void SaveLocations(IEnumerable<LocationItem> locations, GameProfile profile) {
        if (profile == null) {
            Log.Warning("Attempted to call SaveLocations with a null GameProfile.");
            return;
        }

        string locationsPath = GetLocationsFilePath(profile);
        profile.LastLocationsFile = locationsPath;

        try {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(locations?.ToList() ?? new List<LocationItem>(), options);

            var tempPath = locationsPath + ".tmp";
            var backupPath = locationsPath + ".old";

            // Atomic write sequence
            File.WriteAllText(tempPath, json);

            if (File.Exists(locationsPath)) {
                File.Replace(tempPath, locationsPath, backupPath);
                try {
                    if (File.Exists(backupPath)) {
                        File.Delete(backupPath);
                    }
                }
                catch (Exception delEx) {
                    Log.Debug(delEx, "Could not remove temporary backup locations file '{BackupPath}'.", backupPath);
                }
            } 
            else {
                File.Move(tempPath, locationsPath);
            }

            Log.Information("Locations saved successfully to '{Path}'.", locationsPath);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error saving locations to '{Path}'.", locationsPath);
        }
    }

    private static string GetLocationsFilePath(GameProfile profile) {
        if (!string.IsNullOrEmpty(profile.LastLocationsFile)) {
            return profile.LastLocationsFile;
        }

        string appFolder = Helpers.NativeMethods.AppFolder();
        if (!string.Equals(profile.Name, "Default", StringComparison.OrdinalIgnoreCase)) {
            return Path.Combine(appFolder, MakeValidFileName(profile.Name) + "_locations.json");
        }

        return Path.Combine(appFolder, "locations.json");
    }

    private static string MakeValidFileName(string name) {
        if (string.IsNullOrWhiteSpace(name)) return "profile";
        var invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
        var invalidRegStr = string.Format(@"([{0}]* windfall +$)|([{0}]+)", invalidChars);
        return Regex.Replace(name, invalidRegStr, "_");
    }
}