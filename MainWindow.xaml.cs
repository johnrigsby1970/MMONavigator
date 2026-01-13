using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using System.Media;

namespace MMONavigator;

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

        Start();
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

    protected virtual void OnClipboardUpdate() {
        try {
            var text = Clipboard.GetText();
            if (text.Length > Scrubber.MaxLength) return;

            if (Scrubber.TryParse(text, _viewModel.Settings.CoordinateOrder, out _)) {
                _viewModel.CurrentCoordinates = Scrubber.ScrubEntry(text) ?? string.Empty;
            }
        }
        catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"Error processing clipboard: {ex.Message}");
            _viewModel.GoDirection = string.Empty;
        }
    }

    private void Start() {
        NativeMethods.AddClipboardFormatListener(_windowHandle);
        KeepOnTop();
    }

    private void KeepOnTop() {
        NativeMethods.SetWindowPos(_windowHandle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private void Stop() {
        NativeMethods.RemoveClipboardFormatListener(_windowHandle);
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