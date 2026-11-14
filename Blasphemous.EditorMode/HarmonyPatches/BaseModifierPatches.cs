using Blasphemous.EditorMode.Runtime;
using Blasphemous.Framework.Levels;
using Blasphemous.Framework.Levels.Modifiers;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.EditorMode.HarmonyPatches;

/// <summary>Marks additions created by Level Framework without touching its source.</summary>
[HarmonyPatch(typeof(BaseModifier), "Apply")]
internal static class BaseModifier_MarkFrameworkAddition_Patch
{
    private static void Postfix(GameObject obj, ObjectData data)
    {
        if (obj == null || data == null
            || !string.IsNullOrEmpty(data.scene)
            || !string.IsNullOrEmpty(data.path)
            || string.IsNullOrEmpty(data.id))
            return;

        RuntimePlacementMarker marker = obj.GetComponent<RuntimePlacementMarker>();
        if (marker != null
            && string.Equals(marker.Source, "EditorMode", System.StringComparison.OrdinalIgnoreCase))
            return;

        if (marker == null)
            marker = obj.AddComponent<RuntimePlacementMarker>();

        marker.PlacementId = data.id;
        marker.Type = data.type;
        marker.Source = "Framework";
    }
}
