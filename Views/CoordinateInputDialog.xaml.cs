using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.VisualStyles;
using System.Windows.Input;
using MMONavigator.Controls;
using MMONavigator.Helpers;
using MMONavigator.Models;
using MMONavigator.Services;

namespace MMONavigator.Views;

public partial class CoordinateInputDialog : ChildWindow {
    private readonly CoordinateSystem _system;
    private readonly string _coordinateOrder;
    private bool _isParsingClipboard;

    /// <summary>
    /// Returns the normalized string format "X, Y" expected by Scrubber.TryParse.
    /// </summary>
    public string Answer => $"{ParsedX}, {ParsedY}";

    public double ParsedX { get; private set; }
    public double ParsedY { get; private set; }

    public CoordinateInputDialog(
        string question, 
        string title, 
        string defaultAnswer = "", 
        CoordinateSystem system = CoordinateSystem.RightHanded, 
        string coordinateOrder = "x z y d") 
    {
        InitializeComponent();
        DataContext = this;

        _system = system;
        _coordinateOrder = string.IsNullOrWhiteSpace(coordinateOrder) ? "x z y d" : coordinateOrder;

        try {
            Title = title ?? "Enter Coordinates";
            PromptLabel.Text = question ?? "Enter coordinates:";
            
            var systemText = system == CoordinateSystem.LeftHanded 
                ? "Left-Handed (EverQuest style)" 
                : "Right-Handed (Standard Cartesian)";
            SystemInfoLabel.Text = $"Active System: {systemText} | Order: [{_coordinateOrder}]";

            // Parse default values if provided
            if (!string.IsNullOrWhiteSpace(defaultAnswer) && Scrubber.TryParse(defaultAnswer, _coordinateOrder, out var coords)) {
                ParsedX = coords.X;
                ParsedY = coords.Y;
                XTextBox.Text = coords.X.ToString("F2");
                YTextBox.Text = coords.Y.ToString("F2");
            }

            UpdatePreview();

            XTextBox.Focus();
            XTextBox.SelectAll();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error initializing CoordinateInputDialog.");
        }
    }

    private void CoordinateTextBox_TextChanged(object sender, TextChangedEventArgs e) {
        if (_isParsingClipboard || sender is not System.Windows.Controls.TextBox tb) return;

        var text = tb.Text.Trim();

        // Check if the user pasted a multi-value string (e.g. "120.5, 400.2, 12.0" or "y x z")
        if (text.Contains(' ') || text.Contains(',')) {
            if (Scrubber.TryParse(text, _coordinateOrder, out var parsed)) {
                _isParsingClipboard = true;
                ParsedX = parsed.X;
                ParsedY = parsed.Y;
                XTextBox.Text = parsed.X.ToString("F2");
                YTextBox.Text = parsed.Y.ToString("F2");
                _isParsingClipboard = false;
                UpdatePreview();
                return;
            }
        }

        // Direct single numeric entry parsing
        double.TryParse(XTextBox.Text, out var xVal);
        double.TryParse(YTextBox.Text, out var yVal);

        ParsedX = xVal;
        ParsedY = yVal;
        UpdatePreview();
    }

    private void UpdatePreview() {
        if (PreviewTextBlock != null) {
            PreviewTextBlock.Text = $"Normalized Output: X = {ParsedX:F2}, Y = {ParsedY:F2}";
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) {
        ConfirmAndClose();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) {
        CancelAndClose();
    }

    private void InputTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) {
        try {
            if (e.Key == Key.Enter) {
                ConfirmAndClose();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape) {
                CancelAndClose();
                e.Handled = true;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling KeyDown in CoordinateInputDialog.");
        }
    }

    private void ConfirmAndClose() {
        try {
            IsConfirmed = true;
            ManualDialogResult = true;
            SafeCloseDialog();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error confirming CoordinateInputDialog.");
        }
    }

    private void CancelAndClose() {
        try {
            IsConfirmed = false;
            ManualDialogResult = false;
            SafeCloseDialog();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error canceling CoordinateInputDialog.");
        }
    }
}