using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace MMONavigator.Helpers;

public static class DragWindowBehavior
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(DragWindowBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));
    
    public static bool GetIsEnabled(UIElement element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(UIElement element, bool value) => element.SetValue(IsEnabledProperty, value);
    
    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element && (bool)e.NewValue)
        {
            if ((bool)e.NewValue)
            {
                // Use Preview event instead of standard bubbling event
                element.PreviewMouseLeftButtonDown += HandleMouseLeftButtonDown;
                //element.PreviewMouseMove += HandleMouseMove;
                element.PreviewMouseLeftButtonUp += HandleMouseLeftButtonUp;
            }
            else
            {
                element.PreviewMouseLeftButtonDown -= HandleMouseLeftButtonDown;
                //element.PreviewMouseMove -= HandleMouseMove;
                element.PreviewMouseLeftButtonUp -= HandleMouseLeftButtonUp;
            }
        }
    }
    private static System.Windows.Point _mouseOffset;
    private static bool _isDragging;
    private static System.Drawing.Point _lastMousePos;
    private static System.Windows.Threading.DispatcherTimer _dragTimer;

    private static void HandleMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var element = (UIElement)sender;
        var window = Window.GetWindow(element);
    
        _isDragging = true;
        _lastMousePos = System.Windows.Forms.Cursor.Position;
    
        // Create a high-frequency timer for the drag
        _dragTimer = new System.Windows.Threading.DispatcherTimer();
        _dragTimer.Interval = TimeSpan.FromMilliseconds(1); // Run as fast as possible
        _dragTimer.Tick += (s, args) => PerformDrag(window);
        _dragTimer.Start();
    }

    private static void PerformDrag(Window window)
    {
        // If the left button is released, stop immediately
        if (System.Windows.Forms.Control.MouseButtons != System.Windows.Forms.MouseButtons.Left)
        {
            _dragTimer.Stop();
            return;
        }

        System.Drawing.Point currentMousePos = System.Windows.Forms.Cursor.Position;
    
        var source = System.Windows.PresentationSource.FromVisual(window);
        double dpiX = source.CompositionTarget.TransformToDevice.M11;
        double dpiY = source.CompositionTarget.TransformToDevice.M22;

        double deltaX = (currentMousePos.X - _lastMousePos.X) / dpiX;
        double deltaY = (currentMousePos.Y - _lastMousePos.Y) / dpiY;

        window.Left += deltaX;
        window.Top += deltaY;

        _lastMousePos = currentMousePos;
    }

    private static void HandleMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragTimer != null)
        {
            _dragTimer.Stop();
            _dragTimer = null;
        }
    }
}