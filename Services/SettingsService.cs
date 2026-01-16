using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MMONavigator.Models;

namespace MMONavigator.Services;

public interface ISettingsService {
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);
    List<LocationItem> LoadLocations();
    void SaveLocations(IEnumerable<LocationItem> locations);
}

public class SettingsService : ISettingsService {
    private readonly string _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
    private readonly string _locationsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locations.json");

    public AppSettings LoadSettings() {
        try {
            if (File.Exists(_settingsPath)) {
                string json = File.ReadAllText(_settingsPath);
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

    public void SaveSettings(AppSettings settings) {
        try {
            string json = JsonSerializer.Serialize(settings);
            File.WriteAllText(_settingsPath, json);
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
        }
    }

    public List<LocationItem> LoadLocations() {
        try {
            if (File.Exists(_locationsPath)) {
                string json = File.ReadAllText(_locationsPath);
                return JsonSerializer.Deserialize<List<LocationItem>>(json) ?? new List<LocationItem>();
            }
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"Error loading locations: {ex.Message}");
        }
        return new List<LocationItem>();
    }

    public void SaveLocations(IEnumerable<LocationItem> locations) {
        try {
            string json = JsonSerializer.Serialize(locations.ToList());
            File.WriteAllText(_locationsPath, json);
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"Error saving locations: {ex.Message}");
        }
    }
}
