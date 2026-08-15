using System.IO;
using System.Windows;

namespace MMONavigator.Views;

public partial class About : Window {
    public About() {
        InitializeComponent();
        LoadEmbeddedLicense("pack://application:,,,/LICENSE", MainLicenseTextBox);
        LoadEmbeddedLicense("pack://application:,,,/ThirdPartyNotices.txt", ThirdPartyTextBox);
    }
    
    private void LoadEmbeddedLicense(string packUri, System.Windows.Controls.TextBox targetTextBox)
    {
        try
        {
            var uri = new Uri(packUri);
            var resourceStream = System.Windows.Application.GetResourceStream(uri);

            if (resourceStream != null)
            {
                using (var reader = new StreamReader(resourceStream.Stream))
                {
                    targetTextBox.Text = reader.ReadToEnd();
                }
            }
            else
            {
                // Handle case where resource is missing or build action is incorrect
                var ex = new FileNotFoundException($"The embedded resource stream was null for URI: {packUri}");
                Log.Error(ex, "Failed to locate embedded license file.");
                targetTextBox.Text = "Error: License file could not be found in application resources.";
            }
        }
        catch (Exception ex)
        {
            // Ensure errors are explicitly logged to Serilog/Sentry
            Log.Error(ex, "An unexpected exception occurred while loading the license file from {PackUri}", packUri);
            targetTextBox.Text = $"Error loading license file: {ex.Message}";
        }
    }
}