using System.ComponentModel;
using System.Runtime.CompilerServices;

using MMONavigator.Helpers;

namespace MMONavigator.Models;

public class GameProfile : INotifyPropertyChanged {
    private string _name = "Default";
    public string Name {
        get => _name;
        set {
            if (_name != value) {
                _name = value;
                OnPropertyChanged();
            }
        }
    }

    private WatchMode _watchMode = WatchMode.Clipboard;
    public WatchMode WatchMode {
        get => _watchMode;
        set {
            if (_watchMode != value) {
                _watchMode = value;
                OnPropertyChanged();
            }
        }
    }

    private CoordinateSystem _coordinateSystem = CoordinateSystem.RightHanded;
    public CoordinateSystem CoordinateSystem {
        get => _coordinateSystem;
        set {
            if (_coordinateSystem != value) {
                _coordinateSystem = value;
                OnPropertyChanged();
            }
        }
    }

    private string _logFilePath = string.Empty;
    public string LogFilePath {
        get => _logFilePath;
        set {
            if (_logFilePath != value) {
                _logFilePath = value;
                OnPropertyChanged();
            }
        }
    }

    private string _lastLocationsFile = "";
    public string LastLocationsFile {
        get => _lastLocationsFile;
        set {
            if (_lastLocationsFile != value) {
                _lastLocationsFile = value;
                OnPropertyChanged();
            }
        }
    }
    
    private string _logFileRegex = Constants.EQLocationRegex;
    public string LogFileRegex {
        get => _logFileRegex;
        set {
            if (_logFileRegex != value) {
                _logFileRegex = value;
                OnPropertyChanged();
            }
        }
    }

    private string _coordinateOrder = "x z y d";
    public string CoordinateOrder {
        get => _coordinateOrder;
        set {
            if (_coordinateOrder != value) {
                _coordinateOrder = value;
                OnPropertyChanged();
            }
        }
    }

    private MapSettings _mapSettings = new();
    public MapSettings MapSettings {
        get => _mapSettings;
        set {
            if (_mapSettings != value) {
                _mapSettings = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public GameProfile Clone(string newName) {
        return new GameProfile {
            Name = newName,
            WatchMode = this.WatchMode,
            CoordinateSystem = this.CoordinateSystem,
            LogFilePath = this.LogFilePath,
            LogFileRegex = this.LogFileRegex,
            CoordinateOrder = this.CoordinateOrder,
            MapSettings = new MapSettings {
                ImagePath = this.MapSettings.ImagePath,
                Point1 = new MapPoint { X = this.MapSettings.Point1.X, Y = this.MapSettings.Point1.Y, PixelX = this.MapSettings.Point1.PixelX, PixelY = this.MapSettings.Point1.PixelY },
                Point2 = new MapPoint { X = this.MapSettings.Point2.X, Y = this.MapSettings.Point2.Y, PixelX = this.MapSettings.Point2.PixelX, PixelY = this.MapSettings.Point2.PixelY },
                IsCalibrated = this.MapSettings.IsCalibrated
            }
        };
    }
}
