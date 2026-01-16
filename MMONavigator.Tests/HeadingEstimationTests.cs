using MMONavigator.Interfaces;
using MMONavigator.Models;
using MMONavigator.Services;
using MMONavigator.ViewModels;
using Xunit;

namespace MMONavigator.Tests;

public class HeadingEstimationTests
{
    [Fact]
    public void MainViewModel_EstimatesHeading_WhenMoving()
    {
        var settingsService = new MoqSettingsService();
        var watcherService = new MoqWatcherService();
        var vm = new MainViewModel(settingsService, watcherService);
        
        vm.Settings.SelectedProfile.CoordinateOrder = "x y";
        vm.TargetCoordinates = "100 100"; // Ensure target is set so ShowDirection proceeds
        
        // Initial position
        vm.CurrentCoordinates = "0 0";
        // Heading should be null initially, NorthRotation should be 0
        Assert.Equal(0, vm.NorthRotation);

        // Move East (X increases)
        // 0 0 -> 10 0
        vm.CurrentCoordinates = "10 0";
        
        // Direction from (0,0) to (10,0) is East (90 degrees)
        // NorthRotation should be -90.
        Assert.Equal(-90, vm.NorthRotation);
    }

    [Fact]
    public void MainViewModel_MaintainsHeading_WhenStationary()
    {
        var settingsService = new MoqSettingsService();
        var watcherService = new MoqWatcherService();
        var vm = new MainViewModel(settingsService, watcherService);
        
        vm.Settings.SelectedProfile.CoordinateOrder = "x y";
        vm.TargetCoordinates = "100 100";
        
        // Move East to establish heading
        vm.CurrentCoordinates = "0 0";
        vm.CurrentCoordinates = "10 0"; // East (90 deg)
        Assert.Equal(-90, vm.NorthRotation);

        // Stay still (or move less than threshold)
        vm.CurrentCoordinates = "10.5 0"; // Moved 0.5, less than 1.0 threshold
        
        // Should maintain 90 degrees
        Assert.Equal(-90, vm.NorthRotation);
    }

    [Fact]
    public void MainViewModel_UsesRealHeading_WhenAvailable()
    {
        var settingsService = new MoqSettingsService();
        var watcherService = new MoqWatcherService();
        var vm = new MainViewModel(settingsService, watcherService);
        
        vm.Settings.SelectedProfile.CoordinateOrder = "x z y d"; // Default order with 4th component
        vm.TargetCoordinates = "100 100 100";
        
        // 10 0 20 180 (X=10, Z=0, Y=20, Heading=180)
        vm.CurrentCoordinates = "10 0 20 180";
        
        // NorthRotation = -180
        Assert.Equal(-180, vm.NorthRotation);
        
        // Move, but provide a different real heading
        vm.CurrentCoordinates = "20 0 20 270"; // Moved East, but heading is West
        Assert.Equal(-270, vm.NorthRotation);
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
