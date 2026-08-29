using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MMONavigator.Base;
using MMONavigator.Services;

namespace MMONavigator.ViewModels;

public class ChallengeNodeViewModel : ViewModelBase {
    public ChallengeSpecs Specs { get; }
    public ObservableCollection<ChallengeNodeViewModel> Children { get; } = [];

    private ChallengeNodeViewModel? _parent;
    public ChallengeNodeViewModel? Parent {
        get => _parent;
        set => SetField(ref _parent, value);
    }

    private bool _isExpanded = true;
    public bool IsExpanded {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    private bool _isSelected;
    public bool IsSelected {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    // NOTE: ChallengeSpecs does not implement INotifyPropertyChanged.
    // DisplayName will not auto-refresh when Name/LocationId are edited
    // until INPC is added to ChallengeSpecs in a future iteration.
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Specs.Name) ? Specs.Name :
        !string.IsNullOrWhiteSpace(Specs.LocationId) ? Specs.LocationId :
        "(Unnamed)";

    public ChallengeNodeViewModel(ChallengeSpecs specs) {
        Specs = specs;
    }

    // Call this after editing Name or LocationId to refresh the tree label.
    public void RefreshDisplayName() => OnPropertyChanged(nameof(DisplayName));
}
