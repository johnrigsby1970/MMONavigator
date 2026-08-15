using System.Windows;
using System.Windows.Input;

namespace MMONavigator.Helpers;

public static class DragWindowBehavior {
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled", 
            typeof(bool), 
            typeof(DragWindowBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(UIElement element) {
        if (element == null) throw new ArgumentNullException(nameof(element));
        return (bool)element.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(UIElement element, bool value) {
        if (element == null) throw new ArgumentNullException(nameof(element));
        element.SetValue(IsEnabledProperty, value);
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is not UIElement element) return;

        try {
            bool newValue = (bool)e.NewValue;

            if (newValue) {
                element.PreviewMouseLeftButtonDown -= HandleMouseLeftButtonDown;
                element.PreviewMouseLeftButtonDown += HandleMouseLeftButtonDown;
                Log.Debug("DragWindowBehavior attached to {ElementType}.", element.GetType().Name);
            }
            else {
                element.PreviewMouseLeftButtonDown -= HandleMouseLeftButtonDown;
                Log.Debug("DragWindowBehavior detached from {ElementType}.", element.GetType().Name);
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling IsEnabledChanged in DragWindowBehavior.");
        }
    }

    private static void HandleMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (sender is not UIElement element) return;

        try {
            var window = Window.GetWindow(element);
            if (window == null) {
                Log.Warning("DragWindowBehavior: Unable to locate parent Window for element {ElementType}.", element.GetType().Name);
                return;
            }

            // Ensure we only initiate drag when clicking primaryMouseButton directly
            if (e.ButtonState == MouseButtonState.Pressed) {
                Log.Verbose("Initiating DragMove for window '{Title}'.", window.Title);
                
                // Native WPF DragMove hands off window movement directly to the OS window manager loop,
                // eliminating high-CPU DispatcherTimer polling loops and WinForms Cursor dependencies.
                window.DragMove();
            }
        }
        catch (InvalidOperationException) {
            // DragMove throws an InvalidOperationException if called when the left mouse button isn't down.
            Log.Debug("DragMove invoked outside of valid mouse button press state.");
        }
        catch (Exception ex) {
            Log.Error(ex, "Unexpected error executing DragWindowBehavior on element.");
        }
    }
}