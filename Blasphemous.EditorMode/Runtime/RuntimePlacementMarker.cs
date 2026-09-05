using Blasphemous.Framework.Levels;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.EditorMode.Runtime;

/// <summary>Runtime marker used to distinguish overlay additions from original objects.</summary>
internal sealed class RuntimePlacementMarker : MonoBehaviour
{
    internal string PlacementId { get; set; }
    internal string Type { get; set; }
    internal string Source { get; set; }
}

/// <summary>Marks additions created by Level Framework without touching its source.</summary>
[HarmonyPatch(typeof(Blasphemous.Framework.Levels.Modifiers.BaseModifier), nameof(Blasphemous.Framework.Levels.Modifiers.BaseModifier.Apply))]
internal static class FrameworkAdditionMarkerPatch
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
