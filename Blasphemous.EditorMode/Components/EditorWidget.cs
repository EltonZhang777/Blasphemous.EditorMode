using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Editing;
using Blasphemous.EditorMode.Framework;
using Blasphemous.Framework.UI;
using Blasphemous.ModdingAPI;
using Framework.Managers;
using Gameplay.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Blasphemous.EditorMode.Components;

/// <summary>Minimal P0 canvas for browsing and commanding the current Room Overlay.</summary>
[DefaultExecutionOrder(10000)]
internal class EditorWidget : MonoBehaviour
{
    private const int StatusFontSize = 13;
    private const int StatusMinimumFontSize = 10;

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
    private Text _originalStatusText;
    private GameObject _pageButtonObject;
    private Transform _additionsPageRoot;
    private Transform _originalPageRoot;
    private Transform _defaultParent;
    private Transform _catalogRoot;
    private Transform _candidateRoot;
    private Transform _originalCandidateRoot;
    private ScrollRect _originalCandidateScroll;
    private Transform _originalOperationsRoot;
    private ScrollRect _originalOperationsScroll;
    private Text _originalInspectorText;
    private Transform _propertyRoot;
    private RectTransform _propertiesViewport;
    private RectTransform _propertiesContent;
    private InputField _propertiesField;
    private Text _propertiesWarning;
    private Button _propertiesApplyButton;
    private readonly RawPropertiesDraftState _propertiesDraft = new();
    private bool _settingPropertiesText;
    private readonly TextGenerator _propertiesLayoutGenerator = new();
    private readonly TextGenerator _propertiesPreferredWidthGenerator = new();
    private string _propertiesSizedText;
    private float _propertiesSizedFieldWidth;
    private float _propertiesSizedFieldHeight;
    private float _propertiesSizedPixelsPerUnit;
    private string _propertiesGeneratorText;
    private float _propertiesGeneratorPixelsPerUnit;
    private string _propertiesFollowText;
    private int _propertiesFollowCaret = -1;
    private float _propertiesFollowViewportWidth = float.NaN;
    private float _propertiesFollowContentWidth = float.NaN;
    private InputField[] _positionFields;
    private InputField[] _rotationFields;
    private InputField[] _scaleFields;
    private InputField[] _originalPositionFields;
    private InputField[] _originalRotationFields;
    private InputField[] _originalScaleFields;
    private string _selectedId;
    private string _candidateSignature;
    private bool _catalogBuilt;

    private void Awake()
    {
        _root = GetComponent<RectTransform>();
        _root ??= gameObject.AddComponent<RectTransform>();
        _root.sizeDelta = new Vector2(640f, 420f);
        _canvas = GetComponentInParent<Canvas>();

        Image background = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.78f);
        background.raycastTarget = true;

        _additionsPageRoot = CreatePageRoot("Additions Page");
        _originalPageRoot = CreatePageRoot("Original Objects Page");
        _defaultParent = _additionsPageRoot;
        Button pageButton = CreateButton(
            "Page Toggle",
            "Page: Additions",
            new Vector2(0f, 190f),
            () => Main.EditorMode.TrySwitchPage(),
            transform);
        pageButton.GetComponent<RectTransform>().sizeDelta = new Vector2(150f, 26f);
        _pageButtonObject = pageButton.gameObject;

        _statusText = CreateText(
            "Editor Status",
            new Vector2(0f, 148f),
            new Vector2(600f, 62f),
            StatusFontSize,
            TextAnchor.UpperLeft);
        _statusText.verticalOverflow = VerticalWrapMode.Truncate;

        CreateText(
            "Catalog Header",
            new Vector2(-195f, 108f),
            new Vector2(230f, 28f),
            18,
            TextAnchor.MiddleLeft).text = "Content Catalog";
        _catalogRoot = new GameObject("Catalog").transform;
        _catalogRoot.SetParent(_additionsPageRoot, false);
        _catalogRoot.localScale = Vector3.one;
        _candidateRoot = new GameObject("Overlap Candidates").transform;
        _candidateRoot.SetParent(_additionsPageRoot, false);
        _candidateRoot.localScale = Vector3.one;

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
        CreatePropertiesEditor();

        CreateButton("Import", "Import", new Vector2(-235f, -192f), () => Main.EditorMode.ImportCurrentRoom());
        CreateButton("Export", "Export", new Vector2(-155f, -192f), () => Main.EditorMode.ExportCurrentRoom());
        CreateButton("Undo", "Undo", new Vector2(-75f, -192f), () => Main.EditorMode.TryUndo());
        CreateButton("Redo", "Redo", new Vector2(5f, -192f), () => Main.EditorMode.TryRedo());
        CreateButton("Copy", "Copy Ctrl+C", new Vector2(85f, -192f), () => Main.EditorMode.TryCopySelected());
        CreateButton("Paste", "Paste Ctrl+V", new Vector2(165f, -192f), () => Main.EditorMode.TryPasteAtCursor());
        CreateButton("Delete", "Delete", new Vector2(245f, -192f), () => Main.EditorMode.TryDeleteSelected());
        CreateButton("Lock", "Lock", new Vector2(-155f, -160f), () => Main.EditorMode.TryToggleSelectedLock());
        CreateButton("Reset", "Reset", new Vector2(-75f, -160f), () => Main.EditorMode.TryReset());
        CreateButton("Exit", "Exit", new Vector2(5f, -160f), () => Main.EditorMode.ExitFromUi());
        freezeGameButtonObj = CreateButton(
            "Freeze Game Button",
            "Freeze",
            new Vector2(85f, -160f),
            OnFreezeGameButtonClicked).gameObject;
        CreateButton("Pick", "Pick P", new Vector2(165f, -160f), () => Main.EditorMode.TryPickSelected());
        CreateButton("Clear Preview", "Clear", new Vector2(245f, -160f), () => Main.EditorMode.ClearPreview());

        BuildOriginalPageShell();
        _originalPageRoot.gameObject.SetActive(false);
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

    private void OnDestroy()
    {
        ((IDisposable)_propertiesLayoutGenerator).Dispose();
        ((IDisposable)_propertiesPreferredWidthGenerator).Dispose();
    }

    private void Update()
    {
        EditorMode editor = Main.EditorMode;
        if (editor == null)
            return;

        editor.UpdatePreviewAtCursor();

        if (IsInputFieldFocused())
            return;

        if (editor.ActivePage == EditorPage.OriginalObjects)
        {
            bool selectOriginal = editor.IsInputActionPressed("PickPlacement")
                || editor.IsInputActionPressed("SelectPlacement");
            if (selectOriginal && !IsPointerOverEditorControl())
                editor.TrySelectOriginalAtCursor();
            return;
        }

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

    private void LateUpdate()
    {
        FollowPropertiesCaretHorizontally();
    }

    private void FollowPropertiesCaretHorizontally()
    {
        if (_propertiesField == null
            || _propertiesField.textComponent == null
            || _propertiesViewport == null
            || _propertiesContent == null)
            return;

        if (!_propertiesField.isFocused)
        {
            SetPropertiesHorizontalOffset(0f);
            ClearPropertiesFollowCache();
            return;
        }

        string fullText = _propertiesField.text ?? string.Empty;
        Text text = _propertiesField.textComponent;
        string displayedText = text.text ?? string.Empty;
        if (displayedText.Length == 0)
        {
            SetPropertiesHorizontalOffset(0f);
            ClearPropertiesFollowCache();
            return;
        }

        TextGenerator generator = PreparePropertiesGenerator(text, displayedText);
        UpdatePropertiesLayout(text, displayedText, generator);
        float viewportWidth = _propertiesViewport.rect.width;
        if (viewportWidth <= 0f)
            return;

        int caretPosition = _propertiesField.caretPosition;
        float contentWidth = _propertiesContent.rect.width;
        if (string.Equals(_propertiesFollowText, displayedText, StringComparison.Ordinal)
            && _propertiesFollowCaret == caretPosition
            && Mathf.Approximately(_propertiesFollowViewportWidth, viewportWidth)
            && Mathf.Approximately(_propertiesFollowContentWidth, contentWidth))
            return;

        _propertiesFollowText = displayedText;
        _propertiesFollowCaret = caretPosition;
        _propertiesFollowViewportWidth = viewportWidth;
        _propertiesFollowContentWidth = contentWidth;

        int displayedStart = FindDisplayedStartIndex(fullText, displayedText, caretPosition);
        int localCaret = Mathf.Clamp(caretPosition - displayedStart, 0, displayedText.Length);
        float caretX = GetPropertiesCaretX(text, generator, displayedText, localCaret);
        float offset = _propertiesContent.anchoredPosition.x;
        float caretPositionInView = caretX + offset;
        float leftEdge = 2f;
        float rightEdge = viewportWidth - 2f;

        if (caretPositionInView > rightEdge)
            offset -= caretPositionInView - rightEdge;
        else if (caretPositionInView < leftEdge)
            offset += leftEdge - caretPositionInView;

        float minimumOffset = Mathf.Min(0f, viewportWidth - contentWidth);
        SetPropertiesHorizontalOffset(Mathf.Clamp(offset, minimumOffset, 0f));
    }

    private TextGenerator PreparePropertiesGenerator(Text text, string displayedText)
    {
        float pixelsPerUnit = text.pixelsPerUnit;
        if (string.Equals(_propertiesGeneratorText, displayedText, StringComparison.Ordinal)
            && Mathf.Approximately(_propertiesGeneratorPixelsPerUnit, pixelsPerUnit))
            return _propertiesLayoutGenerator;

        TextGenerationSettings settings = text.GetGenerationSettings(Vector2.zero);
        settings.generateOutOfBounds = true;
        _propertiesLayoutGenerator.Populate(displayedText, settings);
        _propertiesGeneratorText = displayedText;
        _propertiesGeneratorPixelsPerUnit = pixelsPerUnit;
        return _propertiesLayoutGenerator;
    }

    private void UpdatePropertiesLayout(Text text, string displayedText, TextGenerator generator)
    {
        if (_propertiesViewport == null || _propertiesContent == null)
            return;

        RectTransform fieldRect = _propertiesField.transform as RectTransform;
        if (fieldRect == null)
            return;

        float fieldWidth = fieldRect.rect.width;
        float fieldHeight = fieldRect.rect.height;
        if (fieldWidth <= 0f || fieldHeight <= 0f)
            return;

        if (string.Equals(_propertiesSizedText, displayedText, StringComparison.Ordinal)
            && Mathf.Approximately(_propertiesSizedFieldWidth, fieldWidth)
            && Mathf.Approximately(_propertiesSizedFieldHeight, fieldHeight)
            && Mathf.Approximately(_propertiesSizedPixelsPerUnit, text.pixelsPerUnit))
            return;

        _propertiesSizedText = displayedText;
        _propertiesSizedFieldWidth = fieldWidth;
        _propertiesSizedFieldHeight = fieldHeight;
        _propertiesSizedPixelsPerUnit = text.pixelsPerUnit;
        float innerWidth = Mathf.Max(1f, fieldWidth - 8f);
        float innerHeight = Mathf.Max(1f, fieldHeight - 8f);
        float lineHeight = generator.lineCount > 0
            ? generator.lines[0].height / text.pixelsPerUnit
            : 0f;
        float contentHeight = innerHeight;
        if (lineHeight > 0f)
        {
            int visibleLines = Mathf.Max(1, Mathf.FloorToInt((innerHeight + 0.001f) / lineHeight));
            contentHeight = Mathf.Min(innerHeight, visibleLines * lineHeight);
        }

        TextGenerationSettings widthSettings = text.GetGenerationSettings(Vector2.zero);
        float contentWidth = _propertiesPreferredWidthGenerator.GetPreferredWidth(displayedText, widthSettings)
            / text.pixelsPerUnit;
        _propertiesContent.sizeDelta = new Vector2(
            Mathf.Max(innerWidth, contentWidth + 4f),
            contentHeight);
        SetPropertiesViewportHeight(contentHeight);
    }

    private void SetPropertiesViewportHeight(float height)
    {
        RectTransform parent = _propertiesViewport?.parent as RectTransform;
        if (parent == null)
            return;

        float parentHeight = parent.rect.height;
        if (parentHeight <= 0f)
            return;

        Vector2 min = _propertiesViewport.offsetMin;
        min.y = Mathf.Max(4f, parentHeight - 4f - height);
        Vector2 max = _propertiesViewport.offsetMax;
        max.y = -4f;
        if (!Mathf.Approximately(_propertiesViewport.offsetMin.y, min.y))
            _propertiesViewport.offsetMin = min;
        if (!Mathf.Approximately(_propertiesViewport.offsetMax.y, max.y))
            _propertiesViewport.offsetMax = max;
    }

    private static int FindDisplayedStartIndex(string fullText, string displayedText, int caretPosition)
    {
        int firstMatch = fullText.IndexOf(displayedText, StringComparison.Ordinal);
        int candidate = firstMatch;
        while (candidate >= 0)
        {
            int end = candidate + displayedText.Length;
            if (caretPosition >= candidate && caretPosition <= end)
                return candidate;

            candidate = fullText.IndexOf(displayedText, candidate + 1, StringComparison.Ordinal);
        }

        return firstMatch < 0 ? 0 : firstMatch;
    }

    private static float GetPropertiesCaretX(
        Text text,
        TextGenerator generator,
        string displayedText,
        int caretIndex)
    {
        float x = 0f;
        if (caretIndex == displayedText.Length && caretIndex > 0
            && (displayedText[caretIndex - 1] == '\n' || displayedText[caretIndex - 1] == '\r'))
        {
            x = 0f;
        }
        else if (caretIndex < generator.characterCountVisible)
        {
            x = generator.characters[caretIndex].cursorPos.x;
        }
        else if (caretIndex > 0)
        {
            UICharInfo previous = generator.characters[caretIndex - 1];
            x = previous.cursorPos.x + previous.charWidth;
        }

        return x / text.pixelsPerUnit;
    }

    private void SetPropertiesHorizontalOffset(float offset)
    {
        if (_propertiesContent == null)
            return;

        Vector2 position = _propertiesContent.anchoredPosition;
        if (Mathf.Approximately(position.x, offset))
            return;

        position.x = offset;
        _propertiesContent.anchoredPosition = position;
    }

    private void ClearPropertiesFollowCache()
    {
        _propertiesFollowText = null;
        _propertiesFollowCaret = -1;
        _propertiesFollowViewportWidth = float.NaN;
        _propertiesFollowContentWidth = float.NaN;
    }

    private bool IsPointerOverEditorControl()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return false;

        PointerEventData pointer = new(eventSystem)
        {
            position = Input.mousePosition
        };
        List<RaycastResult> raycastResults = [];
        eventSystem.RaycastAll(pointer, raycastResults);

        List<EditorPointerHit> hits = [];
        foreach (RaycastResult result in raycastResults)
        {
            GameObject hit = result.gameObject;
            if (hit == null)
                continue;

            Transform hitTransform = hit.transform;
            bool belongsToEditor = hitTransform == transform || hitTransform.IsChildOf(transform);
            bool hasEnabledSelectable = false;
            bool hasEnabledScrollRect = false;
            if (belongsToEditor)
            {
                FindEnabledEditorControls(
                    hitTransform,
                    out hasEnabledSelectable,
                    out hasEnabledScrollRect);
            }
            hits.Add(new EditorPointerHit(
                belongsToEditor,
                hasEnabledSelectable,
                hasEnabledScrollRect));
        }

        return EditorPointerHitPolicy.IsPointerOverInteractiveControl(hits);
    }

    private void FindEnabledEditorControls(
        Transform hitTransform,
        out bool hasEnabledSelectable,
        out bool hasEnabledScrollRect)
    {
        hasEnabledSelectable = false;
        hasEnabledScrollRect = false;
        for (Transform current = hitTransform; current != null; current = current.parent)
        {
            Selectable selectable = current.GetComponent<Selectable>();
            if (selectable != null && selectable.isActiveAndEnabled && selectable.IsInteractable())
                hasEnabledSelectable = true;

            ScrollRect scrollRect = current.GetComponent<ScrollRect>();
            if (scrollRect != null && scrollRect.isActiveAndEnabled)
                hasEnabledScrollRect = true;

            if (current == transform)
                break;
        }
    }

    internal void Refresh()
    {
        Main.EditorMode?.ClearRawPropertiesDiagnostics();
        RefreshCore(false);
    }

    internal void RefreshAfterReport(bool preservePropertiesDraft)
    {
        RefreshCore(preservePropertiesDraft);
    }

    private void RefreshCore(bool preservePropertiesDraft)
    {
        EditorMode editor = Main.EditorMode;
        if (editor == null)
            return;

        EditorPage activePage = editor.ActivePage;
        RefreshPageVisibility(activePage);
        if (activePage == EditorPage.OriginalObjects)
        {
            RefreshOriginalPage(editor);
            return;
        }

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
        PlacementRecord selectedPlacement = editor.PlacementManager?.Selected;
        string selectedPlacementId = selectedPlacement?.Id;
        bool selectionChanged = !string.Equals(_selectedId, selectedPlacementId, StringComparison.OrdinalIgnoreCase);
        if (selectionChanged)
        {
            _selectedId = selectedPlacementId;
            editor.ClearRawPropertiesDiagnostics();
            SetTransformFields(selectedPlacement);
        }
        SetPropertiesEditor(selectedPlacement, preservePropertiesDraft);
        string dependency = Main.DependencyStatus == null
            ? "unknown"
            : Main.DependencyStatus.IsCompatible ? "compatible" : "blocked";
        string locked = selectedPlacement == null
            ? "—"
            : selectedPlacement.IsLocked ? "yes" : "no";
        ApplyReportSurface reportSurface = editor.ReportSurface;
        string statusPrefix = string.Format(
            "Room: {0}   Session: {1}\nSelected: {2}   Locked: {3}   Dependencies: {4}   Preview: {5}\nClipboard: {6}   Last: ",
            room,
            state,
            selected,
            locked,
            dependency,
            editor.PreviewStatus,
            editor.ClipboardStatus);
        _statusText.fontSize = StatusFontSize;
        string status = reportSurface == null || string.IsNullOrEmpty(reportSurface.UiText)
            ? statusPrefix + "—"
            : ComposeStatusText(reportSurface, statusPrefix);
        _statusText.text = status;
        RefreshCandidateList(editor);
    }

    private void RefreshPageVisibility(EditorPage activePage)
    {
        bool showOriginal = activePage == EditorPage.OriginalObjects;
        if (_additionsPageRoot != null)
            _additionsPageRoot.gameObject.SetActive(!showOriginal);
        if (_originalPageRoot != null)
            _originalPageRoot.gameObject.SetActive(showOriginal);

        Text label = _pageButtonObject?.GetComponentInChildren<Text>();
        if (label != null)
            label.text = showOriginal ? "Page: Original" : "Page: Additions";
    }

    private void RefreshOriginalPage(EditorMode editor)
    {
        if (_originalStatusText == null)
            return;

        OriginalObjectPage page = editor.Pages?.OriginalObjects;
        string room = editor.CurrentRoom == null
            ? "<none>"
            : editor.CurrentRoom.FrameworkLevelKey + editor.CurrentRoom.TargetSceneSuffix;
        string selected = page?.Selected == null
            ? "<none>"
            : page.Selected.DisplayText;
        string status = string.Format(
            "Room: {0}   Session: {1}\nCatalog: {2} locatable   Visible: {3}   Overlap candidates: {4}\nSelected Original Object: {5}",
            room,
            editor.Session == null ? "not-ready" : editor.Session.State.ToString(),
            page == null ? 0 : page.CatalogCount,
            page == null ? 0 : page.VisibleCount,
            page == null ? 0 : page.Candidates.Count,
            selected);
        ApplyReportSurface reportSurface = editor.ReportSurface;
        if (reportSurface != null && !string.IsNullOrEmpty(reportSurface.UiText))
        {
            status += "\nLast: " + reportSurface.UiText;
            if (reportSurface.HasActionableMessage)
                status += " " + reportSurface.ActionableMessage;
        }
        _originalStatusText.text = status;

        if (_originalInspectorText != null)
        {
            _originalInspectorText.text = page?.Selected == null
                ? "No Original Object selected.\nUse P or click an object in the room.\nMultiple overlaps require a row click."
                : string.Format(
                    "Identity: {0}\nActive at baseline: {1}   Active now: {2}\nRuntime key: {3}\nEdit: {4}",
                    page.Selected.DisplayText,
                    page.Selected.IsActive ? "yes" : "no (targeted)",
                    page.Selected.IsRuntimeActive ? "yes" : "no",
                    page.Selected.ObjectKey,
                    FormatOriginalEditState(page));
        }

        SetOriginalTransformFields(page);
        BuildOriginalOperations(page);
        BuildOriginalCandidates(page);
    }

    private static string FormatOriginalEditState(OriginalObjectPage page)
    {
        if (page.SelectedHasConflict)
            return "conflict; resolve duplicate imported operations";
        if (page.SelectedHasDeletion)
            return "deletion preview";

        FrameworkObjectData modification = page.SelectedModification;
        return modification == null
            ? "baseline; Apply Modify creates a pending transform edit"
            : "modification " + modification.Type + " / " + modification.Id;
    }

    private void BuildOriginalCandidates(OriginalObjectPage page)
    {
        if (_originalCandidateRoot == null)
            return;

        for (int index = _originalCandidateRoot.childCount - 1; index >= 0; index--)
            UnityEngine.Object.Destroy(_originalCandidateRoot.GetChild(index).gameObject);

        IList<OriginalObjectCandidate> candidates =
            page == null ? new OriginalObjectCandidate[0] : page.Candidates;
        if (candidates.Count == 0)
        {
            if (_originalCandidateScroll != null)
                _originalCandidateScroll.gameObject.SetActive(false);
            SetScrollContentHeight(_originalCandidateRoot, _originalCandidateScroll, _originalCandidateRoot.parent as RectTransform, 0f);
            return;
        }

        if (_originalCandidateScroll != null)
            _originalCandidateScroll.gameObject.SetActive(true);
        float contentHeight = 8f + (candidates.Count * 24f);
        for (int index = 0; index < candidates.Count; index++)
        {
            OriginalObjectCandidate candidate = candidates[index];
            Button row = CreateButton(
                "Original Candidate " + index,
                candidate.DisplayText,
                new Vector2(0f, -4f - (index * 24f)),
                () => Main.EditorMode.TryChooseOriginalCandidate(candidate.DisplayText),
                _originalCandidateRoot);
            RectTransform rowRect = row.GetComponent<RectTransform>();
            ConfigureTopAnchored(rowRect, new Vector2(0f, -4f - (index * 24f)), new Vector2(240f, 22f));
        }

        SetScrollContentHeight(
            _originalCandidateRoot,
            _originalCandidateScroll,
            _originalCandidateRoot.parent as RectTransform,
            contentHeight);
    }

    private void BuildOriginalOperations(OriginalObjectPage page)
    {
        if (_originalOperationsRoot == null)
            return;

        for (int index = _originalOperationsRoot.childCount - 1; index >= 0; index--)
            UnityEngine.Object.Destroy(_originalOperationsRoot.GetChild(index).gameObject);

        if (page == null || page.Operations.Count == 0)
        {
            Text empty = CreateText(
                "Original Operations Empty",
                Vector2.zero,
                new Vector2(240f, 44f),
                10,
                TextAnchor.UpperLeft,
                _originalOperationsRoot);
            empty.text = "No imported or pending modifications/deletions.";
            ConfigureTopAnchored(
                empty.rectTransform,
                new Vector2(0f, -4f),
                new Vector2(240f, 44f));
            SetScrollContentHeight(
                _originalOperationsRoot,
                _originalOperationsScroll,
                _originalOperationsScroll?.viewport,
                132f);
            return;
        }

        const float rowHeight = 56f;
        for (int index = 0; index < page.Operations.Count; index++)
        {
            OriginalObjectOperation operation = page.Operations[index];
            float y = -4f - (index * rowHeight);
            float textX = operation.IsLocatable ? -30f : 0f;
            float textWidth = operation.IsLocatable ? 180f : 230f;
            Text rowText = CreateText(
                "Original Operation " + index,
                Vector2.zero,
                new Vector2(textWidth, 48f),
                9,
                TextAnchor.UpperLeft,
                _originalOperationsRoot);
            rowText.text = FormatOriginalOperation(operation);
            ConfigureTopAnchored(
                rowText.rectTransform,
                new Vector2(textX, y),
                new Vector2(textWidth, 48f));

            if (!operation.IsLocatable)
                continue;

            Button select = CreateButton(
                "Select Original Operation " + index,
                "Select",
                Vector2.zero,
                () => Main.EditorMode.TrySelectOriginal(operation.DisplayIdentity),
                _originalOperationsRoot);
            ConfigureTopAnchored(
                select.GetComponent<RectTransform>(),
                new Vector2(96f, y),
                new Vector2(54f, 22f));
        }

        SetScrollContentHeight(
            _originalOperationsRoot,
            _originalOperationsScroll,
            _originalOperationsScroll?.viewport,
            8f + (page.Operations.Count * rowHeight));
    }

    private static string FormatOriginalOperation(OriginalObjectOperation operation)
    {
        StringBuilder text = new();
        text.Append(operation.Kind == OriginalObjectOperationKind.Modification ? "MODIFY " : "DELETE ")
            .Append(operation.Source)
            .Append(operation.IsImported ? " [imported]" : operation.IsPending ? " [pending]" : " [unknown status]")
            .Append(operation.IsLocatable ? " [LOCATABLE]" : " [UNLOCATABLE]")
            .Append('\n')
            .Append(operation.DisplayIdentity);
        if (!string.IsNullOrEmpty(operation.Type))
            text.Append(" type=").Append(operation.Type);
        if (!string.IsNullOrEmpty(operation.Id))
            text.Append(" id=").Append(operation.Id);
        return text.ToString();
    }

    private void RefreshCandidateList(EditorMode editor)
    {
        if (_candidateRoot == null)
            return;

        RuntimePlacementManager manager = editor.Pages?.Additions ?? editor.PlacementManager;
        IList<PlacementCandidate> candidates =
            manager == null ? new PlacementCandidate[0] : manager.Candidates;
        string signature = BuildCandidateSignature(candidates);
        if (string.Equals(signature, _candidateSignature, StringComparison.Ordinal))
            return;

        _candidateSignature = signature;
        for (int index = _candidateRoot.childCount - 1; index >= 0; index--)
            UnityEngine.Object.Destroy(_candidateRoot.GetChild(index).gameObject);

        bool hasCandidates = candidates.Count > 0;
        _candidateRoot.gameObject.SetActive(hasCandidates);
        if (_catalogRoot != null)
            _catalogRoot.gameObject.SetActive(!hasCandidates);
        if (!hasCandidates)
            return;

        CreateText(
            "Overlap Candidates Header",
            new Vector2(-195f, 98f),
            new Vector2(230f, 24f),
            14,
            TextAnchor.MiddleLeft,
            _candidateRoot).text = "Overlapping Additions";
        for (int index = 0; index < candidates.Count; index++)
        {
            PlacementCandidate candidate = candidates[index];
            Button row = CreateButton(
                "Candidate " + candidate.Identity,
                candidate.DisplayText,
                new Vector2(-195f, 70f - (index * 30f)),
                () => Main.EditorMode.TryChooseAdditionCandidate(candidate.Identity),
                _candidateRoot);
            row.GetComponent<RectTransform>().sizeDelta = new Vector2(230f, 26f);
        }
    }

    private static string BuildCandidateSignature(IList<PlacementCandidate> candidates)
    {
        StringBuilder signature = new();
        for (int index = 0; index < candidates.Count; index++)
        {
            if (index > 0)
                signature.Append('|');
            signature.Append(candidates[index].Identity);
        }
        return signature.ToString();
    }

    private string ComposeStatusText(ApplyReportSurface reportSurface, string prefix)
    {
        string statusText = prefix + reportSurface.UiText;
        string baseText = reportSurface.HasActionableMessage
            ? statusText + "\n" + reportSurface.ActionableMessage
            : statusText;

        int fontSize = StatusFontSize;
        while (fontSize > StatusMinimumFontSize && !FitsStatusText(baseText, fontSize))
            fontSize--;
        _statusText.fontSize = fontSize;

        return ApplyReportStatusText.AppendCodeGroups(
            statusText,
            reportSurface.ActionableMessage,
            reportSurface.LastReport,
            candidate => FitsStatusText(candidate, fontSize));
    }

    private bool FitsStatusText(string text, int fontSize)
    {
        Rect rect = _statusText.rectTransform.rect;
        TextGenerationSettings settings = _statusText.GetGenerationSettings(rect.size);
        settings.fontSize = fontSize;
        float preferredHeight = _statusText.cachedTextGeneratorForLayout.GetPreferredHeight(text, settings)
            / _statusText.pixelsPerUnit;
        return preferredHeight <= rect.height;
    }

    private static bool IsInputFieldFocused()
    {
        GameObject selected = EventSystem.current?.currentSelectedGameObject;
        return selected != null && selected.GetComponentInParent<InputField>() != null;
    }

    private InputField[] CreateTransformFields(string group, float x)
    {
        InputField[] fields = new InputField[3];
        string[] axes = ["X", "Y", "Z"];
        for (int index = 0; index < fields.Length; index++)
        {
            fields[index] = CreateInputField(
                group + " " + axes[index],
                new Vector2(x, 50f - (index * 30f)),
                _additionsPageRoot);
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
            [
                placement.Transform.PositionX.ToString("R", CultureInfo.InvariantCulture),
                placement.Transform.PositionY.ToString("R", CultureInfo.InvariantCulture),
                placement.Transform.PositionZ.ToString("R", CultureInfo.InvariantCulture)
            ]);
        SetFieldValues(
            _rotationFields,
            [
                placement.Transform.RotationX.ToString("R", CultureInfo.InvariantCulture),
                placement.Transform.RotationY.ToString("R", CultureInfo.InvariantCulture),
                placement.Transform.RotationZ.ToString("R", CultureInfo.InvariantCulture)
            ]);
        SetFieldValues(
            _scaleFields,
            [
                placement.Transform.ScaleX.ToString("R", CultureInfo.InvariantCulture),
                placement.Transform.ScaleY.ToString("R", CultureInfo.InvariantCulture),
                placement.Transform.ScaleZ.ToString("R", CultureInfo.InvariantCulture)
            ]);
    }

    private void SetOriginalTransformFields(OriginalObjectPage page)
    {
        PlacementTransform transform = page == null ? null : page.SelectedTransform;
        if (transform == null)
        {
            SetFieldValues(_originalPositionFields, new string[3]);
            SetFieldValues(_originalRotationFields, new string[3]);
            SetFieldValues(_originalScaleFields, new string[3]);
            return;
        }

        SetFieldValues(
            _originalPositionFields,
            [
                transform.PositionX.ToString("R", CultureInfo.InvariantCulture),
                transform.PositionY.ToString("R", CultureInfo.InvariantCulture),
                transform.PositionZ.ToString("R", CultureInfo.InvariantCulture)
            ]);
        SetFieldValues(
            _originalRotationFields,
            [
                transform.RotationX.ToString("R", CultureInfo.InvariantCulture),
                transform.RotationY.ToString("R", CultureInfo.InvariantCulture),
                transform.RotationZ.ToString("R", CultureInfo.InvariantCulture)
            ]);
        SetFieldValues(
            _originalScaleFields,
            [
                transform.ScaleX.ToString("R", CultureInfo.InvariantCulture),
                transform.ScaleY.ToString("R", CultureInfo.InvariantCulture),
                transform.ScaleZ.ToString("R", CultureInfo.InvariantCulture)
            ]);
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

        bool original = Main.EditorMode.ActivePage == EditorPage.OriginalObjects;
        string[] values = new string[9];
        CopyFieldValues(
            original ? _originalPositionFields : _positionFields,
            values,
            0);
        CopyFieldValues(
            original ? _originalRotationFields : _rotationFields,
            values,
            3);
        CopyFieldValues(
            original ? _originalScaleFields : _scaleFields,
            values,
            6);
        Main.EditorMode.TryApplySelectedTransform(values);
    }

    private static void CopyFieldValues(InputField[] fields, string[] target, int offset)
    {
        if (fields == null)
            return;

        for (int index = 0; index < fields.Length; index++)
            target[offset + index] = fields[index] == null ? string.Empty : fields[index].text;
    }

    private void CreatePropertiesEditor()
    {
        _propertyRoot = new GameObject("Properties Section").transform;
        _propertyRoot.SetParent(_additionsPageRoot, false);
        _propertyRoot.localScale = Vector3.one;

        CreateText(
            "Properties Heading",
            new Vector2(66f, -62f),
            new Vector2(80f, 16f),
            13,
            TextAnchor.MiddleLeft,
            _propertyRoot).text = "Properties";
        _propertiesWarning = CreateText(
            "Properties Warning",
            new Vector2(190f, -62f),
            new Vector2(160f, 14f),
            8,
            TextAnchor.MiddleRight,
            _propertyRoot);
        _propertiesWarning.color = new Color(1f, 0.75f, 0.35f, 1f);
        _propertiesWarning.gameObject.SetActive(false);
        _propertiesField = CreatePropertiesInputField(
            "Properties Field",
            new Vector2(100f, -108f),
            new Vector2(230f, 72f),
            _propertyRoot,
            out _propertiesViewport,
            out _propertiesContent);
        _propertiesApplyButton = CreateButton(
            "Apply Properties",
            "Apply",
            new Vector2(282f, -108f),
            ApplyProperties,
            _propertyRoot);
        _propertiesApplyButton.interactable = false;
    }

    private void SetPropertiesEditor(PlacementRecord placement, bool preservePropertiesDraft)
    {
        RawPropertiesText properties = RawPropertiesText.Create(placement?.Properties);
        bool textChanged = _propertiesDraft.Reload(
            placement?.Id,
            properties.Text,
            preservePropertiesDraft);
        bool editable = placement != null && !properties.ContainsLineBreak;

        _propertiesField.readOnly = !editable;
        if (textChanged)
        {
            SetPropertiesFieldText(properties.Text);
            ResetPropertiesView();
        }
        _propertiesWarning.text = properties.ContainsLineBreak ? "Stored CR/LF: read-only" : string.Empty;
        _propertiesWarning.gameObject.SetActive(properties.ContainsLineBreak);
        _propertiesApplyButton.interactable = editable;
    }

    private void SetPropertiesFieldText(string text)
    {
        _settingPropertiesText = true;
        try
        {
            _propertiesField.text = text;
        }
        finally
        {
            _settingPropertiesText = false;
        }
    }

    private void OnPropertiesTextChanged(string text)
    {
        if (_settingPropertiesText)
        {
            return;
        }

        if (!_propertiesDraft.Edit(text) || Main.EditorMode == null)
        {
            return;
        }

        Main.EditorMode.OnRawPropertiesEditStarted();
    }

    private void ApplyProperties()
    {
        if (Main.EditorMode == null)
            return;

        Main.EditorMode.TryApplySelectedProperties(_propertiesField.text);
    }

    private void ResetPropertiesView()
    {
        _propertiesField.caretPosition = 0;
        _propertiesField.ForceLabelUpdate();
        _propertiesSizedText = null;
        _propertiesSizedFieldWidth = float.NaN;
        _propertiesSizedFieldHeight = float.NaN;
        _propertiesSizedPixelsPerUnit = float.NaN;
        _propertiesGeneratorText = null;
        _propertiesGeneratorPixelsPerUnit = float.NaN;
        ClearPropertiesFollowCache();
        SetPropertiesHorizontalOffset(0f);
        RefreshPropertiesLayout();
    }

    private void RefreshPropertiesLayout()
    {
        if (_propertiesField == null || _propertiesField.textComponent == null)
            return;

        Text text = _propertiesField.textComponent;
        string displayedText = text.text ?? string.Empty;
        TextGenerator generator = PreparePropertiesGenerator(text, displayedText);
        UpdatePropertiesLayout(text, displayedText, generator);
    }

    private InputField CreatePropertiesInputField(
        string name,
        Vector2 position,
        Vector2 size,
        Transform parent,
        out RectTransform viewport,
        out RectTransform content)
    {
        GameObject obj = new(name)
        {
            layer = LayerMask.NameToLayer("UI")
        };
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Image image = obj.AddComponent<Image>();
        image.color = Color.white;

        GameObject viewportObject = new("Viewport")
        {
            layer = LayerMask.NameToLayer("UI")
        };
        viewportObject.transform.SetParent(obj.transform, false);
        viewport = viewportObject.AddComponent<RectTransform>();
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(4f, 4f);
        viewport.offsetMax = new Vector2(-4f, -4f);
        Image viewportImage = viewportObject.AddComponent<Image>();
        viewportImage.color = Color.white;
        Mask mask = viewportObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        GameObject contentObject = new("Content")
        {
            layer = LayerMask.NameToLayer("UI")
        };
        contentObject.transform.SetParent(viewportObject.transform, false);
        content = contentObject.AddComponent<RectTransform>();
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(0f, 1f);
        content.pivot = new Vector2(0f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(size.x - 8f, size.y - 8f);

        GameObject textObject = new("Text")
        {
            layer = LayerMask.NameToLayer("UI")
        };
        textObject.transform.SetParent(contentObject.transform, false);
        RectTransform textRect = textObject.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        Text text = textObject.AddComponent<Text>();
        text.font = UIModder.Fonts.Blasphemous ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 12;
        text.alignment = TextAnchor.UpperLeft;
        text.color = Color.black;
        text.raycastTarget = false;
        text.supportRichText = false;

        InputField input = obj.AddComponent<InputField>();
        input.targetGraphic = image;
        input.contentType = InputField.ContentType.Standard;
        input.lineType = InputField.LineType.MultiLineNewline;
        input.textComponent = text;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.lineSpacing = 1f;
        input.readOnly = true;
        input.caretWidth = 1;
        input.onValueChanged.AddListener(OnPropertiesTextChanged);
        return input;
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

    private void BuildOriginalPageShell()
    {
        CreateText(
            "Original Objects Header",
            new Vector2(-195f, 108f),
            new Vector2(230f, 28f),
            18,
            TextAnchor.MiddleLeft,
            _originalPageRoot).text = "Original Objects";
        CreateText(
            "Original Inspector Header",
            new Vector2(150f, 108f),
            new Vector2(250f, 28f),
            18,
            TextAnchor.MiddleLeft,
            _originalPageRoot).text = "Inspector / Original Object";
        _originalStatusText = CreateText(
            "Original Objects Status",
            new Vector2(0f, 148f),
            new Vector2(560f, 62f),
            StatusFontSize,
            TextAnchor.UpperLeft,
            _originalPageRoot);
        _originalStatusText.verticalOverflow = VerticalWrapMode.Truncate;
        _originalInspectorText = CreateText(
            "Original Inspector",
            new Vector2(165f, 67f),
            new Vector2(250f, 40f),
            10,
            TextAnchor.UpperLeft,
            _originalPageRoot);
        CreateText(
            "Original Position Label",
            new Vector2(90f, 37f),
            new Vector2(64f, 18f),
            10,
            TextAnchor.MiddleCenter,
            _originalPageRoot).text = "Position";
        CreateText(
            "Original Rotation Label",
            new Vector2(165f, 37f),
            new Vector2(64f, 18f),
            10,
            TextAnchor.MiddleCenter,
            _originalPageRoot).text = "Rotation";
        CreateText(
            "Original Scale Label",
            new Vector2(240f, 37f),
            new Vector2(64f, 18f),
            10,
            TextAnchor.MiddleCenter,
            _originalPageRoot).text = "Scale";
        _originalPositionFields = CreateOriginalTransformFields("Original Position", 90f);
        _originalRotationFields = CreateOriginalTransformFields("Original Rotation", 165f);
        _originalScaleFields = CreateOriginalTransformFields("Original Scale", 240f);
        Button apply = CreateButton(
            "Apply Original Modification",
            "Apply Modify",
            new Vector2(112f, -56f),
            ApplyTransform,
            _originalPageRoot);
        apply.GetComponent<RectTransform>().sizeDelta = new Vector2(110f, 26f);
        Button remove = CreateButton(
            "Remove Original Edit",
            "Remove Edit",
            new Vector2(238f, -56f),
            () => Main.EditorMode.TryRemoveSelectedOriginalEdit(),
            _originalPageRoot);
        remove.GetComponent<RectTransform>().sizeDelta = new Vector2(86f, 26f);
        CreateText(
            "Original Candidate Header",
            new Vector2(-190f, 86f),
            new Vector2(250f, 22f),
            13,
            TextAnchor.MiddleLeft,
            _originalPageRoot).text = "Overlapping Original Objects — choose a row";
        _originalCandidateRoot = CreateScrollView(
            "Original Candidates",
            _originalPageRoot,
            new Vector2(-190f, -20f),
            new Vector2(250f, 190f),
            out _originalCandidateScroll);
        _originalCandidateScroll.gameObject.SetActive(false);
        CreateText(
            "Original Operations Header",
            new Vector2(165f, -80f),
            new Vector2(250f, 22f),
            10,
            TextAnchor.MiddleLeft,
            _originalPageRoot).text = "Imported / pending operations — select a row";
        _originalOperationsRoot = CreateScrollView(
            "Original Operations",
            _originalPageRoot,
            new Vector2(165f, -124f),
            new Vector2(250f, 68f),
            out _originalOperationsScroll);
        SetScrollContentHeight(_originalOperationsRoot, _originalOperationsScroll, _originalOperationsScroll.viewport, 68f);
        CreateButton(
            "Original Import",
            "Import",
            new Vector2(-200f, -190f),
            () => Main.EditorMode.ImportCurrentRoom(),
            _originalPageRoot);
        CreateButton(
            "Original Export",
            "Export",
            new Vector2(-120f, -190f),
            () => Main.EditorMode.ExportCurrentRoom(),
            _originalPageRoot);
        CreateButton(
            "Original Undo",
            "Undo",
            new Vector2(-40f, -190f),
            () => Main.EditorMode.TryUndo(),
            _originalPageRoot);
        CreateButton(
            "Original Redo",
            "Redo",
            new Vector2(40f, -190f),
            () => Main.EditorMode.TryRedo(),
            _originalPageRoot);
        CreateButton(
            "Original Reset",
            "Reset",
            new Vector2(120f, -190f),
            () => Main.EditorMode.TryReset(),
            _originalPageRoot);
        CreateButton(
            "Original Exit",
            "Exit",
            new Vector2(200f, -190f),
            () => Main.EditorMode.ExitFromUi(),
            _originalPageRoot);
    }

    private InputField[] CreateOriginalTransformFields(string group, float x)
    {
        InputField[] fields = new InputField[3];
        string[] axes = ["X", "Y", "Z"];
        for (int index = 0; index < fields.Length; index++)
        {
            InputField field = CreateInputField(
                group + " " + axes[index],
                new Vector2(x, 17f - (index * 24f)),
                _originalPageRoot);
            field.GetComponent<RectTransform>().sizeDelta = new Vector2(64f, 22f);
            fields[index] = field;
        }

        return fields;
    }

    private Transform CreateScrollView(
        string name,
        Transform parent,
        Vector2 position,
        Vector2 size,
        out ScrollRect scroll)
    {
        GameObject viewportObject = new(name + " Viewport")
        {
            layer = LayerMask.NameToLayer("UI")
        };
        viewportObject.transform.SetParent(parent, false);
        RectTransform viewport = viewportObject.AddComponent<RectTransform>();
        viewport.anchorMin = new Vector2(0.5f, 0.5f);
        viewport.anchorMax = new Vector2(0.5f, 0.5f);
        viewport.pivot = new Vector2(0.5f, 0.5f);
        viewport.anchoredPosition = position;
        viewport.sizeDelta = size;

        Image background = viewportObject.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.12f);
        background.raycastTarget = true;
        viewportObject.AddComponent<RectMask2D>();

        scroll = viewportObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 20f;
        scroll.viewport = viewport;

        GameObject contentObject = new(name + " Content")
        {
            layer = LayerMask.NameToLayer("UI")
        };
        contentObject.transform.SetParent(viewportObject.transform, false);
        RectTransform content = contentObject.AddComponent<RectTransform>();
        content.anchorMin = new Vector2(0.5f, 1f);
        content.anchorMax = new Vector2(0.5f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = size;
        scroll.content = content;
        return content.transform;
    }

    private static void ConfigureTopAnchored(
        RectTransform rect,
        Vector2 position,
        Vector2 size)
    {
        if (rect == null)
            return;

        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void SetScrollContentHeight(
        Transform content,
        ScrollRect scroll,
        RectTransform viewport,
        float height)
    {
        RectTransform contentRect = content as RectTransform;
        if (contentRect == null)
            return;

        float viewportHeight = viewport == null ? height : viewport.rect.height;
        contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x, Mathf.Max(height, viewportHeight));
        if (scroll != null)
            scroll.verticalNormalizedPosition = 1f;
    }

    private Transform CreatePageRoot(string name)
    {
        GameObject obj = new(name)
        {
            layer = LayerMask.NameToLayer("UI")
        };
        obj.transform.SetParent(transform, false);
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return obj.transform;
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
        GameObject obj = new(name)
        {
            layer = LayerMask.NameToLayer("UI")
        };
        obj.transform.SetParent(parent ?? _defaultParent ?? transform, false);
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
        GameObject obj = new(name)
        {
            layer = LayerMask.NameToLayer("UI")
        };
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
        GameObject obj = new(name)
        {
            layer = LayerMask.NameToLayer("UI")
        };
        obj.transform.SetParent(parent ?? _defaultParent ?? transform, false);
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
        GameObject textObject = new("Label")
        {
            layer = LayerMask.NameToLayer("UI")
        };
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
        label?.text = paused ? "Resume" : "Freeze";
    }
}
