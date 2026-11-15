using Blasphemous.EditorMode.Lifecycle;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Blasphemous.EditorMode.Components;

internal static class EditorWidgetCleanup
{
    internal static void RestoreUiInput(
        GraphicRaycaster canvasRaycaster,
        bool addedCanvasRaycaster,
        bool canvasRaycasterWasEnabled,
        BaseInputModule[] inputModules,
        bool[] inputModulesWereEnabled,
        StandaloneInputModule standaloneInputModule,
        ref bool standaloneInputModuleWasAdded,
        GameObject fallbackEventSystem)
    {
        if (canvasRaycaster.IsAlive())
            canvasRaycaster.enabled = !addedCanvasRaycaster && canvasRaycasterWasEnabled;

        if (standaloneInputModuleWasAdded && standaloneInputModule.IsAlive())
            standaloneInputModule.enabled = false;
        standaloneInputModuleWasAdded = false;

        if (inputModules != null)
        {
            for (int index = 0; index < inputModules.Length; index++)
            {
                if (inputModules[index].IsAlive())
                    inputModules[index].enabled = inputModulesWereEnabled[index];
            }
        }

        if (fallbackEventSystem.IsAlive())
            fallbackEventSystem.SetActive(false);
    }
}