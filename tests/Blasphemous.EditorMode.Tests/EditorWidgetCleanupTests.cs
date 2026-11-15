using Blasphemous.EditorMode.Components;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class EditorWidgetCleanupTests
{
    [Fact]
    public void DestroyedUnityObjectsAreSkippedDuringCleanup()
    {
        GraphicRaycaster canvasRaycaster = new() { Destroyed = true, enabled = false };
        BaseInputModule inputModule = new() { Destroyed = true, enabled = false };
        StandaloneInputModule standaloneInputModule = new() { Destroyed = true, enabled = true };
        GameObject fallbackEventSystem = new() { Destroyed = true, Active = true };
        bool standaloneInputModuleWasAdded = true;

        EditorWidgetCleanup.RestoreUiInput(
            canvasRaycaster,
            addedCanvasRaycaster: false,
            canvasRaycasterWasEnabled: true,
            [inputModule],
            [true],
            standaloneInputModule,
            ref standaloneInputModuleWasAdded,
            fallbackEventSystem);

        Assert.False(canvasRaycaster.enabled);
        Assert.False(inputModule.enabled);
        Assert.True(standaloneInputModule.enabled);
        Assert.True(fallbackEventSystem.Active);
        Assert.False(standaloneInputModuleWasAdded);
    }

    [Fact]
    public void AvailableUnityObjectsAreRestoredDuringCleanup()
    {
        GraphicRaycaster canvasRaycaster = new() { enabled = false };
        BaseInputModule availableInputModule = new() { enabled = false };
        BaseInputModule destroyedInputModule = new() { Destroyed = true, enabled = false };
        StandaloneInputModule standaloneInputModule = new() { enabled = true };
        GameObject fallbackEventSystem = new() { Active = true };
        bool standaloneInputModuleWasAdded = true;

        EditorWidgetCleanup.RestoreUiInput(
            canvasRaycaster,
            addedCanvasRaycaster: false,
            canvasRaycasterWasEnabled: true,
            [availableInputModule, destroyedInputModule],
            [true, true],
            standaloneInputModule,
            ref standaloneInputModuleWasAdded,
            fallbackEventSystem);

        Assert.True(canvasRaycaster.enabled);
        Assert.True(availableInputModule.enabled);
        Assert.False(destroyedInputModule.enabled);
        Assert.False(standaloneInputModule.enabled);
        Assert.False(fallbackEventSystem.Active);
        Assert.False(standaloneInputModuleWasAdded);
    }
}
