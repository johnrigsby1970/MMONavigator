using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using MMONavigator.Helpers;
using MMONavigator.Services;

namespace MMONavigator.ViewModels;

public class ChallengeDesignerViewModel : INotifyPropertyChanged {

    // ── Tree ────────────────────────────────────────────────────────────────
    public ObservableCollection<ChallengeNodeViewModel> RootNodes { get; set; } = [];

    private ChallengeNodeViewModel? _selectedNode;
    public ChallengeNodeViewModel? SelectedNode {
        get => _selectedNode;
        set {
            SetField(ref _selectedNode, value);
            OnPropertyChanged(nameof(HasSelectedNode));
            OnPropertyChanged(nameof(EmptyStateVisibility));
        }
    }

    public bool HasSelectedNode => SelectedNode != null;

    // Exposed as Visibility so the XAML doesn't need an InverseBoolToVis converter.
    public Visibility EmptyStateVisibility =>
        HasSelectedNode ? Visibility.Collapsed : Visibility.Visible;

    // ── UI state ────────────────────────────────────────────────────────────
    private bool _showMiniMap;
    public bool ShowMiniMap {
        get => _showMiniMap;
        set => SetField(ref _showMiniMap, value);
    }

    // ── Status ──────────────────────────────────────────────────────────────
    private string? _currentFileName;
    public string? CurrentFileName {
        get => _currentFileName;
        set => SetField(ref _currentFileName, value);
    }

    private int _nodeCount;
    public int NodeCount {
        get => _nodeCount;
        set => SetField(ref _nodeCount, value);
    }

    private string _statusMessage = "Ready";
    public string StatusMessage {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    // ── Commands ─────────────────────────────────────────────────────────────
    public RelayCommand NewChallengeCommand { get; }
    public RelayCommand OpenChallengeCommand { get; }
    public RelayCommand SaveChallengeCommand { get; }
    public RelayCommand AddRootNodeCommand { get; }
    public RelayCommand AddChildNodeCommand { get; }
    public RelayCommand DeleteNodeCommand { get; }
    public RelayCommand PickFromDestinationsCommand { get; }

    public ChallengeDesignerViewModel() {
        NewChallengeCommand      = new RelayCommand(_ => ExecuteNewChallenge());
        OpenChallengeCommand     = new RelayCommand(_ => ExecuteOpenChallenge());
        SaveChallengeCommand     = new RelayCommand(_ => ExecuteSaveChallenge());
        AddRootNodeCommand       = new RelayCommand(_ => ExecuteAddRootNode());
        AddChildNodeCommand      = new RelayCommand(_ => ExecuteAddChildNode(), _ => HasSelectedNode);
        DeleteNodeCommand        = new RelayCommand(_ => ExecuteDeleteNode(),   _ => HasSelectedNode);
        PickFromDestinationsCommand = new RelayCommand(_ => ExecutePickFromDestinations());
    }

    // ── Command handlers (stubs for this iteration) ─────────────────────────
    private void ExecuteNewChallenge() {
        RootNodes.Clear();
        CurrentFileName = null;
        SelectedNode = null;
        RefreshNodeCount();
        StatusMessage = "New challenge created.";
    }

    private void ExecuteOpenChallenge() {
        try {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog {
                Multiselect = false,
                InitialDirectory = Path.Combine(Helpers.NativeMethods.AppFolder(), "challenges"),
                Filter = "Challenge files (*.qst;*.json)|*.qst;*.json|All files (*.*)|*.*"
            };
            
            if (openFileDialog.ShowDialog() == true) {
                CurrentFileName = openFileDialog.FileName;
                var json = File.ReadAllText(CurrentFileName);
                var tempList = JsonSerializer.Deserialize<List<ChallengeNodeViewModel>>(json);
                RootNodes = new ObservableCollection<ChallengeNodeViewModel>(tempList);
                RefreshNodeCount();
            }
            StatusMessage = "Challenge design file opened.";
        }
        catch {
            StatusMessage = "Failed to open challenge design file.";
        }
    }

    private void ExecuteSaveChallenge() {
        try {
            var dialog = new Microsoft.Win32.SaveFileDialog {
                Title = "Download Selected File",
                InitialDirectory = Path.Combine(Helpers.NativeMethods.AppFolder(), "challenges"),
                FileName = Path.GetFileName(CurrentFileName), // Default file name
                DefaultDirectory =  Path.Combine(Helpers.NativeMethods.AppFolder(), "challenges"),
                DefaultExt = ".qst", // Default file extension
                Filter = "Challenge files (*.qst;*.json)|*.qst;*.json|All files (*.*)|*.*" // Filter files by extension
            };
            
            if (dialog.ShowDialog() == true) {
                var filename = dialog.FileName;
                var json = JsonSerializer.Serialize<List<ChallengeNodeViewModel>>(RootNodes.ToList());
                File.WriteAllText(filename, json);
            }
            StatusMessage = "Challenge design file saved.";
        }
        catch {
            StatusMessage = "Failed to save challenge design file.";
        }
    }

    private void ExecuteAddRootNode() {
        var node = new ChallengeNodeViewModel(new ChallengeSpecs { LocationId = "" }) {
            IsExpanded = true
        };
        node.Specs.LocationId = Guid.NewGuid().ToString();
        RootNodes.Add(node);
        RefreshNodeCount();
        StatusMessage = "Root node added.";
    }

    private void ExecuteAddChildNode() {
        if (SelectedNode == null) return;

        var child = new ChallengeNodeViewModel(new ChallengeSpecs { LocationId = "" }) {
            Parent = SelectedNode
        };
        SelectedNode.Children.Add(child);
        SelectedNode.IsExpanded = true;
        RefreshNodeCount();
        StatusMessage = "Child node added.";
    }

    private void ExecuteDeleteNode() {
        if (SelectedNode == null) return;

        if (SelectedNode.Parent == null)
            RootNodes.Remove(SelectedNode);
        else
            SelectedNode.Parent.Children.Remove(SelectedNode);

        SelectedNode = null;
        RefreshNodeCount();
        StatusMessage = "Node deleted.";
    }

    private void ExecutePickFromDestinations() {
        // TODO: open destination picker dialog, write selected CoordinateData
        // back to SelectedNode.Specs.Coordinates
        StatusMessage = "Destination picker: not yet implemented.";
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private void RefreshNodeCount() {
        NodeCount = CountNodes(RootNodes);
    }

    private static int CountNodes(IEnumerable<ChallengeNodeViewModel> nodes) {
        int count = 0;
        foreach (var n in nodes)
            count += 1 + CountNodes(n.Children);
        return count;
    }

    // TODO: BuildTree() — reconstruct ObservableCollection<ChallengeNodeViewModel>
    // from a flat List<ChallengeSpecs> loaded from JSON, using ParentId links.

    // TODO: FlattenTree() — walk RootNodes recursively, assign ParentId from
    // each node's Parent reference, return List<ChallengeSpecs> for serialization.

    // ── INotifyPropertyChanged ───────────────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
