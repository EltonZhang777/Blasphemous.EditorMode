using Blasphemous.EditorMode.Components;
using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Framework;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blasphemous.EditorMode.Editing;

/// <summary>Evidence class for an object observed in a target level scene.</summary>
internal enum OriginalObjectProvenance
{
    Baseline,
    LevelFramework,
    EditorMode,
    LevelFrameworkExtended,
    ThirdParty,
    PostBaseline,
    Clone,
    Unknown
}

/// <summary>Framework scene/path identity of one Original Object.</summary>
internal sealed class OriginalObjectIdentity : IEquatable<OriginalObjectIdentity>
{
    internal OriginalObjectIdentity(string scene, string path)
    {
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
        Path = path ?? throw new ArgumentNullException(nameof(path));
    }

    internal string Scene { get; }
    internal string Path { get; }
    internal string DisplayText { get { return Scene + ":" + Path; } }

    internal static bool TryCreate(
        string scene,
        string path,
        out OriginalObjectIdentity identity,
        out string error)
    {
        identity = null;
        error = null;
        if (!TryNormalizeScene(scene, out string normalizedScene))
        {
            error = "Scene must be decoration, layout, or logic.";
            return false;
        }
        if (string.IsNullOrEmpty(path) || path.Trim().Length == 0)
        {
            error = "Path is required.";
            return false;
        }

        identity = new OriginalObjectIdentity(normalizedScene, path);
        return true;
    }

    internal static bool TryNormalizeScene(string scene, out string normalized)
    {
        normalized = null;
        switch (scene)
        {
            case "decoration":
            case "layout":
            case "logic":
                normalized = scene;
                return true;
            default:
                return false;
        }
    }

    public bool Equals(OriginalObjectIdentity other)
    {
        return other != null
            && string.Equals(Scene, other.Scene, StringComparison.Ordinal)
            && string.Equals(Path, other.Path, StringComparison.Ordinal);
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as OriginalObjectIdentity);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (StringComparer.Ordinal.GetHashCode(Scene) * 397)
                ^ StringComparer.Ordinal.GetHashCode(Path);
        }
    }

    public override string ToString()
    {
        return DisplayText;
    }
}

/// <summary>One object captured before supported mod preload callbacks ran.</summary>
internal sealed class OriginalObjectSnapshotEntry
{
    internal OriginalObjectSnapshotEntry(
        string objectKey,
        string scene,
        string path,
        bool active,
        OriginalObjectProvenance provenance,
        bool addressable,
        string addressError = null,
        bool? activeSelf = null)
    {
        ObjectKey = objectKey ?? throw new ArgumentNullException(nameof(objectKey));
        Scene = scene;
        Path = path;
        IsActive = active;
        IsActiveSelf = activeSelf ?? active;
        Provenance = provenance;
        IsAddressable = addressable;
        AddressError = addressError;
    }

    internal string ObjectKey { get; }
    internal string Scene { get; }
    internal string Path { get; }
    internal bool IsActive { get; }
    internal bool IsActiveSelf { get; }
    internal OriginalObjectProvenance Provenance { get; }
    internal bool IsAddressable { get; }
    internal string AddressError { get; }

    internal string Location
    {
        get
        {
            string identity = string.IsNullOrEmpty(Scene) || string.IsNullOrEmpty(Path)
                ? ObjectKey
                : Scene + ":" + Path;
            return identity + "#" + ObjectKey;
        }
    }
}

/// <summary>Immutable pre-mod snapshot used as the positive provenance boundary.</summary>
internal sealed class OriginalObjectBaseline
{
    internal OriginalObjectBaseline(
        string frameworkLevelKey,
        IEnumerable<OriginalObjectSnapshotEntry> entries,
        ApplyReport captureReport)
    {
        FrameworkLevelKey = frameworkLevelKey ?? string.Empty;
        Entries = new ReadOnlyCollection<OriginalObjectSnapshotEntry>(
            [.. entries ?? []]);
        CaptureReport = captureReport ?? new ApplyReport();
    }

    internal string FrameworkLevelKey { get; }
    internal ReadOnlyCollection<OriginalObjectSnapshotEntry> Entries { get; }
    internal ApplyReport CaptureReport { get; }

    internal bool ContainsObjectKey(string objectKey)
    {
        if (string.IsNullOrEmpty(objectKey))
            return false;

        foreach (OriginalObjectSnapshotEntry entry in Entries)
        {
            if (string.Equals(entry.ObjectKey, objectKey, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}

/// <summary>Current runtime projection of one scene/path.</summary>
internal sealed class OriginalObjectProjection
{
    internal OriginalObjectProjection(
        string objectKey,
        bool active,
        PlacementTransform transform = null,
        string name = null,
        bool? activeSelf = null)
    {
        ObjectKey = objectKey ?? throw new ArgumentNullException(nameof(objectKey));
        IsActive = active;
        IsActiveSelf = activeSelf ?? active;
        Transform = transform ?? PlacementTransform.Default;
        Name = name ?? string.Empty;
    }

    internal string ObjectKey { get; }
    internal bool IsActive { get; }
    internal bool IsActiveSelf { get; }
    internal PlacementTransform Transform { get; }
    internal string Name { get; }
}

/// <summary>Resolves Framework scene/path identities in the current room.</summary>
internal interface IOriginalObjectPathResolver
{
    bool TryResolve(
        string frameworkLevelKey,
        string scene,
        string path,
        out OriginalObjectProjection projection,
        out string error);
}

/// <summary>Locatable baseline object with current activity state.</summary>
internal sealed class OriginalObjectCatalogEntry
{
    internal OriginalObjectCatalogEntry(
        OriginalObjectIdentity identity,
        string objectKey,
        bool active,
        bool runtimeActive,
        PlacementTransform runtimeTransform = null,
        string runtimeName = null,
        bool? runtimeActiveSelf = null,
        bool? baselineActiveSelf = null)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        ObjectKey = objectKey ?? throw new ArgumentNullException(nameof(objectKey));
        IsActive = active;
        IsBaselineActiveSelf = baselineActiveSelf ?? active;
        IsRuntimeActive = runtimeActive;
        IsRuntimeActiveSelf = runtimeActiveSelf ?? runtimeActive;
        RuntimeTransform = runtimeTransform ?? PlacementTransform.Default;
        RuntimeName = runtimeName ?? string.Empty;
    }

    internal OriginalObjectIdentity Identity { get; }
    internal string ObjectKey { get; }
    /// <summary>Whether the object was active in the captured pre-mod baseline.</summary>
    internal bool IsActive { get; }
    internal bool IsBaselineActiveSelf { get; }
    internal bool IsRuntimeActive { get; }
    internal bool IsRuntimeActiveSelf { get; }
    internal PlacementTransform RuntimeTransform { get; }
    internal string RuntimeName { get; }
    internal string DisplayText { get { return Identity.DisplayText; } }

    internal OriginalObjectLiveState LiveState
    {
        get
        {
            return new OriginalObjectLiveState(
                ObjectKey,
                IsRuntimeActiveSelf,
                RuntimeTransform,
                RuntimeName);
        }
    }
}

/// <summary>Verified Original Objects captured from one room's vanilla baseline.</summary>
internal sealed class OriginalObjectCatalog
{
    private readonly Dictionary<OriginalObjectIdentity, OriginalObjectCatalogEntry> _byIdentity;
    private readonly Dictionary<string, OriginalObjectCatalogEntry> _byObjectKey;
    private readonly HashSet<string> _targetedObjectKeys;
    private readonly ReadOnlyCollection<OriginalObjectCatalogEntry> _entries;

    private OriginalObjectCatalog(IEnumerable<OriginalObjectCatalogEntry> entries)
    {
        List<OriginalObjectCatalogEntry> ordered = [.. entries ?? []];
        ordered.Sort((left, right) => CompareIdentity(left.Identity, right.Identity));
        _entries = new ReadOnlyCollection<OriginalObjectCatalogEntry>(ordered);
        _byIdentity = new Dictionary<OriginalObjectIdentity, OriginalObjectCatalogEntry>();
        _byObjectKey = new Dictionary<string, OriginalObjectCatalogEntry>(StringComparer.Ordinal);
        foreach (OriginalObjectCatalogEntry entry in ordered)
        {
            _byIdentity.Add(entry.Identity, entry);
            _byObjectKey[entry.ObjectKey] = entry;
        }

        _targetedObjectKeys = new HashSet<string>(StringComparer.Ordinal);
    }

    internal static OriginalObjectCatalog Empty { get { return new OriginalObjectCatalog([]); } }
    internal ReadOnlyCollection<OriginalObjectCatalogEntry> Entries { get { return _entries; } }
    internal int Count { get { return _entries.Count; } }
    internal int VisibleCount { get { return VisibleEntries.Count; } }

    internal ReadOnlyCollection<OriginalObjectCatalogEntry> VisibleEntries
    {
        get
        {
            List<OriginalObjectCatalogEntry> visible = [];
            foreach (OriginalObjectCatalogEntry entry in _entries)
            {
                if (entry.IsActive || _targetedObjectKeys.Contains(entry.ObjectKey))
                    visible.Add(entry);
            }
            return new ReadOnlyCollection<OriginalObjectCatalogEntry>(visible);
        }
    }

    internal bool TryGetByIdentity(
        OriginalObjectIdentity identity,
        out OriginalObjectCatalogEntry entry)
    {
        entry = null;
        return identity != null && _byIdentity.TryGetValue(identity, out entry);
    }

    internal bool TryGetByObjectKey(
        string objectKey,
        out OriginalObjectCatalogEntry entry)
    {
        entry = null;
        return !string.IsNullOrEmpty(objectKey)
            && _byObjectKey.TryGetValue(objectKey, out entry);
    }

    internal bool TryGetByDisplayText(
        string displayText,
        out OriginalObjectCatalogEntry entry)
    {
        entry = null;
        if (string.IsNullOrEmpty(displayText))
            return false;

        foreach (OriginalObjectCatalogEntry candidate in _entries)
        {
            if (string.Equals(candidate.DisplayText, displayText, StringComparison.Ordinal))
            {
                entry = candidate;
                return true;
            }
        }

        return false;
    }

    internal bool IsVisible(OriginalObjectCatalogEntry entry)
    {
        return entry != null
            && (entry.IsActive || _targetedObjectKeys.Contains(entry.ObjectKey));
    }

    internal void ReplaceTargetedObjectKeys(IEnumerable<string> targetedObjectKeys)
    {
        _targetedObjectKeys.Clear();
        foreach (string targeted in targetedObjectKeys ?? [])
        {
            if (!string.IsNullOrEmpty(targeted))
                _targetedObjectKeys.Add(targeted);
        }
    }

    internal static OriginalObjectCatalog Build(
        OriginalObjectBaseline baseline,
        IEnumerable<string> targetedObjectKeys,
        IOriginalObjectPathResolver resolver,
        ApplyReport report)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));

        OriginalObjectCatalog catalog = new OriginalObjectCatalog([]);
        foreach (string targeted in targetedObjectKeys ?? [])
        {
            if (!string.IsNullOrEmpty(targeted))
                catalog._targetedObjectKeys.Add(targeted);
        }

        if (baseline == null)
        {
            report.Warning(
                ReportCodes.OriginalObjectBaselineMissing,
                "$",
                "No pre-mod Original Object baseline was captured for this room.");
            return catalog;
        }

        report.Merge(baseline.CaptureReport);
        if (baseline.CaptureReport.HasErrors)
            return catalog;
        if (baseline.FrameworkLevelKey.Length == 0)
        {
            report.Warning(
                ReportCodes.OriginalObjectBaselineMissing,
                "$",
                "The captured Original Object baseline has no Framework level key.");
            return catalog;
        }

        List<OriginalObjectCatalogEntry> entries = [];
        HashSet<OriginalObjectIdentity> identities = [];
        HashSet<string> objectKeys = new(StringComparer.Ordinal);
        foreach (OriginalObjectSnapshotEntry snapshot in baseline.Entries)
        {
            if (snapshot.Provenance != OriginalObjectProvenance.Baseline)
            {
                report.Warning(
                    ReportCodes.OriginalObjectProvenanceRejected,
                    snapshot.Location,
                    "Excluded " + snapshot.Provenance + " object from the Original Object catalog.");
                continue;
            }
            string identityError = null;
            if (!snapshot.IsAddressable
                || !OriginalObjectIdentity.TryCreate(
                    snapshot.Scene,
                    snapshot.Path,
                    out OriginalObjectIdentity identity,
                    out identityError))
            {
                report.Warning(
                    ReportCodes.OriginalObjectUnaddressable,
                    snapshot.Location,
                    !string.IsNullOrEmpty(snapshot.AddressError)
                        ? snapshot.AddressError
                        : snapshot.IsAddressable
                            ? identityError
                            : "The object cannot be represented by a Framework scene/path.");
                continue;
            }

            string resolveError = null;
            if (resolver == null
                || !resolver.TryResolve(
                    baseline.FrameworkLevelKey,
                    identity.Scene,
                    identity.Path,
                    out OriginalObjectProjection projection,
                    out resolveError))
            {
                report.Warning(
                    ReportCodes.OriginalObjectUnaddressable,
                    snapshot.Location,
                    resolveError ?? "No current object resolves from this scene/path.");
                continue;
            }
            if (!string.Equals(projection.ObjectKey, snapshot.ObjectKey, StringComparison.Ordinal))
            {
                report.Warning(
                    ReportCodes.OriginalObjectUnaddressable,
                    snapshot.Location,
                    "The scene/path resolves to a different object.");
                continue;
            }
            if (!identities.Add(identity))
            {
                report.Warning(
                    ReportCodes.OriginalObjectUnaddressable,
                    snapshot.Location,
                    "Another baseline object already claims this scene/path.");
                continue;
            }
            if (!objectKeys.Add(snapshot.ObjectKey))
            {
                report.Warning(
                    ReportCodes.OriginalObjectUnaddressable,
                    snapshot.Location,
                    "Another baseline identity already claims this GameObject.");
                continue;
            }

            entries.Add(new OriginalObjectCatalogEntry(
                identity,
                snapshot.ObjectKey,
                snapshot.IsActive,
                projection.IsActive,
                projection.Transform,
                projection.Name,
                projection.IsActiveSelf,
                snapshot.IsActiveSelf));
        }

        OriginalObjectCatalog built = new OriginalObjectCatalog(entries);
        foreach (string targeted in catalog._targetedObjectKeys)
            built._targetedObjectKeys.Add(targeted);
        report.Info(
            ReportCodes.OriginalObjectCatalogReady,
            baseline.FrameworkLevelKey,
            string.Format(
                "Original Object catalog ready: {0} locatable baseline objects, {1} currently visible.",
                built.Count,
                built.VisibleCount));
        return built;
    }

    private static int CompareIdentity(OriginalObjectIdentity left, OriginalObjectIdentity right)
    {
        int scene = SceneOrder(left.Scene).CompareTo(SceneOrder(right.Scene));
        return scene != 0
            ? scene
            : StringComparer.Ordinal.Compare(left.Path, right.Path);
    }

    private static int SceneOrder(string scene)
    {
        return scene == "decoration" ? 0 : scene == "layout" ? 1 : 2;
    }
}

/// <summary>One runtime object observed by the Original Object hit adapter.</summary>
internal sealed class OriginalObjectHit
{
    internal OriginalObjectHit(
        string objectKey,
        string scene,
        string path,
        string diagnostic = null)
    {
        ObjectKey = objectKey ?? string.Empty;
        Scene = scene;
        Path = path;
        Diagnostic = diagnostic;
    }

    internal string ObjectKey { get; }
    internal string Scene { get; }
    internal string Path { get; }
    internal string Diagnostic { get; }
}

/// <summary>Collects every level object under the pointer without choosing one.</summary>
internal interface IOriginalObjectHitSource
{
    bool TryFindAll(
        string frameworkLevelKey,
        out IList<OriginalObjectHit> hits,
        out string error);
}

/// <summary>Original Object operation represented by the current Level Export.</summary>
internal enum OriginalObjectOperationKind
{
    Modification,
    Deletion
}

/// <summary>Read-only view of one imported or pending Original Object Edit.</summary>
internal sealed class OriginalObjectOperation
{
    internal OriginalObjectOperation(
        OriginalObjectOperationKind kind,
        string source,
        string scene,
        string path,
        string type,
        string id,
        bool locatable,
        bool isImported,
        bool isPending,
        bool hasConflict = false)
    {
        Kind = kind;
        Source = source ?? string.Empty;
        Scene = scene;
        Path = path;
        Type = type;
        Id = id;
        IsLocatable = locatable;
        IsImported = isImported;
        IsPending = isPending;
        HasConflict = hasConflict;
    }

    internal OriginalObjectOperationKind Kind { get; }
    internal string Source { get; }
    internal string Scene { get; }
    internal string Path { get; }
    internal string Type { get; }
    internal string Id { get; }
    internal bool IsLocatable { get; }
    internal bool IsImported { get; }
    internal bool IsPending { get; }
    internal bool HasConflict { get; }

    internal string DisplayIdentity
    {
        get
        {
            return string.IsNullOrEmpty(Scene) || string.IsNullOrEmpty(Path)
                ? "<invalid identity>"
                : Scene + ":" + Path;
        }
    }
}

/// <summary>One explicitly selectable Original Object from an overlap hit set.</summary>
internal sealed class OriginalObjectCandidate
{
    internal OriginalObjectCandidate(OriginalObjectCatalogEntry entry)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
    }

    internal OriginalObjectCatalogEntry Entry { get; }
    internal OriginalObjectIdentity Identity { get { return Entry.Identity; } }
    internal string ObjectKey { get { return Entry.ObjectKey; } }
    internal string DisplayText { get { return Entry.DisplayText; } }
    internal EditorPage Page { get { return EditorPage.OriginalObjects; } }
}

/// <summary>Original Objects state and commands for the active room.</summary>
internal sealed class OriginalObjectPage
{
    private readonly LevelEditSession _session;
    private readonly OriginalObjectCatalog _catalog;
    private readonly string _frameworkLevelKey;
    private readonly IOriginalObjectLiveProjector _projector;
    private readonly EditCommandHistory _history = new();
    private readonly Dictionary<OriginalObjectIdentity, OriginalObjectLiveState> _liveStates = [];
    private readonly Dictionary<OriginalObjectIdentity, OriginalObjectLiveState> _importLiveStates = [];
    private readonly List<OriginalObjectOperationProvenance> _importedOperations = [];
    private readonly LevelEditSessionState _importedState;
    private ReadOnlyCollection<OriginalObjectOperation> _operations;
    private ReadOnlyCollection<OriginalObjectCandidate> _candidates;
    private OriginalObjectCatalogEntry _selected;
    private bool _isActive;

    private OriginalObjectPage(
        OriginalObjectCatalog catalog,
        string frameworkLevelKey,
        IOriginalObjectPathResolver resolver,
        IEnumerable<OriginalObjectOperation> operations)
    {
        _session = null;
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _frameworkLevelKey = frameworkLevelKey ?? string.Empty;
        _projector = null;
        _operations = new ReadOnlyCollection<OriginalObjectOperation>(
            [.. operations ?? []]);
        _candidates = new ReadOnlyCollection<OriginalObjectCandidate>([]);
        _importedState = LevelEditSessionState.Clean;
    }

    internal OriginalObjectPage(
        LevelEditSession session,
        OriginalObjectBaseline baseline,
        IOriginalObjectPathResolver resolver,
        ApplyReport report)
        : this(session, baseline, resolver, null, report)
    {
    }

    internal OriginalObjectPage(
        LevelEditSession session,
        OriginalObjectBaseline baseline,
        IOriginalObjectPathResolver resolver,
        IOriginalObjectLiveProjector projector,
        ApplyReport report)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));
        if (report == null)
            throw new ArgumentNullException(nameof(report));

        _session = session;
        _projector = projector;
        _importedState = session.ImportedState;
        _frameworkLevelKey = session.Room.FrameworkLevelKey;
        if (baseline != null
            && !string.Equals(
                baseline.FrameworkLevelKey,
                session.Room.FrameworkLevelKey,
                StringComparison.OrdinalIgnoreCase))
        {
            report.Error(
                ReportCodes.OriginalObjectBaselineMissing,
                baseline.FrameworkLevelKey,
                "The captured Original Object baseline belongs to a different room.");
            baseline = null;
        }

        HashSet<string> targetedObjectKeys = CollectTargetedObjectKeys(
            baseline,
            session.Modifications,
            session.Deletions);
        _catalog = OriginalObjectCatalog.Build(
            baseline,
            targetedObjectKeys,
            resolver,
            report);
        BuildImportedOperationProvenance(session.ImportedDocument);
        foreach (OriginalObjectCatalogEntry entry in _catalog.Entries)
        {
            _liveStates[entry.Identity] = entry.LiveState;
            _importLiveStates[entry.Identity] = entry.LiveState;
        }
        _operations = BuildOperations(report);
        _candidates = new ReadOnlyCollection<OriginalObjectCandidate>([]);
    }

    internal static OriginalObjectPage Empty
    {
        get
        {
            return new OriginalObjectPage(
                OriginalObjectCatalog.Empty,
                string.Empty,
                null,
                []);
        }
    }

    internal int CatalogCount { get { return _catalog.Count; } }
    internal int VisibleCount { get { return _catalog.VisibleCount; } }
    internal bool HasSession { get { return _session != null; } }
    internal ReadOnlyCollection<OriginalObjectCatalogEntry> VisibleEntries { get { return _catalog.VisibleEntries; } }
    internal ReadOnlyCollection<OriginalObjectOperation> Operations { get { return _operations; } }
    internal EditCommandHistory History { get { return _history; } }
    internal ReadOnlyCollection<OriginalObjectCandidate> Candidates
    {
        get
        {
            return _isActive
                ? _candidates
                : new ReadOnlyCollection<OriginalObjectCandidate>([]);
        }
    }

    internal OriginalObjectCatalogEntry Selected
    {
        get { return _isActive ? _selected : null; }
    }

    internal FrameworkObjectData SelectedModification
    {
        get
        {
            return TryGetSelectedResolvedOperations(
                out FrameworkObjectData modification,
                out _,
                out bool conflict)
                && !conflict
                ? modification
                : null;
        }
    }

    internal bool SelectedHasConflict
    {
        get
        {
            TryGetSelectedResolvedOperations(
                out _,
                out _,
                out bool conflict);
            return conflict;
        }
    }

    internal bool SelectedHasDeletion
    {
        get
        {
            return TryGetSelectedResolvedOperations(
                out _,
                out FrameworkObjectData deletion,
                out bool conflict)
                && !conflict
                && deletion != null;
        }
    }

    internal bool SelectedIsRuntimeActive
    {
        get
        {
            OriginalObjectCatalogEntry selected = Selected;
            if (selected == null || !CurrentLiveState(selected.Identity).IsActiveSelf)
                return false;

            return selected.IsRuntimeActiveSelf
                ? selected.IsRuntimeActive
                : selected.IsActive;
        }
    }

    internal PlacementTransform SelectedTransform
    {
        get
        {
            FrameworkObjectData modification = SelectedModification;
            if (modification != null)
                return PlacementTransform.FromFramework(modification);
            return _selected == null ? null : _selected.RuntimeTransform;
        }
    }

    internal RawPropertiesText SelectedPropertiesText
    {
        get
        {
            FrameworkObjectData modification = SelectedModification;
            return RawPropertiesText.Create(modification?.Properties);
        }
    }

    internal bool CanEditSelectedProperties
    {
        get
        {
            return SelectedModification != null
                && !SelectedPropertiesText.ContainsLineBreak;
        }
    }

    internal void SetActive(bool active)
    {
        _isActive = active;
    }

    internal bool TrySelect(
        string identity,
        ApplyReport report,
        out OriginalObjectCatalogEntry selected)
    {
        selected = null;
        if (report == null)
            throw new ArgumentNullException(nameof(report));
        if (!_catalog.TryGetByDisplayText(identity, out OriginalObjectCatalogEntry entry))
        {
            report.Warning(
                ReportCodes.SelectionCandidateNotFound,
                identity ?? "$",
                "The Original Object identity is not present in the locatable baseline catalog.");
            return false;
        }
        if (!_catalog.IsVisible(entry))
        {
            report.Warning(
                ReportCodes.SelectionCandidateNotFound,
                identity,
                "The inactive Original Object has no imported or pending operation targeting it.");
            return false;
        }

        _selected = entry;
        selected = entry;
        report.Info(
            ReportCodes.SelectionCandidateSelected,
            entry.DisplayText,
            "Selected Original Object " + entry.DisplayText + ".");
        return true;
    }

    internal bool TryChooseCandidate(
        string identity,
        ApplyReport report,
        out OriginalObjectCatalogEntry selected)
    {
        selected = null;
        if (report == null)
            throw new ArgumentNullException(nameof(report));

        foreach (OriginalObjectCandidate candidate in _candidates)
        {
            if (!string.Equals(candidate.DisplayText, identity, StringComparison.Ordinal))
                continue;

            _selected = candidate.Entry;
            selected = candidate.Entry;
            report.Info(
                ReportCodes.SelectionCandidateSelected,
                candidate.DisplayText,
                "Selected Original Object " + candidate.DisplayText + " from the overlap list.");
            return true;
        }

        report.Warning(
            ReportCodes.SelectionCandidateNotFound,
            identity ?? "$",
            "The Original Object row is not in the current overlap list.");
        return false;
    }

    internal bool TryPresentCandidates(
        IOriginalObjectHitSource hitSource,
        ApplyReport report)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));
        if (hitSource == null)
        {
            report.Error(
                ReportCodes.SelectionFailed,
                "Original Objects",
                "The Original Object hit source is unavailable.");
            return false;
        }

        bool found;
        IList<OriginalObjectHit> hits;
        string error;
        try
        {
            found = hitSource.TryFindAll(
                _frameworkLevelKey,
                out hits,
                out error);
        }
        catch (Exception exception)
        {
            report.Error(
                ReportCodes.SelectionFailed,
                "Original Objects",
                "Original Object hit collection failed: " + exception.Message);
            return false;
        }

        if (!found)
        {
            if (!string.IsNullOrEmpty(error))
            {
                report.Error(ReportCodes.SelectionFailed, "Original Objects", error);
                return false;
            }

            hits = new OriginalObjectHit[0];
        }

        return TryPresentCandidates(hits, report);
    }

    internal bool TryPresentCandidates(
        IEnumerable<OriginalObjectHit> hits,
        ApplyReport report)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));

        HashSet<OriginalObjectIdentity> matched = [];
        foreach (OriginalObjectHit hit in hits ?? [])
        {
            if (hit == null)
                continue;
            if (!OriginalObjectIdentity.TryCreate(
                hit.Scene,
                hit.Path,
                out OriginalObjectIdentity identity,
                out string identityError))
            {
                report.Warning(
                    ReportCodes.OriginalObjectUnaddressable,
                    DescribeHit(hit),
                    !string.IsNullOrEmpty(hit.Diagnostic)
                        ? hit.Diagnostic
                        : identityError);
                continue;
            }

            if (!_catalog.TryGetByIdentity(identity, out OriginalObjectCatalogEntry entry)
                || !string.Equals(entry.ObjectKey, hit.ObjectKey, StringComparison.Ordinal))
            {
                report.Warning(
                    ReportCodes.OriginalObjectProvenanceRejected,
                    DescribeHit(hit),
                    !string.IsNullOrEmpty(hit.Diagnostic)
                        ? hit.Diagnostic
                        : "The pointer hit is not the same object captured in the pre-mod baseline.");
                continue;
            }
            if (!_catalog.IsVisible(entry))
            {
                report.Warning(
                    ReportCodes.OriginalObjectUnaddressable,
                    DescribeHit(hit),
                    "The inactive baseline object has no imported or pending operation targeting it.");
                continue;
            }

            matched.Add(entry.Identity);
        }

        List<OriginalObjectCandidate> candidates = [];
        foreach (OriginalObjectCatalogEntry entry in _catalog.Entries)
        {
            if (matched.Contains(entry.Identity))
                candidates.Add(new OriginalObjectCandidate(entry));
        }
        _candidates = new ReadOnlyCollection<OriginalObjectCandidate>(candidates);

        if (candidates.Count == 0)
        {
            report.Warning(
                ReportCodes.SelectionNoCandidates,
                "Original Objects",
                "No addressable vanilla Original Object is under the pointer; the existing selection was preserved.");
            return false;
        }

        if (candidates.Count == 1)
        {
            _selected = candidates[0].Entry;
            report.Info(
                ReportCodes.SelectionCandidateSelected,
                candidates[0].DisplayText,
                "Selected the only overlapping Original Object.");
            return true;
        }

        report.Info(
            ReportCodes.SelectionCandidatesAvailable,
            "Original Objects",
            string.Format(
                "{0} Original Objects overlap the pointer. Choose a row explicitly; the existing selection was preserved.",
                candidates.Count));
        return true;
    }

    internal bool TryApplySelectedModification(
        PlacementTransform transform,
        ApplyReport report)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));
        if (_session == null)
        {
            report.Error(
                ReportCodes.OriginalObjectApplyFailed,
                "Original Objects",
                "No editable Level Edit Session is available.");
            return false;
        }
        if (_projector == null)
        {
            report.Error(
                ReportCodes.OriginalObjectApplyFailed,
                "Original Objects",
                "Original Object live projection is unavailable.");
            return false;
        }
        if (_selected == null)
        {
            report.Warning(
                ReportCodes.SelectionNoCandidates,
                "Original Objects",
                "Select a locatable Original Object before applying a modification.");
            return false;
        }
        if (!PlacementValidation.ValidateFiniteTransform(
            transform,
            _selected.DisplayText,
            report))
            return false;
        TryGetResolvedOperations(
            _selected.Identity,
            out List<FrameworkObjectData> modifications,
            out List<FrameworkObjectData> deletions);
        if (modifications.Count + deletions.Count > 1)
        {
            report.Error(
                ReportCodes.OriginalObjectEditConflict,
                _selected.DisplayText,
                "Multiple imported operations target this identity; resolve the conflict before editing.");
            return false;
        }

        FrameworkObjectData imported = modifications.Count == 1 ? modifications[0] : null;
        FrameworkObjectData next = imported == null
            ? new FrameworkObjectData(
                _selected.Identity.Scene,
                _selected.Identity.Path,
                OriginalObjectEditor.EmptyModificationType,
                _session.NextOriginalObjectModificationId(),
                transform.Position,
                transform.Rotation,
                transform.Scale,
                string.Empty,
                [])
            : new FrameworkObjectData(
                imported.Scene,
                imported.Path,
                imported.Type,
                imported.Id,
                transform.Position,
                transform.Rotation,
                transform.Scale,
                imported.Condition,
                imported.Properties);
        OriginalObjectPageSnapshot before = CaptureSnapshot(_selected.Identity);
        bool replacingDeletion = deletions.Count == 1;
        if (replacingDeletion && !TryReactivateAuthoredState(_selected.Identity, report))
            return false;

        if (!_projector.TryApplyModification(
            _frameworkLevelKey,
            next,
            report,
            out OriginalObjectLiveState appliedState))
        {
            if (replacingDeletion
                && !RestoreLiveStateFromSnapshot(before, report))
            {
                report.Error(
                    ReportCodes.HistoryRollbackFailed,
                    _selected.DisplayText,
                    "The live preview could not be rolled back after the modification failed.");
            }
            return false;
        }

        if (!_session.TryUpsertOriginalObjectModification(next, report))
        {
            if (!RestoreLiveStateFromSnapshot(before, report))
            {
                report.Error(
                    ReportCodes.HistoryRollbackFailed,
                    _selected.DisplayText,
                    "The live preview could not be rolled back after the session rejected the modification.");
            }
            return false;
        }

        _liveStates[_selected.Identity] = appliedState;
        _history.Record(new OriginalObjectSnapshotCommand(
            this,
            "Modify Original Object",
            before,
            CaptureSnapshot(_selected.Identity)));
        RefreshState(report);
        report.Applied();
        report.Info(
            ReportCodes.OriginalObjectModificationApplied,
            _selected.DisplayText,
            "Applied Original Object modification " + next.Id + ".");
        return true;
    }

    internal bool TryApplySelectedProperties(
        string rawText,
        ApplyReport report)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));
        if (_session == null)
        {
            report.Error(
                ReportCodes.OriginalObjectApplyFailed,
                "Original Objects",
                "No editable Level Edit Session is available.");
            return false;
        }
        if (_projector == null)
        {
            report.Error(
                ReportCodes.OriginalObjectApplyFailed,
                "Original Objects",
                "Original Object live projection is unavailable.");
            return false;
        }
        if (_selected == null)
        {
            report.Warning(
                ReportCodes.SelectionNoCandidates,
                "Original Objects",
                "Select a locatable Original Object before applying properties.");
            return false;
        }

        TryGetResolvedOperations(
            _selected.Identity,
            out List<FrameworkObjectData> modifications,
            out List<FrameworkObjectData> deletions);
        if (modifications.Count + deletions.Count > 1)
        {
            report.Error(
                ReportCodes.OriginalObjectEditConflict,
                _selected.DisplayText,
                "Multiple imported operations target this identity; resolve the conflict before editing properties.");
            return false;
        }

        FrameworkObjectData current = modifications.Count == 1 ? modifications[0] : null;
        if (current == null)
        {
            report.Warning(
                ReportCodes.OriginalObjectEditNotFound,
                _selected.DisplayText,
                "Apply Modify before editing raw properties.");
            return false;
        }
        if (!ValidateCommittedProperties(current, report))
            return false;

        List<string> properties = RawPropertiesText.Normalize(rawText);
        if (RawPropertiesText.SequenceEqual(current.Properties, properties))
        {
            report.Applied();
            return true;
        }

        FrameworkObjectData next = new(
            current.Scene,
            current.Path,
            current.Type,
            current.Id,
            current.Position,
            current.Rotation,
            current.Scale,
            current.Condition,
            properties);
        OriginalObjectPageSnapshot before = CaptureSnapshot(_selected.Identity);
        bool modifierAvailable = _projector.IsModifierAvailable(current.Type);
        OriginalObjectLiveState appliedState = null;
        if (modifierAvailable
            && !_projector.TryApplyModification(
                _frameworkLevelKey,
                next,
                report,
                out appliedState))
            return false;

        if (!_session.TryUpsertOriginalObjectModification(next, report))
        {
            if (modifierAvailable
                && !RestoreLiveStateFromSnapshot(before, report))
            {
                report.Error(
                    ReportCodes.HistoryRollbackFailed,
                    _selected.DisplayText,
                    "The live preview could not be rolled back after the session rejected the property change.");
            }
            return false;
        }

        if (modifierAvailable)
            _liveStates[_selected.Identity] = appliedState;
        _history.Record(new OriginalObjectSnapshotCommand(
            this,
            "Edit Original Object Properties",
            before,
            CaptureSnapshot(_selected.Identity)));
        RefreshState(report);
        report.Applied();
        if (modifierAvailable)
        {
            report.Info(
                ReportCodes.OriginalObjectModificationApplied,
                _selected.DisplayText,
                "Applied raw properties to Original Object modification " + next.Id + ".");
        }
        else
        {
            report.Warning(
                ReportCodes.OriginalObjectModifierUnavailable,
                _selected.DisplayText,
                "Modifier '" + current.Type + "' is unavailable; raw properties were committed to the session but were not applied in this session.");
        }
        return true;
    }

    private bool ValidateCommittedProperties(
        FrameworkObjectData modification,
        ApplyReport report)
    {
        bool valid = true;
        for (int index = 0; index < modification.Properties.Count; index++)
        {
            string property = modification.Properties[index];
            if (property == null || property.IndexOfAny(['\r', '\n']) < 0)
                continue;

            report.Warning(
                ReportCodes.PropertyInvalid,
                _selected.DisplayText + ".properties[" + index + "]",
                "Committed property contains an embedded CR or LF and cannot be replaced through the line editor.");
            valid = false;
        }

        return valid;
    }

    internal bool TryDeleteSelected(ApplyReport report)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));
        if (_session == null)
        {
            report.Warning(
                ReportCodes.OriginalObjectEditNotFound,
                "Original Objects",
                "No editable Level Edit Session is available.");
            return false;
        }
        if (_projector == null)
        {
            report.Error(
                ReportCodes.OriginalObjectApplyFailed,
                "Original Objects",
                "Original Object live projection is unavailable.");
            return false;
        }
        if (_selected == null)
        {
            report.Warning(
                ReportCodes.SelectionNoCandidates,
                "Original Objects",
                "Select a locatable Original Object before deleting it.");
            return false;
        }

        List<FrameworkObjectData> modifications =
            FindMatching(_session.Modifications, _selected.Identity);
        List<FrameworkObjectData> deletions =
            FindMatching(_session.Deletions, _selected.Identity);
        if (modifications.Count + deletions.Count > 1)
        {
            report.Error(
                ReportCodes.OriginalObjectEditConflict,
                _selected.DisplayText,
                "Multiple imported operations target this identity; resolve the conflict before deleting.");
            return false;
        }
        if (deletions.Count == 1)
        {
            report.Warning(
                ReportCodes.OriginalObjectEditNotFound,
                _selected.DisplayText,
                "The Original Object already has a pending deletion.");
            return false;
        }

        OriginalObjectPageSnapshot before = CaptureSnapshot(_selected.Identity);
        if (!_projector.TryApplyDeletion(
            _frameworkLevelKey,
            _selected.Identity,
            HiddenImportLiveState(_selected.Identity),
            report,
            out OriginalObjectLiveState appliedState))
            return false;

        if (!_session.TryUpsertOriginalObjectDeletion(_selected.Identity, report))
        {
            if (!RestoreLiveStateFromSnapshot(before, report))
            {
                report.Error(
                    ReportCodes.HistoryRollbackFailed,
                    _selected.DisplayText,
                    "The live preview could not be rolled back after the session rejected the deletion.");
            }
            return false;
        }

        _liveStates[_selected.Identity] = appliedState;
        _history.Record(new OriginalObjectSnapshotCommand(
            this,
            "Delete Original Object",
            before,
            CaptureSnapshot(_selected.Identity)));
        RefreshState(report);
        report.Applied();
        report.Info(
            ReportCodes.OriginalObjectDeletionApplied,
            _selected.DisplayText,
            "Applied Original Object deletion for " + _selected.DisplayText + ".");
        return true;
    }

    internal bool TryRemoveSelectedEdit(ApplyReport report)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));
        if (_session == null)
        {
            report.Warning(
                ReportCodes.OriginalObjectEditNotFound,
                "Original Objects",
                "No editable Level Edit Session is available.");
            return false;
        }
        if (_projector == null)
        {
            report.Error(
                ReportCodes.OriginalObjectApplyFailed,
                "Original Objects",
                "Original Object live projection is unavailable.");
            return false;
        }
        if (_selected == null)
        {
            report.Warning(
                ReportCodes.OriginalObjectEditNotFound,
                "Original Objects",
                "Select an Original Object operation before removing it.");
            return false;
        }
        if (!TryGetResolvedOperations(
            _selected.Identity,
            out List<FrameworkObjectData> modifications,
            out List<FrameworkObjectData> deletions))
            return false;
        if (modifications.Count + deletions.Count != 1)
        {
            report.Warning(
                ReportCodes.OriginalObjectEditNotFound,
                _selected.DisplayText,
                modifications.Count + deletions.Count == 0
                    ? "No pending Original Object operation targets this identity."
                    : "The identity has an unresolved conflict and cannot be removed as one operation.");
            return false;
        }
        FrameworkObjectData currentModification =
            modifications.Count == 1 ? modifications[0] : null;
        FrameworkObjectData currentDeletion =
            deletions.Count == 1 ? deletions[0] : null;
        OriginalObjectOperationKind currentKind = currentModification != null
            ? OriginalObjectOperationKind.Modification
            : OriginalObjectOperationKind.Deletion;
        FrameworkObjectData currentOperation =
            currentModification ?? currentDeletion;
        if (WasImported(currentKind, currentOperation))
        {
            report.Warning(
                ReportCodes.OriginalObjectEditNotFound,
                _selected.DisplayText,
                "The imported Original Object operation has no pending change to remove.");
            return false;
        }

        OriginalObjectPageSnapshot before = CaptureSnapshot(_selected.Identity);
        OriginalObjectLiveState importState = ImportLiveState(_selected.Identity);
        if (!_projector.TryRestore(
            _frameworkLevelKey,
            _selected.Identity,
            importState,
            report))
            return false;
        if (!_session.TryRestoreImportedOriginalObjectEdit(_selected.Identity, report))
        {
            if (!RestoreLiveStateFromSnapshot(before, report))
            {
                report.Error(
                    ReportCodes.HistoryRollbackFailed,
                    _selected.DisplayText,
                    "The live preview could not be rolled back after the session rejected the edit removal.");
            }
            return false;
        }

        _liveStates[_selected.Identity] = importState;
        _history.Record(new OriginalObjectSnapshotCommand(
            this,
            "Remove Original Object Edit",
            before,
            CaptureSnapshot(_selected.Identity)));
        RefreshState(report);
        report.Applied();
        report.Info(
            ReportCodes.OriginalObjectEditRemoved,
            _selected.DisplayText,
            "Removed the pending Original Object change and restored the imported state.");
        return true;
    }

    internal bool TryUndo(ApplyReport report)
    {
        return _history.Undo(report);
    }

    internal bool TryRedo(ApplyReport report)
    {
        return _history.Redo(report);
    }

    internal bool TryReset(ApplyReport report)
    {
        if (!TryPrepareReset(report, out OriginalObjectResetPreparation preparation))
            return false;

        preparation.Commit(report);
        return true;
    }

    internal bool TryPrepareReset(
        ApplyReport report,
        out OriginalObjectResetPreparation preparation)
    {
        preparation = null;
        if (report == null)
            throw new ArgumentNullException(nameof(report));
        if (_session == null)
        {
            report.Warning(
                ReportCodes.OriginalObjectEditNotFound,
                "Original Objects",
                "No editable Level Edit Session is available.");
            return false;
        }

        List<FrameworkObjectData> modifications = [.. _session.Modifications];
        List<FrameworkObjectData> deletions = [.. _session.Deletions];
        LevelEditSessionState state = _session.State;
        Dictionary<OriginalObjectIdentity, OriginalObjectLiveState> liveStates = [];
        HashSet<OriginalObjectIdentity> identities = [];
        AddOperationIdentities(_session.Modifications, identities);
        AddOperationIdentities(_session.Deletions, identities);
        AddOperationIdentities(_session.ImportedDocument.Modifications, identities);
        AddOperationIdentities(_session.ImportedDocument.Deletions, identities);
        foreach (OriginalObjectIdentity identity in identities)
        {
            if (_catalog.TryGetByIdentity(identity, out _))
                liveStates[identity] = CurrentLiveState(identity);
        }
        if (_projector == null)
        {
            if (identities.Count != 0)
            {
                report.Error(
                    ReportCodes.OriginalObjectApplyFailed,
                    "Original Objects",
                    "Original Object live projection is unavailable.");
                return false;
            }
        }
        else if (!TryRestoreToImportedState(identities, report))
        {
            return false;
        }

        _session.RestoreOriginalObjectEdits(
            _session.ImportedDocument.Modifications,
            _session.ImportedDocument.Deletions,
            _importedState,
            report);
        foreach (OriginalObjectIdentity identity in identities)
            _liveStates[identity] = ImportLiveState(identity);
        preparation = new OriginalObjectResetPreparation(
            this,
            modifications,
            deletions,
            state,
            liveStates,
            _selected);
        RefreshState(report);
        return true;
    }

    internal bool TryTeardown(ApplyReport report)
    {
        if (!TryPrepareTeardown(report))
            return false;

        CommitTeardown();
        return true;
    }

    internal bool TryPrepareTeardown(ApplyReport report)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));
        if (_session == null)
        {
            _history.Clear();
            return true;
        }

        HashSet<OriginalObjectIdentity> identities = [];
        AddOperationIdentities(_session.Modifications, identities);
        AddOperationIdentities(_session.Deletions, identities);
        AddOperationIdentities(_session.ImportedDocument.Modifications, identities);
        AddOperationIdentities(_session.ImportedDocument.Deletions, identities);
        if (_projector != null)
        {
            if (!TryRestoreToImportedState(identities, report))
                return false;
        }
        else if (identities.Count != 0)
        {
            report.Error(
                ReportCodes.OriginalObjectApplyFailed,
                "Original Objects",
                "Original Object live projection is unavailable during teardown.");
            return false;
        }

        foreach (OriginalObjectIdentity identity in identities)
        {
            if (_catalog.TryGetByIdentity(identity, out _))
                _liveStates[identity] = ImportLiveState(identity);
        }
        return true;
    }

    internal void CommitTeardown()
    {
        _history.Clear();
        _selected = null;
        _candidates = new ReadOnlyCollection<OriginalObjectCandidate>([]);
        _operations = new ReadOnlyCollection<OriginalObjectOperation>([]);
    }

    internal bool TryCancelTeardown(ApplyReport report)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));
        if (_session == null || _projector == null)
            return true;

        HashSet<OriginalObjectIdentity> identities = [];
        AddOperationIdentities(_session.Modifications, identities);
        AddOperationIdentities(_session.Deletions, identities);
        List<OriginalObjectIdentity> ordered = [];
        foreach (OriginalObjectIdentity identity in identities)
        {
            if (_catalog.TryGetByIdentity(identity, out _))
                ordered.Add(identity);
        }
        ordered.Sort(CompareIdentityForRestore);
        Dictionary<OriginalObjectIdentity, OriginalObjectLiveState> before = [];
        foreach (OriginalObjectIdentity identity in ordered)
            before[identity] = CurrentLiveState(identity);

        foreach (OriginalObjectIdentity identity in ordered)
        {
            List<FrameworkObjectData> modifications = FindMatching(
                _session.Modifications,
                identity);
            List<FrameworkObjectData> deletions = FindMatching(
                _session.Deletions,
                identity);
            if (modifications.Count + deletions.Count > 1)
            {
                report.Error(
                    ReportCodes.OriginalObjectEditConflict,
                    identity.DisplayText,
                    "The Original Object preview cannot be reapplied while its operations conflict.");
                RestoreCancelTeardownStates(ordered, before, report);
                return false;
            }

            bool restored;
            if (modifications.Count == 1)
            {
                if (!_projector.IsModifierAvailable(modifications[0].Type))
                {
                    restored = TryRestoreImportedLiveState(
                        identity,
                        report,
                        out OriginalObjectLiveState state);
                    if (restored)
                        _liveStates[identity] = state;
                }
                else
                {
                    bool reactivated = TryReactivateAuthoredState(identity, report);
                    OriginalObjectLiveState state = null;
                    restored = reactivated
                        && _projector.TryApplyModification(
                            _frameworkLevelKey,
                            modifications[0],
                            report,
                            out state);
                    if (restored)
                        _liveStates[identity] = state;
                }
            }
            else if (deletions.Count == 1)
            {
                restored = _projector.TryApplyDeletion(
                    _frameworkLevelKey,
                    identity,
                    HiddenImportLiveState(identity),
                    report,
                    out OriginalObjectLiveState state);
                if (restored)
                    _liveStates[identity] = state;
            }
            else
            {
                restored = _projector.TryRestore(
                    _frameworkLevelKey,
                    identity,
                    ImportLiveState(identity),
                    report);
                if (restored)
                    _liveStates[identity] = ImportLiveState(identity);
            }

            if (!restored)
            {
                RestoreCancelTeardownStates(ordered, before, report);
                return false;
            }
        }

        return true;
    }

    private void RestoreCancelTeardownStates(
        IList<OriginalObjectIdentity> identities,
        IDictionary<OriginalObjectIdentity, OriginalObjectLiveState> states,
        ApplyReport report)
    {
        for (int index = identities.Count - 1; index >= 0; index--)
        {
            OriginalObjectIdentity identity = identities[index];
            if (!states.TryGetValue(identity, out OriginalObjectLiveState state))
                continue;

            TryRestoreLiveState(
                identity,
                state,
                report,
                "The Original Object preview could not be rolled back after teardown cancellation failed.");
        }
    }

    private bool TryRestoreLiveState(
        OriginalObjectIdentity identity,
        OriginalObjectLiveState state,
        ApplyReport report,
        string rollbackFailure)
    {
        if (_projector == null)
            return true;
        if (_projector.TryRestore(
            _frameworkLevelKey,
            identity,
            state,
            report))
        {
            _liveStates[identity] = state;
            return true;
        }

        report.Error(
            ReportCodes.HistoryRollbackFailed,
            identity.DisplayText,
            rollbackFailure);
        return false;
    }

    private bool TryRestoreToImportedState(
        IEnumerable<OriginalObjectIdentity> identities,
        ApplyReport report)
    {
        List<OriginalObjectIdentity> attempted = [];
        List<OriginalObjectIdentity> ordered = [];
        foreach (OriginalObjectIdentity identity in identities ?? [])
        {
            if (_catalog.TryGetByIdentity(identity, out _))
                ordered.Add(identity);
        }
        ordered.Sort(CompareIdentityForRestore);

        foreach (OriginalObjectIdentity identity in ordered)
        {
            attempted.Add(identity);
            if (_projector.TryRestore(
                _frameworkLevelKey,
                identity,
                ImportLiveState(identity),
                report))
                continue;

            RestoreAttemptedLiveStates(attempted, report);
            return false;
        }

        return true;
    }

    private void RestoreAttemptedLiveStates(
        IList<OriginalObjectIdentity> attempted,
        ApplyReport report)
    {
        for (int index = attempted.Count - 1; index >= 0; index--)
        {
            OriginalObjectIdentity identity = attempted[index];
            if (_projector.TryRestore(
                _frameworkLevelKey,
                identity,
                CurrentLiveState(identity),
                report))
                continue;

            report.Error(
                ReportCodes.HistoryRollbackFailed,
                identity.DisplayText,
                "The Original Object previews restored before the failure could not be rolled back.");
        }
    }

    private static int CompareIdentityForRestore(
        OriginalObjectIdentity left,
        OriginalObjectIdentity right)
    {
        int scene = SceneOrderForRestore(left.Scene).CompareTo(SceneOrderForRestore(right.Scene));
        return scene != 0
            ? scene
            : StringComparer.Ordinal.Compare(left.Path, right.Path);
    }

    private static int SceneOrderForRestore(string scene)
    {
        return scene == "decoration" ? 0 : scene == "layout" ? 1 : 2;
    }

    private void RestoreSelection(OriginalObjectCatalogEntry selected)
    {
        if (selected != null && _catalog.IsVisible(selected))
            _selected = selected;
    }

    private bool TryRestoreSnapshot(
        OriginalObjectPageSnapshot snapshot,
        ApplyReport report)
    {
        if (snapshot == null)
        {
            report.Error(
                ReportCodes.HistoryRestoreFailed,
                "$",
                "Original Object history snapshot is missing.");
            return false;
        }

        OriginalObjectIdentity identity = snapshot.Identity;
        List<FrameworkObjectData> targetModifications = FindMatching(
            snapshot.Modifications,
            identity);
        List<FrameworkObjectData> targetDeletions = FindMatching(
            snapshot.Deletions,
            identity);
        if (targetModifications.Count + targetDeletions.Count > 1)
        {
            report.Error(
                ReportCodes.HistoryRestoreFailed,
                identity.DisplayText,
                "The history snapshot contains an unresolved Original Object conflict.");
            return false;
        }

        OriginalObjectLiveState appliedState = null;
        if (_projector != null)
        {
            bool restored;
            if (targetModifications.Count == 1)
            {
                if (!_projector.IsModifierAvailable(targetModifications[0].Type))
                {
                    restored = TryRestoreImportedLiveState(
                        identity,
                        report,
                        out appliedState);
                }
                else
                {
                    OriginalObjectPageSnapshot current = CaptureSnapshot(identity);
                    bool reactivated = TryReactivateAuthoredState(identity, report);
                    restored = reactivated
                        && _projector.TryApplyModification(
                            _frameworkLevelKey,
                            targetModifications[0],
                            report,
                            out appliedState);
                    if (!restored
                        && reactivated
                        && !RestoreLiveStateFromSnapshot(current, report))
                    {
                        report.Error(
                            ReportCodes.HistoryRollbackFailed,
                            identity.DisplayText,
                            "The live preview could not be rolled back after the history restore failed.");
                    }
                }
            }
            else if (targetDeletions.Count == 1)
            {
                restored = _projector.TryApplyDeletion(
                    _frameworkLevelKey,
                    identity,
                    HiddenImportLiveState(identity),
                    report,
                    out appliedState);
            }
            else
            {
                restored = _projector.TryRestore(
                    _frameworkLevelKey,
                    identity,
                    ImportLiveState(identity),
                    report);
            }

            if (!restored)
                return false;
        }

        _session.RestoreOriginalObjectEdits(
            snapshot.Modifications,
            snapshot.Deletions,
            snapshot.State,
            report);
        if (targetModifications.Count == 1)
        {
            _liveStates[identity] = appliedState;
        }
        else if (targetDeletions.Count == 1)
        {
            _liveStates[identity] = appliedState;
        }
        else
        {
            _liveStates[identity] = ImportLiveState(identity);
        }

        RefreshState(report);
        report.Applied();
        return true;
    }

    private bool RestoreLiveStateFromSnapshot(
        OriginalObjectPageSnapshot snapshot,
        ApplyReport report)
    {
        if (_projector == null || snapshot == null)
            return true;

        List<FrameworkObjectData> modifications = FindMatching(
            snapshot.Modifications,
            snapshot.Identity);
        List<FrameworkObjectData> deletions = FindMatching(
            snapshot.Deletions,
            snapshot.Identity);
        if (modifications.Count == 1)
        {
            if (!_projector.IsModifierAvailable(modifications[0].Type))
            {
                return TryRestoreImportedLiveState(
                    snapshot.Identity,
                    report,
                    out _);
            }

            if (!TryReactivateAuthoredState(snapshot.Identity, report))
                return false;

            if (!_projector.TryApplyModification(
                _frameworkLevelKey,
                modifications[0],
                report,
                out OriginalObjectLiveState modificationState))
                return false;

            _liveStates[snapshot.Identity] = modificationState;
            return true;
        }
        if (deletions.Count == 1)
        {
            if (!_projector.TryApplyDeletion(
                _frameworkLevelKey,
                snapshot.Identity,
                HiddenImportLiveState(snapshot.Identity),
                report,
                out OriginalObjectLiveState deletionState))
                return false;

            _liveStates[snapshot.Identity] = deletionState;
            return true;
        }

        OriginalObjectLiveState importState = ImportLiveState(snapshot.Identity);
        if (!_projector.TryRestore(
            _frameworkLevelKey,
            snapshot.Identity,
            importState,
            report))
            return false;

        _liveStates[snapshot.Identity] = importState;
        return true;
    }

    private bool TryRestoreImportedLiveState(
        OriginalObjectIdentity identity,
        ApplyReport report,
        out OriginalObjectLiveState state)
    {
        state = ImportLiveState(identity);
        if (!_projector.TryRestore(
            _frameworkLevelKey,
            identity,
            state,
            report))
        {
            state = null;
            return false;
        }

        _liveStates[identity] = state;
        return true;
    }

    private bool TryReactivateAuthoredState(
        OriginalObjectIdentity identity,
        ApplyReport report)
    {
        if (_projector == null)
            return true;

        OriginalObjectLiveState current = CurrentLiveState(identity);
        if (current.IsActiveSelf
            || !_catalog.TryGetByIdentity(identity, out OriginalObjectCatalogEntry entry)
            || !entry.IsBaselineActiveSelf)
            return true;

        OriginalObjectLiveState authored = new(
            current.ObjectKey,
            true,
            current.Transform,
            current.Name);
        if (!_projector.TryRestore(
            _frameworkLevelKey,
            identity,
            authored,
            report))
            return false;

        _liveStates[identity] = authored;
        return true;
    }

    private void RefreshState(ApplyReport report)
    {
        HashSet<string> targetedObjectKeys = [];
        AddCatalogTargetedObjectKeys(_session.Modifications, targetedObjectKeys);
        AddCatalogTargetedObjectKeys(_session.Deletions, targetedObjectKeys);
        _catalog.ReplaceTargetedObjectKeys(targetedObjectKeys);
        _operations = BuildOperations(report);
        if (_selected != null && !_catalog.IsVisible(_selected))
            _selected = null;
    }

    private ReadOnlyCollection<OriginalObjectOperation> BuildOperations(ApplyReport report)
    {
        if (_session == null)
            return new ReadOnlyCollection<OriginalObjectOperation>([]);

        List<OriginalObjectOperation> operations = [];
        AddOperations(
            OriginalObjectOperationKind.Modification,
            LevelFrameworkContract.ModificationsField,
            _session.Modifications,
            operations,
            report);
        AddOperations(
            OriginalObjectOperationKind.Deletion,
            LevelFrameworkContract.DeletionsField,
            _session.Deletions,
            operations,
            report);
        return new ReadOnlyCollection<OriginalObjectOperation>(operations);
    }

    private bool TryGetSelectedResolvedOperations(
        out FrameworkObjectData modification,
        out FrameworkObjectData deletion,
        out bool conflict)
    {
        modification = null;
        deletion = null;
        conflict = false;
        if (_selected == null)
            return false;

        TryGetResolvedOperations(
            _selected.Identity,
            out List<FrameworkObjectData> modifications,
            out List<FrameworkObjectData> deletions);
        conflict = modifications.Count + deletions.Count > 1;
        modification = modifications.Count == 1 ? modifications[0] : null;
        deletion = deletions.Count == 1 ? deletions[0] : null;
        return !conflict && (modification != null || deletion != null);
    }

    private bool TryGetResolvedOperations(
        OriginalObjectIdentity identity,
        out List<FrameworkObjectData> modifications,
        out List<FrameworkObjectData> deletions)
    {
        modifications = FindMatching(_session.Modifications, identity);
        deletions = FindMatching(_session.Deletions, identity);
        return modifications.Count + deletions.Count == 1;
    }

    private static List<FrameworkObjectData> FindMatching(
        IEnumerable<FrameworkObjectData> operations,
        OriginalObjectIdentity identity)
    {
        List<FrameworkObjectData> matching = [];
        foreach (FrameworkObjectData operation in operations ?? [])
        {
            if (operation != null
                && string.Equals(operation.Scene, identity.Scene, StringComparison.Ordinal)
                && string.Equals(operation.Path, identity.Path, StringComparison.Ordinal))
                matching.Add(operation);
        }

        return matching;
    }

    private OriginalObjectLiveState CurrentLiveState(OriginalObjectIdentity identity)
    {
        if (_liveStates.TryGetValue(identity, out OriginalObjectLiveState state))
            return state;

        return _catalog.TryGetByIdentity(identity, out OriginalObjectCatalogEntry entry)
            ? entry.LiveState
            : new OriginalObjectLiveState(string.Empty, true, PlacementTransform.Default, string.Empty);
    }

    private OriginalObjectLiveState ImportLiveState(OriginalObjectIdentity identity)
    {
        if (_importLiveStates.TryGetValue(identity, out OriginalObjectLiveState state))
            return state;

        return CurrentLiveState(identity);
    }

    private OriginalObjectLiveState HiddenImportLiveState(OriginalObjectIdentity identity)
    {
        OriginalObjectLiveState importState = ImportLiveState(identity);
        return new OriginalObjectLiveState(
            importState.ObjectKey,
            false,
            importState.Transform,
            importState.Name);
    }

    private OriginalObjectPageSnapshot CaptureSnapshot(OriginalObjectIdentity identity)
    {
        return new OriginalObjectPageSnapshot(
            identity,
            _session.Modifications,
            _session.Deletions,
            _session.State);
    }

    private void AddCatalogTargetedObjectKeys(
        IEnumerable<FrameworkObjectData> operations,
        HashSet<string> targeted)
    {
        foreach (FrameworkObjectData operation in operations ?? [])
        {
            if (operation == null
                || !OriginalObjectIdentity.TryCreate(
                    operation.Scene,
                    operation.Path,
                    out OriginalObjectIdentity identity,
                    out _))
                continue;
            if (_catalog.TryGetByIdentity(identity, out OriginalObjectCatalogEntry entry))
                targeted.Add(entry.ObjectKey);
        }
    }

    private static void AddOperationIdentities(
        IEnumerable<FrameworkObjectData> operations,
        HashSet<OriginalObjectIdentity> identities)
    {
        foreach (FrameworkObjectData operation in operations ?? [])
        {
            if (operation != null
                && OriginalObjectIdentity.TryCreate(
                    operation.Scene,
                    operation.Path,
                    out OriginalObjectIdentity identity,
                    out _))
                identities.Add(identity);
        }
    }

    private void BuildImportedOperationProvenance(FrameworkLevelEdit imported)
    {
        AddImportedOperationProvenance(
            OriginalObjectOperationKind.Modification,
            imported.Modifications);
        AddImportedOperationProvenance(
            OriginalObjectOperationKind.Deletion,
            imported.Deletions);
    }

    private void AddImportedOperationProvenance(
        OriginalObjectOperationKind kind,
        IEnumerable<FrameworkObjectData> operations)
    {
        foreach (FrameworkObjectData operation in operations ?? [])
        {
            if (operation == null)
                continue;
            _importedOperations.Add(new OriginalObjectOperationProvenance(
                kind,
                operation));
        }
    }

    private bool WasImported(
        OriginalObjectOperationKind kind,
        FrameworkObjectData operation)
    {
        foreach (OriginalObjectOperationProvenance imported in _importedOperations)
        {
            if (imported.Matches(kind, operation))
                return true;
        }

        return false;
    }

    private static HashSet<string> CollectTargetedObjectKeys(
        OriginalObjectBaseline baseline,
        IEnumerable<FrameworkObjectData> modifications,
        IEnumerable<FrameworkObjectData> deletions)
    {
        HashSet<string> targeted = new(StringComparer.Ordinal);
        AddTargetedObjectKeys(baseline, modifications, targeted);
        AddTargetedObjectKeys(baseline, deletions, targeted);
        return targeted;
    }

    private static void AddTargetedObjectKeys(
        OriginalObjectBaseline baseline,
        IEnumerable<FrameworkObjectData> operations,
        HashSet<string> targeted)
    {
        foreach (FrameworkObjectData operation in operations ?? [])
        {
            if (operation == null
                || !OriginalObjectIdentity.TryCreate(
                    operation.Scene,
                    operation.Path,
                    out OriginalObjectIdentity identity,
                    out _))
                continue;

            AddBaselineObjectKey(baseline, identity, targeted);
        }
    }

    private static void AddBaselineObjectKey(
        OriginalObjectBaseline baseline,
        OriginalObjectIdentity identity,
        HashSet<string> targeted)
    {
        if (baseline == null)
            return;

        foreach (OriginalObjectSnapshotEntry entry in baseline.Entries)
        {
            if (entry == null
                || !entry.IsAddressable
                || !string.Equals(entry.Scene, identity.Scene, StringComparison.Ordinal)
                || !string.Equals(entry.Path, identity.Path, StringComparison.Ordinal))
                continue;

            targeted.Add(entry.ObjectKey);
            return;
        }
    }

    private void AddOperations(
        OriginalObjectOperationKind kind,
        string field,
        IEnumerable<FrameworkObjectData> sourceOperations,
        List<OriginalObjectOperation> destination,
        ApplyReport report)
    {
        int index = 0;
        foreach (FrameworkObjectData source in sourceOperations ?? [])
        {
            string sourceName = field + "[" + index + "]";
            index++;
            bool locatable = TryLocateOperation(source, out _);
            bool imported = WasImported(kind, source);
            int operationCount = CountOperations(source.Scene, source.Path);
            OriginalObjectOperation operation = new(
                kind,
                sourceName,
                source.Scene,
                source.Path,
                source.Type,
                source.Id,
                locatable,
                isImported: imported,
                isPending: !imported,
                hasConflict: operationCount > 1);
            destination.Add(operation);
            if (!locatable)
            {
                report.Warning(
                    ReportCodes.OriginalObjectOperationUnlocatable,
                    sourceName,
                    "The operation remains in the export but does not locate a vanilla baseline Original Object.");
            }
            if (operationCount > 1)
            {
                report.Warning(
                    ReportCodes.OriginalObjectEditConflict,
                    sourceName,
                    "Multiple imported operations target this identity; the entries remain read-only until resolved.");
            }
        }
    }

    private int CountOperations(string scene, string path)
    {
        int count = 0;
        foreach (FrameworkObjectData operation in _session.Modifications)
        {
            if (string.Equals(operation.Scene, scene, StringComparison.Ordinal)
                && string.Equals(operation.Path, path, StringComparison.Ordinal))
                count++;
        }
        foreach (FrameworkObjectData operation in _session.Deletions)
        {
            if (string.Equals(operation.Scene, scene, StringComparison.Ordinal)
                && string.Equals(operation.Path, path, StringComparison.Ordinal))
                count++;
        }

        return count;
    }

    private bool TryLocateOperation(
        FrameworkObjectData operation,
        out OriginalObjectCatalogEntry entry)
    {
        entry = null;
        if (operation == null
            || !OriginalObjectIdentity.TryCreate(
                operation.Scene,
                operation.Path,
                out OriginalObjectIdentity identity,
                out _))
            return false;

        return _catalog.TryGetByIdentity(identity, out entry);
    }

    private static string DescribeHit(OriginalObjectHit hit)
    {
        string identity = string.IsNullOrEmpty(hit.Scene) || string.IsNullOrEmpty(hit.Path)
            ? "<unaddressable>"
            : hit.Scene + ":" + hit.Path;
        return identity + "#" + (hit.ObjectKey ?? string.Empty);
    }

    private sealed class OriginalObjectOperationProvenance
    {
        internal OriginalObjectOperationProvenance(
            OriginalObjectOperationKind kind,
            FrameworkObjectData operation)
        {
            Kind = kind;
            Operation = operation;
        }

        internal OriginalObjectOperationKind Kind { get; }
        internal FrameworkObjectData Operation { get; }

        internal bool Matches(
            OriginalObjectOperationKind kind,
            FrameworkObjectData operation)
        {
            return Kind == kind && ReferenceEquals(Operation, operation);
        }
    }

    private sealed class OriginalObjectPageSnapshot
    {
        internal OriginalObjectPageSnapshot(
            OriginalObjectIdentity identity,
            IEnumerable<FrameworkObjectData> modifications,
            IEnumerable<FrameworkObjectData> deletions,
            LevelEditSessionState state)
        {
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            Modifications = [.. modifications ?? []];
            Deletions = [.. deletions ?? []];
            State = state;
        }

        internal OriginalObjectIdentity Identity { get; }
        internal IList<FrameworkObjectData> Modifications { get; }
        internal IList<FrameworkObjectData> Deletions { get; }
        internal LevelEditSessionState State { get; }
    }

    internal sealed class OriginalObjectResetPreparation
    {
        private readonly OriginalObjectPage _page;
        private readonly List<FrameworkObjectData> _modifications;
        private readonly List<FrameworkObjectData> _deletions;
        private readonly LevelEditSessionState _state;
        private readonly List<KeyValuePair<OriginalObjectIdentity, OriginalObjectLiveState>> _liveStates;
        private readonly OriginalObjectCatalogEntry _selected;
        private bool _pending = true;

        internal OriginalObjectResetPreparation(
            OriginalObjectPage page,
            IEnumerable<FrameworkObjectData> modifications,
            IEnumerable<FrameworkObjectData> deletions,
            LevelEditSessionState state,
            IDictionary<OriginalObjectIdentity, OriginalObjectLiveState> liveStates,
            OriginalObjectCatalogEntry selected)
        {
            _page = page;
            _modifications = [.. modifications ?? []];
            _deletions = [.. deletions ?? []];
            _state = state;
            _liveStates = [.. liveStates ?? new Dictionary<OriginalObjectIdentity, OriginalObjectLiveState>()];
            _selected = selected;
        }

        internal void Commit(ApplyReport report)
        {
            if (!_pending)
                return;

            _pending = false;
            _page._history.Clear();
            report.Info(
                ReportCodes.SessionReset,
                _page._frameworkLevelKey,
                "Original Object edits were reset to the imported snapshot.");
        }

        internal bool Rollback(ApplyReport report)
        {
            if (!_pending)
                return true;

            _pending = false;
            _page._session.RestoreOriginalObjectEdits(
                _modifications,
                _deletions,
                _state,
                report);
            foreach (KeyValuePair<OriginalObjectIdentity, OriginalObjectLiveState> entry in _liveStates)
                _page._liveStates[entry.Key] = entry.Value;

            bool restored = true;
            if (_page._projector != null)
            {
                foreach (KeyValuePair<OriginalObjectIdentity, OriginalObjectLiveState> entry in _liveStates)
                {
                    if (_page._projector.TryRestore(
                        _page._frameworkLevelKey,
                        entry.Key,
                        entry.Value,
                        report))
                        continue;

                    restored = false;
                    report.Error(
                        ReportCodes.HistoryRollbackFailed,
                        entry.Key.DisplayText,
                        "The Original Object preview could not be restored after the global Reset failed.");
                }
            }

            _page.RefreshState(report);
            _page.RestoreSelection(_selected);
            return restored;
        }
    }

    private sealed class OriginalObjectSnapshotCommand : IReversibleEditCommand
    {
        private readonly OriginalObjectPage _page;
        private readonly OriginalObjectPageSnapshot _before;
        private readonly OriginalObjectPageSnapshot _after;

        internal OriginalObjectSnapshotCommand(
            OriginalObjectPage page,
            string name,
            OriginalObjectPageSnapshot before,
            OriginalObjectPageSnapshot after)
        {
            _page = page;
            Name = name;
            _before = before;
            _after = after;
        }

        public string Name { get; }

        public bool Undo(ApplyReport report)
        {
            return _page.TryRestoreSnapshot(_before, report);
        }

        public bool Redo(ApplyReport report)
        {
            return _page.TryRestoreSnapshot(_after, report);
        }
    }
}