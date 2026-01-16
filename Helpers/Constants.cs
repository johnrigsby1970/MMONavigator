using System.Collections.Generic;

namespace MMONavigator.Helpers;

public static class Constants {
    public const string EQLocationRegex = @"Your Location is.*?(-?\d+(?:\.\d+)?)\D+?(-?\d+(?:\.\d+)?)(?:\D+?(-?\d+(?:\.\d+)?))?";
    public static readonly List<string> AvailableCoordinateOrders = new() { "x z y d", "y x z", "y x", "x y z" };
}
