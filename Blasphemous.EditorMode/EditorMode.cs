using Blasphemous.EditorMode.Components;
using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Lifecycle;
using Blasphemous.ModdingAPI;
using Gameplay.UI;
using Blasphemous.Framework.UI;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Blasphemous.EditorMode;

internal class EditorMode : BlasMod
{

    internal EditorWidget EditorWidget { get; private set; }
    internal static SpriteStorage SpriteStorage { get; private set; }

    private static bool _isStartingEditorWidget = false;
    private BlasphemousLifecycleBridge _lifecycleBridge;
    private EditorModeGuard _editorGuard;

    internal EditorMode() : base(ModInfo.MOD_ID, ModInfo.MOD_NAME, ModInfo.MOD_AUTHOR, ModInfo.MOD_VERSION) { }

    protected override void OnInitialize()
    {
        _editorGuard = new EditorModeGuard(
            BlasphemousEditorRuntimeState.Capture,
            BlasphemousEditorRuntimeState.Apply);
        _lifecycleBridge = new BlasphemousLifecycleBridge(
            room => ModLog.Info("Room Overlay ready: " + room.FrameworkLevelKey),
            room =>
            {
                ExitEditorMode();
                ModLog.Info("Room Overlay cleared: " + room.FrameworkLevelKey);
            });

        InputHandler.RegisterDefaultKeybindings(
            new()
            {
                { "OpenEditor", KeyCode.Semicolon },
            });
        SpriteStorage = new(FileHandler);
    }

    protected override void OnLevelPreloaded(string oldLevel, string newLevel)
    {
        _lifecycleBridge.OnLevelPreloaded(oldLevel, newLevel);
    }

    protected override void OnLevelLoaded(string oldLevel, string newLevel)
    {
        bool roomLoaded = _lifecycleBridge.OnLevelLoaded(
            newLevel,
            SceneManager.GetActiveScene().name);

        if (roomLoaded && oldLevel == "MainMenu")
        {
            SafeCreateEditorAfterDelay(1.5f);
        }
    }

    protected override void OnLevelUnloaded(string oldLevel, string newLevel)
    {
        _lifecycleBridge.OnLevelUnloaded(oldLevel, newLevel);
    }

    protected override void OnAllInitialized()
    {
        CreatorProbeResult result = RuntimeCreatorProbe.Inspect(BaseCreatorCatalog.Entries);
        ModLog.Info(result.Message);
        foreach (CreatorProbeDiagnostic diagnostic in result.Diagnostics)
            ModLog.Warn(diagnostic.Message);
    }

    protected override void OnUpdate()
    {
        if (InputHandler.GetKeyDown("OpenEditor"))
            ToggleEditorMode();
    }

    private void ToggleEditorMode()
    {
        if (_editorGuard.IsActive)
        {
            ExitEditorMode();
            return;
        }

        if (!IsDebug)
        {
            ModLog.Warn("Editor Mode activation rejected: debug gate failed.");
            return;
        }

        if (!_lifecycleBridge.TryBeginEditing())
        {
            ModLog.Warn("Editor Mode activation rejected: no compatible loaded room.");
            return;
        }

        if (EditorWidget == null)
        {
            _lifecycleBridge.EndEditing();
            SafeCreateEditorAfterDelay(1.5f);
            ModLog.Warn("Editor Mode activation deferred until its widget is ready.");
            return;
        }

        string error;
        if (!_editorGuard.TryEnter(true, true, CreateEditorState(), out error))
        {
            _lifecycleBridge.EndEditing();
            ModLog.Warn(error);
            return;
        }

        try
        {
            EditorWidget.gameObject.SetActive(true);
            EditorWidget.gameObject.transform.SetAsLastSibling();
        }
        catch (System.Exception exception)
        {
            if (!_editorGuard.TryExit(out error))
                ModLog.Error(error);
            _lifecycleBridge.EndEditing();
            ModLog.Error("Editor Mode widget activation failed: " + exception.Message);
        }
    }

    private void ExitEditorMode()
    {
        string error;
        if (!_editorGuard.TryExit(out error))
        {
            ModLog.Error(error);
            return;
        }

        _lifecycleBridge.EndEditing();
        if (EditorWidget != null)
            EditorWidget.gameObject.SetActive(false);
    }

    private static EditorRuntimeState CreateEditorState()
    {
        return new EditorRuntimeState(
            0f,
            true,
            true,
            false,
            false,
            false,
            false,
            true,
            false,
            false,
            false);
    }

    private GameObject CreateEditorGameObject()
    {
        GameObject obj = new("Editor Widget");

        // Add and configure RectTransform BEFORE setting parent
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.sizeDelta = new(640f, 360f);
        rt.anchorMin = new(0.5f, 0.5f);
        rt.anchorMax = new(0.5f, 0.5f);
        rt.pivot = new(0.5f, 0.5f);
        rt.anchoredPosition = new(-640f, -360f);

        obj.transform.SetParent(UIModder.Parents.CanvasHighRes, false);
        obj.layer = LayerMask.NameToLayer("UI");
        obj.transform.SetAsLastSibling();

        obj.AddComponent<EditorWidget>();

        // deactivate the GameObject after initialization for default.
        obj.SetActive(false);
#if DEBUG
        ModLog.Warn($"Created editor widget at anchor position `{rt.anchoredPosition}`, absolute position {rt.position}!");
        ModLog.Warn($"Editor widget anchor min: `{rt.anchorMin}`, pivot {rt.pivot}!");
#endif

        return obj;
    }

    private IEnumerator DelayAndCreateEditorGameObjectCoroutine(float delaySeconds)
    {
        _isStartingEditorWidget = true;
        yield return new WaitForSeconds(delaySeconds);
        EditorWidget = CreateEditorGameObject().GetComponent<EditorWidget>();
        _isStartingEditorWidget = false;
    }

    private void SafeCreateEditorAfterDelay(float delaySeconds)
    {
        if ((EditorWidget == null || GameObject.Find("Editor Widget") == null) && !_isStartingEditorWidget)
        {
            UIController.instance.StartCoroutine(DelayAndCreateEditorGameObjectCoroutine(delaySeconds));
        }
    }
}
