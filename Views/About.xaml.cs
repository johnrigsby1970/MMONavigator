using System;
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
        }
        catch (Exception ex)
        {
            targetTextBox.Text = $"Error loading license file: {ex.Message}";
        }
    }
}