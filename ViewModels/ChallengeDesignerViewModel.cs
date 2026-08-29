using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using MMONavigator.Base;
using MMONavigator.Helpers;
using MMONavigator.Services;

namespace MMONavigator.ViewModels;

public class ChallengeDesignerViewModel : ViewModelBase {
    private ChallengeOverview? _challengeOVerview;

    public ChallengeOverview? ChallengeOverview {
        get => _challengeOVerview;
        set { SetField(ref _challengeOVerview, value); }
    }

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
        NewChallengeCommand = new RelayCommand(_ => ExecuteNewChallenge());
        OpenChallengeCommand = new RelayCommand(_ => ExecuteOpenChallenge());
        SaveChallengeCommand = new RelayCommand(_ => ExecuteSaveChallenge());
        AddRootNodeCommand = new RelayCommand(_ => ExecuteAddRootNode());
        AddChildNodeCommand = new RelayCommand(_ => ExecuteAddChildNode(), _ => HasSelectedNode);
        DeleteNodeCommand = new RelayCommand(_ => ExecuteDeleteNode(), _ => HasSelectedNode);
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
                Filter = "Challenge files (*.qst;*.json)|*.qst;*.json|All files (*.*)|*.*"
            };

            // 1. Safely handle the InitialDirectory path
            try {
                string initialFolder = Path.Combine(Helpers.NativeMethods.AppFolder(), "challenges");
                if (Directory.Exists(initialFolder)) {
                    openFileDialog.InitialDirectory = initialFolder;
                }
            }
            catch {
                // Fail silently — OpenFileDialog will default to the last used directory or Documents
            }

            if (openFileDialog.ShowDialog() == true) {
                string selectedFile = openFileDialog.FileName;

                if (!string.IsNullOrWhiteSpace(selectedFile) && File.Exists(selectedFile)) {
                    try {
                        // 2. Read file safely
                        string json = File.ReadAllText(selectedFile);

                        if (string.IsNullOrWhiteSpace(json)) {
                            System.Windows.MessageBox.Show("The selected file is empty.", "Invalid File",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            return;
                        }

                        // 3. Deserialize with explicit options / error handling
                        var options = new JsonSerializerOptions {
                            PropertyNameCaseInsensitive = true,
                            AllowTrailingCommas = true
                        };

                        var tempList = JsonSerializer.Deserialize<List<ChallengeNodeViewModel>>(json, options);

                        if (tempList != null) {
                            CurrentFileName = selectedFile;
                            RootNodes = new ObservableCollection<ChallengeNodeViewModel>(tempList);
                        }
                        else {
                            System.Windows.MessageBox.Show("The file did not contain valid challenge data.",
                                "Load Error",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    catch (JsonException ex) {
                        System.Windows.MessageBox.Show(
                            $"Unable to parse challenge file. The file may be corrupt.\n\nDetails: {ex.Message}",
                            "File Format Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    catch (UnauthorizedAccessException) {
                        System.Windows.MessageBox.Show("Access denied. You do not have permission to read this file.",
                            "Permission Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    catch (IOException ex) {
                        System.Windows.MessageBox.Show(
                            $"Could not read the file. It may be in use by another process.\n\nDetails: {ex.Message}",
                            "I/O Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    catch (Exception ex) {
                        // General safety net for unexpected runtime errors
                        System.Windows.MessageBox.Show(
                            $"An unexpected error occurred while opening the file:\n{ex.Message}",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                // 4. Safely refresh node count
                try {
                    RefreshNodeCount();
                }
                catch {
                    // Prevent a secondary UI update failure from taking down the app
                }
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
                DefaultExt = ".qst",
                Filter = "Challenge files (*.qst;*.json)|*.qst;*.json|All files (*.*)|*.*",
                OverwritePrompt = true
            };

            // Safely configure target directory
            try {
                string targetFolder = Path.Combine(Helpers.NativeMethods.AppFolder(), "challenges");
                if (!Directory.Exists(targetFolder)) {
                    Directory.CreateDirectory(targetFolder);
                }

                dialog.InitialDirectory = targetFolder;
                dialog.DefaultDirectory = targetFolder; // Safe if on supported .NET build
            }
            catch {
                // Fail silently — dialog will safely fall back to standard user folder
            }

            // Safely set default file name
            if (!string.IsNullOrWhiteSpace(CurrentFileName)) {
                try {
                    string currentFileName = Path.GetFileName(CurrentFileName);
                    if (!string.IsNullOrWhiteSpace(currentFileName)) {
                        dialog.FileName = currentFileName;
                    }
                }
                catch (ArgumentException) {
                    // Fallback if CurrentFileName contains invalid path characters
                }
            }

            if (dialog.ShowDialog() == true) {
                string filename = dialog.FileName;

                if (!string.IsNullOrWhiteSpace(filename)) {
                    if (RootNodes == null) {
                        StatusMessage = "Failed to save: No challenge data available.";
                        return;
                    }

                    // Serialize with safe defaults
                    var options = new JsonSerializerOptions {
                        WriteIndented = true,
                        IgnoreReadOnlyProperties = true
                    };

                    string json = JsonSerializer.Serialize<List<ChallengeNodeViewModel>>(RootNodes.ToList(), options);

                    // Write to disk
                    File.WriteAllText(filename, json);

                    // Update state ONLY on successful save
                    CurrentFileName = filename;
                    StatusMessage = "Challenge design file saved.";
                }
            }
        }
        catch (UnauthorizedAccessException) {
            StatusMessage = "Failed to save: Access denied to selected folder.";
        }
        catch (IOException) {
            StatusMessage = "Failed to save: File is in use or inaccessible.";
        }
        catch (JsonException) {
            StatusMessage = "Failed to save: Data serialization error.";
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
}