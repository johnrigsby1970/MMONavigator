using System.Windows;

namespace MMONavigator.Models;

public class WindowPlacement {
    public double Top { get; set; } = 100;
    public double Left { get; set; } = 100;
    public double Width { get; set; } = 800;
    public double Height { get; set; } = 600;
    public WindowState State { get; set; } = WindowState.Normal;
}