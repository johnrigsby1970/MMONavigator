using MMONavigator.Models;
using MMONavigator.Services;
using MMONavigator.ViewModels;
using Xunit;
using System.Collections.Generic;
using System;
using MMONavigator.Interfaces;

namespace MMONavigator.Tests;

public class CoordinateSystemTests
{
    [Fact]
    public void GetDirection_RightHanded_ReturnsExpected()
    {
        // Standard RightHanded: +X = East, +Y = North
        // Current (0,0), Target (10, 10) -> NorthEast (45 deg)
        double direction = NavigationCalculator.GetDirection(0, 0, 10, 10, CoordinateSystem.RightHanded);
        Assert.Equal(45, direction, 1);

        // Current (0,0), Target (10, 0) -> East (90 deg)
        direction = NavigationCalculator.GetDirection(0, 0, 10, 0, CoordinateSystem.RightHanded);
        Assert.Equal(90, direction, 1);
        
        // Current (0,0), Target (-10, 0) -> West (270 deg)
        direction = NavigationCalculator.GetDirection(0, 0, -10, 0, CoordinateSystem.RightHanded);
        Assert.Equal(270, direction, 1);
    }

    [Fact]
    public void GetDirection_LeftHanded_ReturnsExpected()
    {
        // LeftHanded: +X = West, +Y = North
        // Current (0,0), Target (10, 0). In this game, moving from 0 to 10 in X is moving WEST.
        // So the direction should be West (270 deg).
        double direction = NavigationCalculator.GetDirection(0, 0, 10, 0, CoordinateSystem.LeftHanded);
        Assert.Equal(270, direction, 1);

        // Current (0,0), Target (-10, 0). Moving from 0 to -10 in X is moving EAST.
        // So the direction should be East (90 deg).
        direction = NavigationCalculator.GetDirection(0, 0, -10, 0, CoordinateSystem.LeftHanded);
        Assert.Equal(90, direction, 1);
        
        // Current (0,0), Target (10, 10). +X is West, +Y is North. This is NorthWest (315 deg).
        direction = NavigationCalculator.GetDirection(0, 0, 10, 10, CoordinateSystem.LeftHanded);
        Assert.Equal(315, direction, 1);
    }

    [Fact]
    public void MainViewModel_RespectsCoordinateSystem()
    {
        var settingsService = new MoqSettingsService();
        var watcherService = new MoqWatcherService();
        var vm = new MainViewModel(settingsService, watcherService);
        
        vm.Settings.SelectedProfile.CoordinateOrder = "x y";
        vm.CurrentCoordinates = "0 0";
        vm.TargetCoordinates = "10 0"; // East in RightHanded, West in LeftHanded
        vm.ShowDirection();

        // Default is RightHanded
        vm.Settings.SelectedProfile.CoordinateSystem = CoordinateSystem.RightHanded;
        vm.ShowDirection();
        Assert.Contains("East", vm.GoDirection);

        // Switch to LeftHanded
        vm.Settings.SelectedProfile.CoordinateSystem = CoordinateSystem.LeftHanded;
        vm.ShowDirection();
        Assert.Contains("West", vm.GoDirection);
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
