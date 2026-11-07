namespace Blasphemous.EditorMode.Runtime;

internal static class ScreenCoordinateMapper
{
    internal static void MapToTargetPixels(
        float screenX,
        float screenY,
        int screenWidth,
        int screenHeight,
        int targetWidth,
        int targetHeight,
        out float targetX,
        out float targetY)
    {
        targetX = screenX;
        targetY = screenY;
        if (screenWidth <= 0 || screenHeight <= 0 || targetWidth <= 0 || targetHeight <= 0)
            return;

        targetX = screenX * targetWidth / screenWidth;
        targetY = screenY * targetHeight / screenHeight;
    }
}
