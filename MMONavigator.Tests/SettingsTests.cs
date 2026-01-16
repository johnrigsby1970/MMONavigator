using System.IO;
using System.Text.Json;
using MMONavigator.Helpers;
using MMONavigator.Models;
using MMONavigator.Services;
using Xunit;

namespace MMONavigator.Tests;

public class SettingsTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    public SettingsTests()
    {
        Cleanup();
    }

    public void Dispose()
    {
        Cleanup();
    }

    private void Cleanup()
    {
        if (File.Exists(_settingsPath))
        {
            File.Delete(_settingsPath);
        }
    }

    [Fact]
    public void LoadSettings_FileNotFound_ReturnsDefaultSettings()
    {
        var service = new SettingsService();
        var settings = service.LoadSettings();

        Assert.NotNull(settings);
        Assert.Single(settings.Profiles);
        Assert.Equal("Default", settings.Profiles[0].Name);
        Assert.Equal("Default", settings.LastSelectedProfileName);
        Assert.Equal(WatchMode.Clipboard, settings.SelectedProfile.WatchMode);
    }

    [Fact]
    public void MigrateLegacySettings_WithLegacyProperties_CreatesDefaultProfile()
    {
        var settings = new AppSettings
        {
            LegacyWatchMode = WatchMode.File,
            LegacyLogFilePath = "C:\\test.log",
            LegacyCoordinateOrder = "y x z"
        };

        settings.MigrateLegacySettings();

        Assert.Single(settings.Profiles);
        var profile = settings.Profiles[0];
        Assert.Equal("Default", profile.Name);
        Assert.Equal(WatchMode.File, profile.WatchMode);
        Assert.Equal("C:\\test.log", profile.LogFilePath);
        Assert.Equal("y x z", profile.CoordinateOrder);
        Assert.Equal(CoordinateSystem.LeftHanded, profile.CoordinateSystem); // Default for File mode
    }

    [Fact]
    public void MigrateLegacySettings_EmptySettings_CreatesDefaultProfile()
    {
        var settings = new AppSettings();
        settings.MigrateLegacySettings();

        Assert.Single(settings.Profiles);
        Assert.Equal("Default", settings.Profiles[0].Name);
    }

    [Fact]
    public void SelectedProfile_WhenProfileMissing_ReturnsFirstOrCreatesDefault()
    {
        var settings = new AppSettings();
        settings.Profiles.Add(new GameProfile { Name = "Custom" });
        settings.LastSelectedProfileName = "NonExistent";

        // Should return "Custom" (the first one) because "NonExistent" doesn't exist
        var profile = settings.SelectedProfile;
        
        Assert.Equal("Custom", profile.Name);
        Assert.Equal("Custom", settings.LastSelectedProfileName);
    }

    [Fact]
    public void SettingsService_SaveAndLoad_PreservesProfiles()
    {
        var service = new SettingsService();
        var settings = new AppSettings();
        // Clear default profiles for testing
        settings.Profiles.Clear();
        
        var profile = new GameProfile 
        { 
            Name = "EverQuest", 
            WatchMode = WatchMode.File,
            LogFilePath = "eqlog.txt"
        };
        settings.Profiles.Add(profile);
        settings.LastSelectedProfileName = "EverQuest";

        service.SaveSettings(settings);
        var loaded = service.LoadSettings();

        // One profile because we cleared before saving
        Assert.Single(loaded.Profiles);
        var eqProfile = loaded.Profiles.FirstOrDefault(p => p.Name == "EverQuest");
        Assert.NotNull(eqProfile);
        Assert.Equal(WatchMode.File, eqProfile.WatchMode);
        Assert.Equal("eqlog.txt", eqProfile.LogFilePath);
        Assert.Equal("EverQuest", loaded.LastSelectedProfileName);
    }
    [Fact]
    public void MigrateLegacySettings_WithPartialLegacyProperties_CreatesDefaultProfileWithDefaults()
    {
        var settings = new AppSettings
        {
            LegacyWatchMode = WatchMode.Clipboard
            // LogFilePath is missing
        };

        settings.MigrateLegacySettings();

        Assert.Single(settings.Profiles);
        var profile = settings.Profiles[0];
        Assert.Equal("Default", profile.Name);
        Assert.Equal(WatchMode.Clipboard, profile.WatchMode);
        Assert.Equal(string.Empty, profile.LogFilePath);
        Assert.Equal("x z y d", profile.CoordinateOrder); // Default for Clipboard
        Assert.Equal(CoordinateSystem.RightHanded, profile.CoordinateSystem); // Default for Clipboard
    }

    [Fact]
    public void LoadSettings_CorruptFile_ReturnsDefaultSettings()
    {
        File.WriteAllText(_settingsPath, "{ \"Profiles\": [ { \"Name\": \"Broken\" } "); // Missing closing braces

        var service = new SettingsService();
        var settings = service.LoadSettings();

        Assert.NotNull(settings);
        Assert.Single(settings.Profiles);
        Assert.Equal("Default", settings.Profiles[0].Name);
    }
}
