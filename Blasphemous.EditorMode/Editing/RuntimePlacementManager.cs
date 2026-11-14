using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blasphemous.EditorMode.Editing;

/// <summary>Runtime object projection owned by one Placement.</summary>
internal interface IRuntimePlacementHandle
{
    string PlacementId { get; }
    bool IsOriginal { get; }
    bool IsAlive { get; }
    bool TryApply(PlacementRecord placement, out string error);
    bool TryDestroy(out string error);
}

/// <summary>Creates a runtime projection after domain validation.</summary>
internal interface IRuntimePlacementFactory
{
    IRuntimePlacementHandle Create(PlacementRecord placement);
}

/// <summary>Optional runtime condition seam for Framework-compatible placements.</summary>
internal interface IRuntimePlacementConditionFilter
{
    bool ShouldProject(PlacementRecord placement, out string reason);
}

/// <summary>Optional seam for adopting objects already created by Level Framework.</summary>
internal interface IRuntimePlacementAdopter
{
    bool TryAdopt(
        PlacementRecord placement,
        out IRuntimePlacementHandle handle,
        out string error);
}

internal interface IRuntimePlacementAdoptionPass
{
    void BeginAdoptionPass();
    void EndAdoptionPass();
}

/// <summary>Creates deterministic, non-conflicting IDs for one session.</summary>
internal static class PlacementIdGenerator
{
    internal static string Next(string type, IEnumerable<string> existingIds)
    {
        string stem = Sanitize(type);
        HashSet<string> existing = new(existingIds ?? [], StringComparer.OrdinalIgnoreCase);
        for (int index = 1; ; index++)
        {
            string candidate = stem + "-" + index;
            if (!existing.Contains(candidate))
                return candidate;
        }
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "placement";

        char[] chars = new char[value.Length];
        int length = 0;
        foreach (char character in value.ToLowerInvariant())
        {
            if ((character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9')
                || character == '-' || character == '_')
                chars[length++] = character;
            else if (length == 0 || chars[length - 1] != '-')
                chars[length++] = '-';
        }

        if (length == 0)
            return "placement";

        string sanitized = new string(chars, 0, length).Trim('-');
        return sanitized.Length == 0 ? "placement" : sanitized;
    }
}

/// <summary>Projects Architect-owned Placements into the current room.</summary>
internal sealed class RuntimePlacementManager
{
    private readonly LevelEditSession _session;
    private readonly ContentCatalog _catalog;
    private readonly IRuntimePlacementFactory _factory;
    private readonly Dictionary<string, IRuntimePlacementHandle> _projections =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly EditCommandHistory _history = new();
    private PlacementManagerSnapshot _initialSnapshot;

    internal RuntimePlacementManager(
        LevelEditSession session,
        ContentCatalog catalog,
        IRuntimePlacementFactory factory)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _initialSnapshot = CaptureSnapshot();
    }

    internal void CaptureInitialSnapshot()
    {
        _initialSnapshot = CaptureSnapshot();
    }

    internal ReadOnlyCollection<string> OwnedPlacementIds
    {
        get { return new ReadOnlyCollection<string>([.. _projections.Keys]); }
    }

    internal PlacementRecord Selected { get; private set; }
    internal PlacementRecord Clipboard { get; private set; }
    internal EditCommandHistory History { get { return _history; } }

    internal bool TryPlace(
        string type,
        PlacementTransform transform,
        IEnumerable<string> properties,
        ApplyReport report,
        out PlacementRecord placement)
    {
        return TryPlace(type, transform, properties, string.Empty, report, out placement);
    }

    internal bool TryPlace(
        string type,
        PlacementTransform transform,
        IEnumerable<string> properties,
        string condition,
        ApplyReport report,
        out PlacementRecord placement)
    {
        return TryPlaceCore(type, transform, properties, report, "place", condition, out placement);
    }

    private bool TryPlaceCore(
        string type,
        PlacementTransform transform,
        IEnumerable<string> properties,
        ApplyReport report,
        string commandName,
        string condition,
        out PlacementRecord placement)
    {
        PlacementManagerSnapshot before = CaptureSnapshot();
        placement = null;
        if (!_catalog.TryGet(type, out CreatorMetadata creator))
        {
            report.Error("CREATOR_UNSUPPORTED", type, "Creator is unavailable or unsupported.");
            return false;
        }

        string id = PlacementIdGenerator.Next(type, AllIds());
        try
        {
            placement = new PlacementRecord(
                id,
                creator.Type,
                transform,
                properties,
                PlacementOwnership.ArchitectOwned,
                false,
                condition);
        }
        catch (Exception exception)
        {
            report.Error("PLACEMENT_INVALID", type, exception.Message);
            placement = null;
            return false;
        }

        if (!PlacementValidation.Validate(placement, creator, report))
        {
            placement = null;
            return false;
        }
        if (!PlacementValidation.ValidateCondition(condition, id, report))
        {
            placement = null;
            return false;
        }

        if (_factory is IRuntimePlacementConditionFilter conditionFilter
            && !conditionFilter.ShouldProject(placement, out string conditionReason))
        {
            report.Warning(
                "CONDITION_UNMET",
                id,
                conditionReason ?? "Placement condition is not active in the current runtime.",
                true);
            placement = null;
            return false;
        }

        IRuntimePlacementHandle handle;
        try
        {
            handle = _factory.Create(placement);
        }
        catch (Exception exception)
        {
            report.Error("RUNTIME_CREATE_FAILED", id, exception.Message);
            placement = null;
            return false;
        }

        if (handle == null
            || handle.IsOriginal
            || !handle.IsAlive
            || !string.Equals(handle.PlacementId, id, StringComparison.OrdinalIgnoreCase))
        {
            if (handle != null && !handle.IsOriginal && handle.IsAlive)
                handle.TryDestroy(out _);
            report.Error("OWNERSHIP_REJECTED", id, "Runtime factory did not return an Architect-owned projection.");
            placement = null;
            return false;
        }

        if (!_session.TryAddPlacement(placement, report))
        {
            handle.TryDestroy(out _);
            placement = null;
            return false;
        }

        _projections.Add(id, handle);
        Selected = placement;
        report.Applied();
        RecordSnapshotCommand(commandName, before, CaptureSnapshot());
        return true;
    }

    internal bool TrySelect(string id, ApplyReport report, out PlacementRecord placement)
    {
        placement = null;
        if (!_projections.TryGetValue(id, out IRuntimePlacementHandle handle)
            || handle == null
            || handle.IsOriginal
            || !handle.IsAlive
            || !_session.TryGetPlacement(id, out placement))
        {
            report.Error("OWNERSHIP_REJECTED", id, "Only live Architect-owned projections are selectable.");
            placement = null;
            return false;
        }

        Selected = placement;
        return true;
    }

    internal bool TrySelectRuntimeObject(
        IRuntimePlacementHandle handle,
        ApplyReport report,
        out PlacementRecord placement)
    {
        placement = null;
        if (handle == null || handle.IsOriginal)
        {
            report.Error("OWNERSHIP_REJECTED", "$", "Original game objects are read-only and cannot be selected.");
            return false;
        }

        return TrySelect(handle.PlacementId, report, out placement);
    }

    internal bool TryProjectExisting(PlacementRecord placement, ApplyReport report)
    {
        if (placement == null
            || placement.Ownership != PlacementOwnership.ArchitectOwned
            || !_session.TryGetPlacement(placement.Id, out PlacementRecord sessionPlacement))
        {
            report.Error("OWNERSHIP_REJECTED", placement == null ? "$" : placement.Id, "Only owned session placements can be projected.");
            return false;
        }

        if (_factory is IRuntimePlacementConditionFilter conditionFilter
            && !conditionFilter.ShouldProject(placement, out string conditionReason))
        {
            if (_projections.TryGetValue(placement.Id, out IRuntimePlacementHandle inactiveHandle)
                && inactiveHandle != null
                && inactiveHandle.IsAlive
                && !inactiveHandle.IsOriginal)
            {
                if (!inactiveHandle.TryDestroy(out string destroyError))
                {
                    report.Error(
                        "RUNTIME_CLEAR_FAILED",
                        placement.Id,
                        destroyError ?? "Inactive projection rejected cleanup.");
                    return false;
                }
            }
            _projections.Remove(placement.Id);
            if (Selected != null && string.Equals(Selected.Id, placement.Id, StringComparison.OrdinalIgnoreCase))
                Selected = null;
            report.Warning(
                "CONDITION_UNMET",
                placement.Id,
                conditionReason ?? "Placement condition is not active in the current runtime.",
                true);
            return true;
        }

        if (_projections.TryGetValue(placement.Id, out IRuntimePlacementHandle existing))
        {
            if (existing == null || existing.IsOriginal || !existing.IsAlive)
            {
                report.Error("RUNTIME_CREATE_FAILED", placement.Id, "Existing projection is not live and owned.");
                return false;
            }

            if (!existing.TryApply(sessionPlacement, out string existingError))
            {
                report.Error("RUNTIME_APPLY_FAILED", placement.Id, existingError ?? "Projection rejected reconciliation.");
                return false;
            }

            report.Applied();
            return true;
        }

        IRuntimePlacementHandle handle;
        if (_factory is IRuntimePlacementAdopter adopter)
        {
            if (adopter.TryAdopt(placement, out handle, out string adoptionError))
            {
                if (handle == null
                    || handle.IsOriginal
                    || !handle.IsAlive
                    || !string.Equals(handle.PlacementId, placement.Id, StringComparison.OrdinalIgnoreCase))
                {
                    if (handle != null && !handle.IsOriginal && handle.IsAlive)
                        handle.TryDestroy(out _);
                    report.Error(
                        "OWNERSHIP_REJECTED",
                        placement.Id,
                        "Runtime adopter returned an invalid reconciliation projection.");
                    return false;
                }

                _projections.Add(placement.Id, handle);
                report.Applied();
                return true;
            }

            if (adoptionError != null)
            {
                report.Error("RUNTIME_ADOPTION_FAILED", placement.Id, adoptionError);
                return false;
            }
        }

        try
        {
            handle = _factory.Create(sessionPlacement);
        }
        catch (Exception exception)
        {
            report.Error("RUNTIME_CREATE_FAILED", placement.Id, exception.Message);
            return false;
        }

        if (handle == null
            || handle.IsOriginal
            || !handle.IsAlive
            || !string.Equals(handle.PlacementId, placement.Id, StringComparison.OrdinalIgnoreCase))
        {
            if (handle != null && !handle.IsOriginal && handle.IsAlive)
                handle.TryDestroy(out _);
            report.Error("OWNERSHIP_REJECTED", placement.Id, "Runtime factory returned an invalid reconciliation projection.");
            return false;
        }

        _projections.Add(placement.Id, handle);
        report.Applied();
        return true;
    }

    internal bool TryCopy(string id, ApplyReport report, out PlacementRecord copied)
    {
        return TryCopyCore(id, report, "copy", out copied);
    }

    private bool TryCopyCore(string id, ApplyReport report, string commandName, out PlacementRecord copied)
    {
        PlacementManagerSnapshot before = CaptureSnapshot();
        copied = null;
        if (!TrySelect(id, report, out PlacementRecord selected))
            return false;

        Clipboard = selected.CopyWithId(selected.Id);
        copied = Clipboard;
        report.Info("PLACEMENT_COPIED", id, "Placement copied to the editor cursor.");
        RecordSnapshotCommand(commandName, before, CaptureSnapshot());
        return true;
    }

    internal bool TryPick(string id, ApplyReport report, out PlacementRecord picked)
    {
        return TryCopyCore(id, report, "pick", out picked);
    }

    internal bool TryPaste(ApplyReport report, out PlacementRecord pasted)
    {
        return TryPaste(null, report, out pasted);
    }

    internal bool TryPaste(
        PlacementTransform transform,
        ApplyReport report,
        out PlacementRecord pasted)
    {
        pasted = null;
        if (Clipboard == null)
        {
            report.Error("CLIPBOARD_EMPTY", "$", "The editor cursor has no Placement.");
            return false;
        }

        return TryPlaceCore(
            Clipboard.Type,
            transform ?? Clipboard.Transform,
            Clipboard.Properties,
            report,
            "paste",
            Clipboard.Condition,
            out pasted);
    }

    internal bool TrySetLocked(string id, bool locked, ApplyReport report)
    {
        PlacementManagerSnapshot before = CaptureSnapshot();
        if (!TrySelect(id, report, out PlacementRecord current))
            return false;

        PlacementRecord changed = current.WithLocked(locked);
        if (!_session.TryReplacePlacement(changed, report))
            return false;

        Selected = changed;
        report.Applied();
        RecordSnapshotCommand("lock", before, CaptureSnapshot());
        return true;
    }

    internal bool TryDelete(string id, ApplyReport report)
    {
        return TryRemove(id, report);
    }

    internal bool TryClear(ApplyReport report)
    {
        bool success = true;
        foreach (string id in new List<string>(_projections.Keys))
        {
            IRuntimePlacementHandle handle = _projections[id];
            if (handle != null && handle.IsAlive && !handle.IsOriginal)
            {
                if (!handle.TryDestroy(out string error))
                {
                    report.Error("RUNTIME_CLEAR_FAILED", id, error ?? "Runtime projection rejected cleanup.");
                    success = false;
                    continue;
                }
            }

            _projections.Remove(id);
        }

        if (success)
            Selected = null;
        return success;
    }

    internal bool TryMove(
        string id,
        PlacementTransform transform,
        ApplyReport report)
    {
        PlacementManagerSnapshot before = CaptureSnapshot();
        if (!TrySelect(id, report, out PlacementRecord current)
            || !_projections.TryGetValue(id, out IRuntimePlacementHandle handle)
            || !_catalog.TryGet(current.Type, out CreatorMetadata creator))
            return false;
        if (current.IsLocked)
        {
            report.Error("PLACEMENT_LOCKED", id, "Locked placement cannot be moved.");
            return false;
        }
        if (!PlacementValidation.ValidateTransform(transform, creator, id, report))
            return false;

        PlacementRecord changed = current.WithTransform(transform);
        if (!handle.TryApply(changed, out string error))
        {
            RestoreRuntime(handle, current, report);
            report.Error("RUNTIME_APPLY_FAILED", id, error ?? "Runtime projection rejected the transform.");
            return false;
        }

        if (!_session.TryReplacePlacement(changed, report))
        {
            RestoreRuntime(handle, current, report);
            return false;
        }

        Selected = changed;
        report.Applied();
        RecordSnapshotCommand("transform", before, CaptureSnapshot());
        return true;
    }

    internal bool TrySetProperty(
        string id,
        string key,
        string rawValue,
        ApplyReport report)
    {
        PlacementManagerSnapshot before = CaptureSnapshot();
        if (!TrySelect(id, report, out PlacementRecord current)
            || !_projections.TryGetValue(id, out IRuntimePlacementHandle handle)
            || !_catalog.TryGet(current.Type, out CreatorMetadata creator))
            return false;
        if (current.IsLocked)
        {
            report.Error("PLACEMENT_LOCKED", id, "Locked placement cannot be edited.");
            return false;
        }

        if (!creator.TryGetProperty(key, out CreatorPropertyDefinition definition))
        {
            report.Error("PROPERTY_UNSUPPORTED", id, "Property key '" + key + "' is not supported.");
            return false;
        }

        if (!definition.TrySerialize(rawValue, out string serialized, out string error))
        {
            report.Error("PROPERTY_INVALID", id, error);
            return false;
        }

        List<string> properties = [.. current.Properties];
        bool replaced = false;
        string prefix = definition.Key + "=";
        for (int index = 0; index < properties.Count; index++)
        {
            if (string.Equals(properties[index], definition.Key, StringComparison.OrdinalIgnoreCase)
                || (properties[index] != null
                    && properties[index].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                properties[index] = serialized;
                replaced = true;
                break;
            }
        }
        if (!replaced)
            properties.Add(serialized);

        PlacementRecord changed = current.WithProperties(properties);
        if (!handle.TryApply(changed, out error))
        {
            RestoreRuntime(handle, current, report);
            report.Error("RUNTIME_APPLY_FAILED", id, error ?? "Runtime projection rejected the property.");
            return false;
        }

        if (!_session.TryReplacePlacement(changed, report))
        {
            RestoreRuntime(handle, current, report);
            return false;
        }

        Selected = changed;
        report.Applied();
        RecordSnapshotCommand("property", before, CaptureSnapshot());
        return true;
    }

    internal bool TryRemove(string id, ApplyReport report)
    {
        PlacementManagerSnapshot before = CaptureSnapshot();
        if (!_projections.TryGetValue(id, out IRuntimePlacementHandle handle)
            || handle == null
            || handle.IsOriginal
            || !_session.TryGetPlacement(id, out PlacementRecord current))
        {
            report.Error("OWNERSHIP_REJECTED", id, "Only Architect-owned placements can be removed.");
            return false;
        }
        if (current.IsLocked)
        {
            report.Error("PLACEMENT_LOCKED", id, "Locked placement cannot be removed.");
            return false;
        }

        if (!handle.TryDestroy(out string error))
        {
            report.Error("RUNTIME_REMOVE_FAILED", id, error ?? "Runtime projection rejected removal.");
            return false;
        }

        if (!_session.TryRemovePlacement(id, report, out _))
            return false;

        _projections.Remove(id);
        if (Selected != null && string.Equals(Selected.Id, id, StringComparison.OrdinalIgnoreCase))
            Selected = null;
        report.Applied();
        RecordSnapshotCommand("delete", before, CaptureSnapshot());
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
        if (!RestoreSnapshot(_initialSnapshot, report))
            return false;

        _history.Clear();
        report.Info(
            "SESSION_RESET",
            _session.Room.ExportFileKey,
            "Session reset to the imported snapshot; Original Save and source export were not written.");
        return true;
    }

    private PlacementManagerSnapshot CaptureSnapshot()
    {
        Dictionary<string, PlacementRecord> projections = new(StringComparer.OrdinalIgnoreCase);
        foreach (string id in _projections.Keys)
        {
            if (_session.TryGetPlacement(id, out PlacementRecord placement))
                projections[id] = placement;
        }

        return new PlacementManagerSnapshot(
            _session.Placements,
            Clipboard,
            Selected?.Id,
            _session.State,
            projections);
    }

    private void RecordSnapshotCommand(
        string name,
        PlacementManagerSnapshot before,
        PlacementManagerSnapshot after)
    {
        _history.Record(new SnapshotEditCommand(this, name, before, after));
    }

    private bool RestoreSnapshot(PlacementManagerSnapshot snapshot, ApplyReport report)
    {
        List<KeyValuePair<IRuntimePlacementHandle, PlacementRecord>> applied = [];
        Dictionary<string, IRuntimePlacementHandle> created = new(StringComparer.OrdinalIgnoreCase);
        List<KeyValuePair<string, PlacementRecord>> destroyed = [];
        HashSet<string> targetIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, PlacementRecord> target in snapshot.Projections)
        {
            if (_factory is IRuntimePlacementConditionFilter conditionFilter
                && !conditionFilter.ShouldProject(target.Value, out string conditionReason))
            {
                report.Warning(
                    "CONDITION_UNMET",
                    target.Key,
                    conditionReason ?? "Placement condition is not active in the current runtime.",
                    true);
                continue;
            }

            targetIds.Add(target.Key);
        }

        foreach (KeyValuePair<string, PlacementRecord> target in snapshot.Projections)
        {
            if (!targetIds.Contains(target.Key))
                continue;

            if (_projections.TryGetValue(target.Key, out IRuntimePlacementHandle handle)
                && handle != null
                && !handle.IsOriginal
                && handle.IsAlive)
            {
                if (!_session.TryGetPlacement(target.Key, out PlacementRecord previous))
                {
                    report.Error("HISTORY_RESTORE_FAILED", target.Key, "Projection has no session Placement.");
                    Rollback(applied, created, destroyed, report);
                    return false;
                }

                if (!handle.TryApply(target.Value, out string error))
                {
                    report.Error("HISTORY_RESTORE_FAILED", target.Key, error ?? "Runtime projection rejected history restore.");
                    Rollback(applied, created, destroyed, report);
                    return false;
                }

                applied.Add(new KeyValuePair<IRuntimePlacementHandle, PlacementRecord>(handle, previous));
                continue;
            }

            try
            {
                handle = _factory.Create(target.Value);
            }
            catch (Exception exception)
            {
                report.Error("HISTORY_RESTORE_FAILED", target.Key, exception.Message);
                Rollback(applied, created, destroyed, report);
                return false;
            }

            if (handle == null
                || handle.IsOriginal
                || !handle.IsAlive
                || !string.Equals(handle.PlacementId, target.Key, StringComparison.OrdinalIgnoreCase))
            {
                if (handle != null && !handle.IsOriginal && handle.IsAlive)
                {
                    if (!handle.TryDestroy(out string cleanupError))
                        report.Error(
                            "HISTORY_ROLLBACK_FAILED",
                            target.Key,
                            cleanupError ?? "Invalid runtime projection could not be removed.");
                }

                report.Error("HISTORY_RESTORE_FAILED", target.Key, "Runtime factory returned an invalid projection.");
                Rollback(applied, created, destroyed, report);
                return false;
            }

            created.Add(target.Key, handle);
        }

        foreach (string id in new List<string>(_projections.Keys))
        {
            if (targetIds.Contains(id))
                continue;

            IRuntimePlacementHandle handle = _projections[id];
            if (handle == null || !handle.IsAlive || handle.IsOriginal)
                continue;

            if (!_session.TryGetPlacement(id, out PlacementRecord previous))
            {
                report.Error("HISTORY_RESTORE_FAILED", id, "Projection has no session Placement.");
                Rollback(applied, created, destroyed, report);
                return false;
            }

            string error;
            bool destroySucceeded;
            try
            {
                destroySucceeded = handle.TryDestroy(out error);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                destroySucceeded = false;
            }

            if (!destroySucceeded)
            {
                report.Error("HISTORY_RESTORE_FAILED", id, error ?? "Runtime projection rejected removal.");
                if (!handle.IsAlive)
                    destroyed.Add(new KeyValuePair<string, PlacementRecord>(id, previous));
                Rollback(applied, created, destroyed, report);
                return false;
            }

            destroyed.Add(new KeyValuePair<string, PlacementRecord>(id, previous));
        }

        _session.RestorePlacements(snapshot.Placements, snapshot.State, report);
        foreach (string id in new List<string>(_projections.Keys))
        {
            if (!targetIds.Contains(id))
                _projections.Remove(id);
        }
        foreach (KeyValuePair<string, IRuntimePlacementHandle> createdProjection in created)
            _projections[createdProjection.Key] = createdProjection.Value;

        Clipboard = snapshot.Clipboard;
        Selected = null;
        if (snapshot.SelectedId != null)
        {
            if (_session.TryGetPlacement(snapshot.SelectedId, out PlacementRecord selected))
                Selected = selected;
        }
        report.Applied();
        return true;
    }

    private void Rollback(
        IEnumerable<KeyValuePair<IRuntimePlacementHandle, PlacementRecord>> applied,
        IEnumerable<KeyValuePair<string, IRuntimePlacementHandle>> created,
        IEnumerable<KeyValuePair<string, PlacementRecord>> destroyed,
        ApplyReport report)
    {
        foreach (KeyValuePair<IRuntimePlacementHandle, PlacementRecord> change in applied)
        {
            try
            {
                if (!change.Key.TryApply(change.Value, out string error))
                    report.Error(
                        "HISTORY_ROLLBACK_FAILED",
                        change.Value.Id,
                        error ?? "Runtime projection could not be restored.");
            }
            catch (Exception exception)
            {
                report.Error("HISTORY_ROLLBACK_FAILED", change.Value.Id, exception.Message);
            }
        }

        foreach (KeyValuePair<string, IRuntimePlacementHandle> projection in created)
        {
            try
            {
                if (!projection.Value.TryDestroy(out string error))
                    report.Error(
                        "HISTORY_ROLLBACK_FAILED",
                        projection.Key,
                        error ?? "Created runtime projection could not be removed.");
            }
            catch (Exception exception)
            {
                report.Error("HISTORY_ROLLBACK_FAILED", projection.Key, exception.Message);
            }
        }

        foreach (KeyValuePair<string, PlacementRecord> projection in destroyed)
        {
            IRuntimePlacementHandle handle;
            try
            {
                handle = _factory.Create(projection.Value);
            }
            catch (Exception exception)
            {
                _projections.Remove(projection.Key);
                report.Error("HISTORY_ROLLBACK_FAILED", projection.Key, exception.Message);
                continue;
            }

            if (handle == null
                || handle.IsOriginal
                || !handle.IsAlive
                || !string.Equals(handle.PlacementId, projection.Key, StringComparison.OrdinalIgnoreCase))
            {
                if (handle != null && !handle.IsOriginal && handle.IsAlive)
                {
                    try
                    {
                        if (!handle.TryDestroy(out string cleanupError))
                            report.Error(
                                "HISTORY_ROLLBACK_FAILED",
                                projection.Key,
                                cleanupError ?? "Invalid restored projection could not be removed.");
                    }
                    catch (Exception exception)
                    {
                        report.Error("HISTORY_ROLLBACK_FAILED", projection.Key, exception.Message);
                    }
                }

                _projections.Remove(projection.Key);
                report.Error(
                    "HISTORY_ROLLBACK_FAILED",
                    projection.Key,
                    "Previously destroyed runtime projection could not be restored.");
                continue;
            }

            _projections[projection.Key] = handle;
        }
    }

    private static void RestoreRuntime(
        IRuntimePlacementHandle handle,
        PlacementRecord placement,
        ApplyReport report)
    {
        if (!handle.TryApply(placement, out string error))
            report.Error("RUNTIME_ROLLBACK_FAILED", placement.Id, error ?? "Runtime projection could not be restored.");
    }

    private sealed class PlacementManagerSnapshot
    {
        internal PlacementManagerSnapshot(
            IEnumerable<PlacementRecord> placements,
            PlacementRecord clipboard,
            string selectedId,
            LevelEditSessionState state,
            IDictionary<string, PlacementRecord> projections)
        {
            Placements = [.. placements ?? []];
            Clipboard = clipboard;
            SelectedId = selectedId;
            State = state;
            Projections = new Dictionary<string, PlacementRecord>(
                projections ?? new Dictionary<string, PlacementRecord>(),
                StringComparer.OrdinalIgnoreCase);
        }

        internal IList<PlacementRecord> Placements { get; }
        internal PlacementRecord Clipboard { get; }
        internal string SelectedId { get; }
        internal LevelEditSessionState State { get; }
        internal IDictionary<string, PlacementRecord> Projections { get; }
    }

    private sealed class SnapshotEditCommand : IReversibleEditCommand
    {
        private readonly RuntimePlacementManager _owner;
        private readonly PlacementManagerSnapshot _before;
        private readonly PlacementManagerSnapshot _after;

        internal SnapshotEditCommand(
            RuntimePlacementManager owner,
            string name,
            PlacementManagerSnapshot before,
            PlacementManagerSnapshot after)
        {
            _owner = owner;
            Name = name;
            _before = before;
            _after = after;
        }

        public string Name { get; }

        public bool Undo(ApplyReport report)
        {
            return _owner.RestoreSnapshot(_before, report);
        }

        public bool Redo(ApplyReport report)
        {
            return _owner.RestoreSnapshot(_after, report);
        }
    }

    private IEnumerable<string> AllIds()
    {
        foreach (PlacementRecord placement in _session.Placements)
            yield return placement.Id;
        foreach (string id in _projections.Keys)
            yield return id;
    }
}
