// MMONavigator 
// Copyright (C) 2026 John Rigsby
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using MMONavigator.Helpers;
using MMONavigator.Models;
using MMONavigator.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

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
        var hwnd = new WindowInteropHelper(this).Handle;
        _viewModel.StartWatcher(hwnd);
    }
    
    private void Stop() {
        _viewModel.StopWatcher();
    }

    //control how the window reacts to being clicked, specifically preventing the window from
    //taking "focus" (becoming the active foreground window) in certain scenarios.
    //I want activity in this window to not steal focus from say another program like my gaming application.
    //So I can click but keep typing elsewhere.
    // private IntPtr HwndHandler(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled) {
    //     // 1. Handle the specific message you need to monitor
    //     if (msg == NativeMethods.WM_CLIPBOARDUPDATE) {
    //         KeepOnTop();
    //         _viewModel.HandleClipboardUpdate();
    //     }
    //     
    //     // 2. Everything else is ignored by this handler and passed through 
    //     // to the default WPF window procedure.
    //     handled = false;
    //     
    //     return IntPtr.Zero;
    // }
    
    private IntPtr HwndHandler(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled) {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE) {
            // 1. Kick the heavy lifting and clipboard reading off the UI thread immediately.
            // We use a fire-and-forget task so this handler returns instantly.
            _ = ProcessClipboardAsync();
        
            // If KeepOnTop() absolutely MUST run on clipboard updates, 
            // let's also defer it slightly so it doesn't fight the game's render cycle.
            _ = DeferKeepOnTopAsync();
        }
    
        handled = false;
        return IntPtr.Zero;
    }
    
    private async Task ProcessClipboardAsync() {
        // Give the game 50-75ms to finish its macro write and close its clipboard handle
        await Task.Delay(75);

        try {
            // Access the ViewModel safely. If HandleClipboardUpdate reads the clipboard,
            // make sure it is wrapped in a try/catch for COMException just in case!
            _viewModel.HandleClipboardUpdate();
        }
        catch (System.Runtime.InteropServices.COMException) {
            // If a collision still happens, your app catches it gracefully 
            // instead of locking up the OS clipboard chain.
        }
    }
    
    private async Task DeferKeepOnTopAsync() {
        await Task.Delay(10); // Tiny pause to let the OS breathe
        KeepOnTop();
    }

    private void KeepOnTop() {
        // SWP_NOACTIVATE is the key here. It prevents your window 
        // from stealing focus from the game/media player.
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, 
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }
    
    // protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    // {
    //     base.OnPreviewMouseDown(e);
    //
    //     // Check if the click is outside the popup
    //     if (LocationPopup.IsOpen && !LocationPopup.IsMouseOver)
    //     {
    //         // Force the popup to close
    //         LocationPopup.IsOpen = false;
    //     
    //         // Ensure the click passes through to the game if desired
    //         // You may need a Native Win32 call if the hit-test is blocked
    //     }
    // }
    
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
        Close_Popup(sender, e);
    }

    private void HideShowTimers_Click(object sender, RoutedEventArgs e) {
        ToggleTimers();
        Close_Popup(sender, e);
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
        licensebutton.Visibility = Visibility.Visible;
        closebutton.Visibility = Visibility.Visible;
    }

    private void TitleBar_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) {
        settingsbutton.Visibility = Visibility.Collapsed;
        timerbutton.Visibility = Visibility.Collapsed;
        togglebutton.Visibility = Visibility.Collapsed;
        licensebutton.Visibility = Visibility.Collapsed;
        closebutton.Visibility = Visibility.Collapsed;
    }
    
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) {
        if (WindowState != WindowState.Minimized) {
            WindowState = WindowState.Minimized;
        }
    }
    
    private void ToggleVisibityButton_Click(object sender, RoutedEventArgs e) {
        Close_Popup(sender, e);
        _viewModel.MainContentVisibility=!_viewModel.MainContentVisibility;
        togglebutton.ToolTip = _viewModel.MainContentVisibility ? "Hide Directions" : "Show Directions";
    }
    
    private void LicenseButton_Click(object sender, RoutedEventArgs e) {
        Close_Popup(sender, e);
        _viewModel.MainContentVisibility=!_viewModel.MainContentVisibility;
    }
    
    
    
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) {
        if (WindowState != WindowState.Normal) {
            WindowState = WindowState.Normal;
        }
    }

    protected override void OnClosed(EventArgs e) {
        // try {
        //     _viewModel.SaveSettings();
        // }
        // catch {
        //     // ignored
        // }
        try {
            _viewModel.StopWatcher();
        }
        catch {
            // ignored
        }
        try {
            HwndSource.FromHwnd(_hwnd)?.RemoveHook(HwndHandler);
        }
        catch {
            // ignored
        }
        try {
            base.OnClosed(e);
        }
        catch {
            // ignored
        }
    }
    
    protected override void OnClosing(CancelEventArgs e)
    {
        _viewModel.SaveSettings(); 
        
        base.OnClosing(e);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) {
        Close_Popup(sender, e);
        Hide();
        // Close the window after a tiny delay so the UI loop finishes 
        // processing the 'Hide' message before the OS-level 'Close' message.
        Dispatcher.BeginInvoke(new Action(() => {
            Close();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }
    
    #endregion
    
    private void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (myGrid.DataContext is MainViewModel vm)
        {
            // Simply flip the state
            vm.IsExpanded = !vm.IsExpanded;
            System.Diagnostics.Debug.WriteLine($"ToggleButton_Click");
        }
    }
    
    private void Close_Popup(object sender, RoutedEventArgs e)
    {
        if (myGrid.DataContext is MainViewModel vm)
        {
            // Simply flip the state
            vm.IsExpanded = false;
            System.Diagnostics.Debug.WriteLine($"TitleBar_MouseLeftButtonUp");
        }
    }
    
    private void LocationPopup_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 1. Force the entire app to the foreground so WPF wakes up
        NativeMethods.SetForegroundWindow(_hwnd);

        // 2. Clear focus from whatever might be blocking
        Keyboard.ClearFocus();

        // 3. Force focus specifically to the TreeView
        LocationTree.Focus();
    
        // 4. Force a re-evaluation of the binding if it feels "stuck"
        // Using CommandManager causes WPF to re-evaluate all CanExecute/Bindings
        CommandManager.InvalidateRequerySuggested();
    }
}