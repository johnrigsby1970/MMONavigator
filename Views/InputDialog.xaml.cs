using System.Windows;
using System.Windows.Input;
using MMONavigator.Controls;

namespace MMONavigator.Views;

public partial class InputDialog : ChildWindow {
    public string Answer => InputTextBox.Text;

    public InputDialog(string question, string title, string defaultAnswer = "") {
        InitializeComponent();
        DataContext = this;

        try {
            Title = title ?? string.Empty;
            PromptLabel.Text = question ?? string.Empty;
            InputTextBox.Text = defaultAnswer ?? string.Empty;
            
            // Set initial focus and select all text so the user can immediately type over the default
            InputTextBox.Focus();
            InputTextBox.SelectAll();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error initializing InputDialog controls.");
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) {
        ConfirmAndClose();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) {
        CancelAndClose();
    }

    private void InputTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) {
        try {
            if (e.Key == Key.Enter) {
                ConfirmAndClose();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape) {
                CancelAndClose();
                e.Handled = true;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling InputTextBox_KeyDown in InputDialog.");
        }
    }

    private void ConfirmAndClose() {
        try {
            IsConfirmed = true;
            ManualDialogResult = true;
                
            // Inherited from ChildWindow
            SafeCloseDialog();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error confirming and closing InputDialog.");
        }
    }

    private void CancelAndClose() {
        try {
            IsConfirmed = false;
            ManualDialogResult = false;
                
            // Inherited from ChildWindow
            SafeCloseDialog();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error canceling and closing InputDialog.");
        }
    }
}