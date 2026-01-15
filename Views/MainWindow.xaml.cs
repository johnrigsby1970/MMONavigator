using System.ComponentModel;
using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using System.IO;
using MMONavigator.Models;
using MMONavigator.Services;
using MMONavigator.ViewModels;

namespace MMONavigator.Views;

//https://stackoverflow.com/questions/21461017/wpf-window-with-transparent-background-containing-opaque-controls
//https://stackoverflow.com/questions/55447212/how-do-i-make-a-transparent-wpf-window-with-the-default-title-bar-functionality
//https://corey255a1.wixsite.com/wundervision/single-post/simple-wpf-compass-control
//https://learn.microsoft.com/en-us/windows/apps/design/style/segoe-ui-symbol-font
//https://stackoverflow.com/questions/2842667/how-to-create-a-semi-transparent-window-in-wpf-that-allows-mouse-events-to-pass

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window {
    private readonly MainViewModel _viewModel;

    public const double StandardRowHeight = 30;
    private const double CollapsedRowHeight = 0;
    public static GridLength StandardGridRowHeight => new GridLength(StandardRowHeight);
    private static readonly GridLength HiddenRowHeight = new GridLength(CollapsedRowHeight);
    
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private readonly IntPtr _windowHandle;

    public event EventHandler? ClipboardUpdate;

    private FileSystemWatcher? _fileWatcher;
    private long _lastFilePosition = 0;
    private readonly object _fileLock = new object();

    public MainWindow() {
        InitializeComponent();
        _viewModel = new MainViewModel();
        myGrid.DataContext = _viewModel;

        Topmost = true;
        Deactivated += (s, e) => KeepOnTop();
        
        Top = 0; //SystemParameters.PrimaryScreenHeight;
        Left = (SystemParameters.PrimaryScreenWidth / 2) - (Width / 2);

        // Initialize state and apply it immediately
        _showSettings = _viewModel.Settings.ShowSettings; 
        ApplySettingsVisibility();

        _showTimers = _viewModel.Settings.ShowTimers;
        ApplyTimersVisibility();

        _windowHandle = new WindowInteropHelper(this).EnsureHandle();
        HwndSource? source = HwndSource.FromHwnd(_windowHandle);
        source?.AddHook(HwndHandler);

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.Settings.PropertyChanged += Settings_PropertyChanged;

        Start();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(MainViewModel.Settings)) {
            System.Diagnostics.Debug.WriteLine("[DEBUG_LOG] ViewModel.Settings changed, re-subscribing");
            _viewModel.Settings.PropertyChanged += Settings_PropertyChanged;
            Start();
        }
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] MainWindow.Settings_PropertyChanged: {e.PropertyName}");
        if (e.PropertyName == nameof(AppSettings.WatchMode) || 
            e.PropertyName == nameof(AppSettings.LogFilePath) ||
            e.PropertyName == nameof(AppSettings.LogFileRegex)) {
            Start();
        }
    }

    private bool _showSettings;
    public bool ShowSettings {
        get => _showSettings;
        set => _showSettings = value;
    }

    private bool _showTimers;
    public bool ShowTimers {
        get => _showTimers;
        set => _showTimers = value;
    }

    private void OnClipboardUpdate() {
        if (_viewModel.Settings.WatchMode != WatchMode.Clipboard) return;
        try {
            var text = Clipboard.GetText();
            if (string.IsNullOrEmpty(text)) return;
            if (text.Length > Scrubber.MaxLength) return;

            if (Scrubber.TryParse(text, _viewModel.Settings.CoordinateOrder, out _)) {
                _viewModel.CurrentCoordinates = Scrubber.ScrubEntry(text) ?? string.Empty;
                System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] Clipboard updated: {_viewModel.CurrentCoordinates}");
            }
        }
        catch (COMException) {
            // Clipboard might be locked by another process
        }
        catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"Error processing clipboard: {ex.Message}");
            _viewModel.GoDirection = string.Empty;
        }
    }

    private void Start() {
        System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] Starting watcher, Mode: {_viewModel.Settings.WatchMode}");
        Stop();
        if (_viewModel.Settings.WatchMode == WatchMode.Clipboard) {
            NativeMethods.AddClipboardFormatListener(_windowHandle);
        } else {
            SetupFileWatcher();
        }
        KeepOnTop();
    }

    private void SetupFileWatcher() {
        if (string.IsNullOrEmpty(_viewModel.Settings.LogFilePath)) {
            System.Diagnostics.Debug.WriteLine("[DEBUG_LOG] Log file path is empty, not starting watcher.");
            return;
        }

        try {
            string? directory = Path.GetDirectoryName(_viewModel.Settings.LogFilePath);
            string? fileName = Path.GetFileName(_viewModel.Settings.LogFilePath);

            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName)) {
                System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] Invalid path or filename: {directory} / {fileName}");
                return;
            }

            if (!Directory.Exists(directory)) {
                System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] Directory does not exist: {directory}");
                return;
            }

            _fileWatcher = new FileSystemWatcher(directory, fileName);
            _fileWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime;
            _fileWatcher.Changed += OnFileChanged;
            _fileWatcher.Created += OnFileChanged;
            _fileWatcher.Deleted += OnFileDeleted;
            _fileWatcher.Renamed += (s, e) => OnFileChanged(s, e);
            _fileWatcher.EnableRaisingEvents = true;

            // Initial read to end
            InitializeFilePosition();
            System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] FileWatcher setup for: {_viewModel.Settings.LogFilePath}");
        }
        catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] Error setting up file watcher: {ex.Message}");
        }
    }

    private void InitializeFilePosition() {
        lock (_fileLock) {
            if (File.Exists(_viewModel.Settings.LogFilePath)) {
                using (var stream = new FileStream(_viewModel.Settings.LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                    _lastFilePosition = stream.Length;
                    System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] Initial file position: {_lastFilePosition}");
                }
            } else {
                _lastFilePosition = 0;
            }
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e) {
        System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] File changed: {e.ChangeType} - {e.FullPath}");
        lock (_fileLock) {
            if (!File.Exists(e.FullPath)) {
                System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] File does not exist: {e.FullPath}");
                return;
            }

            try {
                using (var stream = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                    System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] Stream length: {stream.Length}, Last position: {_lastFilePosition}");
                    if (stream.Length < _lastFilePosition) {
                        // File was probably truncated or replaced
                        System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] File truncated. Resetting position.");
                        _lastFilePosition = 0;
                    }

                    if (stream.Length > _lastFilePosition) {
                        stream.Position = _lastFilePosition;
                        using (var reader = new StreamReader(stream)) {
                            string? line;
                            string? lastMatch = null;
                            while ((line = reader.ReadLine()) != null) {
                                if (string.IsNullOrWhiteSpace(line)) continue;
                                if (LogParser.TryParseLogLine(line, _viewModel.Settings.LogFileRegex, out string coordinates)) {
                                    lastMatch = coordinates;
                                }
                            }

                            if (lastMatch != null) {
                                System.Diagnostics.Debug.WriteLine($"[DEBUG_LOG] Updating coordinates to: {lastMatch}");
                                Dispatcher.Invoke(() => {
                                    _viewModel.CurrentCoordinates = lastMatch;
                                });
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

    private void Stop() {
        System.Diagnostics.Debug.WriteLine("[DEBUG_LOG] Stopping watcher");
        NativeMethods.RemoveClipboardFormatListener(_windowHandle);
        if (_fileWatcher != null) {
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Dispose();
            _fileWatcher = null;
        }
    }

    private void KeepOnTop() {
        // SWP_NOACTIVATE is the key here. It prevents your window 
        // from stealing focus from the game/media player.
        NativeMethods.SetWindowPos(_windowHandle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, 
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private IntPtr HwndHandler(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled) {
        if (msg == WM_CLIPBOARDUPDATE) {
            KeepOnTop();
            // fire event
            ClipboardUpdate?.Invoke(this, new EventArgs());

            // call virtual method
            OnClipboardUpdate();
        }

        handled = false;
        return IntPtr.Zero;
    }


    private static class NativeMethods {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
    }

    #region Title Bar

    //https://stackoverflow.com/questions/55447212/how-do-i-make-a-transparent-wpf-window-with-the-default-title-bar-functionality
    
    private void ConfigureWatcher_Click(object sender, RoutedEventArgs e) {
        var dialog = new WatcherConfigurationDialog(_viewModel.Settings);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true) {
            System.Diagnostics.Debug.WriteLine("[DEBUG_LOG] Watcher configuration dialog OK");
            
            // Suspend property changed handling during multiple updates if needed,
            // but here we want each change to potentially trigger a restart if they are individual.
            // Actually, setting multiple properties on the same Settings object will fire multiple events.
            _viewModel.Settings.WatchMode = dialog.WatchMode;
            _viewModel.Settings.LogFilePath = dialog.LogFilePath;
            _viewModel.Settings.LogFileRegex = dialog.LogFileRegex;
            _viewModel.Settings.CoordinateOrder = dialog.CoordinateOrder;
            _viewModel.Settings.CoordinateSystem = dialog.CoordinateSystem;
            
            _viewModel.SaveSettings();
        }
    }

    private void HideShowSettings_Click(object sender, RoutedEventArgs e) {
        ToggleSettings();
    }

    private void HideShowTimers_Click(object sender, RoutedEventArgs e) {
        ToggleTimers();
    }

    private void ToggleSettings() {
        ShowSettings = !ShowSettings;
        _viewModel.Settings.ShowSettings = ShowSettings;
        ApplySettingsVisibility();
    }

    private void ApplySettingsVisibility() {
        GridLength height = ShowSettings ? StandardGridRowHeight : HiddenRowHeight;
        destinationRow.Height = height;
        targetRow.Height = height;
        settingsRow.Height = height;
    }

    private void ToggleTimers() {
        ShowTimers = !ShowTimers;
        _viewModel.Settings.ShowTimers = ShowTimers;
        ApplyTimersVisibility();
    }

    private void ApplyTimersVisibility() {
        timerRow.Height = ShowTimers ? StandardGridRowHeight : HiddenRowHeight;
    }

    private void TitleBar_MouseEnter(object sender, MouseEventArgs e) {
        settingsbutton.Visibility = Visibility.Visible;
        timerbutton.Visibility = Visibility.Visible;
        minbutton.Visibility = Visibility.Visible;
        closebutton.Visibility = Visibility.Visible;
    }

    private void TitleBar_MouseLeave(object sender, MouseEventArgs e) {
        settingsbutton.Visibility = Visibility.Collapsed;
        timerbutton.Visibility = Visibility.Collapsed;
        minbutton.Visibility = Visibility.Collapsed;
        closebutton.Visibility = Visibility.Collapsed;
    }
    
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) {
        if (WindowState != WindowState.Minimized) {
            WindowState = WindowState.Minimized;
        }
    }
    
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) {
        if (WindowState != WindowState.Normal) {
            WindowState = WindowState.Normal;
        }
    }

    protected override void OnClosed(EventArgs e) {
        _viewModel.SaveSettings();
        HwndSource.FromHwnd(_windowHandle)?.RemoveHook(HwndHandler);
        Stop();
        base.OnClosed(e);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) {
        Close();
    }

    private void Rectangle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        this.DragMove();
    }

    #endregion
}