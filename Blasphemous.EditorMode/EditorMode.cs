using Blasphemous.EditorMode.Components;
using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Editing;
using Blasphemous.EditorMode.Framework;
using Blasphemous.EditorMode.Lifecycle;
using Blasphemous.EditorMode.Runtime;
using Blasphemous.Framework.UI;
using Blasphemous.ModdingAPI;
using Framework.FrameworkCore;
using Framework.Managers;
using Gameplay.GameControllers.Environment;
using Gameplay.UI;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Tools.Level.Layout;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Blasphemous.EditorMode;

internal class EditorMode : BlasMod
{

    internal EditorWidget EditorWidget { get; private set; }

    internal bool IsActive { get { return _editorGuard != null && _editorGuard.IsActive; } }
    internal static SpriteStorage SpriteStorage { get; private set; }
    internal ContentCatalog Catalog { get; private set; }
    internal RoomIdentity CurrentRoom { get { return _lifecycleBridge?.CurrentRoom; } }
    internal LevelEditSession Session { get { return _overlayReconciler?.CurrentSession; } }
    internal RuntimePlacementManager PlacementManager { get { return _overlayReconciler?.Manager; } }
    internal EditorPageController Pages { get { return _overlayReconciler?.Pages; } }
    internal EditorPage ActivePage
    {
        get { return Pages == null ? EditorPage.Additions : Pages.ActivePage; }
    }
    internal ApplyReportSurface ReportSurface { get { return _reportPublisher.Surface; } }
    internal string PreviewStatus { get { return _preview == null ? "none" : _preview.Metadata.DisplayName + " (follows cursor)"; } }

    internal string ClipboardStatus
    {
        get
        {
            RuntimePlacementManager manager = PlacementManager;
            return manager == null || manager.Clipboard == null ? "empty" : manager.Clipboard.Id;
        }
    }

    internal bool TryResumeGameplay(out string error)
    {
        if (_editorGuard == null)
        {
            error = "Editor Mode state guard is not ready.";
            return false;
        }

        return _editorGuard.TryResumeGameplay(out error);
    }

    private static bool _isStartingEditorWidget = false;
    private bool _verboseDiagnostics;
    private BlasphemousLifecycleBridge _lifecycleBridge;
    private EditorModeGuard _editorGuard;
    private ApplyReportPublisher _reportPublisher;
    private UnityRuntimePlacementFactory _runtimeFactory;
    private RoomOverlayReconciler _overlayReconciler;
    private readonly UnityOriginalObjectBaselineCapture _originalObjectBaselineCapture = new();
    private OriginalObjectBaseline _originalObjectBaseline;
    private bool _originalObjectBaselineSubscribed;
    private Coroutine _roomPreparation;
    private Coroutine _creatorPreload;
    private bool _creatorPreloadStarted;
    private bool _pickAfterCandidateChoice;
    private PreviewInstance _preview;

    internal EditorMode() : base(ModInfo.MOD_ID, ModInfo.MOD_NAME, ModInfo.MOD_AUTHOR, ModInfo.MOD_VERSION) { }

    protected override void OnInitialize()
    {
        EditorModeConfig config;
        try
        {
            config = ConfigHandler.Load<EditorModeConfig>() ?? new EditorModeConfig();
        }
        catch (System.Exception exception)
        {
            ModLog.Warn(
                "Unable to load Editor Mode configuration; using defaults. "
                + exception.GetType().Name + ": " + exception.Message);
            config = new EditorModeConfig();
        }

        _verboseDiagnostics = config.VerboseDiagnostics;
        _reportPublisher = new ApplyReportPublisher(
            new ApplyReportSurface(),
            (severity, message) =>
            {
                if (severity == ApplySeverity.Error)
                    ModLog.Error(message);
                else if (severity == ApplySeverity.Warning)
                    ModLog.Warn(message);
                else
                    ModLog.Info(message);
            },
            _verboseDiagnostics);
        _editorGuard = new EditorModeGuard(
            BlasphemousEditorRuntimeState.Capture,
            BlasphemousEditorRuntimeState.Apply);
        _lifecycleBridge = new BlasphemousLifecycleBridge(
            OnRoomLoaded,
            OnRoomUnloaded);

        InputHandler.RegisterDefaultKeybindings(
            new()
            {
                { "OpenEditor", KeyCode.Semicolon },
                { "PickPlacement", KeyCode.P },
                { "PlacePreview", KeyCode.E },
                { "SelectPlacement", KeyCode.Mouse0 },
                { "CopyPlacement", KeyCode.C },
                { "PastePlacement", KeyCode.V },
                { "LeftControl", KeyCode.LeftControl },
                { "RightControl", KeyCode.RightControl },
            });
        SpriteStorage = new(FileHandler);
        LevelManager.OnLevelPreLoaded += CaptureOriginalObjectBaseline;
        _originalObjectBaselineSubscribed = true;
    }

    protected override void OnLevelPreloaded(string oldLevel, string newLevel)
    {
        _lifecycleBridge.OnLevelPreloaded(oldLevel, newLevel);
        RefreshCurrentLevelConfig();
    }

    private void RefreshCurrentLevelConfig()
    {
        LogicManager logic = Core.Logic;
        if (logic == null)
            return;

        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || !activeScene.isLoaded)
            return;

        LevelInitializer initializer = null;
        foreach (GameObject root in activeScene.GetRootGameObjects())
        {
            LevelInitializer candidate = root.GetComponentInChildren<LevelInitializer>();
            if (candidate == null || !candidate.isActiveAndEnabled)
                continue;

            initializer = candidate;
            break;
        }

        if (initializer == null)
            return;

        if (initializer.GetComponentInChildren<LevelEffectsStore>() == null)
        {
            ApplyReport report = new();
            report.Error(
                ReportCodes.RoomEffectsStoreMissing,
                activeScene.name,
                "The active room has no LevelEffectsStore component; its context was not refreshed.");
            PublishReport("Room context refresh", report);
            return;
        }

        if (logic.CurrentLevelConfig == initializer)
            return;

        logic.SetCurrentLevelConfig(initializer);
        ModLog.Warn("Refreshed the active room context before spawning: " + activeScene.name);
    }

    protected override void OnLevelLoaded(string oldLevel, string newLevel)
    {
        bool roomLoaded = _lifecycleBridge.OnLevelLoaded(
            newLevel,
            SceneManager.GetActiveScene().name);

        if (roomLoaded)
        {
            StartCreatorPreload();
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

        Catalog = ContentCatalog.FromProbe(
            result,
            metadata => UnityPreviewFactory.Create(
                metadata,
                FindCurrentObjectHolder,
                CursorWorldPosition,
                _verboseDiagnostics));
        _runtimeFactory = new UnityRuntimePlacementFactory(FindCurrentObjectHolder, _verboseDiagnostics);
        _overlayReconciler = new RoomOverlayReconciler(Catalog, _runtimeFactory);
        if (_lifecycleBridge.CurrentRoom != null)
        {
            StartCreatorPreload();
            StartRoomPreparation(_lifecycleBridge.CurrentRoom);
        }
    }

    protected override void OnDispose()
    {
        if (_originalObjectBaselineSubscribed)
        {
            LevelManager.OnLevelPreLoaded -= CaptureOriginalObjectBaseline;
            _originalObjectBaselineSubscribed = false;
        }
        _originalObjectBaseline = null;
        ExitEditorMode();
        DisposePreview();
        ClearRoomOverlay();
    }

    protected override void OnUpdate()
    {
        if (InputHandler.GetKeyDown("OpenEditor"))
            ToggleEditorMode();
    }

    internal bool IsInputActionPressed(string actionName) => InputHandler.GetKeyDown(actionName);

    internal bool IsInputActionHeld(string actionName) => InputHandler.GetKey(actionName);

    private void ToggleEditorMode()
    {
        if (_editorGuard.IsActive)
        {
            ExitEditorMode();
            return;
        }

        if (!_lifecycleBridge.TryBeginEditing())
        {
            ModLog.Warn("Editor Mode activation rejected: no compatible loaded room.");
            return;
        }

        bool reportOnlyEntry = _roomPreparation == null
            && ReportSurface != null
            && ReportSurface.HasActionableMessage;
        if ((Session == null || PlacementManager == null) && !reportOnlyEntry)
        {
            _lifecycleBridge.EndEditing();
            ModLog.Warn("Editor Mode activation rejected: Room Overlay session is not ready.");
            return;
        }

        if (EditorWidget == null)
        {
            _lifecycleBridge.EndEditing();
            SafeCreateEditorAfterDelay(1.5f);
            ModLog.Warn("Editor Mode activation deferred until its widget is ready.");
            return;
        }

        if (!_editorGuard.TryEnter(true, true, CreateEditorState(), out string error))
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
        if (!_editorGuard.TryExit(out string error))
        {
            ModLog.Error(error);
            return;
        }

        DisposePreview();
        _lifecycleBridge.EndEditing();
        EditorWidget?.gameObject.SetActive(false);
    }

    internal LevelEditSessionExportResult ExportCurrentRoom()
    {
        if (Session == null || _lifecycleBridge.CurrentRoom == null)
        {
            ApplyReport report = new();
            report.Error(ReportCodes.RoomRequired, "export", "No editable Room Overlay is loaded.");
            PublishReport("Level export", report);
            return null;
        }

        LevelEditSessionExportResult result = Session.Export(RoomExportPath(_lifecycleBridge.CurrentRoom));
        PublishReport("Level export", result.Report);
        return result;
    }

    internal bool ImportCurrentRoom()
    {
        RoomIdentity room = _lifecycleBridge.CurrentRoom;
        if (room == null || _overlayReconciler == null)
        {
            ApplyReport report = new();
            report.Error(ReportCodes.RoomRequired, "import", "No editable Room Overlay is loaded.");
            PublishReport("Level import", report);
            return false;
        }

        return PrepareRoom(room);
    }

    internal bool TryPlaceAtCursor(string type)
    {
        if (!RequireAdditionsPage("Place"))
            return false;

        RuntimePlacementManager manager = PlacementManager;
        if (manager == null)
        {
            ApplyReport unavailable = new();
            unavailable.Error(ReportCodes.PlaceNotReady, "placement", "Placement editing is not ready.");
            PublishReport("Place", unavailable);
            return false;
        }

        ApplyReport report = new();
        Vector3 position = CursorWorldPosition();
        LogCursorPosition("CURSOR_PLACE", position);
        bool result = manager.TryPlace(
            type,
            new PlacementTransform(
                position.x,
                position.y,
                position.z,
                0f,
                0f,
                0f,
                1f,
                1f,
                1f),
            [],
            report,
            out _);
        PublishReport("Place", report);
        return result;
    }

    internal bool TryPlacePreviewAtCursor()
    {
        if (!RequireAdditionsPage("Place"))
            return false;

        if (_preview != null)
            return TryPlaceAtCursor(_preview.Metadata.Type);

        ApplyReport report = new();
        report.Warning(ReportCodes.PlaceNoPreview, "preview", "Select a catalog entry and create a preview before placing it.");
        PublishReport("Place", report);
        return false;
    }

    internal bool TryUndo()
    {
        if (ActivePage != EditorPage.Additions)
            return ReportPageCommandUnavailable("Undo");

        ApplyReport report = new();
        bool result = Pages != null && Pages.TryUndo(report);
        PublishReport("Undo", report);
        return result;
    }

    internal bool TryRedo()
    {
        if (ActivePage != EditorPage.Additions)
            return ReportPageCommandUnavailable("Redo");

        ApplyReport report = new();
        bool result = Pages != null && Pages.TryRedo(report);
        PublishReport("Redo", report);
        return result;
    }

    internal bool TryDeleteSelected()
    {
        if (!RequireAdditionsPage("Delete"))
            return false;

        RuntimePlacementManager manager = PlacementManager;
        if (manager == null || manager.Selected == null)
            return false;

        ApplyReport report = new();
        bool result = manager.TryDelete(manager.Selected.Id, report);
        PublishReport("Delete", report);
        return result;
    }

    internal bool TryToggleSelectedLock()
    {
        if (!RequireAdditionsPage("Lock"))
            return false;

        RuntimePlacementManager manager = PlacementManager;
        if (manager == null || manager.Selected == null)
            return false;

        ApplyReport report = new();
        bool result = manager.TrySetLocked(
            manager.Selected.Id,
            !manager.Selected.IsLocked,
            report);
        PublishReport("Lock", report);
        return result;
    }

    internal bool TryCopySelected()
    {
        if (!RequireAdditionsPage("Copy"))
            return false;

        RuntimePlacementManager manager = PlacementManager;
        if (manager == null || manager.Selected == null)
            return false;

        ApplyReport report = new();
        bool result = manager.TryCopy(manager.Selected.Id, report, out _);
        PublishReport("Copy", report);
        return result;
    }

    internal bool TryPickSelected()
    {
        if (!RequireAdditionsPage("Pick"))
            return false;

        RuntimePlacementManager manager = PlacementManager;
        ApplyReport report = new();
        if (manager == null)
        {
            report.Error(ReportCodes.PickNotReady, "clipboard", "Placement editing is not ready.");
            PublishReport("Pick", report);
            return false;
        }
        if (manager.Selected == null)
        {
            report.Warning(ReportCodes.PickNoSelection, "clipboard", "Select or place an Architect-owned placement before clicking Pick.");
            PublishReport("Pick", report);
            return false;
        }

        return TryPickPlacement(manager.Selected.Id, "Pick");
    }

    internal bool TryPickAtCursor()
    {
        if (!RequireAdditionsPage("Pick at cursor"))
            return false;

        _pickAfterCandidateChoice = false;
        bool found = TryFindAllAtCursor(out IList<IRuntimePlacementHandle> handles, out string error);
        ApplyReport report = new();
        if (!found && error != null)
        {
            report.Error(ReportCodes.PickFailed, "cursor", error);
            PublishReport("Pick at cursor", report);
            return false;
        }

        bool result = Pages != null && Pages.TryPresentAdditionCandidates(handles, report);
        if (result && Pages.AdditionCandidates.Count == 1)
            result = Pages.Additions.TryPick(Pages.AdditionCandidates[0].Id, report, out _);
        else if (result)
            _pickAfterCandidateChoice = true;

        PublishReport("Pick at cursor", report);
        return result;
    }

    internal void ReportPickBlockedByEditorControl()
    {
        ApplyReport report = new();
        report.Warning(ReportCodes.PickPointerOverEditor, "cursor", "Move the pointer over the room and outside editor controls before pressing P.");
        PublishReport("Pick at cursor", report);
    }

    internal bool TrySwitchPage()
    {
        _pickAfterCandidateChoice = false;
        EditorPageController pages = Pages;
        ApplyReport report = new();
        if (pages == null)
        {
            report.Error(ReportCodes.RoomRequired, "page", "No editable Room Overlay is loaded.");
            PublishReport("Page", report);
            return false;
        }

        bool result = pages.TrySwitchPage(report);
        PublishReport("Page", report);
        return result;
    }

    internal bool TryChooseAdditionCandidate(string identity)
    {
        EditorPageController pages = Pages;
        bool pickAfterSelection = _pickAfterCandidateChoice;
        _pickAfterCandidateChoice = false;
        ApplyReport report = new();
        if (pages == null
            || !pages.TryChooseAdditionCandidate(identity, report, out PlacementRecord selected))
        {
            if (report.Entries.Count == 0)
            {
                report.Error(
                    ReportCodes.SelectionCandidateNotFound,
                    identity ?? "$",
                    "The Additions candidate is unavailable.");
            }
            PublishReport("Select candidate", report);
            return false;
        }

        report.Info(
            ReportCodes.SelectionCandidateSelected,
            selected.Id,
            "Selected " + selected.Type + " / " + selected.Id + " from the overlap list.");
        if (pickAfterSelection
            && !pages.Additions.TryPick(selected.Id, report, out _))
        {
            PublishReport("Pick candidate", report);
            return false;
        }

        PublishReport(pickAfterSelection ? "Pick candidate" : "Select candidate", report);
        return true;
    }

    internal bool TryChooseOriginalCandidate(string identity)
    {
        EditorPageController pages = Pages;
        if (pages == null || ActivePage != EditorPage.OriginalObjects)
            return ReportPageCommandUnavailable("Select Original Object");

        ApplyReport report = new();
        if (!pages.TryChooseOriginalCandidate(
            identity,
            report,
            out _))
        {
            if (report.Entries.Count == 0)
            {
                report.Error(
                    ReportCodes.SelectionCandidateNotFound,
                    identity ?? "$",
                    "The Original Object candidate is unavailable.");
            }
            PublishReport("Select Original Object", report);
            return false;
        }

        PublishReport("Select Original Object", report);
        return true;
    }

    internal bool TrySelectOriginal(string identity)
    {
        EditorPageController pages = Pages;
        if (pages == null || ActivePage != EditorPage.OriginalObjects)
            return ReportPageCommandUnavailable("Select Original Object");

        ApplyReport report = new();
        if (!pages.TrySelectOriginal(identity, report, out _))
        {
            if (report.Entries.Count == 0)
            {
                report.Error(
                    ReportCodes.SelectionCandidateNotFound,
                    identity ?? "$",
                    "The Original Object operation target is unavailable.");
            }
            PublishReport("Select Original Object", report);
            return false;
        }

        PublishReport("Select Original Object", report);
        return true;
    }

    private bool RequireAdditionsPage(string operation)
    {
        return ActivePage == EditorPage.Additions
            || ReportPageCommandUnavailable(operation);
    }

    private bool ReportPageCommandUnavailable(string operation)
    {
        ApplyReport report = new();
        report.Warning(
            ReportCodes.PageCommandUnavailable,
            ActivePage.ToString(),
            "The " + operation + " command is unavailable on the " + ActivePage + " page.");
        PublishReport(operation, report);
        return false;
    }

    internal bool TryPasteAtCursor()
    {
        if (!RequireAdditionsPage("Paste"))
            return false;

        RuntimePlacementManager manager = PlacementManager;
        if (manager == null)
            return false;

        ApplyReport report = new();
        PlacementRecord clipboard = manager.Clipboard;
        if (clipboard == null)
        {
            report.Error(ReportCodes.ClipboardEmpty, "paste", "The editor cursor has no Placement.");
            PublishReport("Paste", report);
            return false;
        }

        Vector3 position = CursorWorldPosition();
        LogCursorPosition("CURSOR_PASTE", position);
        bool result = manager.TryPaste(
            new PlacementTransform(
                position.x,
                position.y,
                position.z,
                clipboard.Transform.RotationX,
                clipboard.Transform.RotationY,
                clipboard.Transform.RotationZ,
                clipboard.Transform.ScaleX,
                clipboard.Transform.ScaleY,
                clipboard.Transform.ScaleZ),
            report,
            out _);
        PublishReport("Paste", report);
        return result;
    }

    internal bool TryPreviewAtCursor(string type)
    {
        if (!RequireAdditionsPage("Preview"))
            return false;

        if (Catalog == null)
            return false;

        LogCursorPosition("CURSOR_PREVIEW", CursorWorldPosition());
        DisposePreview();
        if (!Catalog.TryCreatePreview(type, out _preview, out string error))
        {
            ApplyReport report = new();
            report.Error(ReportCodes.PreviewFailed, type ?? "<null>", error);
            PublishReport("Preview", report);
            return false;
        }

        ApplyReport success = new();
        success.Info(
            ReportCodes.PreviewCreated,
            type,
            "Preview follows the pointer through the level view; Clear removes it. It is not part of the Room Overlay.");
        PublishReport("Preview", success);
        return true;
    }

    internal void UpdatePreviewAtCursor()
    {
        if (IsActive && ActivePage == EditorPage.Additions && _preview != null)
            _preview.FollowCursor();
    }

    internal void ClearPreview()
    {
        if (!RequireAdditionsPage("Clear preview"))
            return;

        if (_preview == null)
            return;

        DisposePreview();
        ApplyReport report = new();
        report.Info(ReportCodes.PreviewDisposed, "preview", "Isolated preview discarded.");
        PublishReport("Preview", report);
    }

    internal bool TryApplySelectedTransform(string[] values)
    {
        if (!RequireAdditionsPage("Transform"))
            return false;

        RuntimePlacementManager manager = PlacementManager;
        if (manager == null || manager.Selected == null)
            return false;

        ApplyReport report = new();
        if (values == null || values.Length != 9)
        {
            report.Error(ReportCodes.TransformInvalid, manager.Selected.Id, "Exactly nine transform values are required.");
            PublishReport("Transform", report);
            return false;
        }

        float[] parsed = new float[values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            if (!float.TryParse(values[index], NumberStyles.Float, CultureInfo.InvariantCulture, out parsed[index])
                || float.IsNaN(parsed[index])
                || float.IsInfinity(parsed[index]))
            {
                report.Error(ReportCodes.TransformInvalid, manager.Selected.Id, "Transform values must be finite numbers.");
                PublishReport("Transform", report);
                return false;
            }
        }

        bool result = manager.TryMove(
            manager.Selected.Id,
            new PlacementTransform(
                parsed[0], parsed[1], parsed[2],
                parsed[3], parsed[4], parsed[5],
                parsed[6], parsed[7], parsed[8]),
            report);
        PublishReport("Transform", report);
        return result;
    }

    internal bool TrySetSelectedProperty(string key, string value)
    {
        if (!RequireAdditionsPage("Property"))
            return false;

        RuntimePlacementManager manager = PlacementManager;
        if (manager == null || manager.Selected == null)
            return false;

        ApplyReport report = new();
        bool result = manager.TrySetProperty(manager.Selected.Id, key, value, report);
        PublishReport("Property", report);
        return result;
    }

    internal bool TrySelectAtCursor()
    {
        if (!RequireAdditionsPage("Select"))
            return false;

        _pickAfterCandidateChoice = false;
        bool found = TryFindAllAtCursor(out IList<IRuntimePlacementHandle> handles, out string error);
        if (!found && error != null)
        {
            ApplyReport failure = new();
            failure.Error(ReportCodes.SelectionFailed, "cursor", error);
            PublishReport("Select", failure);
            return false;
        }

        ApplyReport report = new();
        bool result = Pages != null && Pages.TryPresentAdditionCandidates(handles, report);
        PublishReport("Select", report);
        return result;
    }

    internal bool TrySelectOriginalAtCursor()
    {
        EditorPageController pages = Pages;
        RoomIdentity room = _lifecycleBridge?.CurrentRoom;
        if (pages == null || room == null)
        {
            ApplyReport failure = new();
            failure.Error(ReportCodes.RoomRequired, "Original Objects", "No editable Room Overlay is loaded.");
            PublishReport("Select Original Object", failure);
            return false;
        }
        if (ActivePage != EditorPage.OriginalObjects)
            return ReportPageCommandUnavailable("Select Original Object");

        IOriginalObjectHitSource hitSource = new UnityOriginalObjectHitSource(
            room.FrameworkLevelKey,
            Input.mousePosition);
        ApplyReport report = new();
        bool result = pages.TryPresentOriginalCandidates(hitSource, report);
        PublishReport("Select Original Object", report);
        return result;
    }

    internal bool TryReset()
    {
        if (!RequireAdditionsPage("Reset"))
            return false;

        return RunManagerCommand("Reset", (manager, report) => manager.TryReset(report));
    }

    internal void ExitFromUi()
    {
        ExitEditorMode();
    }

    private void PublishReport(string operation, ApplyReport report)
    {
        _reportPublisher.Publish(operation, report);
        EditorWidget?.Refresh();
    }

    private bool RunManagerCommand(
        string operation,
        System.Func<RuntimePlacementManager, ApplyReport, bool> command)
    {
        RuntimePlacementManager manager = PlacementManager;
        if (manager == null)
            return false;

        ApplyReport report = new();
        bool result = command(manager, report);
        PublishReport(operation, report);
        return result;
    }

    private void OnRoomLoaded(RoomIdentity room)
    {
        ModLog.Info("Room Overlay ready: " + room.FrameworkLevelKey);
        StartRoomPreparation(room);
    }

    private void OnRoomUnloaded(RoomIdentity room)
    {
        ExitEditorMode();
        DisposePreview();
        ClearRoomOverlay();
        ModLog.Info("Room Overlay cleared: " + room.FrameworkLevelKey);
    }

    private void StartRoomPreparation(RoomIdentity room)
    {
        if (room == null || _overlayReconciler == null)
            return;

        if (UIController.instance == null)
        {
            PrepareRoom(room);
            return;
        }

        if (_roomPreparation != null)
            UIController.instance.StopCoroutine(_roomPreparation);
        _roomPreparation = UIController.instance.StartCoroutine(PrepareRoomAfterFrame(room));
    }

    private IEnumerator PrepareRoomAfterFrame(RoomIdentity room)
    {
        yield return null;
        while (_creatorPreload != null)
            yield return null;
        _roomPreparation = null;
        if (_lifecycleBridge.CurrentRoom == null || !_lifecycleBridge.CurrentRoom.Equals(room))
            yield break;
        PrepareRoom(room);
    }

    private bool PrepareRoom(RoomIdentity room)
    {
        if (room == null || _overlayReconciler == null)
            return false;

        string path = RoomExportPath(room);
        FrameworkFileResult file = LevelFrameworkAdapter.ImportFile(path, room.ExportFileKey);
        OriginalObjectBaseline baseline = _originalObjectBaseline;
        if (baseline != null
            && !string.Equals(
                baseline.FrameworkLevelKey,
                room.FrameworkLevelKey,
                System.StringComparison.OrdinalIgnoreCase))
        {
            baseline = null;
        }
        RoomOverlayReconcileResult result = _overlayReconciler.Apply(
            room,
            file,
            baseline,
            new UnityOriginalObjectPathResolver());
        PublishReport("Room import", result.Report);
        if (!result.Succeeded)
        {
            ModLog.Warn("Room Overlay session was not created; see the Room import summary.");
            return false;
        }

        return true;
    }

    private void ClearRoomOverlay()
    {
        if (_roomPreparation != null && UIController.instance != null)
            UIController.instance.StopCoroutine(_roomPreparation);
        _roomPreparation = null;

        if (_overlayReconciler == null)
            return;

        ApplyReport report = new();
        if (!_overlayReconciler.TryClear(report) && report.Entries.Count > 0)
            PublishReport("Room cleanup", report);
    }

    private void DisposePreview()
    {
        if (_preview == null)
            return;

        _preview.Dispose();
        _preview = null;
    }

    private void StartCreatorPreload()
    {
        // Level Framework preload from MainMenu previously failed with "Scene to unload is invalid".
        if (_lifecycleBridge == null
            || _lifecycleBridge.CurrentRoom == null
            || _creatorPreloadStarted
            || Catalog == null
            || UIController.instance == null)
            return;

        _creatorPreloadStarted = true;
        _creatorPreload = UIController.instance.StartCoroutine(PreloadCreatorSources());
    }

    private IEnumerator PreloadCreatorSources()
    {
        try
        {
            yield return null;
            foreach (CreatorMetadata metadata in Catalog.Entries)
            {
                if (!RuntimeCreatorRegistry.TryGet(metadata.Type, out Blasphemous.Framework.Levels.ObjectCreator creator, out string error))
                {
                    ModLog.Warn(error);
                    continue;
                }
                if (creator.Loader == null || creator.Loader.Result != null)
                    continue;

                IEnumerator load;
                try
                {
                    load = creator.Loader.Apply();
                }
                catch (System.Exception exception)
                {
                    ModLog.Error("Creator source preload could not start for " + metadata.Type + ": " + exception.Message);
                    continue;
                }

                while (true)
                {
                    bool hasNext;
                    object current = null;
                    try
                    {
                        hasNext = load.MoveNext();
                        if (hasNext)
                            current = load.Current;
                    }
                    catch (System.Exception exception)
                    {
                        ModLog.Error("Creator source preload failed for " + metadata.Type + ": " + exception.Message);
                        break;
                    }

                    if (!hasNext)
                        break;
                    yield return current;
                }

                if (creator.Loader.Result == null)
                    ModLog.Warn("Creator source preload returned no object: " + metadata.Type);
            }
        }
        finally
        {
            _creatorPreload = null;
        }
    }

    private string RoomExportPath(RoomIdentity room)
    {
        return Path.Combine(
            Path.Combine(
                Path.Combine(FileHandler.ModdingFolder, "levels"),
                ModInfo.MOD_NAME),
            room.ExportFileKey + ".json");
    }

    private static Transform FindCurrentObjectHolder()
    {
        LevelInitializer initializer = Object.FindObjectOfType<LevelInitializer>();
        return initializer?.transform;
    }

    private static Vector3 CursorWorldPosition()
    {
        if (Camera.main == null)
            return Vector3.zero;

        Vector2 cameraPixels = UnityScreenCoordinateMapper.ToCameraPixels(Camera.main, Input.mousePosition);
        return UnityScreenCoordinateMapper.ToWorldPosition(Camera.main, cameraPixels);
    }

    private void LogCursorPosition(string action, Vector3 worldPosition)
    {
        if (!_verboseDiagnostics
            && string.Equals(action, "CURSOR_PREVIEW", System.StringComparison.Ordinal))
            return;

        Camera camera = Camera.main;
        Vector2 screenPosition = Input.mousePosition;
        Vector2 cameraPixels = UnityScreenCoordinateMapper.ToCameraPixels(camera, screenPosition);
        string targetTextureSize = camera == null || camera.targetTexture == null
            ? "<none>"
            : camera.targetTexture.width + "x" + camera.targetTexture.height;
        ModLog.Debug(
            "[DIAG] " + action
            + " screenPosition=" + screenPosition
            + " cameraPixelPosition=" + cameraPixels
            + " screenSize=" + Screen.width + "x" + Screen.height
            + " targetTextureSize=" + targetTextureSize
            + " worldPosition=" + worldPosition
            + " camera=" + (camera == null ? "<none>" : camera.name)
            + " orthographic=" + (camera != null && camera.orthographic)
            + " pixelRect=" + (camera == null ? "<none>" : camera.pixelRect.ToString()));
    }

    private bool TryPickPlacement(string id, string action)
    {
        RuntimePlacementManager manager = PlacementManager;
        ApplyReport report = new();
        if (manager == null)
        {
            report.Error(ReportCodes.PickNotReady, "clipboard", "Placement editing is not ready.");
            PublishReport(action, report);
            return false;
        }

        bool result = manager.TryPick(id, report, out _);
        PublishReport(action, report);
        return result;
    }

    private bool TryFindAllAtCursor(
        out IList<IRuntimePlacementHandle> handles,
        out string error)
    {
        handles = new IRuntimePlacementHandle[0];
        error = null;
        if (_runtimeFactory == null || PlacementManager == null)
        {
            error = "Placement editing is not ready.";
            return false;
        }

        return _runtimeFactory.TryFindAllAtScreenPoint(Input.mousePosition, out handles, out error);
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
            false,
            true,
            (int)CursorLockMode.None);
    }

    private void CaptureOriginalObjectBaseline(Level oldLevel, Level newLevel)
    {
        string frameworkLevelKey = newLevel?.LevelName ?? string.Empty;
        if (string.IsNullOrEmpty(frameworkLevelKey)
            || string.Equals(frameworkLevelKey, "MainMenu", System.StringComparison.OrdinalIgnoreCase))
        {
            _originalObjectBaseline = null;
            return;
        }

        OriginalObjectBaseline baseline = _originalObjectBaselineCapture.Capture(frameworkLevelKey);
        _originalObjectBaseline = baseline;
        if (baseline.CaptureReport.HasErrors)
        {
            ModLog.Error(
                "Editor Mode could not capture the pre-mod Original Object baseline for "
                + frameworkLevelKey
                + ". Original Objects remain unavailable for this room.");
        }
        else
        {
            ModLog.Info(
                "Captured pre-mod Original Object baseline for "
                + frameworkLevelKey
                + " before ModdingAPI dispatched OnLevelPreloaded.");
        }
    }

    private GameObject CreateEditorGameObject()
    {
        GameObject obj = new("Editor Widget");

        // Add and configure RectTransform BEFORE setting parent
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.sizeDelta = new(640f, 420f);
        rt.anchorMin = new(0.5f, 0.5f);
        rt.anchorMax = new(0.5f, 0.5f);
        rt.pivot = new(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;

        obj.transform.SetParent(UIModder.Parents.CanvasHighRes, false);
        obj.layer = LayerMask.NameToLayer("UI");
        obj.transform.SetAsLastSibling();

        obj.AddComponent<EditorWidget>();
        rt.localScale = Vector3.one * (rt.parent.GetComponent<RectTransform>().rect.width * 0.8f / rt.rect.width);

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
        if (UIController.instance == null)
        {
            ModLog.Warn("Editor Mode widget creation deferred because UI Framework is not ready.");
            return;
        }

        if ((EditorWidget == null || GameObject.Find("Editor Widget") == null) && !_isStartingEditorWidget)
        {
            UIController.instance.StartCoroutine(DelayAndCreateEditorGameObjectCoroutine(delaySeconds));
        }
    }
}