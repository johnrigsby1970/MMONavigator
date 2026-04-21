using MMONavigator.ViewModels;

namespace MMONavigator.Helpers;

using System.Windows;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

public static class ClickThroughBehavior {
    // Win32 constants
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(ClickThroughBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(UIElement element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(UIElement element) => (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is UIElement element) {
            if ((bool)e.NewValue) {
                element.GotFocus += HandleGotFocus;
                element.LostFocus += HandleLostFocus;
                // Re-add this specifically for Map/Non-Focusable areas
                element.PreviewMouseDown += HandleGotFocus;
                element.MouseEnter += HandleMouseEnter;
                element.MouseLeave += HandleMouseLeave;
            }
            else {
                element.GotFocus -= HandleGotFocus;
                element.LostFocus -= HandleLostFocus;// Re-add this specifically for Map/Non-Focusable areas
                element.PreviewMouseDown -= HandleGotFocus;
                element.MouseEnter -= HandleMouseEnter;
                element.MouseLeave -= HandleMouseLeave;
            }
        }
    }
    private static void HandleMouseEnter(object sender, MouseEventArgs e)
    {
        // When the mouse enters the element, disable the "Click-Through"
        // so the window becomes interactive again.
        if (sender is UIElement element)
        {
            SetClickThroughStyle(element, false);
        }
    }

    private static void HandleMouseLeave(object sender, MouseEventArgs e)
    {
        // When the mouse leaves the area, re-enable the "Click-Through"
        // so the window becomes "ghost-like" again.
        if (sender is UIElement element)
        {
            SetClickThroughStyle(element, true);
        }
    }
    
    private static void SetClickThroughStyle(UIElement element, bool enableClickThrough)
    {
        var window = Window.GetWindow(element);
        if (window == null) return;

        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

        if (enableClickThrough)
        {
            // Add the WS_EX_NOACTIVATE style
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_NOACTIVATE);
        }
        else
        {
            // Remove the WS_EX_NOACTIVATE style
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_NOACTIVATE);
        }
    }

    private static void HandleGotFocus(object sender, RoutedEventArgs e) {
        var element = (UIElement)sender;
        var window = Window.GetWindow(element);
        if (window == null) return;

        var hwnd = new WindowInteropHelper(window).Handle;
        int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

        // Remove the NOACTIVATE style to allow interaction
        SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_NOACTIVATE);
        window.Activate();
    }

    private static void HandleLostFocus(object sender, RoutedEventArgs e) {
        if (sender is FrameworkElement fe) {
            // Define a timer to delay the style application. 
            //Adding a short delay (e.g., 50–100ms) in HandleLostFocus prevents "flicker."
            //It gives the WPF focus-change cycle a moment to settle so you don't accidentally
            //re-enable "Click-Through" mode while the user is simply moving the cursor
            //between two controls that implement this same logic (textbox and expander of a combobox).
            DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };

            timer.Tick += (s, args) => {
                timer.Stop();
                
                // Safety Check: Is focus still inside this window?
                var window = Window.GetWindow(fe);
                if (window == null) return;
            
                // If the user clicked another control in the SAME window, 
                // don't turn on click-through.
                if (FocusManager.GetFocusedElement(window) != null) return;
                
                // Because DataContext is inherited, fe.DataContext will correctly 
                // point to your _viewModel even if it's set on the parent Grid.
                if (fe.DataContext is MainViewModel vm) {
                    IntPtr hwnd = new WindowInteropHelper(window).Handle;
                    int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

                    // Directly using your logic
                    if (vm.Settings.KeyboardClickThrough) {
                        SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_NOACTIVATE);
                    }
                    else {
                        SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_NOACTIVATE);
                    }
                }
            };

            timer.Start();
        }
    }
    
    public static void ForceBackgroundFocus(IntPtr backgroundHwnd)
    {
        // Explicitly set focus back to the target application
        if (backgroundHwnd != IntPtr.Zero)
        {
            Helpers.NativeMethods.SetForegroundWindow(backgroundHwnd);
        }
    }
}