using UnityEngine;

namespace Blasphemous.EditorMode.Runtime;

internal static class UnityScreenCoordinateMapper
{
    internal static Vector2 ToCameraPixels(Camera camera, Vector2 screenPosition)
    {
        if (camera == null || camera.targetTexture == null)
            return screenPosition;

        ScreenCoordinateMapper.MapToTargetPixels(
            screenPosition.x,
            screenPosition.y,
            Screen.width,
            Screen.height,
            camera.targetTexture.width,
            camera.targetTexture.height,
            out float targetX,
            out float targetY);
        return new Vector2(targetX, targetY);
    }

    internal static Vector3 ToWorldPosition(Camera camera, Vector2 cameraPixels)
    {
        if (camera == null)
            return Vector3.zero;

        Vector3 world = camera.ScreenToWorldPoint(new Vector3(
            cameraPixels.x,
            cameraPixels.y,
            Mathf.Abs(camera.transform.position.z)));
        world.z = 0f;
        return world;
    }
}
