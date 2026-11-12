using Blasphemous.EditorMode.Runtime;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class ScreenCoordinateMapperTests
{
    [Theory]
    [InlineData(1920, 1080, 960f, 540f, 640, 360, 320f, 180f)]
    [InlineData(1280, 800, 640f, 400f, 640, 360, 320f, 180f)]
    public void Maps_screen_position_to_target_texture_dimensions(
        int screenWidth,
        int screenHeight,
        float screenX,
        float screenY,
        int targetWidth,
        int targetHeight,
        float expectedX,
        float expectedY)
    {
        ScreenCoordinateMapper.MapToTargetPixels(
            screenX,
            screenY,
            screenWidth,
            screenHeight,
            targetWidth,
            targetHeight,
            out float targetX,
            out float targetY);

        Assert.InRange(targetX, expectedX - 0.001f, expectedX + 0.001f);
        Assert.InRange(targetY, expectedY - 0.001f, expectedY + 0.001f);
    }

    [Fact]
    public void Preserves_position_when_dimensions_are_invalid()
    {
        ScreenCoordinateMapper.MapToTargetPixels(
            12.5f,
            24.5f,
            0,
            1080,
            640,
            360,
            out float targetX,
            out float targetY);

        Assert.Equal(12.5f, targetX);
        Assert.Equal(24.5f, targetY);
    }
}
