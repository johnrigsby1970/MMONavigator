using System;
using MMONavigator.Models;
using MMONavigator.Services;
using MMONavigator.ViewModels;
using Xunit;

public class CoordinateSystemTests
{
    private MapViewModel CreateTestViewModel(CoordinateSystem system)
    {
        var settings = new MapSettings
        {
            IsCalibrated = true,
            // Place calibration points in the middle of the 1000x1000 image (PixelX = 500)
            Point1 = new MapPoint { X = 100, Y = 100, PixelX = 500, PixelY = 500 },
            Point2 = new MapPoint { X = 200, Y = 200, PixelX = 600, PixelY = 400 }
        };

        var appSettings = new AppSettings();
        var vm = new MapViewModel(settings, appSettings)
        {
            CoordinateSystem = system
        };

        // Create a 1000x1000 32-bit ARGB dummy bitmap using standard WPF APIs
        int width = 1000;
        int height = 1000;
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];

        vm.MapImage = System.Windows.Media.Imaging.BitmapSource.Create(
            width, 
            height, 
            96, 
            96, 
            System.Windows.Media.PixelFormats.Pbgra32, 
            null, 
            pixels, 
            stride);

        return vm;
    }

    [Theory]
    [InlineData(CoordinateSystem.RightHanded, 150.0, 150.0)]
    [InlineData(CoordinateSystem.RightHanded, 300.0, 50.0)]
    [InlineData(CoordinateSystem.LeftHanded, 150.0, 150.0)]
    [InlineData(CoordinateSystem.LeftHanded, 300.0, 50.0)]
    public void RoundTrip_GameToPixelToGame_ReturnsOriginalCoordinates(CoordinateSystem system, double inputX, double inputY)
    {
        // 1. Arrange
        var vm = CreateTestViewModel(system);
        var originalPos = new CoordinateData(inputX, inputY, null, null);

        // 2. Act: Forward transform (Game Space -> Pixel Space)
        var method = typeof(MapViewModel).GetMethod(
            "CalculatePixelPosition", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var (pixelX, pixelY, vis) = ((double x, double y, System.Windows.Visibility vis))
            method!.Invoke(vm, new object[] { originalPos })!;

        Assert.Equal(System.Windows.Visibility.Visible, vis);

        // 3. Act: Reverse transform (Pixel Space -> Game Space)
        var calculatedPos = vm.GetCoordinatesFromPixels(pixelX, pixelY);

        // 4. Assert: Coordinates match within floating-point tolerance
        Assert.NotNull(calculatedPos);
        Assert.Equal(inputX, calculatedPos.Value.X, 2);
        Assert.Equal(inputY, calculatedPos.Value.Y, 2);
    }
}