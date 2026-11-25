using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Editing;
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
    private InputField[] _positionFields;
    private InputField[] _rotationFields;
    private InputField[] _scaleFields;
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
        _propertyRoot = new GameObject("Properties").transform;
        _propertyRoot.SetParent(_additionsPageRoot, false);

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
        _canvasRaycaster?.enabled = !_addedCanvasRaycaster && _canvasRaycasterWasEnabled;

        if (_standaloneInputModuleWasAdded && _standaloneInputModule != null)
        {
            _standaloneInputModule.enabled = false;
            _standaloneInputModuleWasAdded = false;
        }

        if (_inputModulesBeforeEditor != null)
        {
            for (int i = 0; i < _inputModulesBeforeEditor.Length; i++)
            {
                _inputModulesBeforeEditor[i]?.enabled = _inputModulesWereEnabled[i];
            }
        }

        _inputModulesBeforeEditor = null;
        _inputModulesWereEnabled = null;
        _standaloneInputModule = null;
        _fallbackEventSystem?.SetActive(false);
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

        foreach (RaycastResult result in raycastResults)
        {
            GameObject hit = result.gameObject;
            if (hit == null)
                continue;

            Transform hitTransform = hit.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform))
                return true;
        }

        return false;
    }

    internal void Refresh()
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
                    "Identity: {0}\nActive at baseline: {1}\nActive now: {2}\nRuntime key: {3}\nLocatable: yes\nEditing: unavailable in this ticket",
                    page.Selected.DisplayText,
                    page.Selected.IsActive ? "yes" : "no (targeted)",
                    page.Selected.IsRuntimeActive ? "yes" : "no",
                    page.Selected.ObjectKey);
        }

        BuildOriginalOperations(page);
        BuildOriginalCandidates(page);
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
        text.Append(" [read-only]");
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
        string baseText = prefix + reportSurface.UiText;
        if (reportSurface.HasActionableMessage)
            baseText += "\n" + reportSurface.ActionableMessage;

        List<DiagnosticCodeCount> groups = new List<DiagnosticCodeCount>();
        foreach (ApplyReportEntry entry in reportSurface.LastReport.Entries)
        {
            if (entry.Severity == ApplySeverity.Info)
                continue;

            DiagnosticCodeCount group = groups.Find(existing =>
                string.Equals(existing.Code, entry.Code, StringComparison.Ordinal));
            if (group == null)
                groups.Add(new DiagnosticCodeCount(entry.Code));
            else
                group.Count++;
        }

        int fontSize = StatusFontSize;
        while (fontSize > StatusMinimumFontSize && !FitsStatusText(baseText, fontSize))
            fontSize--;
        _statusText.fontSize = fontSize;

        if (groups.Count == 0)
            return baseText;

        string candidate = baseText + "\n" + FormatGroups(groups, groups.Count);
        if (FitsStatusText(candidate, fontSize))
            return candidate;

        for (int included = groups.Count - 1; included >= 0; included--)
        {
            int omitted = groups.Count - included;
            string marker = "+" + omitted + " more diagnostic code(s)";
            candidate = included == 0
                ? baseText + "\n" + marker
                : baseText + "\n" + FormatGroups(groups, included) + "\n" + marker;
            if (FitsStatusText(candidate, fontSize))
                return candidate;
        }

        return baseText;
    }

    private static string FormatGroups(List<DiagnosticCodeCount> groups, int count)
    {
        StringBuilder text = new();
        for (int index = 0; index < count; index++)
        {
            if (index > 0)
                text.Append(", ");
            DiagnosticCodeCount group = groups[index];
            text.Append(group.Code).Append(" x").Append(group.Count);
        }

        return text.ToString();
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

    private sealed class DiagnosticCodeCount
    {
        internal DiagnosticCodeCount(string code)
        {
            Code = code;
            Count = 1;
        }

        internal string Code { get; }
        internal int Count { get; set; }
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

        if (editor.Catalog == null || !editor.Catalog.TryGet(placement.Type, out CreatorMetadata metadata)
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
            new Vector2(165f, 42f),
            new Vector2(250f, 82f),
            12,
            TextAnchor.UpperLeft,
            _originalPageRoot);
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
            new Vector2(165f, -15f),
            new Vector2(250f, 22f),
            13,
            TextAnchor.MiddleLeft,
            _originalPageRoot).text = "Imported / pending operations (read-only)";
        _originalOperationsRoot = CreateScrollView(
            "Original Operations",
            _originalPageRoot,
            new Vector2(165f, -95f),
            new Vector2(250f, 132f),
            out _originalOperationsScroll);
        SetScrollContentHeight(_originalOperationsRoot, _originalOperationsScroll, _originalOperationsScroll.viewport, 132f);
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
