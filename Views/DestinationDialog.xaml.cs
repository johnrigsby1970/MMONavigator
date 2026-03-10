using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace MMONavigator.Views;

public partial class DestinationDialog : Window, INotifyPropertyChanged {
    public string Answer => InputTextBox.Text;
    public string Group => GroupTextBox.Text;

    public DestinationDialog(string defaultAnswer = "", string defaultGroup = "", List<string>? groups = null) {
        InitializeComponent();
        DataContext = this;
        InputTextBox.Text = defaultAnswer;
        GroupTextBox.Text = defaultGroup;

        Groups = new ObservableCollection<string>();
        foreach (var group in groups) {
            Groups.Add(group);
        }

        InputTextBox.Focus();
        InputTextBox.SelectAll();
    }

    private ObservableCollection<string> _groups = new();
    public ObservableCollection<string> Groups {
        get => _groups;
        set => SetField(ref _groups, value);
    }
    
    private bool _selectedGroup;
    public bool SelectedGroup {
        get => _selectedGroup;
        set => SetField(ref _selectedGroup, value);
    }
    
    private void OkButton_Click(object sender, RoutedEventArgs e) {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) {
        DialogResult = false;
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
