using System.Diagnostics;
using System.IO;
using System.Text.Json;
using MMONavigator.Models;
using System.Text.RegularExpressions;

namespace MMONavigator.Services;

public interface ISettingsService {
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);
    List<LocationItem> LoadLocations(GameProfile profile);
    void SaveLocations(IEnumerable<LocationItem> locations, GameProfile profile);
}

public class SettingsService : ISettingsService {

    private readonly string _settingsPath = string.Empty;
    private string _locationsPath = string.Empty;

    public SettingsService() {
        var appFolder = Helpers.NativeMethods.AppFolder();
        _settingsPath = Path.Combine(appFolder, "settings.json");
        _locationsPath = Path.Combine(appFolder, "locations.json");
    }
    
    public AppSettings LoadSettings() {
        try {
            if (File.Exists(_settingsPath)) {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                settings.MigrateLegacySettings();
                return settings;
            }
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
        }
        var newSettings = new AppSettings();
        newSettings.MigrateLegacySettings();
        return newSettings;
    }

    private static string MakeValidFileName(string name)
    {
        var invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
        var invalidRegStr = string.Format(@"([{0}]* windfall +$)|([{0}]+)", invalidChars);
        return Regex.Replace(name, invalidRegStr, "_");
    }
    
    public void SaveSettings(AppSettings settings) {
        try {
            var json = JsonSerializer.Serialize(settings);
            File.WriteAllText(_settingsPath, json);
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
        }
    }

    public List<LocationItem> LoadLocations(GameProfile profile) {
        try {
            if (string.IsNullOrEmpty(profile.LastLocationsFile)) {
                if (profile.Name != "Default") {
                    _locationsPath = Path.Combine(Helpers.NativeMethods.AppFolder(),
                        MakeValidFileName(profile.Name) + "_locations.json");
                }
                else {
                    _locationsPath = Path.Combine(Helpers.NativeMethods.AppFolder(), "locations.json");
                }
            }
            else {
                _locationsPath = profile.LastLocationsFile;
            }

            if (File.Exists(_locationsPath)) {
                var json = File.ReadAllText(_locationsPath);
                return JsonSerializer.Deserialize<List<LocationItem>>(json) ?? new List<LocationItem>();
            }
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"Error loading locations: {ex.Message}");
        }
        return new List<LocationItem>();
    }

    public void SaveLocations(IEnumerable<LocationItem> locations, GameProfile profile) {
        try {
            if (string.IsNullOrEmpty(profile.LastLocationsFile)) {
                if (profile.Name != "Default") {
                    _locationsPath = Path.Combine(Helpers.NativeMethods.AppFolder(),
                        MakeValidFileName(profile.Name) + "_locations.json");
                }
                else {
                    _locationsPath = Path.Combine(Helpers.NativeMethods.AppFolder(), "locations.json");
                }

                profile.LastLocationsFile = _locationsPath;
            }
            else {
                _locationsPath = profile.LastLocationsFile;
            }

            string json = JsonSerializer.Serialize(locations.ToList());
            File.WriteAllText(_locationsPath, json);
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"Error saving locations: {ex.Message}");
        }
    }
}
