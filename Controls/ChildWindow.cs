using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using MMONavigator.Helpers;

namespace MMONavigator.Controls;

public class ChildWindow : Window, INotifyPropertyChanged {
    private HwndSource? _hwndSource;
    private DispatcherTimer? _gracePeriodTimer;
    private IntPtr _hwnd;
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
        set {
            if (_hoverTrackDisabled != value) {
                _hoverTrackDisabled = value;
                Log.Debug("Hover tracking state changed. HoverTrackDisabled: {Disabled}", _hoverTrackDisabled);
                OnPropertyChanged();
            }
        }
    }

    private void InitializeTimer() {
        try {
            // 1. Stop and unhook existing timer if re-initialized
            if (_gracePeriodTimer != null) {
                _gracePeriodTimer.Stop();
                _gracePeriodTimer.Tick -= GracePeriodTimer_Tick;
                _gracePeriodTimer = null;
            }
            
            _gracePeriodTimer = new DispatcherTimer {
                Interval = TimeSpan.FromMilliseconds(GraceDurationInMilliSeconds)
            };
            _gracePeriodTimer.Tick += GracePeriodTimer_Tick;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error initializing grace period timer in ChildWindow.");
        }
    }

    private void GracePeriodTimer_Tick(object? sender, EventArgs e) {
        try {
            _gracePeriodTimer?.Stop();
            HoverTrackDisabled = false;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing GracePeriodTimer_Tick in ChildWindow.");
        }
    }

    public void PauseHoverTracking() {
        try {
            if (!HoverTrackDisabled) {
                HoverTrackDisabled = true;
                _gracePeriodTimer?.Stop();
                _gracePeriodTimer?.Start();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error pausing hover tracking in ChildWindow.");
        }
    }

    protected override void OnSourceInitialized(EventArgs e) {
        base.OnSourceInitialized(e);

        try {
            _hwnd = new WindowInteropHelper(this).Handle;
            InitializeTimer();

            if (_hwnd != IntPtr.Zero) {
                // Apply NOACTIVATE style
                int extendedStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, extendedStyle | NativeMethods.WS_EX_NOACTIVATE);

                _hwndSource = HwndSource.FromHwnd(_hwnd);
                _hwndSource?.AddHook(HwndHandler);
            }
            else {
                Log.Warning("ChildWindow OnSourceInitialized: HWND handle is zero.");
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error during OnSourceInitialized in ChildWindow.");
        }
    }

    protected virtual void OnBeforeCleanup() {
        // Default is empty. Child classes can override this.
    }

    /// <summary>
    /// Executes safe teardown by detaching focus and severing the 
    /// Win32 parent handle link before WPF destroys the window.
    /// </summary>
    public void SafeCloseDialog() {
        try {
            // 1. Detach focus and caret selection from active text controls
            FocusManager.SetFocusedElement(this, null);
            Keyboard.ClearFocus();

            // 2. Sever the native Win32 owner/parent link at the OS level
            var helper = new WindowInteropHelper(this);
            if (helper.Handle != IntPtr.Zero) {
                NativeMethods.SetWindowLong(helper.Handle, NativeMethods.GWL_HWNDPARENT, 0);
            }

            // 3. Hide to clear WPF modal state
            Hide();

            // 4. Defer Close() to the background UI tick
            Dispatcher.BeginInvoke(new Action(() => {
                try {
                    Close();
                }
                catch (Exception ex) {
                    Log.Error(ex, "Error executing deferred Close in ChildWindow.");
                }
            }), DispatcherPriority.Background);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing SafeCloseDialog in ChildWindow.");
        }
    }

    protected override void OnClosing(CancelEventArgs e) {
        try {
            // 1. Call the "Hook" for child-specific logic
            OnBeforeCleanup();

            // 2. Detach focus and caret selection
            FocusManager.SetFocusedElement(this, null);
            Keyboard.ClearFocus();

            // 3. Sever native Win32 owner link to prevent DoDialogHide NREs
            var helper = new WindowInteropHelper(this);
            if (helper.Handle != IntPtr.Zero) {
                NativeMethods.SetWindowLong(helper.Handle, NativeMethods.GWL_HWNDPARENT, 0);
            }

            // 4. Win32 hook cleanup logic
            if (_hwndSource != null) {
                _hwndSource.RemoveHook(HwndHandler);
                _hwndSource.Dispose();
                _hwndSource = null;
            }

            // 5. Disable "No Activate" style for clean exit
            if (_hwnd != IntPtr.Zero) {
                int style = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, style & ~NativeMethods.WS_EX_NOACTIVATE);
            }

            // 6. Timer cleanup
            if (_gracePeriodTimer != null) {
                _gracePeriodTimer.Stop();
                _gracePeriodTimer.Tick -= GracePeriodTimer_Tick;
                _gracePeriodTimer = null;
            }

            if (ManualDialogResult == null) {
                ManualDialogResult = false;
            }
        }
        catch (Exception ex) {
            Log.Warning(ex, "Error during ChildWindow OnClosing cleanup.");
        }
        finally {
            base.OnClosing(e);
        }
    }
    
    public bool IsConfirmed { get; set; }
    

    //control how the window reacts to being clicked, specifically preventing the window from
    //taking "focus" (becoming the active foreground window) in certain scenarios.
    //I want activity in this window to not steal focus from another program like my gaming application.
    //So I can click but keep typing elsewhere.
    protected virtual IntPtr HwndHandler(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled) {
        try {
            // WM_MOUSEACTIVATE = 0x0021
            if (msg == NativeMethods.WM_MOUSEACTIVATE) {
                //This message is sent by Windows to a window that is currently inactive when the user clicks the mouse
                //inside it. The OS is essentially asking the window: "The user just clicked you; do you want to
                //become the active window?"
            
                //Only return MA_NOACTIVATE if we are NOT in the middle of a dialog.
                //MA_NOACTIVATE tells the operating system to not activate (bring to the foreground/focus) a window when a user
                //clicks inside it, but to still process the mouse click (e.g., clicking a button inside that window still works)
                if (!IsDialogActive) {
                    handled = true;
                    return NativeMethods.MA_NOACTIVATE;
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in ChildWindow HwndHandler message processing.");
        }

        return IntPtr.Zero;
    }

    // Ensure style does NOT include WS_EX_TRANSPARENT
    public void AddNoActivateStyle() {
        try {
            EnsureWindowHandle();
            if (_hwnd != IntPtr.Zero) {
                int style = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
                // ONLY add NOACTIVATE
                NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, style | NativeMethods.WS_EX_NOACTIVATE);
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error adding WS_EX_NOACTIVATE style.");
        }
    }

    public void RemoveNoActivateStyle() {
        try {
            EnsureWindowHandle();
            if (_hwnd != IntPtr.Zero) {
                int style = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
                // Remove only NOACTIVATE
                NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, style & ~NativeMethods.WS_EX_NOACTIVATE);
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error removing WS_EX_NOACTIVATE style.");
        }
    }

    private void EnsureWindowHandle() {
        if (_hwnd == IntPtr.Zero) {
            _hwnd = new WindowInteropHelper(this).Handle;
        }
    }

    //because a window might be transparent or otherwise set to ignore
    //certain events, sometimes we need to fake a legal owner window  
    public static void ConfigureDialogToHaveAValidOwner(Window owner, out Window helperWindow) {
        if (owner == null) {
            throw new ArgumentNullException(nameof(owner));
        }

        try {
            helperWindow = new Window {
                Width = 0,
                Height = 0,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                Opacity = 0,
                Topmost = owner.Topmost,
                Left = owner.Left,
                Top = owner.Top
            };
            helperWindow.Show();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in ConfigureDialogToHaveAValidOwner.");
            throw;
        }
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