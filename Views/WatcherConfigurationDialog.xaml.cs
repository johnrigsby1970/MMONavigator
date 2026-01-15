using System.Windows;
using Microsoft.Win32;
using MMONavigator.Models;

using MMONavigator.Models;
using MMONavigator.ViewModels;

namespace MMONavigator.Views;

public partial class WatcherConfigurationDialog : Window {
    public WatchMode WatchMode => ClipboardRadio.IsChecked == true ? WatchMode.Clipboard : WatchMode.File;
    public string LogFilePath => FilePathTextBox.Text;
    public string LogFileRegex => RegexTextBox.Text;
    public string CoordinateOrder => OrderComboBox.SelectedItem?.ToString() ?? "x z y d";

    public WatcherConfigurationDialog(AppSettings settings) {
        InitializeComponent();
        
        if (settings.WatchMode == WatchMode.Clipboard) {
            ClipboardRadio.IsChecked = true;
        } else {
            FileRadio.IsChecked = true;
        }

        FilePathTextBox.Text = settings.LogFilePath;
        RegexTextBox.Text = settings.LogFileRegex;
        
        OrderComboBox.ItemsSource = settings.AvailableCoordinateOrders;
        OrderComboBox.SelectedItem = settings.CoordinateOrder;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e) {
        OpenFileDialog openFileDialog = new OpenFileDialog();
        openFileDialog.Filter = "Log files (*.log;*.txt)|*.log;*.txt|All files (*.*)|*.*";
        if (openFileDialog.ShowDialog() == true) {
            FilePathTextBox.Text = openFileDialog.FileName;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) {
        DialogResult = false;
    }
}
