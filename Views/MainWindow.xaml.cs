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
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
    private IntPtr _hwnd;

    public IntPtr GetWindowHandle() => new WindowInteropHelper(this).Handle;

    public const double StandardRowHeight = 30;
    private const double CollapsedRowHeight = 0;
    public static GridLength StandardGridRowHeight => new GridLength(StandardRowHeight);
    private static readonly GridLength HiddenRowHeight = new GridLength(CollapsedRowHeight);

    public MainWindow() {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        myGrid.DataContext = _viewModel;

        Topmost = true;
        Deactivated += MainWindow_Deactivated;

        Top = 0;
        Left = (SystemParameters.PrimaryScreenWidth / 2) - (Width / 2);

        // Initialize state and apply it immediately
        _viewModel.ShowSettings = _viewModel.Settings.ShowSettings;
        ApplySettingsVisibility();

        _viewModel.ShowTimers = _viewModel.Settings.ShowTimers;
        ApplyTimersVisibility();

        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Attach initial settings handler safely
        if (_viewModel.Settings != null) {
            _viewModel.Settings.PropertyChanged -= Settings_PropertyChanged;
            _viewModel.Settings.PropertyChanged += Settings_PropertyChanged;
        }
    }
    
    #region Premium Features
    
    private void EnterCode_Click(object sender, RoutedEventArgs e)
    {
        // Simple input dialog or custom text prompt implementation
        // Once you get the string from the user:
        // var viewModel = DataContext as MainViewModel;
        // viewModel?.ValidateAndApplyOverrideCode(userInput);
    }
    
    #endregion
    
    
    #region Location Controls & Tree Interactivity

    private void CoordinateTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) {
        if (e.Key == Key.Enter) {
            if (sender is System.Windows.Controls.TextBox textBox) {
                // Force the binding to update immediately
                var bindingExpression = textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty);
                bindingExpression?.UpdateSource();

                // Optional: Remove focus or move focus away so it acts like a submission
                Keyboard.ClearFocus();
            }
            e.Handled = true; // Prevents sound dings
        }
    }
    
    private void LocationTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        try {
            if (e.OriginalSource is DependencyObject clickedObj) {
                var toggleButton = FindParent<System.Windows.Controls.Primitives.ToggleButton>(clickedObj);
                if (toggleButton != null) return; // Standard folder expander arrow clicked

                var treeViewItem = FindParent<TreeViewItem>(clickedObj);
                if (treeViewItem?.DataContext is LocationItem item) {
                    // If it's a Parent/Folder node:
                    if (item.Items != null && item.Items.Count > 0) {
                        treeViewItem.IsExpanded = !treeViewItem.IsExpanded;
                        treeViewItem.IsSelected = false;
                        Keyboard.ClearFocus();
                        e.Handled = true;
                        return;
                    }

                    // Destination Leaf Clicked
                    if (myGrid.DataContext is MainViewModel vm) {
                        vm.SelectedLocation = item;
                        vm.IsExpanded = false;
                        e.Handled = true;
                    }
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in LocationTree_PreviewMouseLeftButtonDown.");
        }
    }
    private void HideLocHint_Click(object sender, RoutedEventArgs e) {
        try {
            if (DataContext is MainViewModel vm) {
                vm.HideLocHint = true;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error hiding location hint.");
        }
    }
    
    private void LocationTree_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) {
        try {
            if (myGrid.DataContext is not MainViewModel vm) return;
    
            if (e.Key == Key.Enter || e.Key == Key.Space) {
                if (sender is System.Windows.Controls.TreeView treeView && treeView.SelectedItem is LocationItem item) {
                    if (item.Items == null || item.Items.Count == 0) { // Destination Leaf
                        vm.SelectedLocation = item;
                        vm.IsExpanded = false;
                        e.Handled = true;
                    }
                }
            }
            else if (e.Key == Key.Escape) {
                vm.IsExpanded = false;
                e.Handled = true;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling LocationTree_KeyDown.");
        }
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject {
        DependencyObject parentObject = VisualTreeHelper.GetParent(child);
        if (parentObject == null) return null;
        if (parentObject is T parent) return parent;
        return FindParent<T>(parentObject);
    }

    #endregion

    protected override void OnSourceInitialized(EventArgs e) {
        base.OnSourceInitialized(e);

        try {
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
        catch (Exception ex) {
            Log.Error(ex, "Failed to complete OnSourceInitialized in MainWindow.");
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(MainViewModel.Settings)) {
            // Unhook old subscription if possible, then hook new settings reference
            _viewModel.Settings.PropertyChanged -= Settings_PropertyChanged;
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
        try {
            var hwnd = new WindowInteropHelper(this).Handle;
            _viewModel.StartWatcher(hwnd);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error starting location watcher.");
        }
    }

    private void Stop() {
        try {
            _viewModel.StopWatcher();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error stopping location watcher.");
        }
    }
    
    private const int WM_NCACTIVATE = 0x0086;
    
    private IntPtr HwndHandler(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled) {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE) {
            //_ = ProcessClipboardAsync(); //DELETEME
            _ = DeferKeepOnTopAsync();
        }
        else if (msg == WM_NCACTIVATE) {
            // When wparam is FALSE, the window is losing focus. Force HWND_TOPMOST!
            if (wparam == IntPtr.Zero) {
                Dispatcher.BeginInvoke(new Action(KeepOnTop), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        return IntPtr.Zero;
    }

    //DELETEME
    // private async Task ProcessClipboardAsync() {
    //     // Give the game 50-75ms to finish its macro write and close its clipboard handle
    //     await Task.Delay(75);
    //
    //     try {
    //         // Access the ViewModel safely. If HandleClipboardUpdate reads the clipboard,
    //         // make sure it is wrapped in a try/catch for COMException just in case!
    //         _viewModel.HandleClipboardUpdate();
    //     }
    //     catch (COMException ex) {
    //         // Log at Debug level so clipboard collisions during game updates don't flood Sentry as errors
    //         Log.Debug(ex, "Clipboard access collision occurred (COMException). Continuing gracefully.");
    //     }
    //     catch (Exception ex) {
    //         Log.Error(ex, "Unexpected error processing clipboard update.");
    //     }
    // }

    private async Task DeferKeepOnTopAsync() {
        await Task.Delay(10); // Tiny pause to let the OS breathe
        KeepOnTop();
    }

    private void KeepOnTop() {
        try {
            // SWP_NOACTIVATE is the key here. It prevents your window 
            // from stealing focus from the game/media player.
            var hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
        catch (Exception ex) {
            Log.Warning(ex, "Failed to apply SWP_NOACTIVATE in KeepOnTop.");
        }
    }
    
    private void MainWindow_Deactivated(object? sender, EventArgs e) {
        try {
            if (_viewModel != null && _viewModel.IsExpanded) {
                _viewModel.IsExpanded = false;
            }
            KeepOnTop();
        }
        catch (Exception ex) {
            Log.Warning(ex, "Error in MainWindow_Deactivated.");
        }
    }

    #region Title Bar Commands & Interactions

    //https://stackoverflow.com/questions/55447212/how-do-i-make-a-transparent-wpf-window-with-the-default-title-bar-functionality

    private void ConfigureWatcher_Click(object sender, RoutedEventArgs e) {
        try {
            var dialog = new WatcherConfigurationDialog(_viewModel.Settings) {
                Owner = this
            };
            dialog.ShowDialog();

            // Check your manual property instead of the built-in DialogResult
            if (dialog.ManualDialogResult == true) {
                Log.Information("Watcher configuration dialog saved. Reloading profile settings.");

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
        catch (Exception ex) {
            Log.Error(ex, "Error occurred during Watcher Configuration dialog interaction.");
        }
    }

    private void HideShowSettings_Click(object sender, RoutedEventArgs e) {
        try {
            ToggleSettings();
            Close_Popup(sender, e);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error toggling settings visibility.");
        }
    }

    private void HideShowTimers_Click(object sender, RoutedEventArgs e) {
        try {
            ToggleTimers();
            Close_Popup(sender, e);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error toggling timers visibility.");
        }
    }

    private void ToggleSettings() {
        _viewModel.ShowSettings = !_viewModel.ShowSettings;
        _viewModel.Settings.ShowSettings = _viewModel.ShowSettings;
        ApplySettingsVisibility();
    }

    private void ApplySettingsVisibility() { }

    private void ToggleTimers() {
        _viewModel.ShowTimers = !_viewModel.ShowTimers;
        _viewModel.Settings.ShowTimers = _viewModel.ShowTimers;
        ApplyTimersVisibility();
    }

    private void ApplyTimersVisibility() { }

    private void TitleBar_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) {
        try {
            settingsbutton.Visibility = Visibility.Visible;
            timerbutton.Visibility = Visibility.Visible;
            togglebutton.Visibility = Visibility.Visible;
            licensebutton.Visibility = Visibility.Visible;
            closebutton.Visibility = Visibility.Visible;
            coffebutton.Visibility = Visibility.Visible;
        }
        catch (Exception ex) {
            Log.Warning(ex, "Error showing title bar buttons on MouseEnter.");
        }
    }

    private void TitleBar_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) {
        try {
            settingsbutton.Visibility = Visibility.Collapsed;
            timerbutton.Visibility = Visibility.Collapsed;
            togglebutton.Visibility = Visibility.Collapsed;
            licensebutton.Visibility = Visibility.Collapsed;
            closebutton.Visibility = Visibility.Collapsed;
            coffebutton.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) {
            Log.Warning(ex, "Error hiding title bar buttons on MouseLeave.");
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) {
        try {
            if (WindowState != WindowState.Minimized) {
                WindowState = WindowState.Minimized;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error minimizing window.");
        }
    }

    private void ToggleVisibityButton_Click(object sender, RoutedEventArgs e) {
        try {
            Close_Popup(sender, e);
            _viewModel.MainContentVisibility = !_viewModel.MainContentVisibility;
            togglebutton.ToolTip = _viewModel.MainContentVisibility ? "Hide Directions" : "Show Directions";
        }
        catch (Exception ex) {
            Log.Error(ex, "Error toggling main content visibility.");
        }
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) {
        try {
            if (WindowState != WindowState.Normal) {
                WindowState = WindowState.Normal;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error maximizing window.");
        }
    }

    protected override void OnClosing(CancelEventArgs e) {
        try {
            _viewModel.SaveSettings();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error saving settings on window closing.");
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e) {
        try {
            _viewModel.StopWatcher();
        }
        catch (Exception ex) {
            Log.Warning(ex, "Exception while stopping watcher during shutdown.");
        }

        try {
            if (_hwnd != IntPtr.Zero) {
                HwndSource.FromHwnd(_hwnd)?.RemoveHook(HwndHandler);
            }
        }
        catch (Exception ex) {
            Log.Warning(ex, "Exception while removing HwndSource hook during shutdown.");
        }

        try {
            base.OnClosed(e);
        }
        catch (Exception ex) {
            Log.Warning(ex, "Exception in base.OnClosed execution.");
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) {
        try {
            Close_Popup(sender, e);
            Hide();
            // Close the window after a tiny delay so the UI loop finishes 
            // processing the 'Hide' message before the OS-level 'Close' message.
            Dispatcher.BeginInvoke(new Action(() => { Close(); }),
                System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing window close sequence.");
        }
    }

    #endregion

    private void ToggleButton_Click(object sender, RoutedEventArgs e) {
        try {
            if (myGrid.DataContext is MainViewModel vm) {
                vm.IsExpanded = !vm.IsExpanded;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error toggling expanded state.");
        }
    }

    private void Close_Popup(object sender, RoutedEventArgs e) {
        try {
            if (myGrid.DataContext is MainViewModel vm) {
                vm.IsExpanded = false;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error closing popup.");
        }
    }
}