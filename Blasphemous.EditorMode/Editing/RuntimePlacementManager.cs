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

/// <summary>Creates deterministic, non-conflicting IDs for one session.</summary>
internal static class PlacementIdGenerator
{
    internal static string Next(string type, IEnumerable<string> existingIds)
    {
        string stem = Sanitize(type);
        var existing = new HashSet<string>(existingIds ?? new string[0], StringComparer.OrdinalIgnoreCase);
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

        var chars = new char[value.Length];
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
        new Dictionary<string, IRuntimePlacementHandle>(StringComparer.OrdinalIgnoreCase);

    internal RuntimePlacementManager(
        LevelEditSession session,
        ContentCatalog catalog,
        IRuntimePlacementFactory factory)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    internal ReadOnlyCollection<string> OwnedPlacementIds
    {
        get { return new ReadOnlyCollection<string>(new List<string>(_projections.Keys)); }
    }

    internal PlacementRecord Selected { get; private set; }

    internal bool TryPlace(
        string type,
        PlacementTransform transform,
        IEnumerable<string> properties,
        ApplyReport report,
        out PlacementRecord placement)
    {
        placement = null;
        CreatorMetadata creator;
        if (!_catalog.TryGet(type, out creator))
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
                PlacementOwnership.ArchitectOwned);
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
        return true;
    }

    internal bool TrySelect(string id, ApplyReport report, out PlacementRecord placement)
    {
        placement = null;
        IRuntimePlacementHandle handle;
        if (!_projections.TryGetValue(id, out handle)
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

    internal bool TryMove(
        string id,
        PlacementTransform transform,
        ApplyReport report)
    {
        PlacementRecord current;
        IRuntimePlacementHandle handle;
        CreatorMetadata creator;
        if (!TrySelect(id, report, out current)
            || !_projections.TryGetValue(id, out handle)
            || !_catalog.TryGet(current.Type, out creator))
            return false;
        if (current.IsLocked)
        {
            report.Error("PLACEMENT_LOCKED", id, "Locked placement cannot be moved.");
            return false;
        }
        if (!PlacementValidation.ValidateTransform(transform, creator, id, report))
            return false;

        PlacementRecord changed = current.WithTransform(transform);
        string error;
        if (!handle.TryApply(changed, out error))
        {
            report.Error("RUNTIME_APPLY_FAILED", id, error ?? "Runtime projection rejected the transform.");
            return false;
        }

        if (!_session.TryReplacePlacement(changed, report))
        {
            handle.TryApply(current, out _);
            return false;
        }

        Selected = changed;
        report.Applied();
        return true;
    }

    internal bool TrySetProperty(
        string id,
        string key,
        string rawValue,
        ApplyReport report)
    {
        PlacementRecord current;
        IRuntimePlacementHandle handle;
        CreatorMetadata creator;
        if (!TrySelect(id, report, out current)
            || !_projections.TryGetValue(id, out handle)
            || !_catalog.TryGet(current.Type, out creator))
            return false;
        if (current.IsLocked)
        {
            report.Error("PLACEMENT_LOCKED", id, "Locked placement cannot be edited.");
            return false;
        }

        CreatorPropertyDefinition definition;
        if (!creator.TryGetProperty(key, out definition))
        {
            report.Error("PROPERTY_UNSUPPORTED", id, "Property key '" + key + "' is not supported.");
            return false;
        }

        string serialized;
        string error;
        if (!definition.TrySerialize(rawValue, out serialized, out error))
        {
            report.Error("PROPERTY_INVALID", id, error);
            return false;
        }

        var properties = new List<string>(current.Properties);
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
            report.Error("RUNTIME_APPLY_FAILED", id, error ?? "Runtime projection rejected the property.");
            return false;
        }

        if (!_session.TryReplacePlacement(changed, report))
        {
            handle.TryApply(current, out _);
            return false;
        }

        Selected = changed;
        report.Applied();
        return true;
    }

    internal bool TryRemove(string id, ApplyReport report)
    {
        IRuntimePlacementHandle handle;
        PlacementRecord current;
        if (!_projections.TryGetValue(id, out handle)
            || handle == null
            || handle.IsOriginal
            || !_session.TryGetPlacement(id, out current))
        {
            report.Error("OWNERSHIP_REJECTED", id, "Only Architect-owned placements can be removed.");
            return false;
        }
        if (current.IsLocked)
        {
            report.Error("PLACEMENT_LOCKED", id, "Locked placement cannot be removed.");
            return false;
        }

        string error;
        if (!handle.TryDestroy(out error))
        {
            report.Error("RUNTIME_REMOVE_FAILED", id, error ?? "Runtime projection rejected removal.");
            return false;
        }

        PlacementRecord removed;
        if (!_session.TryRemovePlacement(id, report, out removed))
            return false;

        _projections.Remove(id);
        if (Selected != null && string.Equals(Selected.Id, id, StringComparison.OrdinalIgnoreCase))
            Selected = null;
        report.Applied();
        return true;
    }

    private IEnumerable<string> AllIds()
    {
        foreach (PlacementRecord placement in _session.Placements)
            yield return placement.Id;
        foreach (string id in _projections.Keys)
            yield return id;
    }
}
