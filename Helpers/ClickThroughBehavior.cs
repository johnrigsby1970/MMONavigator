using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using MMONavigator.ViewModels;

namespace MMONavigator.Helpers;

public static class ClickThroughBehavior {
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled", 
            typeof(bool), 
            typeof(ClickThroughBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(UIElement element, bool value) {
        if (element == null) throw new ArgumentNullException(nameof(element));
        element.SetValue(IsEnabledProperty, value);
    }

    public static bool GetIsEnabled(UIElement element) {
        if (element == null) throw new ArgumentNullException(nameof(element));
        return (bool)element.GetValue(IsEnabledProperty);
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is not UIElement element) return;

        try {
            bool newValue = (bool)e.NewValue;

            if (newValue) {
                // RoutedEventHandler (object, RoutedEventArgs)
                
                element.GotFocus -= HandleGotFocus;
                element.LostFocus -= HandleLostFocus;
                element.GotFocus += HandleGotFocus;
                element.LostFocus += HandleLostFocus;

                // MouseEventHandler (object, MouseEventArgs)
                element.MouseEnter -= HandleMouseEnter;
                element.MouseLeave -= HandleMouseLeave;
                element.MouseEnter += HandleMouseEnter;
                element.MouseLeave += HandleMouseLeave;

                // MouseButtonEventHandler (object, MouseButtonEventArgs)
                element.PreviewMouseDown -= HandlePreviewMouseDown;
                element.PreviewMouseDown += HandlePreviewMouseDown;

                Log.Debug("ClickThroughBehavior attached to {ElementType}.", element.GetType().Name);
            }
            else {
                element.GotFocus -= HandleGotFocus;
                element.LostFocus -= HandleLostFocus;
                element.MouseEnter -= HandleMouseEnter;
                element.MouseLeave -= HandleMouseLeave;
                element.PreviewMouseDown -= HandlePreviewMouseDown;

                Log.Debug("ClickThroughBehavior detached from {ElementType}.", element.GetType().Name);
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling IsEnabledChanged in ClickThroughBehavior.");
        }
    }

    private static void HandleMouseEnter(object sender, System.Windows.Input.MouseEventArgs e) {
        // When the mouse enters the element, disable the "Click-Through"
        // so the window becomes interactive again.
        if (sender is UIElement element) {
            SetClickThroughStyle(element, enableClickThrough: false);
        }
    }

    private static void HandleMouseLeave(object sender, System.Windows.Input.MouseEventArgs e) {
        if (sender is UIElement element) {
            SetClickThroughStyle(element, enableClickThrough: true);
        }
    }

    private static void HandlePreviewMouseDown(object sender, MouseButtonEventArgs e) {
        // When the mouse leaves the area, re-enable the "Click-Through"
        // so the window becomes "ghost-like" again.
        if (sender is UIElement element) {
            HandleGotFocus(element, e);
        }
    }

    private static void SetClickThroughStyle(UIElement element, bool enableClickThrough) {
        try {
            var window = Window.GetWindow(element);
            if (window == null) return;

            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int extendedStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);

            if (enableClickThrough) {
                // Add the WS_EX_NOACTIVATE style
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, extendedStyle | NativeMethods.WS_EX_NOACTIVATE);
            }
            else {
                // Remove the WS_EX_NOACTIVATE style
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, extendedStyle & ~NativeMethods.WS_EX_NOACTIVATE);
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error setting click-through style (Enable={Enable}).", enableClickThrough);
        }
    }

    private static void HandleGotFocus(object sender, RoutedEventArgs e) {
        try {
            if (sender is not UIElement element) return;
            var window = Window.GetWindow(element);
            if (window == null) return;

            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int extendedStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);

            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, extendedStyle & ~NativeMethods.WS_EX_NOACTIVATE);
            window.Activate();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling GotFocus in ClickThroughBehavior.");
        }
    }

    private static void HandleLostFocus(object sender, RoutedEventArgs e) {
        if (sender is not FrameworkElement fe) return;

        try {
            //Define a timer to delay the style application. 
            //Adding a short delay (e.g., 50–100ms) in HandleLostFocus prevents "flicker."
            //It gives the WPF focus-change cycle a moment to settle so you don't accidentally
            //re-enable "Click-Through" mode while the user is simply moving the cursor
            //between two controls that implement this same logic (textbox and expander of a combobox).
            DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };

            EventHandler tickHandler = null!;
            tickHandler = (s, args) => {
                timer.Tick -= tickHandler; // Unhook listener to avoid Dispatcher memory leaks
                timer.Stop();

                try {
                    var window = Window.GetWindow(fe);
                    if (window == null) return;

                    // Safety Check: Is focus still inside this window?
                    if (FocusManager.GetFocusedElement(window) != null) return;

                    if (fe.DataContext is MainViewModel vm) {
                        IntPtr hwnd = new WindowInteropHelper(window).Handle;
                        if (hwnd == IntPtr.Zero) return;

                        int extendedStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);

                        if (vm.Settings.KeyboardClickThrough) {
                            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, extendedStyle | NativeMethods.WS_EX_NOACTIVATE);
                        }
                        else {
                            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, extendedStyle & ~NativeMethods.WS_EX_NOACTIVATE);
                        }
                    }
                }
                catch (Exception ex) {
                    Log.Error(ex, "Error processing delayed LostFocus click-through update.");
                }
            };

            timer.Tick += tickHandler;
            timer.Start();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error initializing LostFocus timer in ClickThroughBehavior.");
        }
    }

    public static void ForceBackgroundFocus(IntPtr backgroundHwnd) {
        try {
            if (backgroundHwnd != IntPtr.Zero) {
                NativeMethods.SetForegroundWindow(backgroundHwnd);
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error setting foreground window to HWND {Hwnd}.", backgroundHwnd);
        }
    }
}