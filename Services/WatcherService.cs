using System.IO;
using System.Runtime.InteropServices;
using MMONavigator.Helpers;
using MMONavigator.Interfaces;
using MMONavigator.Models;

namespace MMONavigator.Services;

public class WatcherService : IWatcherService {
    public event EventHandler<string>? LocationUpdated;

    private FileSystemWatcher? _fileWatcher;
    private long _lastFilePosition;
    private readonly object _fileLock = new object();
    private IntPtr _windowHandle;
    private AppSettings? _settings;

    public void Start(AppSettings settings, IntPtr windowHandle) {
        System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] Starting watcher service, Mode: {settings.SelectedProfile.WatchMode}");
        Stop();
        _settings = settings;
        _windowHandle = windowHandle;

        if (settings.SelectedProfile.WatchMode == WatchMode.Clipboard) {
            NativeMethods.AddClipboardFormatListener(_windowHandle);
        } else {
            SetupFileWatcher();
        }
    }

    public void Stop() {
        System.Diagnostics.Debug.WriteLine("[DEBUG_LOG] Stopping watcher service");
        if (_windowHandle != IntPtr.Zero) {
            NativeMethods.RemoveClipboardFormatListener(_windowHandle);
        }
        if (_fileWatcher != null) {
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Dispose();
            _fileWatcher = null;
        }
    }

    public void HandleClipboardUpdate() {
        if (_settings?.SelectedProfile.WatchMode != WatchMode.Clipboard) return;
        try {
            var text = System.Windows.Clipboard.GetText();
            if (string.IsNullOrEmpty(text)) return;
            if (text.Length > Scrubber.MaxLength) return;

            if (Scrubber.TryParse(text, _settings.SelectedProfile.CoordinateOrder, out _)) {
                string coordinates = Scrubber.ScrubEntry(text) ?? string.Empty;
                LocationUpdated?.Invoke(this, coordinates);
                System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] Clipboard updated: {coordinates}");
            }
        }
        catch (COMException) {
            // Clipboard might be locked by another process
        }
        catch (ThreadStateException) {
            // Thread not in STA mode
        }
        catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"Error processing clipboard: {ex.Message}");
        }
    }

    private void SetupFileWatcher() {
        if (_settings == null || string.IsNullOrEmpty(_settings.SelectedProfile.LogFilePath)) {
            System.Diagnostics.Debug.WriteLine("[DEBUG_LOG] Log file path is empty, not starting watcher.");
            return;
        }

        try {
            var fullPath = _settings.SelectedProfile.LogFilePath;
            
            // Basic path validation
            if (fullPath.Length >= 260) {
                System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] Log file path is too long: {fullPath}");
                return;
            }

            var directory = Path.GetDirectoryName(fullPath);
            var fileName = Path.GetFileName(fullPath);

            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName)) {
                System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] Invalid path or filename: {directory} / {fileName}");
                return;
            }

            if (!Directory.Exists(directory)) {
                System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] Directory does not exist: {directory}");
                // We don't create the game's log directory, but we should be ready if it appears
                // FileSystemWatcher can only be created for an existing directory.
                return;
            }

            _fileWatcher = new FileSystemWatcher(directory, fileName);
            _fileWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime;
            _fileWatcher.Changed += OnFileChanged;
            _fileWatcher.Created += OnFileChanged;
            _fileWatcher.Deleted += OnFileDeleted;
            _fileWatcher.Renamed += (s, e) => OnFileChanged(s, e);
            _fileWatcher.Error += (s, e) => {
                System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] FileWatcher error: {e.GetException().Message}");
            };
            _fileWatcher.EnableRaisingEvents = true;

            InitializeFilePosition();
            System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] FileWatcher setup for: {fullPath}");
        }
        catch (UnauthorizedAccessException) {
            System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] Access denied to log directory.");
        }
        catch (ArgumentException ex) {
            System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] Invalid log path argument: {ex.Message}");
        }
        catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] Error setting up file watcher: {ex.Message}");
        }
    }

    private void InitializeFilePosition() {
        if (_settings == null) return;
        lock (_fileLock) {
            try {
                if (File.Exists(_settings.SelectedProfile.LogFilePath)) {
                    using (var stream = new FileStream(_settings.SelectedProfile.LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                        _lastFilePosition = stream.Length;
                        System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] Initial file position: {_lastFilePosition}");
                    }
                } else {
                    _lastFilePosition = 0;
                }
            }
            catch (IOException ex) {
                System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] IOException during initialization: {ex.Message}");
                _lastFilePosition = 0;
            }
            catch (UnauthorizedAccessException) {
                System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] Access denied during initialization.");
                _lastFilePosition = 0;
            }
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e) {
        if (_settings == null) return;
        System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] File changed: {e.ChangeType} - {e.FullPath}");
        lock (_fileLock) {
            if (!File.Exists(e.FullPath)) {
                System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] File does not exist: {e.FullPath}");
                return;
            }

            try {
                using (var stream = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                    if (stream.Length < _lastFilePosition) {
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
                                    System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] Log parsed: {coordinates}");
                                    lastMatch = coordinates;
                                }
                            }

                            if (lastMatch != null) {
                                LocationUpdated?.Invoke(this, lastMatch);
                            }
                            _lastFilePosition = stream.Position;
                        }
                    }
                }
            }
            catch (IOException ex) {
                System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] IOException reading log file: {ex.Message}");
            }
            catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] Error reading log file: {ex.Message}");
            }
        }
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e) {
        lock (_fileLock) {
            _lastFilePosition = 0;
        }
    }

    public void Dispose() {
        Stop();
    }
}