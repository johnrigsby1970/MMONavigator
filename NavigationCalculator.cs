namespace MMONavigator;

public static class NavigationCalculator {
    private const byte RightOfStraightThreshold = 5;
    private const int LeftOfStraightThreshold = 355;
    private const int FullCircleDegrees = 360;
    private const int HalfCircleDegrees = 180;
    private const double RadToDeg = HalfCircleDegrees / Math.PI;
        
    public static string GetCompassDirection(double angle) {
        angle = (angle % FullCircleDegrees + FullCircleDegrees) % FullCircleDegrees;
        return angle switch {
            >= 0 and < 22.5 => "North",
            >= 22.5 and < 67.5 => "NorthEast",
            >= 67.5 and < 112.5 => "East",
            >= 112.5 and < 157.5 => "SouthEast",
            >= 157.5 and < 202.5 => "South",
            >= 202.5 and < 247.5 => "SouthWest",
            >= 247.5 and < 292.5 => "West",
            >= 292.5 and < 337.5 => "NorthWest",
            _ => "N"
        };
    }

    public static double GetDirection(double x1, double y1, double x2, double y2) {
        //We need to account for Cartesian vs. Compass. Where standard math
        //(Cartesian), 0° is Right (East), and 90° is Up (North). Like on a protractor.
        //In the game, 0° is Up (North), and 90° is Right (East).
        
        //What is the angle of the line going from the current position (x1, y1),
        //to the target destination (x2, y2) 

        var dx = x2 - x1;
        var dy = y2 - y1;
        // var angleRad = Math.Atan2(dy, dx);
        // var angleDeg = angleRad * 180 / Math.PI;
        // while (angleDeg < 0) angleDeg += 360;
        // while (angleDeg >= 360) angleDeg -= 360;
        // return ReverseAngle(angleDeg);
        
        // By swapping dx and dy, 0 degrees becomes North (Up) 
        // and positive results go Clockwise (East).
        var angleRad = Math.Atan2(dx, dy);
        var angleDeg = angleRad * RadToDeg;

        // Normalize to 0-360 range
        return (angleDeg + FullCircleDegrees) % FullCircleDegrees;
    }

    // private static double ReverseAngle(double angleInDegrees) {
    //     var normalizedAngle = angleInDegrees % 360;
    //     if (normalizedAngle < 0) normalizedAngle += 360;
    //     var reversed = (180 - normalizedAngle) % 360;
    //     reversed = reversed - 90;
    //     if (reversed < 0) reversed += 360;
    //     return reversed;
    // }

    public static string DetermineDirection(double targetHeading, double currentHeading) {
        var diff = (targetHeading - currentHeading + FullCircleDegrees) % FullCircleDegrees;
        return diff switch {
            > LeftOfStraightThreshold or < RightOfStraightThreshold => "",
            < HalfCircleDegrees => "Right",
            _ => "Left"
        };
    }
}
