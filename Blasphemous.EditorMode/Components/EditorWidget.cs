using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Editing;
using Blasphemous.ModdingAPI;
using Blasphemous.Framework.UI;
using Framework.Managers;
using Gameplay.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Blasphemous.EditorMode.Components;

/// <summary>Minimal P0 canvas for browsing and commanding the current Room Overlay.</summary>
[DefaultExecutionOrder(10000)]
internal class EditorWidget : MonoBehaviour
{
    internal GameObject freezeGameButtonObj;

    private RectTransform _root;
    private Canvas _canvas;
    private GraphicRaycaster _canvasRaycaster;
    private GameObject _fallbackEventSystem;
    private BaseInputModule[] _inputModulesBeforeEditor;
    private bool[] _inputModulesWereEnabled;
    private StandaloneInputModule _standaloneInputModule;
    private bool _standaloneInputModuleWasAdded;
    private bool _addedCanvasRaycaster;
    private bool _canvasRaycasterWasEnabled;
    private Text _statusText;
    private Transform _catalogRoot;
    private Transform _propertyRoot;
    private InputField[] _positionFields;
    private InputField[] _rotationFields;
    private InputField[] _scaleFields;
    private string _selectedId;
    private bool _catalogBuilt;

    private void Awake()
    {
        _root = GetComponent<RectTransform>();
        _root = _root ?? gameObject.AddComponent<RectTransform>();
        _root.sizeDelta = new Vector2(640f, 420f);
        _canvas = GetComponentInParent<Canvas>();

        Image background = gameObject.GetComponent<Image>();
        if (background == null)
            background = gameObject.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.78f);
        background.raycastTarget = true;

        _statusText = CreateText(
            "Editor Status",
            new Vector2(0f, 165f),
            new Vector2(600f, 90f),
            13,
            TextAnchor.UpperLeft);

        CreateText(
            "Catalog Header",
            new Vector2(-195f, 108f),
            new Vector2(230f, 28f),
            18,
            TextAnchor.MiddleLeft).text = "Content Catalog";
        _catalogRoot = new GameObject("Catalog").transform;
        _catalogRoot.SetParent(transform, false);
        _catalogRoot.localScale = Vector3.one;

        CreateText(
            "Inspector Header",
            new Vector2(150f, 108f),
            new Vector2(250f, 28f),
            18,
            TextAnchor.MiddleLeft).text = "Inspector / Selection";

        CreateText("Position Label", new Vector2(100f, 78f), new Vector2(72f, 22f), 12, TextAnchor.MiddleCenter).text = "Position";
        CreateText("Rotation Label", new Vector2(190f, 78f), new Vector2(72f, 22f), 12, TextAnchor.MiddleCenter).text = "Rotation";
        CreateText("Scale Label", new Vector2(280f, 78f), new Vector2(72f, 22f), 12, TextAnchor.MiddleCenter).text = "Scale";
        _positionFields = CreateTransformFields("Position", 100f);
        _rotationFields = CreateTransformFields("Rotation", 190f);
        _scaleFields = CreateTransformFields("Scale", 280f);
        CreateButton("Apply Transform", "Apply", new Vector2(190f, -38f), ApplyTransform);
        _propertyRoot = new GameObject("Properties").transform;
        _propertyRoot.SetParent(transform, false);

        CreateButton("Import", "Import", new Vector2(-235f, -178f), () => Main.EditorMode.ImportCurrentRoom());
        CreateButton("Export", "Export", new Vector2(-155f, -178f), () => Main.EditorMode.ExportCurrentRoom());
        CreateButton("Undo", "Undo", new Vector2(-75f, -178f), () => Main.EditorMode.TryUndo());
        CreateButton("Redo", "Redo", new Vector2(5f, -178f), () => Main.EditorMode.TryRedo());
        CreateButton("Copy", "Copy Ctrl+C", new Vector2(85f, -178f), () => Main.EditorMode.TryCopySelected());
        CreateButton("Paste", "Paste Ctrl+V", new Vector2(165f, -178f), () => Main.EditorMode.TryPasteAtCursor());
        CreateButton("Delete", "Delete", new Vector2(245f, -178f), () => Main.EditorMode.TryDeleteSelected());
        CreateButton("Lock", "Lock", new Vector2(-155f, -142f), () => Main.EditorMode.TryToggleSelectedLock());
        CreateButton("Reset", "Reset", new Vector2(-75f, -142f), () => Main.EditorMode.TryReset());
        CreateButton("Exit", "Exit", new Vector2(5f, -142f), () => Main.EditorMode.ExitFromUi());
        freezeGameButtonObj = CreateButton(
            "Freeze Game Button",
            "Freeze",
            new Vector2(85f, -142f),
            OnFreezeGameButtonClicked).gameObject;
        CreateButton("Pick", "Pick P", new Vector2(165f, -142f), () => Main.EditorMode.TryPickSelected());
        CreateButton("Clear Preview", "Clear", new Vector2(245f, -142f), () => Main.EditorMode.ClearPreview());
    }

    private void OnEnable()
    {
        EnsureUiInput();
        Refresh();
        RefreshFreezeButton();
    }

    private void OnDisable()
    {
        if (_canvasRaycaster != null)
            _canvasRaycaster.enabled = !_addedCanvasRaycaster && _canvasRaycasterWasEnabled;

        if (_standaloneInputModuleWasAdded && _standaloneInputModule != null)
        {
            _standaloneInputModule.enabled = false;
            _standaloneInputModuleWasAdded = false;
        }

        if (_inputModulesBeforeEditor != null)
        {
            for (int i = 0; i < _inputModulesBeforeEditor.Length; i++)
            {
                if (_inputModulesBeforeEditor[i] != null)
                    _inputModulesBeforeEditor[i].enabled = _inputModulesWereEnabled[i];
            }
        }

        _inputModulesBeforeEditor = null;
        _inputModulesWereEnabled = null;
        _standaloneInputModule = null;
        if (_fallbackEventSystem != null)
            _fallbackEventSystem.SetActive(false);
    }

    private void Update()
    {
        EditorMode editor = Main.EditorMode;
        if (editor == null)
            return;

        editor.UpdatePreviewAtCursor();

        if (IsInputFieldFocused())
            return;

        if (editor.IsInputActionPressed("PickPlacement"))
        {
            if (IsPointerOverEditorControl())
            {
                editor.ReportPickBlockedByEditorControl();
                return;
            }

            editor.TryPickAtCursor();
            return;
        }

        if (editor.IsInputActionPressed("PlacePreview"))
        {
            if (!IsPointerOverEditorControl())
                editor.TryPlacePreviewAtCursor();
            return;
        }

        bool controlPressed = editor.IsInputActionHeld("LeftControl")
            || editor.IsInputActionHeld("RightControl");

        if (controlPressed && editor.IsInputActionPressed("CopyPlacement"))
        {
            editor.TryCopySelected();
            return;
        }

        if (controlPressed && editor.IsInputActionPressed("PastePlacement"))
        {
            editor.TryPasteAtCursor();
            return;
        }

        if (editor.IsInputActionPressed("SelectPlacement") && !IsPointerOverEditorControl())
        {
            editor.TrySelectAtCursor();
        }
    }

    private bool IsPointerOverEditorControl()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return false;

        PointerEventData pointer = new PointerEventData(eventSystem);
        pointer.position = Input.mousePosition;
        List<RaycastResult> raycastResults = new List<RaycastResult>();
        eventSystem.RaycastAll(pointer, raycastResults);

        foreach (RaycastResult result in raycastResults)
        {
            GameObject hit = result.gameObject;
            if (hit != null
                && hit.transform.IsChildOf(transform)
                && hit.GetComponentInParent<Selectable>() != null)
                return true;
        }

        return false;
    }

    internal void Refresh()
    {
        EditorMode editor = Main.EditorMode;
        if (editor == null)
            return;

        if (!_catalogBuilt && editor.Catalog != null)
            BuildCatalog(editor.Catalog);

        if (_statusText == null)
            return;

        string room = editor.CurrentRoom == null
            ? "<none>"
            : editor.CurrentRoom.FrameworkLevelKey + editor.CurrentRoom.TargetSceneSuffix;
        string state = editor.Session == null ? "not-ready" : editor.Session.State.ToString();
        string selected = editor.PlacementManager == null || editor.PlacementManager.Selected == null
            ? "<none>"
            : editor.PlacementManager.Selected.Id;
        PlacementRecord selectedPlacement = editor.PlacementManager == null
            ? null
            : editor.PlacementManager.Selected;
        string selectedPlacementId = selectedPlacement == null ? null : selectedPlacement.Id;
        if (!string.Equals(_selectedId, selectedPlacementId, StringComparison.OrdinalIgnoreCase))
        {
            _selectedId = selectedPlacementId;
            SetTransformFields(selectedPlacement);
            BuildPropertyEditor(editor, selectedPlacement);
        }
        string dependency = Main.DependencyStatus == null
            ? "unknown"
            : Main.DependencyStatus.IsCompatible ? "compatible" : "blocked";
        string locked = selectedPlacement == null
            ? "—"
            : selectedPlacement.IsLocked ? "yes" : "no";
        ApplyReportSurface reportSurface = editor.ReportSurface;
        string report = reportSurface == null
            ? ""
            : reportSurface.UiText;
        string status = string.Format(
            "Room: {0}   Session: {1}\nSelected: {2}   Locked: {3}\nDependencies: {4}   Preview: {5}\nClipboard: {6}   Last: {7}",
            room,
            state,
            selected,
            locked,
            dependency,
            editor.PreviewStatus,
            editor.ClipboardStatus,
            string.IsNullOrEmpty(report) ? "—" : report);
        if (reportSurface != null && !string.IsNullOrEmpty(reportSurface.ActionableMessage))
            status += "\n" + reportSurface.ActionableMessage;
        _statusText.text = status;
    }

    private static bool IsInputFieldFocused()
    {
        GameObject selected = EventSystem.current == null
            ? null
            : EventSystem.current.currentSelectedGameObject;
        return selected != null && selected.GetComponentInParent<InputField>() != null;
    }

    private InputField[] CreateTransformFields(string group, float x)
    {
        InputField[] fields = new InputField[3];
        string[] axes = { "X", "Y", "Z" };
        for (int index = 0; index < fields.Length; index++)
        {
            fields[index] = CreateInputField(
                group + " " + axes[index],
                new Vector2(x, 50f - (index * 30f)),
                transform);
        }

        return fields;
    }

    private void SetTransformFields(PlacementRecord placement)
    {
        if (placement == null)
        {
            SetFieldValues(_positionFields, new string[3]);
            SetFieldValues(_rotationFields, new string[3]);
            SetFieldValues(_scaleFields, new string[3]);
            return;
        }

        SetFieldValues(
            _positionFields,
            new[]
            {
                placement.Transform.PositionX.ToString("R", CultureInfo.InvariantCulture),
                placement.Transform.PositionY.ToString("R", CultureInfo.InvariantCulture),
                placement.Transform.PositionZ.ToString("R", CultureInfo.InvariantCulture)
            });
        SetFieldValues(
            _rotationFields,
            new[]
            {
                placement.Transform.RotationX.ToString("R", CultureInfo.InvariantCulture),
                placement.Transform.RotationY.ToString("R", CultureInfo.InvariantCulture),
                placement.Transform.RotationZ.ToString("R", CultureInfo.InvariantCulture)
            });
        SetFieldValues(
            _scaleFields,
            new[]
            {
                placement.Transform.ScaleX.ToString("R", CultureInfo.InvariantCulture),
                placement.Transform.ScaleY.ToString("R", CultureInfo.InvariantCulture),
                placement.Transform.ScaleZ.ToString("R", CultureInfo.InvariantCulture)
            });
    }

    private static void SetFieldValues(InputField[] fields, string[] values)
    {
        if (fields == null)
            return;

        for (int index = 0; index < fields.Length; index++)
            fields[index].text = values == null || index >= values.Length ? string.Empty : values[index] ?? string.Empty;
    }

    private void ApplyTransform()
    {
        if (Main.EditorMode == null)
            return;

        string[] values = new string[9];
        CopyFieldValues(_positionFields, values, 0);
        CopyFieldValues(_rotationFields, values, 3);
        CopyFieldValues(_scaleFields, values, 6);
        Main.EditorMode.TryApplySelectedTransform(values);
    }

    private static void CopyFieldValues(InputField[] fields, string[] target, int offset)
    {
        for (int index = 0; index < fields.Length; index++)
            target[offset + index] = fields[index] == null ? string.Empty : fields[index].text;
    }

    private void BuildPropertyEditor(EditorMode editor, PlacementRecord placement)
    {
        if (_propertyRoot == null)
            return;

        for (int index = _propertyRoot.childCount - 1; index >= 0; index--)
            UnityEngine.Object.Destroy(_propertyRoot.GetChild(index).gameObject);

        if (placement == null)
        {
            CreateText(
                "No Selection Properties",
                new Vector2(190f, -76f),
                new Vector2(220f, 28f),
                12,
                TextAnchor.MiddleLeft,
                _propertyRoot).text = "Select an owned placement.";
            return;
        }

        CreatorMetadata metadata;
        if (editor.Catalog == null || !editor.Catalog.TryGet(placement.Type, out metadata)
            || metadata.PropertyDefinitions.Count == 0)
        {
            CreateText(
                "No Properties",
                new Vector2(190f, -76f),
                new Vector2(220f, 28f),
                12,
                TextAnchor.MiddleLeft,
                _propertyRoot).text = "No editable properties.";
            return;
        }

        int row = 0;
        foreach (CreatorPropertyDefinition definition in metadata.PropertyDefinitions)
        {
            float y = -76f - (row++ * 32f);
            CreatorPropertyDefinition property = definition;
            CreateText(
                "Property " + property.Key,
                new Vector2(72f, y),
                new Vector2(90f, 26f),
                11,
                TextAnchor.MiddleLeft,
                _propertyRoot).text = property.Key + " (" + property.Type + ")";
            InputField input = CreateInputField(
                "Value " + property.Key,
                new Vector2(190f, y),
                _propertyRoot);
            input.contentType = InputField.ContentType.Standard;
            input.text = FindPropertyValue(placement, property.Key);
            CreateButton(
                "Apply " + property.Key,
                "Set",
                new Vector2(280f, y),
                () => Main.EditorMode.TrySetSelectedProperty(property.Key, input.text),
                _propertyRoot);
        }
    }

    private static string FindPropertyValue(PlacementRecord placement, string key)
    {
        string prefix = key + "=";
        foreach (string property in placement.Properties)
        {
            if (property != null && property.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return property.Substring(prefix.Length);
            if (string.Equals(property, key, StringComparison.OrdinalIgnoreCase))
                return property;
        }

        return string.Empty;
    }

    private void BuildCatalog(ContentCatalog catalog)
    {
        _catalogBuilt = true;
        int index = 0;
        foreach (CreatorMetadata metadata in catalog.Search(null, null))
        {
            CreatorMetadata entry = metadata;
            float y = 74f - (index++ * 36f);
            CreateText(
                "Catalog Info " + entry.Type,
                new Vector2(-70f, y),
                new Vector2(120f, 34f),
                8,
                TextAnchor.MiddleLeft,
                _catalogRoot).text = string.Format(
                    "{0}\n{1} | {2}\n{3}/{4}",
                    entry.DisplayName,
                    entry.Category,
                    entry.TransformCapabilities,
                    entry.SourceScene,
                    entry.SourcePath);
            CreateButton(
                "Preview " + entry.Type,
                "Preview",
                new Vector2(-240f, y),
                () => Main.EditorMode.TryPreviewAtCursor(entry.Type),
                _catalogRoot);
            CreateButton(
                "Place " + entry.Type,
                "Place",
                new Vector2(-160f, y),
                () => Main.EditorMode.TryPlaceAtCursor(entry.Type),
                _catalogRoot);
        }

        if (index == 0)
        {
            CreateText(
                "No Content",
                new Vector2(-195f, 70f),
                new Vector2(230f, 32f),
                14,
                TextAnchor.MiddleLeft).text = "No verified creator";
        }
    }

    private void EnsureUiInput()
    {
        if (_canvas != null)
        {
            _canvasRaycaster = _canvas.GetComponent<GraphicRaycaster>();
            if (_canvasRaycaster == null)
            {
                _canvasRaycaster = _canvas.gameObject.AddComponent<GraphicRaycaster>();
                _addedCanvasRaycaster = true;
            }

            _canvasRaycasterWasEnabled = _canvasRaycaster.enabled;
            _canvasRaycaster.enabled = true;
        }

        EventSystem eventSystem = EventSystem.current;
        bool createdFallbackEventSystem = eventSystem == null;
        if (createdFallbackEventSystem)
        {
            if (_fallbackEventSystem == null)
            {
                _fallbackEventSystem = new GameObject("Editor Mode Event System");
                _fallbackEventSystem.transform.SetParent(transform, false);
                _fallbackEventSystem.SetActive(false);
                _fallbackEventSystem.AddComponent<EventSystem>();
                _fallbackEventSystem.AddComponent<StandaloneInputModule>();
            }

            _fallbackEventSystem.SetActive(true);
            eventSystem = _fallbackEventSystem.GetComponent<EventSystem>();
        }
        else if (_fallbackEventSystem != null && eventSystem.gameObject != _fallbackEventSystem)
            _fallbackEventSystem.SetActive(false);

        _inputModulesBeforeEditor = eventSystem.GetComponents<BaseInputModule>();
        _inputModulesWereEnabled = new bool[_inputModulesBeforeEditor.Length];
        string previousInputModule = null;
        for (int i = 0; i < _inputModulesBeforeEditor.Length; i++)
        {
            BaseInputModule module = _inputModulesBeforeEditor[i];
            if (module == null)
                continue;

            _inputModulesWereEnabled[i] = module.enabled;
            if (module.enabled && previousInputModule == null)
                previousInputModule = module.GetType().Name;

            if (_standaloneInputModule == null && module.GetType() == typeof(StandaloneInputModule))
                _standaloneInputModule = (StandaloneInputModule)module;
        }

        if (_standaloneInputModule == null)
        {
            _standaloneInputModule = eventSystem.gameObject.AddComponent<StandaloneInputModule>();
            _standaloneInputModuleWasAdded = true;
        }

        for (int i = 0; i < _inputModulesBeforeEditor.Length; i++)
        {
            BaseInputModule module = _inputModulesBeforeEditor[i];
            if (module != null && module != _standaloneInputModule)
                module.enabled = false;
        }

        _standaloneInputModule.enabled = true;
        ModLog.Info(string.Format(
            "Editor Mode is using the Unity StandaloneInputModule for pointer input (previous module: {0}).",
            previousInputModule ?? "none"));

        Debug.Assert(_canvas == null || (_canvasRaycaster != null && _canvasRaycaster.enabled));
        Debug.Assert(_standaloneInputModule.isActiveAndEnabled);
        for (int i = 0; i < _inputModulesBeforeEditor.Length; i++)
        {
            BaseInputModule module = _inputModulesBeforeEditor[i];
            if (module != null && module != _standaloneInputModule)
                Debug.Assert(!module.isActiveAndEnabled);
        }
    }

    private Text CreateText(
        string name,
        Vector2 position,
        Vector2 size,
        int fontSize,
        TextAnchor alignment,
        Transform parent = null)
    {
        GameObject obj = new GameObject(name);
        obj.layer = LayerMask.NameToLayer("UI");
        obj.transform.SetParent(parent == null ? transform : parent, false);
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Text text = obj.AddComponent<Text>();
        text.font = UIModder.Fonts.Blasphemous ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private InputField CreateInputField(string name, Vector2 position, Transform parent)
    {
        GameObject obj = new GameObject(name);
        obj.layer = LayerMask.NameToLayer("UI");
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(72f, 24f);
        Image image = obj.AddComponent<Image>();
        image.color = Color.white;
        InputField input = obj.AddComponent<InputField>();
        input.targetGraphic = image;
        input.contentType = InputField.ContentType.DecimalNumber;
        input.lineType = InputField.LineType.SingleLine;
        Text text = CreateChildText(obj.transform, string.Empty);
        text.color = Color.black;
        text.alignment = TextAnchor.MiddleCenter;
        input.textComponent = text;
        return input;
    }

    private Button CreateButton(
        string name,
        string label,
        Vector2 position,
        UnityAction action,
        Transform parent = null)
    {
        GameObject obj = new GameObject(name);
        obj.layer = LayerMask.NameToLayer("UI");
        obj.transform.SetParent(parent == null ? transform : parent, false);
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(72f, 30f);
        Image image = obj.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.75f);
        Button button = obj.AddComponent<Button>();
        button.interactable = true;
        if (action != null)
            button.onClick.AddListener(action);

        Text text = CreateChildText(obj.transform, label);
        text.color = Color.black;
        return button;
    }

    private static Text CreateChildText(Transform parent, string contents)
    {
        GameObject textObject = new GameObject("Label");
        textObject.layer = LayerMask.NameToLayer("UI");
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Text text = textObject.AddComponent<Text>();
        text.font = UIModder.Fonts.Blasphemous ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 13;
        text.alignment = TextAnchor.MiddleCenter;
        text.text = contents;
        text.raycastTarget = false;
        return text;
    }

    private void OnFreezeGameButtonClicked()
    {
        LogicManager logic = Core.Logic;
        if (logic == null)
            return;

        bool paused = UIController.instance != null
            ? UIController.instance.Paused
            : logic.IsPaused;
        if (paused)
        {
            ModLog.Info("Resuming game...");
            logic.ResumeGame();
            EditorMode editor = Main.EditorMode;
            string error = null;
            if (editor == null || !editor.TryResumeGameplay(out error))
            {
                logic.PauseGame();
                ModLog.Error(error ?? "Editor Mode is unavailable while resuming gameplay.");
            }
        }
        else
        {
            ModLog.Info("Pausing game...");
            logic.PauseGame();
        }

        RefreshFreezeButton();
    }

    private void RefreshFreezeButton()
    {
        if (freezeGameButtonObj == null || Core.Logic == null)
            return;

        bool paused = UIController.instance != null
            ? UIController.instance.Paused
            : Core.Logic.IsPaused;
        Text label = freezeGameButtonObj.GetComponentInChildren<Text>();
        if (label != null)
            label.text = paused ? "Resume" : "Freeze";
    }
}
