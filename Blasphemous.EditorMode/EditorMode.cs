using Blasphemous.EditorMode.Components;
using Blasphemous.ModdingAPI;
using Gameplay.UI;
using Blasphemous.Framework.UI;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.EditorMode;

internal class EditorMode : BlasMod
{

    internal EditorWidget EditorWidget { get; private set; }
    internal static SpriteStorage SpriteStorage { get; private set; }

    private static bool _isStartingEditorWidget = false;

    internal EditorMode() : base(ModInfo.MOD_ID, ModInfo.MOD_NAME, ModInfo.MOD_AUTHOR, ModInfo.MOD_VERSION) { }

    protected override void OnInitialize()
    {
        InputHandler.RegisterDefaultKeybindings(
            new()
            {
                { "OpenEditor", KeyCode.Semicolon },
            });
        SpriteStorage = new(FileHandler);
    }

    protected override void OnLevelLoaded(string oldLevel, string newLevel)
    {
        if (oldLevel == "MainMenu")
        {
            SafeCreateEditorAfterDelay(1.5f);
        }
    }

    protected override void OnUpdate()
    {
        if (InputHandler.GetKeyDown("OpenEditor"))
        {
            SafeCreateEditorAfterDelay(1.5f);
            if (EditorWidget != null)
            {
                EditorWidget.gameObject.SetActive(!EditorWidget.gameObject.activeSelf);
                if (EditorWidget.gameObject.activeSelf)
                {
                    EditorWidget.gameObject.transform.SetAsLastSibling();
                }

#if DEBUG
                Cursor.visible = EditorWidget.gameObject.activeInHierarchy;
                ModLog.Warn($"{(EditorWidget.gameObject.activeSelf ? "Activated" : "Deactivated")} editor widget!");
                ModLog.Warn($"Active in hierarchy? {EditorWidget.gameObject.activeInHierarchy}");
                ModLog.Warn($"Cursor lock state: {Cursor.lockState}, visibility: {Cursor.visible}, position: {Input.mousePosition}");
#endif
            }
        }
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
