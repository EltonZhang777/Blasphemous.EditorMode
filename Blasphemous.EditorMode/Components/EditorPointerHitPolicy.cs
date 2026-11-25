using System.Collections.Generic;

namespace Blasphemous.EditorMode.Components;

internal static class EditorPointerHitPolicy
{
    internal static bool IsPointerOverInteractiveControl(IEnumerable<EditorPointerHit> hits)
    {
        if (hits == null)
            return false;

        foreach (EditorPointerHit hit in hits)
        {
            if (hit.IsInteractiveControl)
                return true;
        }

        return false;
    }
}