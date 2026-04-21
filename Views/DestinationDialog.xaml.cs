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
// // Instead of: DialogResult = true;
//     
//         // Dispatch to the UI thread's next available tick
//         Dispatcher.BeginInvoke(new Action(() => {
//             this.DialogResult = true;
//         }), System.Windows.Threading.DispatcherPriority.Background);
// Clear the owner before closing. 
        // This stops WPF's internal focus-tracking from trying to "return" focus 
        // to the owner, which is what triggers the crash.
        //this.Owner = null; 
    
        this.IsConfirmed = true;
        this.ManualDialogResult = true;
// 3. Instead of this.Close(), hide the window first to remove it from 
        // the UI thread's "modal" tracking before the hard-close occurs.
        this.Hide(); 
    
        // 4. Close the window after a tiny delay so the UI loop finishes 
        // processing the 'Hide' message before the OS-level 'Close' message.
        Dispatcher.BeginInvoke(new Action(() => {
            this.Close();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) {
// // Instead of: DialogResult = false;
//     
//         // Dispatch to the UI thread's next available tick
//         Dispatcher.BeginInvoke(new Action(() => {
//             this.DialogResult = false;
//         }), System.Windows.Threading.DispatcherPriority.Background);

// Clear the owner before closing. 
        // This stops WPF's internal focus-tracking from trying to "return" focus 
        // to the owner, which is what triggers the crash.
        //this.Owner = null; 
    
        this.IsConfirmed = false;
        this.ManualDialogResult = false;
// 3. Instead of this.Close(), hide the window first to remove it from 
        // the UI thread's "modal" tracking before the hard-close occurs.
        this.Hide(); 
    
        // 4. Close the window after a tiny delay so the UI loop finishes 
        // processing the 'Hide' message before the OS-level 'Close' message.
        Dispatcher.BeginInvoke(new Action(() => {
            this.Close();
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
