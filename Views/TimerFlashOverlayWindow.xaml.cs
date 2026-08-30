using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using MMONavigator.Helpers;

namespace MMONavigator.Views;

public partial class TimerFlashOverlayWindow : Window {
    public TimerFlashOverlayWindow(Rect monitorBounds) {
        InitializeComponent();

        // Fit overlay exactly to the target monitor's full screen dimensions
        Left = monitorBounds.Left;
        Top = monitorBounds.Top;
        Width = monitorBounds.Width;
        Height = monitorBounds.Height;

        Loaded += TimerFlashOverlayWindow_Loaded;
    }

    private void TimerFlashOverlayWindow_Loaded(object sender, RoutedEventArgs e) {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) {
            int extendedStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            // WS_EX_TRANSPARENT + WS_EX_NOACTIVATE ensures zero input capture or game focus stealing
            extendedStyle |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE;
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, extendedStyle);
        }

        if (Resources["FlashFadeStoryboard"] is Storyboard sb) {
            sb.Begin();
        }
    }

    private void Storyboard_Completed(object? sender, EventArgs e) {
        Close();
    }

    /// <summary>
    /// Spawns a full-screen flash on the monitor hosting targetWindow.
    /// </summary>
    public static void ShowFlash(Window targetWindow) {
        if (targetWindow == null) return;

        targetWindow.Dispatcher.Invoke(() => {
            // Get the screen hosting targetWindow using Win32 monitor functions
            var hwnd = new WindowInteropHelper(targetWindow).Handle;
            var monitorBounds = GetTargetMonitorBounds(hwnd, targetWindow);

            var flashOverlay = new TimerFlashOverlayWindow(monitorBounds);
            flashOverlay.Show();
        });
    }

    private static Rect GetTargetMonitorBounds(IntPtr hwnd, Window targetWindow) {
        try {
            // Fetch monitor handle for the host window
            IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var monitorInfo = new NativeMethods.MONITORINFO();
            monitorInfo.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(monitorInfo);

            if (NativeMethods.GetMonitorInfo(monitor, ref monitorInfo)) {
                // Convert Physical Pixel screen dimensions to WPF Logical Units (DPI scale)
                PresentationSource source = PresentationSource.FromVisual(targetWindow);
                double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

                var rc = monitorInfo.rcMonitor;
                return new Rect(
                    rc.Left / dpiX,
                    rc.Top / dpiY,
                    (rc.Right - rc.Left) / dpiX,
                    (rc.Bottom - rc.Top) / dpiY
                );
            }
        }
        catch (Exception ex) {
            Log.Warning(ex, "Failed to resolve target monitor bounds; defaulting to Virtual Screen.");
        }

        // Fallback: Default to primary virtual screen bounds
        return new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight
        );
    }
}