namespace MMONavigator.Models;

public class DungeonMapSet
{
    public string SetName { get; set; } = "New Dungeon Set";
    public List<DungeonMapLayerConfig> Layers { get; set; } = new();
}