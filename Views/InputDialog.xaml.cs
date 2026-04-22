using System.Windows;
using System.Windows.Input;
using MMONavigator.Controls;

namespace MMONavigator.Views;

public partial class InputDialog : ChildWindow {
    public string Answer => InputTextBox.Text;

    public InputDialog(string question, string title, string defaultAnswer = "") {
        InitializeComponent();
        DataContext = this;
        Title = title;
        PromptLabel.Text = question;
        InputTextBox.Text = defaultAnswer;
        InputTextBox.Focus();
        InputTextBox.SelectAll();
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

    private void InputTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) {
        if (e.Key == Key.Enter) {
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
    }
}
