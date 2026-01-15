using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MMONavigator.Models;

public enum WatchMode {
    Clipboard,
    File
}

public class AppSettings : INotifyPropertyChanged {
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

    private string _logFileRegex = @"Your Location is.*?(-?\d+(?:\.\d+)?)\D+?(-?\d+(?:\.\d+)?)(?:\D+?(-?\d+(?:\.\d+)?))?";
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

    public List<string> AvailableCoordinateOrders { get; set; } = new List<string> { "x z y d", "y x", "x y" };

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
