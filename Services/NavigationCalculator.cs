using MMONavigator.Models;

namespace MMONavigator.Services;

public static class NavigationCalculator {
    private const byte RightOfStraightThreshold = 5;
    private const int LeftOfStraightThreshold = 355;
    private const int FullCircleDegrees = 360;
    private const int HalfCircleDegrees = 180;
    private const double RadToDeg = HalfCircleDegrees / Math.PI;

    public static string GetCompassDirection(double angle) {
        try {
            if (double.IsNaN(angle) || double.IsInfinity(angle)) {
                Log.Warning("GetCompassDirection received an invalid angle: {Angle}", angle);
                return "North";
            }

            // Normalize angle to strict [0, 360) range
            angle = NormalizeAngle(angle);

            return angle switch {
                >= 0 and < 22.5 => "North",
                >= 22.5 and < 67.5 => "NorthEast",
                >= 67.5 and < 112.5 => "East",
                >= 112.5 and < 157.5 => "SouthEast",
                >= 157.5 and < 202.5 => "South",
                >= 202.5 and < 247.5 => "SouthWest",
                >= 247.5 and < 292.5 => "West",
                >= 292.5 and < 337.5 => "NorthWest",
                _ => "North" // Fixed consistency: returns "North" for 337.5° - 360°
            };
        }
        catch (Exception ex) {
            Log.Error(ex, "Unexpected exception in GetCompassDirection for angle {Angle}.", angle);
            return "North";
        }
    }

    public static double GetDirection(double x1, double y1, double x2, double y2, CoordinateSystem coordinateSystem = CoordinateSystem.RightHanded) {
        try {
            //We need to account for Cartesian vs. Compass. Where standard math
            //(Cartesian), 0° is Right (East), and 90° is Up (North). Like on a protractor.
            //In the game, 0° is Up (North), and 90° is Right (East).
        
            //What is the angle of the line going from the current position (x1, y1),
            //to the target destination (x2, y2) 

            // Guard against NaN or Infinity parameters
            if (double.IsNaN(x1) || double.IsNaN(y1) || double.IsNaN(x2) || double.IsNaN(y2) ||
                double.IsInfinity(x1) || double.IsInfinity(y1) || double.IsInfinity(x2) || double.IsInfinity(y2)) {
                Log.Warning("GetDirection called with invalid coordinates: P1({X1}, {Y1}), P2({X2}, {Y2})", x1, y1, x2, y2);
                return 0.0;
            }

            var dx = x2 - x1;
            var dy = y2 - y1;

            if (coordinateSystem == CoordinateSystem.LeftHanded) {
                // In the left-handed system, +X is West.
                // Standard Navigation expects +X to be East.
                // So we negate dx to treat it as if +X was East.
                // In left-handed systems, +X is West. Negate dx to align with standard navigation.
                dx = -dx;
            }

            // By swapping dx and dy, 0 degrees becomes North (Up) 
            // and positive results go Clockwise (East).
            // Swapping dx and dy converts Cartesian angle to compass bearing (0° = North, clockwise)
            var angleRad = Math.Atan2(dx, dy);
            var angleDeg = angleRad * RadToDeg;

            return NormalizeAngle(angleDeg);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error calculating direction from P1({X1}, {Y1}) to P2({X2}, {Y2}).", x1, y1, x2, y2);
            return 0.0;
        }
    }

    public static string DetermineDirection(double targetHeading, double currentHeading) {
        try {
            if (double.IsNaN(targetHeading) || double.IsNaN(currentHeading) ||
                double.IsInfinity(targetHeading) || double.IsInfinity(currentHeading)) {
                Log.Warning("DetermineDirection called with invalid headings: Target={Target}, Current={Current}", targetHeading, currentHeading);
                return string.Empty;
            }

            var diff = NormalizeAngle(targetHeading - currentHeading);

            return diff switch {
                > LeftOfStraightThreshold or < RightOfStraightThreshold => string.Empty, // On target
                < HalfCircleDegrees => "Right",
                _ => "Left"
            };
        }
        catch (Exception ex) {
            Log.Error(ex, "Error determining relative turn direction. Target={Target}, Current={Current}", targetHeading, currentHeading);
            return string.Empty;
        }
    }

    private static double NormalizeAngle(double angle) {
        angle %= FullCircleDegrees;
        if (angle < 0) {
            angle += FullCircleDegrees;
        }
        return angle;
    }
}