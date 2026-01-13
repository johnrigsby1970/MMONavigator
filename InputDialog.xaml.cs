using System.Windows;
using System.Windows.Input;

namespace MMONavigator;

public partial class InputDialog : Window {
    public string Answer => InputTextBox.Text;

    public InputDialog(string question, string title, string defaultAnswer = "") {
        InitializeComponent();
        Title = title;
        PromptLabel.Text = question;
        InputTextBox.Text = defaultAnswer;
        InputTextBox.Focus();
        InputTextBox.SelectAll();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) {
        DialogResult = false;
    }

    private void InputTextBox_KeyDown(object sender, KeyEventArgs e) {
        if (e.Key == Key.Enter) {
            DialogResult = true;
        }
    }
}
