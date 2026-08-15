using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MMONavigator.Controls;

namespace MMONavigator.Views;

public sealed partial class DestinationDialog : ChildWindow {
    public string Answer => InputTextBox?.Text ?? string.Empty;
    public string Group => GroupTextBox?.Text ?? string.Empty;

    private ObservableCollection<string>? _groups;
    public ObservableCollection<string>? Groups {
        get => _groups;
        set => SetField(ref _groups, value);
    }

    private string? _selectedGroup;
    public string? SelectedGroup {
        get => _selectedGroup;
        set => SetField(ref _selectedGroup, value);
    }
    
    public DestinationDialog(string? defaultAnswer = "", string? defaultGroup = "", List<string>? groups = null) {
        InitializeComponent();
        DataContext = this;

        InputTextBox.Text = defaultAnswer ?? string.Empty;
        GroupTextBox.Text = defaultGroup ?? string.Empty;

        Groups = groups != null 
            ? new ObservableCollection<string>(groups) 
            : new ObservableCollection<string>();

        // Defer focus and text selection until the control layout pass completes
        Loaded -= OnDialogLoaded;
        Loaded += OnDialogLoaded;
    }

    private void OnDialogLoaded(object sender, RoutedEventArgs e) {
        Loaded -= OnDialogLoaded;

        try {
            Dispatcher.BeginInvoke(new Action(() => {
                InputTextBox.Focus();
                InputTextBox.SelectAll();
            }), DispatcherPriority.Loaded);
        }
        catch (Exception ex) {
            Log.Warning(ex, "Failed to set focus on InputTextBox during DestinationDialog load.");
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) {
        try {
            IsConfirmed = true;
            ManualDialogResult = true;
            
            // Inherited from ChildWindow
            SafeCloseDialog();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling OkButton_Click in DestinationDialog.");
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) {
        try {
            IsConfirmed = false;
            ManualDialogResult = false;
            
            // Inherited from ChildWindow
            SafeCloseDialog();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling CancelButton_Click in DestinationDialog.");
        }
    }
}