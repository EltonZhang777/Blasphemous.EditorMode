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
        string addressError = null)
    {
        ObjectKey = objectKey ?? throw new ArgumentNullException(nameof(objectKey));
        Scene = scene;
        Path = path;
        IsActive = active;
        Provenance = provenance;
        IsAddressable = addressable;
        AddressError = addressError;
    }

    internal string ObjectKey { get; }
    internal string Scene { get; }
    internal string Path { get; }
    internal bool IsActive { get; }
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
    internal OriginalObjectProjection(string objectKey, bool active)
    {
        ObjectKey = objectKey ?? throw new ArgumentNullException(nameof(objectKey));
        IsActive = active;
    }

    internal string ObjectKey { get; }
    internal bool IsActive { get; }
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
        bool runtimeActive)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        ObjectKey = objectKey ?? throw new ArgumentNullException(nameof(objectKey));
        IsActive = active;
        IsRuntimeActive = runtimeActive;
    }

    internal OriginalObjectIdentity Identity { get; }
    internal string ObjectKey { get; }
    /// <summary>Whether the object was active in the captured pre-mod baseline.</summary>
    internal bool IsActive { get; }
    internal bool IsRuntimeActive { get; }
    internal string DisplayText { get { return Identity.DisplayText; } }
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
                projection.IsActive));
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
        bool isPending)
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

/// <summary>Read-only Original Objects state for the active room.</summary>
internal sealed class OriginalObjectPage
{
    private readonly OriginalObjectCatalog _catalog;
    private readonly string _frameworkLevelKey;
    private readonly ReadOnlyCollection<OriginalObjectCatalogEntry> _visibleEntries;
    private readonly ReadOnlyCollection<OriginalObjectOperation> _operations;
    private ReadOnlyCollection<OriginalObjectCandidate> _candidates;
    private OriginalObjectCatalogEntry _selected;
    private bool _isActive;

    private OriginalObjectPage(
        OriginalObjectCatalog catalog,
        string frameworkLevelKey,
        IOriginalObjectPathResolver resolver,
        IEnumerable<OriginalObjectOperation> operations)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _frameworkLevelKey = frameworkLevelKey ?? string.Empty;
        _visibleEntries = catalog.VisibleEntries;
        _operations = new ReadOnlyCollection<OriginalObjectOperation>(
            [.. operations ?? []]);
        _candidates = new ReadOnlyCollection<OriginalObjectCandidate>([]);
    }

    internal OriginalObjectPage(
        LevelEditSession session,
        OriginalObjectBaseline baseline,
        IOriginalObjectPathResolver resolver,
        ApplyReport report)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));
        if (report == null)
            throw new ArgumentNullException(nameof(report));

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

        List<FrameworkObjectData> modifications = [.. session.ImportedDocument.Modifications];
        List<FrameworkObjectData> deletions = [.. session.ImportedDocument.Deletions];
        HashSet<string> targetedObjectKeys = CollectTargetedObjectKeys(
            baseline,
            modifications,
            deletions);
        _catalog = OriginalObjectCatalog.Build(
            baseline,
            targetedObjectKeys,
            resolver,
            report);
        _visibleEntries = _catalog.VisibleEntries;

        List<OriginalObjectOperation> operations = [];
        AddOperations(
            OriginalObjectOperationKind.Modification,
            LevelFrameworkContract.ModificationsField,
            modifications,
            operations,
            report);
        AddOperations(
            OriginalObjectOperationKind.Deletion,
            LevelFrameworkContract.DeletionsField,
            deletions,
            operations,
            report);
        _operations = new ReadOnlyCollection<OriginalObjectOperation>(operations);
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
    internal int VisibleCount { get { return _visibleEntries.Count; } }
    internal ReadOnlyCollection<OriginalObjectCatalogEntry> VisibleEntries { get { return _visibleEntries; } }
    internal ReadOnlyCollection<OriginalObjectOperation> Operations { get { return _operations; } }
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
            OriginalObjectOperation operation = new(
                kind,
                sourceName,
                source.Scene,
                source.Path,
                source.Type,
                source.Id,
                locatable,
                isImported: true,
                isPending: false);
            destination.Add(operation);
            if (!locatable)
            {
                report.Warning(
                    ReportCodes.OriginalObjectOperationUnlocatable,
                    sourceName,
                    "The operation remains in the export but does not locate a vanilla baseline Original Object.");
            }
        }
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
}