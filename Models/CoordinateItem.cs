namespace MMONavigator.Models;

public class CoordinateItem
{
    public CoordinateSystem Value { get; set; }
    public string Label { get; set; }

    // This ensures that if the ComboBox gets confused, it just shows the label.
    public override string ToString()
    {
        return Label;
    }
}