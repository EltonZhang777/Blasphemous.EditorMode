using Blasphemous.EditorMode.Components;
using Blasphemous.ModdingAPI;
using UnityEngine;

namespace Blasphemous.EditorMode;

public class EditorMode : BlasMod
{
    internal EditorMode() : base(ModInfo.MOD_ID, ModInfo.MOD_NAME, ModInfo.MOD_AUTHOR, ModInfo.MOD_VERSION) { }

    protected override void OnInitialize()
    {
        InputHandler.RegisterDefaultKeybindings(
            new()
            {
                { "OpenEditor", KeyCode.Semicolon },
            });
    }

    private GameObject CreateEditorGameObject()
    {
        GameObject obj = new("Editor Widget");
        obj.transform.SetParent(GameObject.Find("/Game UI/Content").transform);
        obj.AddComponent<EditorWidget>();

        // set the GameObject's position to the middle of the screen
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = new(0.5f, 0.5f);
        rt.anchorMax = new(0.5f, 0.5f);
        rt.pivot = new(0.5f, 0.5f);
        rt.anchoredPosition = new(0f, 0f);
        rt.sizeDelta = new(640f, 360f);

        // deactivate the GameObject after initialization for default.
        obj.SetActive(false);

        return obj;
    }
}
