using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using MMONavigator.Helpers;

namespace MMONavigator.Models;

public enum WatchMode {
    Clipboard,
    File
}

public enum CoordinateSystem {
    RightHanded, // +X = East, +Y = North
    LeftHanded   // +X = West, +Y = North
}

public class AppSettings : INotifyPropertyChanged {
    private ObservableCollection<GameProfile> _profiles = new();
    public ObservableCollection<GameProfile> Profiles {
        get => _profiles;
        set {
            _profiles = value;
            OnPropertyChanged();
        }
    }

    private string _lastSelectedProfileName = "Default";
    public string LastSelectedProfileName {
        get => _lastSelectedProfileName;
        set {
            if (_lastSelectedProfileName != value) {
                _lastSelectedProfileName = value;
                OnPropertyChanged();
            }
        }
    }

    [JsonIgnore]
    public GameProfile SelectedProfile {
        get {
            var profile = Profiles.FirstOrDefault(p => p.Name == LastSelectedProfileName);
            if (profile == null) {
                if (Profiles.Count == 0) {
                    profile = new GameProfile { Name = "Default" };
                    Profiles.Add(profile);
                } else {
                    profile = Profiles[0];
                }
                LastSelectedProfileName = profile.Name;
            }
            return profile;
        }
    }

    // Legacy properties for backward compatibility during deserialization
    [JsonPropertyName("WatchMode")]
    public WatchMode? LegacyWatchMode { get; set; }
    [JsonPropertyName("CoordinateSystem")]
    public CoordinateSystem? LegacyCoordinateSystem { get; set; }
    [JsonPropertyName("LogFilePath")]
    public string? LegacyLogFilePath { get; set; }
    [JsonPropertyName("LogFileRegex")]
    public string? LegacyLogFileRegex { get; set; }
    [JsonPropertyName("CoordinateOrder")]
    public string? LegacyCoordinateOrder { get; set; }

    public void MigrateLegacySettings() {
        if (Profiles.Count == 0 && (LegacyWatchMode.HasValue || !string.IsNullOrEmpty(LegacyLogFilePath))) {
            var watchMode = LegacyWatchMode ?? WatchMode.Clipboard;
            var defaultProfile = new GameProfile {
                Name = "Default",
                WatchMode = watchMode,
                CoordinateSystem = LegacyCoordinateSystem ?? (watchMode == WatchMode.File ? CoordinateSystem.LeftHanded : CoordinateSystem.RightHanded),
                LogFilePath = LegacyLogFilePath ?? string.Empty,
                LogFileRegex = LegacyLogFileRegex ?? Constants.EQLocationRegex,
                CoordinateOrder = LegacyCoordinateOrder ?? (watchMode == WatchMode.File ? "y x z" : "x z y d")
            };
            Profiles.Add(defaultProfile);
            LastSelectedProfileName = defaultProfile.Name;
        }

        if (Profiles.Count == 0) {
            Profiles.Add(new GameProfile { Name = "Default" });
            LastSelectedProfileName = "Default";
        }
    }

    // Application level settings
    private bool _showSettings = true;
    public bool ShowSettings {
        get => _showSettings;
        set {
            if (_showSettings != value) {
                _showSettings = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _showTimers = false;
    public bool ShowTimers {
        get => _showTimers;
        set {
            if (_showTimers != value) {
                _showTimers = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
