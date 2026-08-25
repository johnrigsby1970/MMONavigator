using System.IO;
using System.Runtime.InteropServices;
using MMONavigator.Helpers;
using MMONavigator.Interfaces;
using MMONavigator.Models;

namespace MMONavigator.Services;

public class LogFileLocationProvider : ILocationProvider{
    public event EventHandler<string>? LocationUpdated;

    private FileSystemWatcher? _fileWatcher;
    private long _lastFilePosition;
    private readonly object _fileLock = new object();
    private IntPtr _windowHandle;
    private AppSettings? _settings;

    public void Start(AppSettings settings, IntPtr windowHandle) {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        
        Log.Information("Starting LocationProvider. Mode: {WatchMode}, WindowHandle: {Handle}", 
            settings.SelectedProfile?.WatchMode, windowHandle);

        Stop();
        _windowHandle = windowHandle;

        try {
            SetupFileWatcher();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error starting LocationProvider.");
        }
    }

    public void Stop() {
        Log.Information("Stopping LocationProvider.");
        
        if (_fileWatcher != null) {
            try {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Changed -= OnFileChanged;
                _fileWatcher.Created -= OnFileChanged;
                _fileWatcher.Deleted -= OnFileDeleted;
                _fileWatcher.Dispose();
            }
            catch (Exception ex) {
                Log.Warning(ex, "Error disposing FileSystemWatcher during LocationProvider.Stop.");
            }
            finally {
                _fileWatcher = null;
            }
        }
    }
    
    private void SetupFileWatcher() {
        if (_settings?.SelectedProfile == null || string.IsNullOrEmpty(_settings.SelectedProfile.LogFilePath)) {
            Log.Information("Log file path is empty; FileSystemWatcher will not be started.");
            return;
        }

        try {
            var fullPath = _settings.SelectedProfile.LogFilePath;
            
            if (fullPath.Length >= 260) {
                Log.Warning("Log file path exceeds standard path limits ({Length} chars): {FullPath}", fullPath.Length, fullPath);
                return;
            }

            var directory = Path.GetDirectoryName(fullPath);
            var fileName = Path.GetFileName(fullPath);

            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName)) {
                Log.Warning("Invalid directory or filename derived from log path: '{FullPath}'", fullPath);
                return;
            }

            if (!Directory.Exists(directory)) {
                Log.Warning("Target directory for log file watcher does not exist: '{Directory}'", directory);
                // We don't create the game's log directory, but we should be ready if it appears
                // FileSystemWatcher can only be created for an existing directory.
                return;
            }

            _fileWatcher = new FileSystemWatcher(directory, fileName) {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime
            };

            _fileWatcher.Changed -= OnFileChanged;
            _fileWatcher.Created -= OnFileChanged;
            _fileWatcher.Deleted -= OnFileDeleted;
            _fileWatcher.Renamed -= OnFileRenamed;
            _fileWatcher.Error -= OnFileWatcherError;
            
            _fileWatcher.Changed += OnFileChanged;
            _fileWatcher.Created += OnFileChanged;
            _fileWatcher.Deleted += OnFileDeleted;
            _fileWatcher.Renamed += OnFileRenamed;
            _fileWatcher.Error += OnFileWatcherError;

            _fileWatcher.EnableRaisingEvents = true;

            InitializeFilePosition();
            Log.Information("FileSystemWatcher successfully configured for '{FullPath}'.", fullPath);
        }
        catch (UnauthorizedAccessException ex) {
            Log.Error(ex, "Access denied establishing FileSystemWatcher for '{LogPath}'.", _settings.SelectedProfile.LogFilePath);
        }
        catch (ArgumentException ex) {
            Log.Error(ex, "Invalid argument configuring FileSystemWatcher for '{LogPath}'.", _settings.SelectedProfile.LogFilePath);
        }
        catch (Exception ex) {
            Log.Error(ex, "Unexpected error setting up FileSystemWatcher for '{LogPath}'.", _settings.SelectedProfile.LogFilePath);
        }
    }

    private void OnFileWatcherError(object sender, ErrorEventArgs e) {
        var ex = e.GetException();
        Log.Error(ex, "FileSystemWatcher internal error occurred.");
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e) {
        OnFileChanged(sender, e);
    }

    private void InitializeFilePosition() {
        if (_settings?.SelectedProfile == null) return;

        lock (_fileLock) {
            try {
                var logPath = _settings.SelectedProfile.LogFilePath;
                if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath)) {
                    using (var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                        _lastFilePosition = stream.Length;
                        Log.Debug("Initialized log file pointer to end of file: {Position} bytes", _lastFilePosition);
                    }
                } else {
                    _lastFilePosition = 0;
                }
            }
            catch (IOException ex) {
                Log.Warning(ex, "IOException initializing log file stream position.");
                _lastFilePosition = 0;
            }
            catch (UnauthorizedAccessException ex) {
                Log.Warning(ex, "Access denied initializing log file stream position.");
                _lastFilePosition = 0;
            }
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e) {
        if (_settings?.SelectedProfile == null) return;

        lock (_fileLock) {
            if (!File.Exists(e.FullPath)) {
                Log.Debug("File change event received for non-existent path: '{FullPath}'", e.FullPath);
                return;
            }

            try {
                using (var stream = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                    // Handle log truncation or file rotation
                    if (stream.Length < _lastFilePosition) {
                        Log.Information("Log file truncation detected. Resetting file pointer to 0.");
                        _lastFilePosition = 0;
                    }

                    if (stream.Length > _lastFilePosition) {
                        stream.Position = _lastFilePosition;
                        using (var reader = new StreamReader(stream)) {
                            string? line;
                            string? lastMatch = null;

                            while ((line = reader.ReadLine()) != null) {
                                if (string.IsNullOrWhiteSpace(line)) continue;

                                if (LogParser.TryParseLogLine(line, _settings.SelectedProfile.LogFileRegex, out string coordinates)) {
                                    lastMatch = coordinates;
                                }
                            }

                            if (lastMatch != null) {
                                Log.Debug("Log line successfully parsed coordinates: {Coordinates}", lastMatch);
                                LocationUpdated?.Invoke(this, lastMatch);
                            }

                            _lastFilePosition = stream.Position;
                        }
                    }
                }
            }
            catch (IOException ex) {
                Log.Warning(ex, "IOException reading log file '{FullPath}'.", e.FullPath);
            }
            catch (Exception ex) {
                Log.Error(ex, "Unexpected error reading log file '{FullPath}'.", e.FullPath);
            }
        }
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e) {
        lock (_fileLock) {
            Log.Information("Log file deleted: '{FullPath}'. Resetting file pointer.", e.FullPath);
            _lastFilePosition = 0;
        }
    }

    public void Dispose() {
        Stop();
    }
}