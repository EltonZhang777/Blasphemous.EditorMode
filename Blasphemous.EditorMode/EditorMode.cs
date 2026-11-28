using Blasphemous.EditorMode.Components;
using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Editing;
using Blasphemous.EditorMode.Framework;
using Blasphemous.EditorMode.Lifecycle;
using Blasphemous.EditorMode.Runtime;
using Blasphemous.Framework.UI;
using Blasphemous.ModdingAPI;
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
    private const string PropertiesOperation = "Properties";

    internal EditorWidget EditorWidget { get; private set; }

    internal bool IsActive { get { return _editorGuard != null && _editorGuard.IsActive; } }
    internal static SpriteStorage SpriteStorage { get; private set; }
    internal ContentCatalog Catalog { get; private set; }
    internal RuntimeCreatorInventory CreatorInventory { get; private set; }
    internal CreatorCatalogController CreatorCatalog { get; private set; }
    internal RoomIdentity CurrentRoom { get { return _lifecycleBridge?.CurrentRoom; } }
    internal LevelEditSession Session { get { return _overlayReconciler?.CurrentSession; } }
    internal RuntimePlacementManager PlacementManager { get { return _overlayReconciler?.Manager; } }
    internal ApplyReportSurface ReportSurface { get { return _reportPublisher.Surface; } }
    internal string PreviewStatus
    {
        get
        {
            return _catalogActions == null
                ? "none"
                : _catalogActions.PreviewStatus;
        }
    }

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
    private Coroutine _roomPreparation;
    private RoomIdentity _pendingRoomPreparation;
    private bool _creatorSourceLoadRunning;
    private bool _creatorSourceLoadsDisabled;
    private int _creatorSourceLoadToken;
    private CreatorCatalogActionRouter _catalogActions;

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
            });
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
            PublishReport("Room context refresh", report, false);
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
            SafeCreateEditorAfterDelay(1.5f);
        }
    }

    protected override void OnLevelUnloaded(string oldLevel, string newLevel)
    {
        _lifecycleBridge.OnLevelUnloaded(oldLevel, newLevel);
    }

    protected override void OnAllInitialized()
    {
        _creatorSourceLoadsDisabled = false;
        CreatorInventory = RuntimeCreatorInventoryReader.Read();
        List<string> invalidatedLoadedSourceTypes = [];
        if (CreatorCatalog != null)
        {
            CreatorCatalog.ResetSourceStates();
            invalidatedLoadedSourceTypes.AddRange(
                CreatorCatalog.GetInvalidatedLoadedSourceTypes());
            InvalidateCreatorSourceLoads();
        }
        CreatorCatalog = new CreatorCatalogController(
            CreatorInventory,
            TryInspectRuntimeSource,
            invalidatedLoadedSourceTypes);
        Catalog = ContentCatalog.FromInventory(
            CreatorInventory,
            metadata => UnityPreviewFactory.Create(
                metadata,
                FindCurrentObjectHolder,
                CursorWorldPosition,
                _verboseDiagnostics));
        _catalogActions = new CreatorCatalogActionRouter(
            CreatorCatalog,
            Catalog,
            TryPlaceAtCursor);
        PublishCreatorInventoryDiagnostics(CreatorInventory);
        PublishCreatorSourceProbeFailures(CreatorCatalog);
        _runtimeFactory = new UnityRuntimePlacementFactory(
            FindCurrentObjectHolder,
            _verboseDiagnostics,
            MarkCreatorRuntimeLookupFailed);
        _overlayReconciler = new RoomOverlayReconciler(Catalog, _runtimeFactory);
        if (_lifecycleBridge.CurrentRoom != null)
        {
            StartRoomPreparation(_lifecycleBridge.CurrentRoom);
        }
    }

    protected override void OnDispose()
    {
        _creatorSourceLoadsDisabled = true;
        ExitEditorMode();
        DisposePreview();
        ClearRoomOverlay();
        InvalidateCreatorSourceLoads();
        CreatorCatalog?.ResetSourceStates();
    }

    protected override void OnUpdate()
    {
        if (_pendingRoomPreparation != null)
            TryStartRoomPreparation();

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

        if (!_editorGuard.TryEnter(true, CreateEditorState(), out string error))
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
        CreatorCatalog?.InvalidateActiveSourceLoad();
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

    internal bool TryBeginImportCurrentRoom()
    {
        RoomIdentity room = _lifecycleBridge.CurrentRoom;
        if (room == null || _overlayReconciler == null)
        {
            ApplyReport report = new();
            report.Error(ReportCodes.RoomRequired, "import", "No editable Room Overlay is loaded.");
            PublishReport("Level import", report, false);
            return false;
        }

        StartRoomPreparation(room);
        return true;
    }

    internal bool TryPlaceAtCursor(string type)
    {
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
        PublishReport("Place", report, !result);
        return result;
    }

    internal bool TryUndo()
    {
        return RunManagerCommand("Undo", (manager, report) => manager.TryUndo(report));
    }

    internal bool TryRedo()
    {
        return RunManagerCommand("Redo", (manager, report) => manager.TryRedo(report));
    }

    internal bool TryDeleteSelected()
    {
        RuntimePlacementManager manager = PlacementManager;
        if (manager == null || manager.Selected == null)
            return false;

        ApplyReport report = new();
        bool result = manager.TryDelete(manager.Selected.Id, report);
        PublishReport("Delete", report, false);
        return result;
    }

    internal bool TryToggleSelectedLock()
    {
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
        if (!TryFindAtCursor(out IRuntimePlacementHandle handle, out string error))
        {
            ApplyReport failure = new();
            if (error == null)
            {
                failure.Warning(ReportCodes.PickNoTarget, "cursor", "Point to an Architect-owned placement before pressing P.");
            }
            else
            {
                failure.Error(ReportCodes.PickFailed, "cursor", error);
            }
            PublishReport("Pick at cursor", failure);
            return false;
        }

        ApplyReport report = new();
        RuntimePlacementManager.ReportCreatorLookupFallback(handle, report);
        return TryPickPlacement(handle.PlacementId, "Pick at cursor", report);
    }

    internal void ReportPickBlockedByEditorControl()
    {
        ApplyReport report = new();
        report.Warning(ReportCodes.PickPointerOverEditor, "cursor", "Move the pointer over the room and outside editor controls before pressing P.");
        PublishReport("Pick at cursor", report);
    }

    internal bool TryPasteAtCursor()
    {
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
        PublishReport("Paste", report, !result);
        return result;
    }

    internal bool TrySelectCatalogCreator(string type)
    {
        CreatorCatalogActionRouter actions = _catalogActions;
        if (actions == null)
            return false;

        bool selected = actions.TrySelect(type, out ApplyReport report);
        if (report != null)
            PublishReport("Preview", report);
        else if (selected)
            EditorWidget?.RefreshAfterReport(true);
        if (selected)
            EnsureCreatorSourceLoad();
        return selected;
    }

    internal bool TryPreviewSelectedCreator()
    {
        CreatorCatalogActionRouter actions = _catalogActions;
        if (actions == null)
        {
            PublishCatalogActionUnavailable(
                "Preview",
                ReportCodes.PreviewFailed,
                "The creator catalog is not ready.");
            return false;
        }

        LogCursorPosition("CURSOR_PREVIEW", CursorWorldPosition());
        bool result = actions.TryPreviewSelected(out ApplyReport report);
        if (report != null)
            PublishReport("Preview", report);
        return result;
    }

    internal bool TryPlaceSelectedCreator()
    {
        CreatorCatalogActionRouter actions = _catalogActions;
        if (actions == null)
        {
            PublishCatalogActionUnavailable(
                "Place",
                ReportCodes.PlaceNotReady,
                "The creator catalog is not ready.");
            return false;
        }

        bool result = actions.TryPlaceSelected(out ApplyReport report);
        if (report != null)
            PublishReport("Place", report);
        return result;
    }

    internal void UpdatePreviewAtCursor()
    {
        if (IsActive)
            _catalogActions?.FollowPreview();
    }

    internal void ClearPreview()
    {
        if (_catalogActions == null || _catalogActions.Preview == null)
            return;

        ApplyReport report = new();
        System.Exception error = _catalogActions.ClearPreview();
        if (error == null)
        {
            report.Info(ReportCodes.PreviewDisposed, "preview", "Isolated preview discarded.");
        }
        else
        {
            report.Error(
                ReportCodes.PreviewFailed,
                "preview",
                "The isolated preview could not be discarded: " + error.Message);
        }
        PublishReport("Preview", report);
    }

    internal bool TryApplySelectedTransform(string[] values)
    {
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

    internal bool TryApplySelectedProperties(string text)
    {
        RuntimePlacementManager manager = PlacementManager;
        if (manager == null || manager.Selected == null)
            return false;

        ApplyReport report = new();
        bool result = manager.TryApplyProperties(manager.Selected.Id, text, report);
        PublishReport(PropertiesOperation, report, !result);
        return result;
    }

    internal void OnRawPropertiesEditStarted()
    {
        if (!ClearRawPropertiesDiagnostics())
        {
            return;
        }

        EditorWidget?.RefreshAfterReport(true);
    }

    internal bool ClearRawPropertiesDiagnostics()
    {
        return _reportPublisher.Surface.ClearOperation(PropertiesOperation);
    }

    internal bool TrySelectAtCursor()
    {
        if (!TryFindAtCursor(out IRuntimePlacementHandle handle, out string error))
        {
            if (error != null)
            {
                ApplyReport failure = new();
                failure.Error(ReportCodes.SelectionFailed, "cursor", error);
                PublishReport("Select", failure, true);
            }
            return false;
        }

        ApplyReport report = new();
        RuntimePlacementManager.ReportCreatorLookupFallback(handle, report);
        bool result = PlacementManager.TrySelectRuntimeObject(handle, report, out _);
        PublishReport("Select", report, !result);
        return result;
    }

    internal bool TryReset()
    {
        return RunManagerCommand("Reset", (manager, report) => manager.TryReset(report));
    }

    internal void ExitFromUi()
    {
        ExitEditorMode();
    }

    private void PublishReport(
        string operation,
        ApplyReport report,
        bool preservePropertiesDraft = true)
    {
        _reportPublisher.Publish(operation, report);
        EditorWidget?.RefreshAfterReport(preservePropertiesDraft);
    }

    private void PublishCreatorInventoryDiagnostics(RuntimeCreatorInventory inventory)
    {
        if (inventory.Diagnostics.Count == 0)
        {
            ModLog.Info(inventory.Message);
            return;
        }

        ApplyReport report = new();
        foreach (CreatorInventoryDiagnostic diagnostic in inventory.Diagnostics)
        {
            if (diagnostic.Severity == ApplySeverity.Error)
                report.Error(diagnostic.Code, diagnostic.Location, diagnostic.Message);
            else if (diagnostic.Severity == ApplySeverity.Warning)
                report.Warning(diagnostic.Code, diagnostic.Location, diagnostic.Message);
            else
                report.Info(diagnostic.Code, diagnostic.Location, diagnostic.Message);
        }

        PublishReport("Creator inventory", report);
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
        PublishReport(operation, report, false);
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
        InvalidateCreatorSourceLoads();
        CreatorCatalog?.ResetSourceStates();
        CreatorCatalog?.SetPlacementReady(
            false,
            "No editable Room Overlay is loaded.");
        PublishCreatorSourceProbeFailures(CreatorCatalog);
        ModLog.Info("Room Overlay cleared: " + room.FrameworkLevelKey);
    }

    private void StartRoomPreparation(RoomIdentity room)
    {
        if (room == null || _overlayReconciler == null)
            return;

        _pendingRoomPreparation = room;
        TryStartRoomPreparation();
    }

    private void TryStartRoomPreparation()
    {
        RoomIdentity room = _pendingRoomPreparation;
        if (room == null || _overlayReconciler == null)
        {
            _pendingRoomPreparation = null;
            return;
        }
        if (!IsCurrentRoom(room))
        {
            _pendingRoomPreparation = null;
            return;
        }
        if (UIController.instance == null)
            return;

        _pendingRoomPreparation = null;
        if (_roomPreparation != null)
            UIController.instance.StopCoroutine(_roomPreparation);
        _roomPreparation = UIController.instance.StartCoroutine(PrepareRoomAfterFrame(room));
    }

    private IEnumerator PrepareRoomAfterFrame(RoomIdentity room)
    {
        yield return null;
        if (!IsCurrentRoom(room))
        {
            _roomPreparation = null;
            yield break;
        }

        FrameworkFileResult file = LevelFrameworkAdapter.ImportFile(
            RoomExportPath(room),
            room.ExportFileKey);
        List<string> sourceTypes = [.. LevelEditSession.GetReferencedCreatorTypes(
            file,
            Catalog.Entries)];
        foreach (string type in sourceTypes)
            CreatorCatalog?.RequireSourceLoad(type);
        EnsureCreatorSourceLoad();

        while (RequiresSourcePreparation(sourceTypes))
        {
            if (!IsCurrentRoom(room))
            {
                _roomPreparation = null;
                yield break;
            }

            foreach (string type in sourceTypes)
            {
                if (CreatorCatalog?.GetSourceState(type) == CreatorCatalogSourceState.NotRequested)
                    CreatorCatalog.RequireSourceLoad(type);
            }
            EnsureCreatorSourceLoad();
            yield return null;
        }

        _roomPreparation = null;
        if (!IsCurrentRoom(room))
            yield break;

        PrepareRoom(room, file);
    }

    private bool IsCurrentRoom(RoomIdentity room)
    {
        return _lifecycleBridge != null
            && _lifecycleBridge.CurrentRoom != null
            && _lifecycleBridge.CurrentRoom.Equals(room);
    }

    private bool RequiresSourcePreparation(IEnumerable<string> types)
    {
        if (CreatorCatalog == null)
            return false;

        foreach (string type in types)
        {
            CreatorCatalogSourceState state = CreatorCatalog.GetSourceState(type);
            if (state == CreatorCatalogSourceState.NotRequested
                || state == CreatorCatalogSourceState.Loading)
                return true;
        }

        return false;
    }

    private bool PrepareRoom(RoomIdentity room, FrameworkFileResult file)
    {
        if (room == null || _overlayReconciler == null || file == null)
            return false;

        RoomOverlayReconcileResult result = _overlayReconciler.Apply(room, file);
        if (!result.Succeeded)
        {
            CreatorCatalog?.SetPlacementReady(
                false,
                "The editable Room Overlay is not ready.");
            PublishReport("Room import", result.Report, false);
            ModLog.Warn("Room Overlay session was not created; see the Room import summary.");
            return false;
        }

        CreatorCatalog?.SetPlacementReady(true);
        PublishReport("Room import", result.Report, false);
        EditorWidget?.RefreshAfterReport(true);
        return true;
    }

    private void ClearRoomOverlay()
    {
        _pendingRoomPreparation = null;
        if (_roomPreparation != null && UIController.instance != null)
            UIController.instance.StopCoroutine(_roomPreparation);
        _roomPreparation = null;

        if (_overlayReconciler == null)
            return;

        ApplyReport report = new();
        if (!_overlayReconciler.TryClear(report) && report.Entries.Count > 0)
            PublishReport("Room cleanup", report, false);
    }

    private void DisposePreview()
    {
        System.Exception error = _catalogActions?.ClearPreview();
        if (error != null)
            ModLog.Error("Preview cleanup failed: " + error.Message);
    }

    private void PublishCatalogActionUnavailable(
        string operation,
        string code,
        string reason)
    {
        ApplyReport report = new();
        report.Warning(
            code,
            operation.ToLowerInvariant(),
            string.IsNullOrEmpty(reason)
                ? "The selected creator is not available for " + operation.ToLowerInvariant() + "."
                : reason);
        PublishReport(operation, report);
    }

    private void PublishCreatorSourceFailure(
        string type,
        string reason,
        bool registryFailure)
    {
        try
        {
            ApplyReport failure = new();
            failure.Error(
                registryFailure
                    ? ReportCodes.CreatorUnsupported
                    : ReportCodes.RuntimeCreateFailed,
                type,
                reason);
            PublishReport("Creator source", failure);
        }
        catch (System.Exception exception)
        {
            ModLog.Error("Creator source failure could not be reported: " + exception.Message);
        }
    }

    private void MarkCreatorRuntimeLookupFailed(string type, string reason)
    {
        if (string.IsNullOrEmpty(type))
            return;

        CreatorCatalog?.TrySetSourceState(
            type,
            CreatorCatalogSourceState.Failed,
            reason);
    }

    private void RefreshEditorAfterCreatorSourceLoad()
    {
        try
        {
            EditorWidget?.RefreshAfterReport(true);
        }
        catch (System.Exception exception)
        {
            ModLog.Error("Creator source UI refresh failed: " + exception.Message);
        }
    }

    private void EnsureCreatorSourceLoad()
    {
        if (_creatorSourceLoadsDisabled
            || _creatorSourceLoadRunning
            || CreatorCatalog == null
            || UIController.instance == null)
            return;

        _creatorSourceLoadRunning = true;
        int token = ++_creatorSourceLoadToken;
        try
        {
            UIController.instance.StartCoroutine(ProcessCreatorSourceLoads(token, CreatorCatalog));
        }
        catch (System.Exception exception)
        {
            _creatorSourceLoadRunning = false;
            string reason = "Creator source load could not start: " + exception.Message;
            ModLog.Error(reason);
            try
            {
                if (CreatorCatalog != null
                    && CreatorCatalog.TryFailNextSourceLoad(reason, out string failedType))
                {
                    PublishCreatorSourceFailure(failedType, reason, false);
                }

                PublishCreatorSourceProbeFailures(CreatorCatalog);
                RefreshEditorAfterCreatorSourceLoad();
            }
            catch (System.Exception reportingError)
            {
                ModLog.Error("Creator source scheduling failure could not be reported: " + reportingError.Message);
            }
        }
    }

    private void InvalidateCreatorSourceLoads()
    {
        _creatorSourceLoadToken++;
    }

    private IEnumerator ProcessCreatorSourceLoads(
        int token,
        CreatorCatalogController catalog)
    {
        string activeType = null;
        int activeGeneration = 0;
        bool completionRecorded = false;
        bool loadIterationInProgress = false;
        try
        {
            while (token == _creatorSourceLoadToken
                && ReferenceEquals(CreatorCatalog, catalog)
                && catalog.TryBeginNextSourceLoad(out string type, out int generation))
            {
                loadIterationInProgress = true;
                activeType = type;
                activeGeneration = generation;
                completionRecorded = false;
                ModLog.Info(
                    string.Equals(catalog.SelectedType, type, System.StringComparison.Ordinal)
                        ? "Creator source loading: " + type
                            + "; Preview disabled: " + catalog.PreviewUnavailableReason
                            + "; Place disabled: " + catalog.PlaceUnavailableReason
                        : "Creator source loading: " + type);
                string reason = null;
                bool registryFailure = false;
                bool succeeded = false;

                if (!RuntimeCreatorRegistry.TryGet(
                    type,
                    out Blasphemous.Framework.Levels.ObjectCreator creator,
                    out reason))
                {
                    registryFailure = true;
                }
                else if (creator.Loader == null)
                {
                    reason = "Creator has no runtime loader.";
                }
                else
                {
                    IEnumerator load = null;
                    try
                    {
                        load = creator.Loader.Apply();
                    }
                    catch (System.Exception exception)
                    {
                        reason = "Creator source load could not start: " + exception.Message;
                    }

                    if (load == null)
                    {
                        reason ??= "Creator source load returned no enumerator.";
                    }
                    else
                    {
                        RefreshEditorAfterCreatorSourceLoad();
                        yield return null;

                        bool failed = false;
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
                                failed = true;
                                reason = "Creator source load failed: " + exception.Message;
                                break;
                            }

                            if (!hasNext)
                                break;

                            yield return current;
                        }

                        if (failed)
                        {
                            reason ??= "Creator source load failed.";
                        }
                        else
                        {
                            if (CreatorSourceProbe.TryConfirmLoadedSource(
                                () => creator.Loader.Result,
                                out string sourceFailure,
                                IsUnavailableUnityObject))
                                succeeded = true;
                            else
                                reason = sourceFailure;
                        }
                    }
                }

                bool currentCompletion = catalog.CompleteSourceLoad(
                    type,
                    generation,
                    succeeded,
                    reason);
                completionRecorded = true;
                activeType = null;
                if (currentCompletion && !succeeded)
                {
                    PublishCreatorSourceFailure(type, reason, registryFailure);
                }
                else if (currentCompletion)
                {
                    ModLog.Info("Creator source ready: " + type);
                }

                yield return null;
                loadIterationInProgress = false;
            }
        }
        finally
        {
            _creatorSourceLoadRunning = false;
            bool interrupted = !completionRecorded && activeType != null;
            if (!completionRecorded && activeType != null)
            {
                try
                {
                    if (catalog.CompleteSourceLoad(
                        activeType,
                        activeGeneration,
                        false,
                        "Creator source load was interrupted."))
                    {
                        PublishCreatorSourceFailure(
                            activeType,
                            "Creator source load was interrupted.",
                            false);
                    }
                }
                catch (System.Exception exception)
                {
                    ModLog.Error("Creator source interruption could not be recorded: " + exception.Message);
                }
            }

            if (!_creatorSourceLoadsDisabled)
            {
                try
                {
                    if (ReferenceEquals(CreatorCatalog, catalog))
                        PublishCreatorSourceProbeFailures(catalog);
                    RefreshEditorAfterCreatorSourceLoad();
                    if (interrupted
                        || loadIterationInProgress
                        || token != _creatorSourceLoadToken
                        || !ReferenceEquals(CreatorCatalog, catalog))
                    {
                        EnsureCreatorSourceLoad();
                    }
                }
                catch (System.Exception exception)
                {
                    ModLog.Error("Creator source scheduler cleanup failed: " + exception.Message);
                }
            }
        }
    }

    private static CreatorSourceProbeResult TryInspectRuntimeSource(string type)
    {
        if (!RuntimeCreatorRegistry.TryGet(
                type,
                out Blasphemous.Framework.Levels.ObjectCreator creator,
                out string error))
        {
            return CreatorSourceProbeResult.Failed(error, true);
        }
        if (creator.Loader == null)
            return CreatorSourceProbeResult.Failed("Creator has no runtime loader.");

        return CreatorSourceProbe.Inspect(
            () => creator.Loader.Result,
            IsUnavailableUnityObject);
    }

    private static bool IsUnavailableUnityObject(object value)
    {
        return value is UnityEngine.Object unityObject && unityObject == null;
    }

    private void PublishCreatorSourceProbeFailures(CreatorCatalogController catalog)
    {
        if (catalog == null)
            return;

        foreach (CreatorSourceProbeFailure failure in catalog.DrainProbeFailures())
        {
            PublishCreatorSourceFailure(
                failure.Type,
                failure.Reason,
                failure.RegistryFailure);
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

    private bool TryPickPlacement(
        string id,
        string action,
        ApplyReport report = null)
    {
        RuntimePlacementManager manager = PlacementManager;
        report ??= new();
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

    private bool TryFindAtCursor(out IRuntimePlacementHandle handle, out string error)
    {
        handle = null;
        error = null;
        if (_runtimeFactory == null || PlacementManager == null)
        {
            error = "Placement editing is not ready.";
            return false;
        }

        return _runtimeFactory.TryFindAtScreenPoint(Input.mousePosition, out handle, out error);
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