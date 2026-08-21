namespace MMONavigator.Models;

public class DungeonMapLayerConfig
{
    public string LayerId { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public float ZElevation { get; set; }
    public double Opacity { get; set; } = 0.30;
    public bool IsActiveDrawLayer { get; set; }
    public double Width { get; set; } = 0.0;
    public double Height { get; set; } = 0.0;
}