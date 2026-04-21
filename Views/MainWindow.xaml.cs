using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using MMONavigator.Helpers;
using MMONavigator.Models;
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
public partial class MainWindow : Window, IWindowHandleProvider {
    private readonly MainViewModel _viewModel;
    public IntPtr GetWindowHandle() => new WindowInteropHelper(this).Handle;
    public const double StandardRowHeight = 30;
    private const double CollapsedRowHeight = 0;
    public static GridLength StandardGridRowHeight => new GridLength(StandardRowHeight);
    private static readonly GridLength HiddenRowHeight = new GridLength(CollapsedRowHeight);
    private IntPtr _hwnd;
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        
        _hwnd = new WindowInteropHelper(this).Handle;
        _viewModel.InitializeWindow(_hwnd);
        
        // Only keep this if you have other HwndSource hooks (like your Clipboard handler)
        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(HwndHandler);
        StartWatcher();
        // You no longer need to call UpdateKeyboardClickThrough() here 
        // because the behavior will handle the activation state 
        // as soon as the user interacts with a control.
    }

    public MainWindow() {
        InitializeComponent();
        _viewModel = new MainViewModel();
        myGrid.DataContext = _viewModel;

        Topmost = true;
        Deactivated += (s, e) => KeepOnTop();

        Top = 0; //SystemParameters.PrimaryScreenHeight;
        Left = (SystemParameters.PrimaryScreenWidth / 2) - (Width / 2);

        // Initialize state and apply it immediately
        _viewModel.ShowSettings = _viewModel.Settings.ShowSettings;
        ApplySettingsVisibility();

        _viewModel.ShowTimers = _viewModel.Settings.ShowTimers;
        ApplyTimersVisibility();

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.Settings.PropertyChanged += Settings_PropertyChanged;

        //StartWatcher();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(MainViewModel.Settings)) {
            _viewModel.Settings.PropertyChanged += Settings_PropertyChanged;
            StartWatcher();
        }
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(AppSettings.LastSelectedProfileName)) {
            StartWatcher();
        }
    }

    private void StartWatcher() {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        _viewModel.StartWatcher(hwnd);
    }
    
    private void Stop() {
        _viewModel.StopWatcher();
    }

    private IntPtr HwndHandler(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled) {
        // 1. Handle the specific message you need to monitor
        if (msg == WM_CLIPBOARDUPDATE) {
            KeepOnTop();
            _viewModel.HandleClipboardUpdate();
        }
        
        // 2. Everything else is ignored by this handler and passed through 
        // to the default WPF window procedure.
        handled = false;
        return IntPtr.Zero;
    }

    private void KeepOnTop() {
        // SWP_NOACTIVATE is the key here. It prevents your window 
        // from stealing focus from the game/media player.
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, 
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }
    
    #region Title Bar

    //https://stackoverflow.com/questions/55447212/how-do-i-make-a-transparent-wpf-window-with-the-default-title-bar-functionality
    
    private void ConfigureWatcher_Click(object sender, RoutedEventArgs e) {
        var dialog = new WatcherConfigurationDialog(_viewModel.Settings);
        dialog.Owner = this;
        dialog.ShowDialog(); 

        // Check your manual property instead of the built-in DialogResult
        if (dialog.ManualDialogResult == true)
        {
            System.Diagnostics.Debug.WriteLine("[DEBUG_LOG] Watcher configuration dialog OK");
            
            // The dialog now manages profiles and settings directly on the passed _viewModel.Settings
            // We just need to ensure they are saved and the view model is notified of potential changes
            // that might not have fired PropertyChanged yet (though they should have).
            
            _viewModel.SaveSettings();
            _viewModel.LoadLocations();
            _viewModel.UpdateListStatus();
            
            // Force a refresh of the watcher just in case
            _viewModel.StartWatcher(new WindowInteropHelper(this).Handle);
        }
    }

    private void HideShowSettings_Click(object sender, RoutedEventArgs e) {
        ToggleSettings();
    }

    private void HideShowTimers_Click(object sender, RoutedEventArgs e) {
        ToggleTimers();
    }

    private void ToggleSettings() {
        _viewModel.ShowSettings = !_viewModel.ShowSettings;
        _viewModel.Settings.ShowSettings = _viewModel.ShowSettings;
        ApplySettingsVisibility();
    }

    private void ApplySettingsVisibility() {
        // GridLength height = _viewModel.ShowSettings ? StandardGridRowHeight : HiddenRowHeight;
        // destinationRow.Height = height;
        // targetRow.Height = height;
        // settingsRow.Height = height;
    }

    private void ToggleTimers() {
        _viewModel.ShowTimers = !_viewModel.ShowTimers;
        _viewModel.Settings.ShowTimers = _viewModel.ShowTimers;
        ApplyTimersVisibility();
    }

    private void ApplyTimersVisibility() {
        //timerRow.Height = _viewModel.ShowTimers ? StandardGridRowHeight : HiddenRowHeight;
    }

    private void TitleBar_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) {
        settingsbutton.Visibility = Visibility.Visible;
        timerbutton.Visibility = Visibility.Visible;
        togglebutton.Visibility = Visibility.Visible;
        //minbutton.Visibility = Visibility.Visible;
        closebutton.Visibility = Visibility.Visible;
    }

    private void TitleBar_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) {
        settingsbutton.Visibility = Visibility.Collapsed;
        timerbutton.Visibility = Visibility.Collapsed;
        togglebutton.Visibility = Visibility.Collapsed;
        closebutton.Visibility = Visibility.Collapsed;
    }
    
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) {
        if (WindowState != WindowState.Minimized) {
            WindowState = WindowState.Minimized;
        }
    }
    
    private void ToggleVisibityButton_Click(object sender, RoutedEventArgs e) {
        _viewModel.MainContentVisibility=!_viewModel.MainContentVisibility;
        togglebutton.ToolTip = _viewModel.MainContentVisibility ? "Hide Directions" : "Show Directions";
    }
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) {
        if (WindowState != WindowState.Normal) {
            WindowState = WindowState.Normal;
        }
    }

    protected override void OnClosed(EventArgs e) {
        try {
            _viewModel.SaveSettings();
        }
        finally{}
        try {
            _viewModel.StopWatcher();
        }
        finally{}
        try {
            HwndSource.FromHwnd(_hwnd)?.RemoveHook(HwndHandler);
        }
        finally{}
        // try {
        //     IntPtr hwnd = new WindowInteropHelper(this).Handle;
        //     HwndSource.FromHwnd(hwnd)?.RemoveHook(HwndHandler);
        // }
        // finally{}
        try {
            base.OnClosed(e);
        }
        finally{}
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) {
        Hide();
        Dispatcher.BeginInvoke(new Action(() => {
            this.Close();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }
    // private HwndSource _popupHwndSource;
    // private void Popup_Opened(object sender, EventArgs e)
    // {
    //     if (sender is Popup popup && popup.Child != null)
    //     {
    //         var hwnd = ((HwndSource)PresentationSource.FromVisual(popup.Child)).Handle;
    //     
    //         // 1. Hook the popup's message loop so we can intercept mouse clicks
    //         _popupHwndSource = HwndSource.FromHwnd(hwnd);
    //         _popupHwndSource.AddHook(PopupHwndHandler);
    //     }
    // }
    //
    // private IntPtr PopupHwndHandler(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled)
    // {
    //     // WM_MOUSEACTIVATE = 0x0021
    //     if (msg == 0x0021)
    //     {
    //         handled = true;
    //         // MA_NOACTIVATE (3): Allow the mouse click, but do NOT take focus
    //         return (IntPtr)3; 
    //     }
    //     return IntPtr.Zero;
    // }
    //
    // private void Popup_Closed(object sender, EventArgs e)
    // {
    //     // Clean up the hook so we don't leak memory
    //     _popupHwndSource?.RemoveHook(PopupHwndHandler);
    //     _popupHwndSource = null;
    // }
    //
    // private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    // {
    //     // If the popup is open, intercept the arrow keys
    //     if (LocationPopup.IsOpen)
    //     {
    //         if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Enter)
    //         {
    //             // Manually route the key to the TreeView
    //             var treeView = LocationPopup.Child as ExtendedTreeView;
    //         
    //             // Create a new KeyEventArgs to 'replay' the key press into the tree
    //             var routedEvent = Keyboard.KeyDownEvent;
    //             var newArgs = new System.Windows.Input.KeyEventArgs(e.KeyboardDevice, e.InputSource, e.Timestamp, e.Key) 
    //             { 
    //                 RoutedEvent = routedEvent 
    //             };
    //         
    //             treeView.RaiseEvent(newArgs);
    //         
    //             // Mark as handled so the game doesn't see the arrow keys
    //             e.Handled = true;
    //         }
    //     }
    // }
    #endregion
}