using MMONavigator.Interfaces;
using MMONavigator.Models;
using MMONavigator.Services;
using MMONavigator.ViewModels;
using Xunit;

namespace MMONavigator.Tests;

public class LogParserTests
{
    private const string DefaultRegex = @"Your Location is.*?(-?\d+(?:\.\d+)?)\D+?(-?\d+(?:\.\d+)?)(?:\D+?(-?\d+(?:\.\d+)?))?";

    [Theory]
    [InlineData("Your location is 0, 0, 100", "0 0 100")]
    [InlineData("Your location is 123 456", "123 456")]
    [InlineData("Your location is -10, 20.5, -30", "-10 20.5 -30")]
    [InlineData("Some prefix Your location is 1, 2, 3", "1 2 3")]
    public void TryParseLogLine_StandardLines_ReturnsExpected(string line, string expected)
    {
        bool result = LogParser.TryParseLogLine(line, DefaultRegex, out string coordinates);
        Assert.True(result);
        Assert.Equal(expected, coordinates);
    }

    [Fact]
    public void TryParseLogLine_MultipleMatchesOnSingleLine_ReturnsLast()
    {
        string line = "Your location is 2000, 0, 2200Your location is 2000, 0, 2100Your location is 2000, 0, 4500";
        string expected = "2000 0 4500";
        
        bool result = LogParser.TryParseLogLine(line, DefaultRegex, out string coordinates);
        
        Assert.True(result);
        Assert.Equal(expected, coordinates);
    }

    [Fact]
    public void TryParseLogLine_MixedCasing_Matches()
    {
        string line = "YOUR LOCATION IS 10, 20, 30";
        string expected = "10 20 30";

        bool result = LogParser.TryParseLogLine(line, DefaultRegex, out string coordinates);

        Assert.True(result);
        Assert.Equal(expected, coordinates);
    }

    [Fact]
    public void TryParseLogLine_InvalidRegex_UsesFallback()
    {
        string line = "Your location is 50, 60, 70";
        string invalidRegex = "["; // This will throw on Regex creation
        string expected = "50 60 70";

        bool result = LogParser.TryParseLogLine(line, invalidRegex, out string coordinates);

        Assert.True(result);
        Assert.Equal(expected, coordinates);
    }

    [Fact]
    public void TryParseLogLine_NoMatch_ReturnsFalse()
    {
        string line = "This line has no coordinates";
        
        bool result = LogParser.TryParseLogLine(line, DefaultRegex, out string coordinates);

        Assert.False(result);
        Assert.Empty(coordinates);
    }

    [Theory]
    [InlineData("Your location is 10, 20, 30, 40", "10 20 30")]
    public void TryParseLogLine_FourCoordinates_ReturnsOnlyThreeWithDefaultRegex(string line, string expected)
    {
        // The default regex only has 3 groups for numbers. 
        bool result = LogParser.TryParseLogLine(line, DefaultRegex, out string coordinates);
        
        Assert.True(result);
        Assert.Equal(expected, coordinates); 
    }
    
    [Fact]
    public void TryParseLogLine_FallbackHandlesFourCoordinates()
    {
        string line = "Your location is 10, 20, 30, 40";
        string regexThatFails = "something that wont match";
        string expected = "10 20 30 40";

        bool result = LogParser.TryParseLogLine(line, regexThatFails, out string coordinates);

        Assert.True(result);
        Assert.Equal(expected, coordinates);
    }
    [Fact]
    public void TryParse_RespectsCoordinateOrder()
    {
        string input = "10 20 30";
        
        // y x order
        // values[0] is 10, values[1] is 20
        // result.X = values[1] = 20, result.Y = values[0] = 10
        Scrubber.TryParse(input, "y x", out var resultYX);
        Assert.Equal(20, resultYX.X);
        Assert.Equal(10, resultYX.Y);

        // x y order
        // values[0]=10, values[1]=20
        // result.X = 10, result.Y = 20
        Scrubber.TryParse(input, "x y", out var resultXY);
        Assert.Equal(10, resultXY.X);
        Assert.Equal(20, resultXY.Y);

        // x z y d order
        // values[0]=10, values[1]=20, values[2]=30
        // result.X = values[0] = 10, result.Y = values[2] = 30
        Scrubber.TryParse(input, "x z y d", out var resultXZYD);
        Assert.Equal(10, resultXZYD.X);
        Assert.Equal(30, resultXZYD.Y);
    }

    [Fact]
    public void MainViewModel_ShowDirection_RespectsCoordinateOrder()
    {
        var settingsService = new MoqSettingsService();
        var watcherService = new MoqWatcherService();
        var vm = new MainViewModel(settingsService, watcherService);
        
        // Input "10 20" with "y x" order: Y=10, X=20
        vm.Settings.CoordinateOrder = "y x";
        vm.CurrentCoordinates = "10 20";
        vm.TargetCoordinates = "0 0"; // Target at origin
        
        // If Y=10, X=20, distance to 0,0 is sqrt(10^2 + 20^2) = sqrt(500) approx 22.36
        Assert.Equal(22, vm.DistanceInt);

        // Now change order to "x z y d" (Default)
        // Input "10 20" means X=10, Y=20 (from values[1] which doesn't exist? Wait.)
        // Scrubber.TryParse for "x z y d" and 2 components uses values[XIndexInXY=0] and values[YIndexInXY=1]
        vm.Settings.CoordinateOrder = "x z y d";
        vm.CurrentCoordinates = "10 20";
        // Now X=10, Y=20. Distance to 0,0 is still sqrt(10^2 + 20^2) = 22.36
        // Let's use 10 0 for CurrentCoordinates
        vm.CurrentCoordinates = "10 0";
        // y x: Y=10, X=0. Distance 10.
        vm.Settings.CoordinateOrder = "y x";
        Assert.Equal(10, vm.DistanceInt);
        
        // x z y d: X=10, Y=0. Distance 10.
        // Still the same because distance is symmetric.
        
        // Let's check GoDirection string which includes compass direction
        // y x: Y=10, X=0. Target 0,0. Vector to target is (0-0, 0-10) = (0, -10). That is South.
        vm.Settings.CoordinateOrder = "y x";
        vm.CurrentCoordinates = "10 0";
        vm.TargetCoordinates = "0 0";
        Assert.Contains("South", vm.GoDirection);

        // x z y d: X=10, Y=0. Target 0,0. Vector to target is (0-10, 0-0) = (-10, 0). That is West.
        vm.Settings.CoordinateOrder = "x z y d";
        Assert.Contains("West", vm.GoDirection);

        // x y: X=10, Y=0. Target 0,0. Vector to target is (0-10, 0-0) = (-10, 0). That is West.
        vm.Settings.CoordinateOrder = "x y";
        vm.CurrentCoordinates = "10 0";
        vm.TargetCoordinates = "0 0";
        Assert.Contains("West", vm.GoDirection);
        
        // x y: X=0, Y=10. Target 0,0. Vector (0, -10). That is South.
        vm.CurrentCoordinates = "0 10";
        Assert.Contains("South", vm.GoDirection);
    }

    private class MoqSettingsService : ISettingsService
    {
        public AppSettings LoadSettings() => new AppSettings();
        public void SaveSettings(AppSettings settings) { }
        public List<LocationItem> LoadLocations() => new List<LocationItem>();
        public void SaveLocations(IEnumerable<LocationItem> locations) { }
    }

    private class MoqWatcherService : IWatcherService
    {
        public event EventHandler<string>? LocationUpdated;
        public void Start(AppSettings settings, IntPtr windowHandle) { }
        public void Stop() { }
        public void Dispose() { }
    }
}
