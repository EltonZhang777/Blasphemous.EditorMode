using Blasphemous.ModdingAPI;
using Framework.Managers;
using Gameplay.UI;
using UnityEngine;
using UnityEngine.UI;
using Blasphemous.Framework.UI;

namespace Blasphemous.EditorMode.Components;
internal class EditorWidget : MonoBehaviour
{
    internal GameObject cursorObj;
    internal GameObject freezeGameButtonObj;

    internal Vector2 ScaledMousePosition => new Vector2(
            Input.mousePosition.x * (640f / Screen.width),
            Input.mousePosition.y * (360f / Screen.height));

    private void Awake()
    {
#if DEBUG
        // add test text
        GameObject testTextObj = new("Test Text");
        testTextObj.SetActive(true);
        testTextObj.layer = LayerMask.NameToLayer("UI");
        testTextObj.transform.SetParent(UIController.instance.transform);
        RectTransform testTextRt = testTextObj.AddComponent<RectTransform>();
        testTextRt.sizeDelta = new(200f, 100f);
        testTextRt.anchoredPosition = new(0f, 0f);
        Text testText = testTextObj.AddComponent<Text>();
        testText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        testText.text = "Editor Widget Active";
        ModLog.Warn($"Created test text at {testText.transform.position}, active: {testTextObj.activeSelf}!");
#endif

        // initialize cursor sprite
        cursorObj = new("Cursor");
        cursorObj.SetActive(true);
        cursorObj.layer = LayerMask.NameToLayer("UI");
        cursorObj.transform.SetParent(gameObject.transform);
        RectTransform cursorRt = cursorObj.AddComponent<RectTransform>();
        // set the anchor of the cursor to bottom-left, pivot of the cursor to top-left
        cursorRt.anchorMin = new(0f, 0f);
        cursorRt.anchorMax = new(0f, 0f);
        cursorRt.pivot = new(0f, 1f);
        cursorRt.sizeDelta = new(40f, 40f);
        // add cursor image
        Image cursorImage = cursorObj.AddComponent<Image>();
        cursorImage.sprite = EditorMode.SpriteStorage.Cursor;

        // initialize game freeze button
        freezeGameButtonObj = new("Freeze Game Button");
        freezeGameButtonObj.SetActive(true);
        freezeGameButtonObj.layer = LayerMask.NameToLayer("UI");
        freezeGameButtonObj.transform.SetParent(gameObject.transform);
        RectTransform freezeGameRt = freezeGameButtonObj.AddComponent<RectTransform>();
        freezeGameRt.sizeDelta = new(40f, 40f);
        freezeGameRt.anchoredPosition = new(285f, 75f);
        Image freezeGameButtonImage = freezeGameButtonObj.AddComponent<Image>();
        freezeGameButtonImage.sprite = EditorMode.SpriteStorage.Cursor; // WIP sprite, pending actual sprite
        Button freezeGameButton = freezeGameButtonObj.AddComponent<Button>();
        freezeGameButton.onClick.AddListener(OnFreezeGameButtonClicked);
#if DEBUG
        ModLog.Warn($"freezeGameButtonObj.transform.position: {freezeGameButtonObj.transform.position}");
#endif
    }

    private void Update()
    {
        // move mouse cursor sprite to mouse position
        RectTransform cursorRt = cursorObj.GetComponent<RectTransform>();
        cursorRt.anchoredPosition = ScaledMousePosition;
    }


    private void OnFreezeGameButtonClicked()
    {
        if (UIController.instance.Paused)
        {
            Core.Logic.ResumeGame();
        }
        else
        {
            Core.Logic.PauseGame();
        }
    }
}
