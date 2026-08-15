using System.Windows;
using MMONavigator.ViewModels;

namespace MMONavigator.Views;

public partial class ChallengeDesignerWindow : Window {
    private ChallengeDesignerViewModel ViewModel => (ChallengeDesignerViewModel)DataContext;

    public ChallengeDesignerWindow() {
        InitializeComponent();
        DataContext = new ChallengeDesignerViewModel();
    }

    private void ChallengeTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) {
        try {
            ViewModel.SelectedNode = e.NewValue as ChallengeNodeViewModel;
        }
        catch (Exception ex) {
            Log.Error(ex, "Failed to set SelectedNode in ChallengeTree for value {NewValue}", e.NewValue);
            System.Diagnostics.Debug.WriteLine(
                $"[DEBUG_LOG]ChallengeTree_SelectedItemChanged error: {ex.Message}");
        }
    }
}