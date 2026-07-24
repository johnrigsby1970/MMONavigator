using MMONavigator.Services;
using Xunit;

namespace MMONavigator.Tests;

public class ScrubberTests
{
    [Theory]
    [InlineData("10.5 20.7", "x y", 10.5, 20.7, null, null)]
    [InlineData("10.5 20.7 30.1", "x z y d", 10.5, 30.1, 20.7, null)]
    [InlineData("10.5 20.7 30.1 40.2", "x z y d", 10.5, 30.1, 20.7, 40.2)]
    [InlineData("20.7 10.5", "y x", 10.5, 20.7, null, null)]
    [InlineData("20.7 10.5 30.1", "y x z", 10.5, 20.7, 30.1, null)]
    public void TryParse_ValidInput_ReturnsExpected(string input, string order, double expectedX, double expectedY, double? expectedZ, double? expectedHeading)
    {
        bool success = Scrubber.TryParse(input, order, out var result);
        
        Assert.True(success);
        Assert.Equal(expectedX, result.X, 1);
        Assert.Equal(expectedY, result.Y, 1);
        Assert.Equal(expectedZ, result.Z);
        Assert.Equal(expectedHeading, result.Heading);
    }

    [Fact]
    public void TryParse_CultureInvariant_ParsesDotsRegardlessOfCulture()
    {
        // Even if we are on a system that uses commas, Scrubber.TryParse (via InvariantCulture) should parse dots.
        // We can't easily change the system culture in a unit test, but we can verify that dot parsing works.
        bool success = Scrubber.TryParse("10.5 20.5", "x y", out var result);
        Assert.True(success);
        Assert.Equal(10.5, result.X);
    }

    [Fact]
    public void TryParse_InsufficientComponents_ReturnsFalse()
    {
        // MinCoordinateComponents is 2
        bool success = Scrubber.TryParse("10.5", "x y", out var result);
        Assert.False(success);
    }

    [Fact]
    public void TryParse_MismatchedOrderAndInput_ReturnsExpectedOrHandlesSafely()
    {
        // "y x z" but only 2 numbers provided.
        // The code handles this by defaulting z to 0 if length <= 2
        bool success = Scrubber.TryParse("20.7 10.5", "y x z", out var result);
        Assert.True(success);
        Assert.Equal(10.5, result.X);
        Assert.Equal(20.7, result.Y);
        Assert.Equal(0, result.Z);
    }

    [Fact]
    public void ScrubEntry_FiltersInvalidInput()
    {
        // Numbers separated by comma and space should be scrubbed
        string? input = "10.5, 20.5";
        string? expected = "10.5 20.5";
        Assert.Equal(expected, Scrubber.ScrubEntry(input));
    }

    [Fact]
    public void ScrubEntry_RejectsMixedContent()
    {
        // If numbers are separated by non-comma/non-whitespace text, it should return original string
        string? input = "10.5 and 20.5"; 
        Assert.Equal(input, Scrubber.ScrubEntry(input));
    }
}
