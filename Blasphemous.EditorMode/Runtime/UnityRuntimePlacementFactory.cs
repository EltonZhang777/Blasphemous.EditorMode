using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Editing;
using Blasphemous.Framework.Levels;
using Blasphemous.Framework.Levels.Modifiers;
using Blasphemous.ModdingAPI;
using Framework.Managers;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Tools.Level.Layout;
using UnityEngine;

namespace Blasphemous.EditorMode.Runtime;

/// <summary>Creates and adopts owned Unity projections from the base Framework registry.</summary>
internal sealed class UnityRuntimePlacementFactory :
    IRuntimePlacementFactory,
    IRuntimePlacementConditionFilter,
    IRuntimePlacementAdopter,
    IRuntimePlacementAdoptionPass
{
    private readonly Func<Transform> _holderProvider;
    private readonly bool _verboseDiagnostics;
    private bool _adoptionPassActive;
    private Transform _adoptionPassHolder;
    private Dictionary<string, Dictionary<string, Queue<RuntimePlacementMarker>>> _adoptionMarkers;

    internal UnityRuntimePlacementFactory(Func<Transform> holderProvider, bool verboseDiagnostics)
    {
        _holderProvider = holderProvider ?? FindCurrentObjectHolder;
        _verboseDiagnostics = verboseDiagnostics;
    }

    public void BeginAdoptionPass()
    {
        _adoptionPassHolder = _holderProvider();
        _adoptionMarkers = BuildFrameworkMarkerLookup(_adoptionPassHolder);
        _adoptionPassActive = true;
    }

    public void EndAdoptionPass()
    {
        _adoptionPassActive = false;
        _adoptionPassHolder = null;
        _adoptionMarkers = null;
    }

    public IRuntimePlacementHandle Create(PlacementRecord placement)
    {
        if (placement == null)
            throw new ArgumentNullException(nameof(placement));

        if (!RuntimeCreatorRegistry.TryGet(placement.Type, out ObjectCreator creator, out string error))
            throw new InvalidOperationException(error);

        if (creator.Loader == null || creator.Loader.Result == null)
        {
            throw new InvalidOperationException(
                "Creator '" + placement.Type + "' has no loaded source object. "
                + "The Framework creator preload has not completed.");
        }

        Transform holder = _holderProvider() ?? throw new InvalidOperationException("Current LevelInitializer object holder is unavailable.");
        GameObject clone = null;
        try
        {
            clone = UnityEngine.Object.Instantiate(creator.Loader.Result, holder);
            UnityRuntimePlacementHandle handle = new(
                placement.Id,
                placement.Type,
                clone,
                creator);
            if (!handle.TryApply(placement, out string applyError))
                throw new InvalidOperationException(applyError ?? "Runtime projection rejected the placement.");

            MarkEditorProjection(clone, placement);
            return handle;
        }
        catch
        {
            if (clone != null)
                UnityEngine.Object.Destroy(clone);
            throw;
        }
    }

    public bool TryAdopt(
        PlacementRecord placement,
        out IRuntimePlacementHandle handle,
        out string error)
    {
        handle = null;
        error = null;
        if (placement == null)
        {
            error = "Placement is required.";
            return false;
        }

        if (!RuntimeCreatorRegistry.TryGet(placement.Type, out ObjectCreator creator, out error))
            return false;

        Transform holder = _adoptionPassActive ? _adoptionPassHolder : _holderProvider();
        if (holder == null)
        {
            error = "Current LevelInitializer object holder is unavailable.";
            return false;
        }

        Dictionary<string, Dictionary<string, Queue<RuntimePlacementMarker>>> lookup =
            _adoptionPassActive ? _adoptionMarkers : BuildFrameworkMarkerLookup(holder);
        if (!TryTakeMatchingMarker(lookup, holder, placement, out RuntimePlacementMarker marker))
            return false;

        UnityRuntimePlacementHandle adopted = new(
            placement.Id,
            placement.Type,
            marker.gameObject,
            creator);
        if (!adopted.TryApply(placement, out string applyError))
        {
            error = applyError ?? "Framework projection rejected adoption.";
            return false;
        }

        MarkEditorProjection(marker.gameObject, placement);
        handle = adopted;
        return true;
    }

    private static Dictionary<string, Dictionary<string, Queue<RuntimePlacementMarker>>> BuildFrameworkMarkerLookup(
        Transform holder)
    {
        Dictionary<string, Dictionary<string, Queue<RuntimePlacementMarker>>> lookup =
            new(
                StringComparer.OrdinalIgnoreCase);
        if (holder == null)
            return lookup;

        RuntimePlacementMarker[] markers = UnityEngine.Object.FindObjectsOfType<RuntimePlacementMarker>();
        foreach (RuntimePlacementMarker marker in markers)
        {
            if (marker == null || marker.transform == null
                || !marker.transform.IsChildOf(holder)
                || !string.Equals(marker.Source, "Framework", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(marker.PlacementId)
                || string.IsNullOrEmpty(marker.Type))
                continue;

            if (!lookup.TryGetValue(marker.PlacementId, out Dictionary<string, Queue<RuntimePlacementMarker>> byType))
            {
                byType = new Dictionary<string, Queue<RuntimePlacementMarker>>(StringComparer.OrdinalIgnoreCase);
                lookup.Add(marker.PlacementId, byType);
            }

            if (!byType.TryGetValue(marker.Type, out Queue<RuntimePlacementMarker> matchingMarkers))
            {
                matchingMarkers = new Queue<RuntimePlacementMarker>();
                byType.Add(marker.Type, matchingMarkers);
            }

            matchingMarkers.Enqueue(marker);
        }

        return lookup;
    }

    private static bool TryTakeMatchingMarker(
        Dictionary<string, Dictionary<string, Queue<RuntimePlacementMarker>>> lookup,
        Transform holder,
        PlacementRecord placement,
        out RuntimePlacementMarker marker)
    {
        marker = null;
        if (lookup == null)
            return false;

        if (!lookup.TryGetValue(placement.Id, out Dictionary<string, Queue<RuntimePlacementMarker>> byType)
            || !byType.TryGetValue(placement.Type, out Queue<RuntimePlacementMarker> candidates))
            return false;

        while (candidates.Count > 0)
        {
            RuntimePlacementMarker candidate = candidates.Dequeue();
            if (candidate == null || candidate.transform == null
                || !candidate.transform.IsChildOf(holder)
                || !string.Equals(candidate.Source, "Framework", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(candidate.PlacementId, placement.Id, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(candidate.Type, placement.Type, StringComparison.OrdinalIgnoreCase))
                continue;

            marker = candidate;
            break;
        }

        if (candidates.Count == 0)
        {
            byType.Remove(placement.Type);
            if (byType.Count == 0)
                lookup.Remove(placement.Id);
        }

        return marker != null;
    }

    public bool ShouldProject(PlacementRecord placement, out string reason)
    {
        reason = null;
        if (placement == null || string.IsNullOrEmpty(placement.Condition))
            return true;

        int colon = placement.Condition.IndexOf(':');
        if (colon <= 0 || colon == placement.Condition.Length - 1)
        {
            reason = "Condition is malformed.";
            return false;
        }

        string kind = placement.Condition.Substring(0, colon);
        string value = placement.Condition.Substring(colon + 1);
        try
        {
            switch (kind)
            {
                case "flag":
                    return Core.Events.GetFlag(value);
                case "penitence":
                    return Core.PenitenceManager.GetCurrentPenitence() != null
                        && Core.PenitenceManager.GetCurrentPenitence().Id == value;
                case "gamemode":
                    return Core.GameModeManager.CheckGameModeActive(value);
                default:
                    reason = "Condition kind is unsupported: " + kind;
                    return false;
            }
        }
        catch (Exception exception)
        {
            reason = "Condition evaluation failed: " + exception.Message;
            return false;
        }
    }

    internal bool TryFindAtScreenPoint(
        Vector2 screenPoint,
        out IRuntimePlacementHandle handle,
        out string error)
    {
        handle = null;
        error = null;
        Transform holder = _holderProvider();
        if (holder == null)
        {
            error = "Current LevelInitializer object holder is unavailable.";
            return false;
        }

        Camera camera = Camera.main;
        if (camera == null)
        {
            error = "Main camera is unavailable.";
            return false;
        }

        Vector2 cameraPixels = UnityScreenCoordinateMapper.ToCameraPixels(camera, screenPoint);
        Vector3 world = UnityScreenCoordinateMapper.ToWorldPosition(camera, cameraPixels);
        Ray screenRay = camera.ScreenPointToRay(cameraPixels);
        RuntimePlacementMarker marker = FindMarkerAtScreenPoint(
            holder,
            new Vector2(world.x, world.y),
            screenRay);
        if (_verboseDiagnostics)
        {
            string targetTextureSize = camera.targetTexture == null
                ? "<none>"
                : camera.targetTexture.width + "x" + camera.targetTexture.height;
            ModLog.Debug(
                "[DIAG] PICK_HIT_TEST screenPosition=" + screenPoint
                + " cameraPixelPosition=" + cameraPixels
                + " screenSize=" + Screen.width + "x" + Screen.height
                + " targetTextureSize=" + targetTextureSize
                + " worldPosition=" + world
                + " camera=" + camera.name
                + " orthographic=" + camera.orthographic
                + " pixelRect=" + camera.pixelRect
                + " rayOrigin=" + screenRay.origin
                + " rayDirection=" + screenRay.direction
                + " result=" + (marker == null ? "<none>" : marker.PlacementId));
        }
        if (marker == null)
            return false;

        if (!RuntimeCreatorRegistry.TryGet(marker.Type, out ObjectCreator creator, out error))
            return false;

        handle = new UnityRuntimePlacementHandle(
            marker.PlacementId,
            marker.Type,
            marker.gameObject,
            creator);
        return true;
    }

    private static void MarkEditorProjection(GameObject obj, PlacementRecord placement)
    {
        RuntimePlacementMarker marker = obj.GetComponent<RuntimePlacementMarker>() ?? obj.AddComponent<RuntimePlacementMarker>();
        marker.PlacementId = placement.Id;
        marker.Type = placement.Type;
        marker.Source = "EditorMode";
    }

    private static RuntimePlacementMarker FindMarkerAtScreenPoint(
        Transform holder,
        Vector2 worldPoint,
        Ray screenRay)
    {
        Collider2D[] hits = Physics2D.OverlapPointAll(worldPoint);
        foreach (Collider2D hit in hits)
        {
            RuntimePlacementMarker marker = hit?.GetComponentInParent<RuntimePlacementMarker>();
            if (IsSelectableMarker(marker, holder))
                return marker;
        }

        RuntimePlacementMarker[] markers = holder.GetComponentsInChildren<RuntimePlacementMarker>(true);
        foreach (RuntimePlacementMarker marker in markers)
        {
            if (!IsSelectableMarker(marker, holder))
                continue;
            Renderer[] renderers = marker.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                continue;

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                bool rayHit = renderer.enabled
                    && renderer.gameObject.activeInHierarchy
                    && renderer.bounds.IntersectRay(screenRay);
                if (rayHit)
                    return marker;
            }
        }

        return null;
    }

    private static bool IsSelectableMarker(RuntimePlacementMarker marker, Transform holder)
    {
        return marker != null
            && marker.transform != null
            && marker.transform.IsChildOf(holder)
            && string.Equals(marker.Source, "EditorMode", StringComparison.OrdinalIgnoreCase);
    }

    private static Transform FindCurrentObjectHolder()
    {
        LevelInitializer initializer = UnityEngine.Object.FindObjectOfType<LevelInitializer>();
        return initializer?.transform;
    }
}

/// <summary>Unity projection for one Architect-owned Placement.</summary>
internal sealed class UnityRuntimePlacementHandle : IRuntimePlacementHandle
{
    private readonly string _type;
    private readonly ObjectCreator _creator;
    private readonly BaseModifier _baseModifier = new();
    private GameObject _object;

    internal UnityRuntimePlacementHandle(
        string placementId,
        string type,
        GameObject obj,
        ObjectCreator creator)
    {
        PlacementId = placementId;
        _type = type;
        _object = obj;
        _creator = creator;
    }

    public string PlacementId { get; }
    public bool IsOriginal { get { return false; } }
    public bool IsAlive { get { return _object != null; } }

    public bool TryApply(PlacementRecord placement, out string error)
    {
        error = null;
        if (!IsAlive)
        {
            error = "Runtime projection is no longer alive.";
            return false;
        }
        if (placement == null
            || !string.Equals(placement.Id, PlacementId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(placement.Type, _type, StringComparison.OrdinalIgnoreCase))
        {
            error = "Placement identity does not match the runtime projection.";
            return false;
        }

        try
        {
            ObjectData data = RuntimeObjectData.Create(placement);
            _baseModifier.Apply(_object, data);
            if (_creator != null && _creator.Modifier != null)
                _creator.Modifier.Apply(_object, data);
            return true;
        }
        catch (Exception exception)
        {
            error = "Runtime projection apply failed: " + exception.Message;
            return false;
        }
    }

    public bool TryDestroy(out string error)
    {
        error = null;
        if (!IsAlive)
        {
            _object = null;
            return true;
        }

        try
        {
            UnityEngine.Object.Destroy(_object);
            _object = null;
            return true;
        }
        catch (Exception exception)
        {
            error = "Runtime projection destroy failed: " + exception.Message;
            return false;
        }
    }
}

/// <summary>Maps the editor record to the Framework's readonly ObjectData DTO.</summary>
internal static class RuntimeObjectData
{
    internal static ObjectData Create(PlacementRecord placement)
    {
        JObject json = new()
        {
            ["type"] = placement.Type,
            ["id"] = placement.Id,
            ["position"] = Vector(placement.Transform.PositionX, placement.Transform.PositionY, placement.Transform.PositionZ),
            ["rotation"] = Vector(placement.Transform.RotationX, placement.Transform.RotationY, placement.Transform.RotationZ),
            ["scale"] = Vector(placement.Transform.ScaleX, placement.Transform.ScaleY, placement.Transform.ScaleZ),
            ["condition"] = placement.Condition,
            ["properties"] = new JArray(placement.Properties)
        };
        return json.ToObject<ObjectData>();
    }

    private static JObject Vector(float x, float y, float z)
    {
        return new JObject
        {
            ["x"] = x,
            ["y"] = y,
            ["z"] = z
        };
    }
}

/// <summary>Reflective access to the base Framework's internal creator registry.</summary>
internal static class RuntimeCreatorRegistry
{
    private const BindingFlags StaticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;

    internal static bool TryGet(string type, out ObjectCreator creator, out string error)
    {
        creator = null;
        error = null;
        if (string.IsNullOrEmpty(type))
        {
            error = "Creator type is required.";
            return false;
        }

        try
        {
            PropertyInfo property = typeof(LevelRegister).GetProperty("Creators", StaticNonPublic);
            if (property == null)
            {
                error = "Level Framework creator registry is unavailable.";
                return false;
            }

            if (property.GetValue(null, null) is not System.Collections.IEnumerable entries)
            {
                error = "Level Framework creator registry returned no entries.";
                return false;
            }

            foreach (object entry in entries)
            {
                Type entryType = entry.GetType();
                PropertyInfo key = entryType.GetProperty("Key");
                PropertyInfo value = entryType.GetProperty("Value");
                if (key == null || value == null
                    || !string.Equals(key.GetValue(entry, null) as string, type, StringComparison.OrdinalIgnoreCase))
                    continue;

                creator = value.GetValue(entry, null) as ObjectCreator;
                if (creator == null)
                {
                    error = "Creator '" + type + "' has no runtime definition.";
                    return false;
                }

                return true;
            }

            error = "Creator '" + type + "' is not registered by Level Framework.";
            return false;
        }
        catch (Exception exception)
        {
            error = "Level Framework creator lookup failed safely: " + exception.Message;
            return false;
        }
    }
}