using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using MMONavigator.Helpers;

namespace MMONavigator.Controls;

public class ChildWindow : Window, INotifyPropertyChanged {
    private HwndSource? _hwndSource;

    private DispatcherTimer? _gracePeriodTimer;
    
    private IntPtr _hwnd; // Cache the handle
    private const int GraceDurationInMilliSeconds = 3000;
    
    // A custom field to hold the result because of the way these windows are loaded DialogResult is lost
    public bool? ManualDialogResult { get; set; }

    private bool _isDialogActive;

    public bool IsDialogActive {
        get => _isDialogActive;
        set {
            if (_isDialogActive != value) {
                if (!value) {
                    PauseHoverTracking();
                }
                
                _isDialogActive = value;
                OnPropertyChanged();
            }
        }
    }
    
    private bool _hoverTrackDisabled;

    public bool HoverTrackDisabled {
        get => _hoverTrackDisabled;
        set { _hoverTrackDisabled = value;
            if (_hoverTrackDisabled) {
                Debug.WriteLine("Hover tracking disabled");
            }
            if (!_hoverTrackDisabled) {
                Debug.WriteLine("Hover tracking enabled");
            }
            OnPropertyChanged();
        }
    }
    
    // Give user time to return to hovering after using a dialog
    private void InitializeTimer()
    {
        _gracePeriodTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(GraceDurationInMilliSeconds) // The "Grace" duration
        };
        // ReSharper disable twice UnusedParameter.Local
        _gracePeriodTimer.Tick += (s, e) => 
        {
            _gracePeriodTimer.Stop();
            HoverTrackDisabled = false;
        };
    }

    public void PauseHoverTracking() {
        if (!HoverTrackDisabled) {
            HoverTrackDisabled = true;
            _gracePeriodTimer?.Stop();
            _gracePeriodTimer?.Start();
        }
    }
    
    protected override void OnSourceInitialized(EventArgs e) {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        InitializeTimer();
        // Apply NOACTIVATE style
        int extendedStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, extendedStyle | NativeMethods.WS_EX_NOACTIVATE);

        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(HwndHandler);
    }

    protected virtual void OnBeforeCleanup() 
    {
        // Default is empty. Child classes can override this.
    }
    
    protected override void OnClosing(CancelEventArgs e) {
        // 1. Call the "Hook" for child-specific logic
        OnBeforeCleanup();
        
        // 2. Win32 cleanup logic
        _hwndSource?.RemoveHook(HwndHandler);
        _hwndSource?.Dispose();
        _hwndSource = null;

        // 2. Disable "No Activate" style for clean exit
        int style = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, style & ~NativeMethods.WS_EX_NOACTIVATE);

        _gracePeriodTimer?.Stop();
        
        base.OnClosing(e);
    }

    //control how the window reacts to being clicked, specifically preventing the window from
    //taking "focus" (becoming the active foreground window) in certain scenarios.
    //I want activity in this window to not steal focus from another program like my gaming application.
    //So I can click but keep typing elsewhere.
    protected virtual IntPtr HwndHandler(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled) {
        // WM_MOUSEACTIVATE = 0x0021
        if (msg == NativeMethods.WM_MOUSEACTIVATE) {
            //This message is sent by Windows to a window that is currently inactive when the user clicks the mouse
            //inside it. The OS is essentially asking the window: "The user just clicked you; do you want to
            //become the active window?"
            
            //Only return MA_NOACTIVATE if we are NOT in the middle of a dialog.
            //MA_NOACTIVATE tells the operating system to not activate (bring to the foreground/focus) a window when a user
            //clicks inside it, but to still process the mouse click (e.g., clicking a button inside that window still works)
            if (!IsDialogActive) {
                handled = true; //This tells the WPF/Win32 messaging system that you have manually dealt with this message, and it shouldn't be passed further down the chain.
                return NativeMethods.MA_NOACTIVATE; //This is the "magic" return value. It tells Windows: "Process the mouse click so buttons still work, but do not bring this window to the front or give it keyboard focus."
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
    
    //because a window might be transparent or otherwise set to ignore
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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}