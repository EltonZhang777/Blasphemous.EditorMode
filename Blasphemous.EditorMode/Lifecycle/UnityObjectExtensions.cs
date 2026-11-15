using UnityEngine;

namespace Blasphemous.EditorMode.Lifecycle;

internal static class UnityObjectExtensions
{
    internal static bool IsAlive(this UnityEngine.Object value)
    {
        return value != null;
    }
}