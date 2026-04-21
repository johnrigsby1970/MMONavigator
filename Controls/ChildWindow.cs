using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using MMONavigator.Helpers;

namespace MMONavigator.Controls;

public class ChildWindow : Window {
    private HwndSource? _hwndSource;

    private IntPtr _hwnd; // Cache the handle

    // A custom field to hold the result
    public bool? ManualDialogResult { get; set; }

    public bool IsDialogActive { get; set; }

    protected override void OnSourceInitialized(EventArgs e) {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // Apply NOACTIVATE style
        int extendedStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, extendedStyle | NativeMethods.WS_EX_NOACTIVATE);

        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(HwndHandler);
    }

    protected override void OnClosing(CancelEventArgs e) {
        // 1. Clean up hookg
        _hwndSource?.RemoveHook(HwndHandler);
        _hwndSource?.Dispose();
        _hwndSource = null;

        // 2. Disable "No Activate" style for clean exit
        int style = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, style & ~NativeMethods.WS_EX_NOACTIVATE);

        base.OnClosing(e);
    }

    protected virtual IntPtr HwndHandler(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled) {
        // WM_MOUSEACTIVATE = 0x0021
        if (msg == 0x0021) {
            // Only return MA_NOACTIVATE if we are NOT in the middle of a dialog
            if (!IsDialogActive) {
                handled = true;
                return (IntPtr)3;
            }
        }

        return IntPtr.Zero;
    }

    // Ensure style does NOT include WS_EX_TRANSPARENT
    public void AddNoActivateStyle() {
        int style = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        // ONLY add NOACTIVATE
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, style | NativeMethods.WS_EX_NOACTIVATE);
    }
    
    public void RemoveNoActivateStyle() {
        int style = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        // Remove only NOACTIVATE
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, style & ~NativeMethods.WS_EX_NOACTIVATE);
    }

    // private void PreviewMouseDown(object sender, MouseButtonEventArgs e) {
    //     // When the user clicks the textbox, remove the NOACTIVATE 
    //     // so the box can accept keyboard input
    //     RemoveNoActivateStyle();
    // }
    //
    // private void LostFocus(object sender, RoutedEventArgs e) {
    //     // When the user clicks away, put the NOACTIVATE back
    //     AddNoActivateStyle();
    // }
    
    //because a window might be transaparent or otherwise set to ignore
    //certain events, sometimes we need to fake a legal owner window  
    public static void ConfigureDialogToHaveAValidOwner(Window owner, out Window helperWindow) {
        helperWindow = new Window {
            Width = 0, Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Opacity = 0,
            Topmost = owner.Topmost // Match the owner's topmost state
        };
        helperWindow.Show();
    }
}