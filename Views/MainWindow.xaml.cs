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

        _viewModel.StartWatcher(_windowHandle);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(MainViewModel.Settings)) {
            _viewModel.Settings.PropertyChanged += Settings_PropertyChanged;
            _viewModel.StartWatcher(_windowHandle);
        }
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(AppSettings.WatchMode) || 
            e.PropertyName == nameof(AppSettings.LogFilePath) ||
            e.PropertyName == nameof(AppSettings.LogFileRegex)) {
            _viewModel.StartWatcher(_windowHandle);
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

    private void Stop() {
        _viewModel.StopWatcher();
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
            _viewModel.HandleClipboardUpdate();
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
        _viewModel.StopWatcher();
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