using Blasphemous.EditorMode.Components;
using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Editing;
using Blasphemous.EditorMode.Framework;
using Blasphemous.EditorMode.Lifecycle;
using Blasphemous.EditorMode.Runtime;
using Blasphemous.ModdingAPI;
using Gameplay.UI;
using Blasphemous.Framework.UI;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Tools.Level.Layout;

namespace Blasphemous.EditorMode;

internal class EditorMode : BlasMod
{

    internal EditorWidget EditorWidget { get; private set; }
    internal static SpriteStorage SpriteStorage { get; private set; }
    internal ContentCatalog Catalog { get; private set; }
    internal RoomIdentity CurrentRoom { get { return _lifecycleBridge == null ? null : _lifecycleBridge.CurrentRoom; } }
    internal LevelEditSession Session { get { return _overlayReconciler == null ? null : _overlayReconciler.CurrentSession; } }
    internal RuntimePlacementManager PlacementManager { get { return _overlayReconciler == null ? null : _overlayReconciler.Manager; } }
    internal ApplyReportSurface ReportSurface { get { return _reportPublisher.Surface; } }

    private static bool _isStartingEditorWidget = false;
    private BlasphemousLifecycleBridge _lifecycleBridge;
    private EditorModeGuard _editorGuard;
    private ApplyReportPublisher _reportPublisher;
    private UnityRuntimePlacementFactory _runtimeFactory;
    private RoomOverlayReconciler _overlayReconciler;
    private Coroutine _roomPreparation;
    private Coroutine _creatorPreload;
    private bool _creatorPreloadStarted;
    private PreviewInstance _preview;

    internal EditorMode() : base(ModInfo.MOD_ID, ModInfo.MOD_NAME, ModInfo.MOD_AUTHOR, ModInfo.MOD_VERSION) { }

    protected override void OnInitialize()
    {
        _reportPublisher = new ApplyReportPublisher(
            new ApplyReportSurface(),
            message => ModLog.Info(message));
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
            });
        SpriteStorage = new(FileHandler);
    }

    protected override void OnLevelPreloaded(string oldLevel, string newLevel)
    {
        _lifecycleBridge.OnLevelPreloaded(oldLevel, newLevel);
    }

    protected override void OnLevelLoaded(string oldLevel, string newLevel)
    {
        bool roomLoaded = _lifecycleBridge.OnLevelLoaded(
            newLevel,
            SceneManager.GetActiveScene().name);

        if (string.Equals(newLevel, "MainMenu", System.StringComparison.OrdinalIgnoreCase))
            StartCreatorPreload();

        if (roomLoaded)
            SafeCreateEditorAfterDelay(1.5f);
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
                CursorWorldPosition));
        _runtimeFactory = new UnityRuntimePlacementFactory(FindCurrentObjectHolder);
        _overlayReconciler = new RoomOverlayReconciler(Catalog, _runtimeFactory);
        StartCreatorPreload();
        if (_lifecycleBridge.CurrentRoom != null)
            StartRoomPreparation(_lifecycleBridge.CurrentRoom);
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
            ToggleEditorMode();
    }

    private void ToggleEditorMode()
    {
        if (_editorGuard.IsActive)
        {
            ExitEditorMode();
            return;
        }

        if (!IsDebug)
        {
            ModLog.Warn("Editor Mode activation rejected: debug gate failed.");
            return;
        }

        if (!_lifecycleBridge.TryBeginEditing())
        {
            ModLog.Warn("Editor Mode activation rejected: no compatible loaded room.");
            return;
        }

        if (Session == null || PlacementManager == null)
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

        string error;
        if (!_editorGuard.TryEnter(true, true, CreateEditorState(), out error))
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
        string error;
        if (!_editorGuard.TryExit(out error))
        {
            ModLog.Error(error);
            return;
        }

        _lifecycleBridge.EndEditing();
        if (EditorWidget != null)
            EditorWidget.gameObject.SetActive(false);
    }

    internal LevelEditSessionExportResult ExportCurrentRoom()
    {
        if (Session == null || _lifecycleBridge.CurrentRoom == null)
        {
            var report = new ApplyReport();
            report.Error("ROOM_REQUIRED", "export", "No editable Room Overlay is loaded.");
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
            var report = new ApplyReport();
            report.Error("ROOM_REQUIRED", "import", "No editable Room Overlay is loaded.");
            PublishReport("Level import", report);
            return false;
        }

        return PrepareRoom(room);
    }

    internal bool TryPlaceAtCursor(string type)
    {
        RuntimePlacementManager manager = PlacementManager;
        if (manager == null)
            return false;

        var report = new ApplyReport();
        Vector3 position = CursorWorldPosition();
        PlacementRecord placement;
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
            new string[0],
            report,
            out placement);
        PublishReport("Place", report);
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

        var report = new ApplyReport();
        bool result = manager.TryDelete(manager.Selected.Id, report);
        PublishReport("Delete", report);
        return result;
    }

    internal bool TryToggleSelectedLock()
    {
        RuntimePlacementManager manager = PlacementManager;
        if (manager == null || manager.Selected == null)
            return false;

        var report = new ApplyReport();
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

        var report = new ApplyReport();
        PlacementRecord ignored;
        bool result = manager.TryCopy(manager.Selected.Id, report, out ignored);
        PublishReport("Copy", report);
        return result;
    }

    internal bool TryPasteAtCursor()
    {
        RuntimePlacementManager manager = PlacementManager;
        if (manager == null)
            return false;

        var report = new ApplyReport();
        PlacementRecord clipboard = manager.Clipboard;
        if (clipboard == null)
        {
            report.Error("CLIPBOARD_EMPTY", "paste", "The editor cursor has no Placement.");
            PublishReport("Paste", report);
            return false;
        }

        Vector3 position = CursorWorldPosition();
        PlacementRecord ignored;
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
            out ignored);
        PublishReport("Paste", report);
        return result;
    }

    internal bool TryPreviewAtCursor(string type)
    {
        if (Catalog == null)
            return false;

        DisposePreview();
        string error;
        if (!Catalog.TryCreatePreview(type, out _preview, out error))
        {
            var report = new ApplyReport();
            report.Error("PREVIEW_FAILED", type ?? "<null>", error);
            PublishReport("Preview", report);
            return false;
        }

        var success = new ApplyReport();
        success.Info("PREVIEW_CREATED", type, "Isolated preview created; it is not part of the Room Overlay.");
        PublishReport("Preview", success);
        return true;
    }

    internal bool TryApplySelectedTransform(string[] values)
    {
        RuntimePlacementManager manager = PlacementManager;
        if (manager == null || manager.Selected == null)
            return false;

        var report = new ApplyReport();
        if (values == null || values.Length != 9)
        {
            report.Error("TRANSFORM_INVALID", manager.Selected.Id, "Exactly nine transform values are required.");
            PublishReport("Transform", report);
            return false;
        }

        var parsed = new float[values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            if (!float.TryParse(values[index], NumberStyles.Float, CultureInfo.InvariantCulture, out parsed[index])
                || float.IsNaN(parsed[index])
                || float.IsInfinity(parsed[index]))
            {
                report.Error("TRANSFORM_INVALID", manager.Selected.Id, "Transform values must be finite numbers.");
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
        RuntimePlacementManager manager = PlacementManager;
        if (manager == null || manager.Selected == null)
            return false;

        var report = new ApplyReport();
        bool result = manager.TrySetProperty(manager.Selected.Id, key, value, report);
        PublishReport("Property", report);
        return result;
    }

    internal bool TrySelectAtCursor()
    {
        if (_runtimeFactory == null || PlacementManager == null)
            return false;

        IRuntimePlacementHandle handle;
        string error;
        if (!_runtimeFactory.TryFindAtScreenPoint(Input.mousePosition, out handle, out error))
        {
            if (error != null)
            {
                var failure = new ApplyReport();
                failure.Error("SELECTION_FAILED", "cursor", error);
                PublishReport("Select", failure);
            }
            return false;
        }

        var report = new ApplyReport();
        PlacementRecord ignored;
        bool result = PlacementManager.TrySelectRuntimeObject(handle, report, out ignored);
        PublishReport("Select", report);
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

    private void PublishReport(string operation, ApplyReport report)
    {
        _reportPublisher.Publish(operation, report);
        if (EditorWidget != null)
            EditorWidget.Refresh();
    }

    private bool RunManagerCommand(
        string operation,
        System.Func<RuntimePlacementManager, ApplyReport, bool> command)
    {
        RuntimePlacementManager manager = PlacementManager;
        if (manager == null)
            return false;

        var report = new ApplyReport();
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
        FrameworkFileResult file = File.Exists(path)
            ? LevelFrameworkAdapter.ImportFile(path, room.ExportFileKey)
            : LevelFrameworkAdapter.EmptyFile(path);
        RoomOverlayReconcileResult result = _overlayReconciler.Apply(room, file);
        PublishReport("Room import", result.Report);
        if (!result.Succeeded)
        {
            ModLog.Warn("Room Overlay session was not created: " + result.StatusMessage);
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

        var report = new ApplyReport();
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
        if (_creatorPreloadStarted || Catalog == null || UIController.instance == null)
            return;

        _creatorPreloadStarted = true;
        _creatorPreload = UIController.instance.StartCoroutine(PreloadCreatorSources());
    }

    private IEnumerator PreloadCreatorSources()
    {
        yield return null;
        foreach (CreatorMetadata metadata in Catalog.Entries)
        {
            Blasphemous.Framework.Levels.ObjectCreator creator;
            string error;
            if (!RuntimeCreatorRegistry.TryGet(metadata.Type, out creator, out error))
            {
                ModLog.Warn(error);
                continue;
            }
            if (creator.Loader == null || creator.Loader.Result != null)
                continue;

            IEnumerator load = creator.Loader.Apply();
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
        _creatorPreload = null;
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
        return initializer == null ? null : initializer.transform;
    }

    private static Vector3 CursorWorldPosition()
    {
        if (Camera.main == null)
            return Vector3.zero;

        Vector3 screen = Input.mousePosition;
        float depth = Mathf.Abs(Camera.main.transform.position.z);
        Vector3 world = Camera.main.ScreenToWorldPoint(new Vector3(screen.x, screen.y, depth));
        world.z = 0f;
        return world;
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
            false);
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
