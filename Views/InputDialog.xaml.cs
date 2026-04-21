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

    private void InputTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) {
        if (e.Key == Key.Enter) {
// // Instead of: DialogResult = true;
//     
//             // Dispatch to the UI thread's next available tick
//             Dispatcher.BeginInvoke(new Action(() => {
//                 this.DialogResult = true;
//             }), System.Windows.Threading.DispatcherPriority.Background);
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
    }
}
