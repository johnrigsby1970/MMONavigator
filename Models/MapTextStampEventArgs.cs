using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace MMONavigator.Models;

public class MapTextStampEventArgs : EventArgs
{
    public string Text { get; init; } = string.Empty;

    /// <summary>Top-left X of the text box in unscaled map pixels (Canvas.Left of the control).</summary>
    public double X { get; init; }

    /// <summary>Top-left Y of the text box in unscaled map pixels (Canvas.Top of the control).</summary>
    public double Y { get; init; }

    /// <summary>Width of the bounding box in unscaled map pixels.</summary>
    public double Width { get; init; }

    /// <summary>Height of the bounding box in unscaled map pixels.</summary>
    public double Height { get; init; }

    /// <summary>Clockwise rotation in degrees, applied around the bounding box center.</summary>
    public double RotationAngle { get; init; }

    // ── Text properties ──
    public string FontFamilyName { get; init; } = "Segoe UI";
    public double FontSize { get; init; }
    public bool IsBold { get; init; }
    public bool IsItalic { get; init; }
    public bool IsUnderline { get; init; }
    public Color TextColor { get; init; }
    public double TextOpacity { get; init; }
    /// <summary>Uniform inner padding between text and bounding box edges, in map pixels.</summary>
    public double TextPadding { get; init; }
    public TextAlignment TextAlignment { get; init; }

    // ── Box/container properties ──
    public Color BackgroundColor { get; init; }
    public double BackgroundOpacity { get; init; }
    public Color BoxBorderColor { get; init; }
    public double BoxBorderThickness { get; init; }
    public CornerRadius CornerRadius { get; init; }
}
