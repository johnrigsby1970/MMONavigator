namespace MMONavigator.Models;

public class DungeonMapLayer {
    public string? LayerId { get; set; }
    public string? ImagePath { get; set; }
    public float AssignedZ { get; set; }         // Known Z-elevation for this map
    public float MapWidth { get; set; }
    public float MapHeight { get; set; }
    public float BaseOpacity { get; set; } = 0.5f;

    // 3D Quad Mesh reference used by the 3D engine
    public object Mesh3DReference { get; set; } = null!;
}