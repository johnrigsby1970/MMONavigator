using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using MMONavigator.Controls;

namespace MMONavigator.Views;

public sealed partial class DestinationDialog : ChildWindow, INotifyPropertyChanged {
    public string Answer => InputTextBox.Text;
    public string Group => GroupTextBox.Text;

    public DestinationDialog(string? defaultAnswer = "", string? defaultGroup = "", List<string>? groups = null) {
        InitializeComponent();
        DataContext = this;
        InputTextBox.Text = defaultAnswer??"";
        GroupTextBox.Text = defaultGroup;

        Groups = new ObservableCollection<string>();
        if (groups != null) {
            foreach (var group in groups) {
                Groups.Add(group);
            }
        }
        
        InputTextBox.Focus();
        InputTextBox.SelectAll();
    }

    private ObservableCollection<string> _groups;
    public ObservableCollection<string> Groups {
        get => _groups;
        set => SetField(ref _groups, value);
    }
    
    private string _selectedGroup;
    public string SelectedGroup {
        get => _selectedGroup;
        set => SetField(ref _selectedGroup, value);
    }
    
    public bool IsConfirmed { get; private set; }
    
    private void OkButton_Click(object sender, RoutedEventArgs e) {
        //See notes.txt
        IsConfirmed = true;
        ManualDialogResult = true;
        Hide(); 
        
        // Close the window after a tiny delay so the UI loop finishes 
        // processing the 'Hide' message before the OS-level 'Close' message.
        Dispatcher.BeginInvoke(new Action(() => {
            Close();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) {
        //See notes.txt
        IsConfirmed = false;
        ManualDialogResult = false;
        Hide(); 

        // Close the window after a tiny delay so the UI loop finishes 
        // processing the 'Hide' message before the OS-level 'Close' message.
        Dispatcher.BeginInvoke(new Action(() => {
            Close();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
