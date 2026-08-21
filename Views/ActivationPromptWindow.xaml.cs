using System.Diagnostics;
using System.Windows;
using MMONavigator.ViewModels;

namespace MMONavigator.Views
{
    public partial class ActivationPromptWindow : Window
    {
        public bool IsUnlocked { get; private set; } = false;
        private readonly MainViewModel _viewModel;

        public ActivationPromptWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
        }

        private void PurchaseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Open your official Microsoft Store product listing page
                // Replace with your actual MS Store protocol link (e.g., ms-windows-store://pdp/?productid=YOUR_ID)
                string storeUrl = "ms-windows-store://pdp/?productid=9NKB40C027N7"; 
                
                Process.Start(new ProcessStartInfo
                {
                    FileName = storeUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Fallback to web browser store link if protocol fails
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://apps.microsoft.com/store/apps",
                    UseShellExecute = true
                });
            }
        }

        private void ToggleCodePanel_Click(object sender, RoutedEventArgs e)
        {
            // Toggle visibility of secret code input row
            CodeEntryPanel.Visibility = CodeEntryPanel.Visibility == Visibility.Visible 
                ? Visibility.Collapsed 
                : Visibility.Visible;
        }

        private void ApplyCodeButton_Click(object sender, RoutedEventArgs e)
        {
            string code = TxtOverrideCode.Text.Trim();
            
            if (_viewModel.ValidateAndApplyOverrideCode(code))
            {
                IsUnlocked = true;
                System.Windows.MessageBox.Show("3D Map successfully unlocked via code!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            else
            {
                TxtErrorMsg.Text = "Invalid override code. Please check and try again.";
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}