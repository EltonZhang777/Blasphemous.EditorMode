using System;
using System.Collections.Generic;
using System.Reflection;
using Blasphemous.EditorMode.Components;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.EditorMode.HarmonyPatches;

/// <summary>Lets Editor Mode own the cursor while Debug Mod's hitbox window is suppressed.</summary>
[HarmonyPatch]
internal static class HitboxWindow_PreserveEditorModeCursor_Patch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        System.Type type = AccessTools.TypeByName("Blasphemous.DebugMod.HitboxViewer.HitboxWindow");
        if (type == null)
            yield break;

        MethodBase method = AccessTools.Method(type, "OnGUI", Type.EmptyTypes);
        if (method != null)
            yield return method;
    }

    [HarmonyPrefix]
    private static bool Prefix()
    {
        EditorMode editor = Main.EditorMode;
        if (editor == null || !editor.IsActive)
            return true;

        Cursor.visible = true;
        return false;
    }
}
