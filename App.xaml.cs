using System.Windows;

namespace MMONavigator;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application {
    
    protected override void OnStartup(StartupEventArgs e) {
        // Disable hardware acceleration for this process
        System.Windows.Media.RenderOptions.ProcessRenderMode = 
            System.Windows.Interop.RenderMode.SoftwareOnly;
        
        base.OnStartup(e);
    }
}