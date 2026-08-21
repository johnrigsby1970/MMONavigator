namespace MMONavigator.Models;

/// <summary>
/// Represents a single calibration point mapping a 2D map pixel coordinate 
/// to its corresponding in-game 3D world coordinate.
/// </summary>
public class CalibrationPoint {
    /// <summary>
    /// X pixel position on the 2D map image (measured from Left).
    /// </summary>
    public double PixelX { get; set; }

    /// <summary>
    /// Y pixel position on the 2D map image (measured from Top).
    /// </summary>
    public double PixelY { get; set; }

    /// <summary>
    /// Game world X coordinate (East/West axis).
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Game world Y coordinate (North/South axis).
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Game world Z coordinate (Elevation / Altitude).
    /// </summary>
    public double Z { get; set; }

    public CalibrationPoint() { }

    public CalibrationPoint(double pixelX, double pixelY, double worldX, double worldY, double worldZ = 0.0) {
        PixelX = pixelX;
        PixelY = pixelY;
        X = worldX;
        Y = worldY;
        Z = worldZ;
    }
}