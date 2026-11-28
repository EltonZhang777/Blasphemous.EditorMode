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
using Gameplay.UI;
using System.Collections;
using System.Collections.Generic;
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
    internal RoomIdentity CurrentRoom { get { return _lifecycleBridge?.CurrentRoom; } }
    internal LevelEditSession Session { get { return _overlayReconciler?.CurrentSession; } }
    internal RuntimePlacementManager PlacementManager { get { return _overlayReconciler?.Manager; } }
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
    private readonly RoomRuntimeContextAdapter _roomRuntimeContext = new();
    private UnityRuntimePlacementFactory _runtimeFactory;
    private RoomOverlayReconciler _overlayReconciler;
    private Coroutine _roomPreparation;
    private Coroutine _creatorPreload;
    private bool _creatorPreloadStarted;
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
                {
                    ModLog.Error(message);
                }
                else if (severity == ApplySeverity.Warning)
                {
                    ModLog.Warn(message);
                }
                else
                {
                    ModLog.Info(message);
                }
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
        RoomRuntimeContextRecord record = _roomRuntimeContext.ObserveAndRebindAtPreload(newLevel);
        LogRoomRuntimeContext(record);
        if (record.Kind == RoomRuntimeContextRecordKind.Failed
            && record.Reason == RoomRuntimeContextReasons.StoreComponentMissing)
        {
            ApplyReport report = new();
            report.Error(
                ReportCodes.RoomEffectsStoreMissing,
                record.ActiveScene,
                "The active room has no LevelEffectsStore component; its context was not refreshed.");
            PublishReport("Room context refresh", report, false);
        }
    }

    private void LogRoomRuntimeContext(RoomRuntimeContextRecord record)
    {
        string line = record.ToLogLine();
        if (record.State == RoomRuntimeContextState.Failed)
        {
            ModLog.Error(line);
        }
        else if (record.State == RoomRuntimeContextState.ObservedNotReady)
        {
            ModLog.Warn(line);
        }
        else
        {
            ModLog.Info(line);
        }
    }

    protected override void OnLevelLoaded(string oldLevel, string newLevel)
    {
        LogRoomRuntimeContext(_roomRuntimeContext.ObserveAfterLoad(newLevel));
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
        ExitEditorMode();
        DisposePreview();
        ClearRoomOverlay();
    }

    protected override void OnUpdate()
    {
        if (InputHandler.GetKeyDown("OpenEditor"))
        {
            ToggleEditorMode();
        }
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
            {
                ModLog.Error(error);
            }
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
            PublishReport("Level import", report, false);
            return false;
        }

        return PrepareRoom(room);
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

    internal bool TryPlacePreviewAtCursor()
    {
        if (_preview != null)
        {
            return TryPlaceAtCursor(_preview.Metadata.Type);
        }

        ApplyReport report = new();
        report.Warning(ReportCodes.PlaceNoPreview, "preview", "Select a catalog entry and create a preview before placing it.");
        PublishReport("Place", report);
        return false;
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
        {
            return false;
        }

        ApplyReport report = new();
        bool result = manager.TryDelete(manager.Selected.Id, report);
        PublishReport("Delete", report, false);
        return result;
    }

    internal bool TryToggleSelectedLock()
    {
        RuntimePlacementManager manager = PlacementManager;
        if (manager == null || manager.Selected == null)
        {
            return false;
        }

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
        {
            return false;
        }

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
            report.Warning(ReportCodes.PickNoSelection, "clipboard", "Select or place an Editable Overlay Placement before clicking Pick.");
            PublishReport("Pick", report);
            return false;
        }

        return TryPickPlacement(manager.Selected.Id, "Pick");
    }

    internal bool TryPickAtCursor()
    {
        if (!TryFindAtCursor(out IRuntimePlacementHandle handle, out string error))
        {
            ApplyReport report = new();
            if (error == null)
            {
                report.Warning(ReportCodes.PickNoTarget, "cursor", "Point to an Editable Overlay Placement before pressing P.");
            }
            else
            {
                report.Error(ReportCodes.PickFailed, "cursor", error);
            }
            PublishReport("Pick at cursor", report);
            return false;
        }

        return TryPickPlacement(handle.PlacementId, "Pick at cursor");
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
        {
            return false;
        }

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

    internal bool TryPreviewAtCursor(string type)
    {
        if (Catalog == null)
        {
            return false;
        }

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
        if (IsActive && _preview != null)
        {
            _preview.FollowCursor();
        }
    }

    internal void ClearPreview()
    {
        if (_preview == null)
        {
            return;
        }

        DisposePreview();
        ApplyReport report = new();
        report.Info(ReportCodes.PreviewDisposed, "preview", "Isolated preview discarded.");
        PublishReport("Preview", report);
    }

    internal bool TryApplySelectedTransform(TransformInspectorValues values)
    {
        RuntimePlacementManager manager = PlacementManager;
        if (manager == null || manager.Selected == null)
        {
            return false;
        }

        ApplyReport report = new();
        PlacementTransform transform = null;
        string error = null;
        bool valid = values != null
            && values.TryCreateTransform(out transform, out error);
        if (!valid)
        {
            report.Error(
                ReportCodes.TransformInvalid,
                manager.Selected.Id,
                values == null ? "Transform values are required." : error);
            PublishReport("Transform", report);
            return false;
        }

        bool result = manager.TryMove(manager.Selected.Id, transform, report);
        PublishReport("Transform", report);
        return result;
    }

    internal bool TryApplySelectedProperties(string text)
    {
        RuntimePlacementManager manager = PlacementManager;
        if (manager == null || manager.Selected == null)
        {
            return false;
        }

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

    private bool RunManagerCommand(
        string operation,
        System.Func<RuntimePlacementManager, ApplyReport, bool> command)
    {
        RuntimePlacementManager manager = PlacementManager;
        if (manager == null)
        {
            return false;
        }

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
        ModLog.Info("Room Overlay cleared: " + room.FrameworkLevelKey);
    }

    private void StartRoomPreparation(RoomIdentity room)
    {
        if (room == null || _overlayReconciler == null)
        {
            return;
        }

        if (UIController.instance == null)
        {
            PrepareRoom(room);
            return;
        }

        if (_roomPreparation != null)
        {
            UIController.instance.StopCoroutine(_roomPreparation);
        }
        _roomPreparation = UIController.instance.StartCoroutine(PrepareRoomAfterFrame(room));
    }

    private IEnumerator PrepareRoomAfterFrame(RoomIdentity room)
    {
        yield return null;
        while (_creatorPreload != null)
            yield return null;
        _roomPreparation = null;
        if (_lifecycleBridge.CurrentRoom == null || !_lifecycleBridge.CurrentRoom.Equals(room))
        {
            yield break;
        }
        PrepareRoom(room);
    }

    private bool PrepareRoom(RoomIdentity room)
    {
        if (room == null || _overlayReconciler == null)
        {
            return false;
        }

        string path = RoomExportPath(room);
        FrameworkFileResult file = LevelFrameworkAdapter.ImportFile(path, room.ExportFileKey);
        RoomOverlayReconcileResult result = _overlayReconciler.Apply(room, file);
        PublishReport("Room import", result.Report, false);
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
        {
            UIController.instance.StopCoroutine(_roomPreparation);
        }
        _roomPreparation = null;

        if (_overlayReconciler == null)
        {
            return;
        }

        ApplyReport report = new();
        if (!_overlayReconciler.TryClear(report) && report.Entries.Count > 0)
        {
            PublishReport("Room cleanup", report, false);
        }
    }

    private void DisposePreview()
    {
        if (_preview == null)
        {
            return;
        }

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
        {
            return;
        }

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
                {
                    continue;
                }

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
                        {
                            current = load.Current;
                        }
                    }
                    catch (System.Exception exception)
                    {
                        ModLog.Error("Creator source preload failed for " + metadata.Type + ": " + exception.Message);
                        break;
                    }

                    if (!hasNext)
                    {
                        break;
                    }
                    yield return current;
                }

                if (creator.Loader.Result == null)
                {
                    ModLog.Warn("Creator source preload returned no object: " + metadata.Type);
                }
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
        {
            return Vector3.zero;
        }

        Vector2 cameraPixels = UnityScreenCoordinateMapper.ToCameraPixels(Camera.main, Input.mousePosition);
        return UnityScreenCoordinateMapper.ToWorldPosition(Camera.main, cameraPixels);
    }

    private void LogCursorPosition(string action, Vector3 worldPosition)
    {
        if (!_verboseDiagnostics
            && string.Equals(action, "CURSOR_PREVIEW", System.StringComparison.Ordinal))
        {
            return;
        }

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