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

    private void Awake()
    {
#if DEBUG
        // output absolute position of editor widget
        ModLog.Warn($"Editor widget absolute position: {transform.position}");

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
        cursorImage.raycastTarget = false;

        // initialize game freeze button
        freezeGameButtonObj = new("Freeze Game Button");
        freezeGameButtonObj.SetActive(true);
        freezeGameButtonObj.layer = LayerMask.NameToLayer("UI");
        freezeGameButtonObj.transform.SetParent(gameObject.transform);
        freezeGameButtonObj.transform.SetAsLastSibling();
        RectTransform freezeGameRt = freezeGameButtonObj.AddComponent<RectTransform>();
        freezeGameRt.sizeDelta = new(140f, 40f);
        freezeGameRt.anchoredPosition = new(250f, 75f);
        freezeGameRt.anchorMin = new(0.5f, 0.5f);
        freezeGameRt.anchorMax = new(0.5f, 0.5f);
        freezeGameRt.pivot = new(0.5f, 0.5f);
        Image freezeGameButtonImage = freezeGameButtonObj.AddComponent<Image>();
        freezeGameButtonImage.color = new Color(1f, 1f, 1f, 0.6f);
        Button freezeGameButton = freezeGameButtonObj.AddComponent<Button>();
        freezeGameButton.interactable = true;
        freezeGameButton.onClick.AddListener(OnFreezeGameButtonClicked);

        // add text to the button
        GameObject freezeGameText = new("Freeze Game Text");
        freezeGameText.transform.SetParent(freezeGameButtonObj.transform);
        RectTransform textRt = freezeGameText.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
        Text text = freezeGameText.AddComponent<Text>();
        text.text = "Freeze Game";
        text.font = UIModder.Fonts.Blasphemous;
        text.fontSize = 20;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.black;
#if DEBUG
        ModLog.Warn($"freezeGameButtonObj.transform.position: {freezeGameButtonObj.transform.position}");
#endif
    }

    private void Update()
    {
        // move mouse cursor sprite to mouse position
        RectTransform cursorRt = cursorObj.GetComponent<RectTransform>();
        cursorRt.anchoredPosition = Input.mousePosition;
    }


    private void OnFreezeGameButtonClicked()
    {
        if (UIController.instance.Paused)
        {
            ModLog.Info("Resuming game...");
            Core.Logic.ResumeGame();
        }
        else
        {
            ModLog.Info("Pausing game...");
            Core.Logic.PauseGame();
        }
    }
}
