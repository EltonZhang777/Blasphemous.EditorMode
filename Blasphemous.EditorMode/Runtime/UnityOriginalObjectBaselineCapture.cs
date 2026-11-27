using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Editing;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Blasphemous.EditorMode.Runtime;

/// <summary>Captures the pre-mod DECO, LAYOUT, and LOGIC scene baseline.</summary>
internal sealed class UnityOriginalObjectBaselineCapture
{
    internal OriginalObjectBaseline Capture(string frameworkLevelKey)
    {
        ApplyReport report = new();
        if (string.IsNullOrEmpty(frameworkLevelKey))
        {
            report.Error(
                ReportCodes.OriginalObjectBaselineMissing,
                "$",
                "Cannot capture Original Objects without a Framework level key.");
            return new OriginalObjectBaseline(string.Empty, [], report);
        }

        List<OriginalObjectSnapshotEntry> entries = [];
        try
        {
            foreach (string sceneType in UnityOriginalObjectPaths.SceneTypes)
            {
                if (!UnityOriginalObjectPaths.TryGetScene(
                    frameworkLevelKey,
                    sceneType,
                    out Scene scene,
                    out string sceneError))
                {
                    report.Error(
                        ReportCodes.OriginalObjectBaselineMissing,
                        sceneType,
                        sceneError);
                    return new OriginalObjectBaseline(frameworkLevelKey, [], report);
                }

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root == null || UnityOriginalObjectPaths.IsTemporaryRoot(root.name))
                        continue;
                    CaptureHierarchy(root.transform, frameworkLevelKey, entries);
                }
            }
        }
        catch (Exception exception)
        {
            report.Error(
                ReportCodes.OriginalObjectBaselineMissing,
                frameworkLevelKey,
                "Pre-mod Original Object capture failed: " + exception.Message);
            return new OriginalObjectBaseline(frameworkLevelKey, [], report);
        }

        return new OriginalObjectBaseline(frameworkLevelKey, entries, report);
    }

    private static void CaptureHierarchy(
        Transform root,
        string frameworkLevelKey,
        List<OriginalObjectSnapshotEntry> entries)
    {
        CaptureObject(root.gameObject, frameworkLevelKey, entries);
        for (int index = 0; index < root.childCount; index++)
            CaptureHierarchy(root.GetChild(index), frameworkLevelKey, entries);
    }

    private static void CaptureObject(
        GameObject gameObject,
        string frameworkLevelKey,
        List<OriginalObjectSnapshotEntry> entries)
    {
        bool addressable = UnityOriginalObjectPaths.TryGetAddress(
            gameObject,
            frameworkLevelKey,
            out string scene,
            out string path,
            out string addressError);
        OriginalObjectProvenance provenance = ClassifyProvenance(gameObject.transform);
        entries.Add(new OriginalObjectSnapshotEntry(
            UnityOriginalObjectPaths.GetObjectKey(gameObject),
            scene,
            path,
            gameObject.activeInHierarchy,
            provenance,
            addressable,
            addressError,
            gameObject.activeSelf,
            new PlacementTransform(
                gameObject.transform.position.x,
                gameObject.transform.position.y,
                gameObject.transform.position.z,
                gameObject.transform.eulerAngles.x,
                gameObject.transform.eulerAngles.y,
                gameObject.transform.eulerAngles.z,
                gameObject.transform.localScale.x,
                gameObject.transform.localScale.y,
                gameObject.transform.localScale.z),
            gameObject.name));
    }

    private static OriginalObjectProvenance ClassifyProvenance(Transform transform)
    {
        for (Transform current = transform; current != null; current = current.parent)
        {
            RuntimePlacementMarker marker = current.GetComponent<RuntimePlacementMarker>();
            if (marker == null)
                continue;
            if (string.Equals(marker.Source, "Framework", StringComparison.OrdinalIgnoreCase))
                return OriginalObjectProvenance.LevelFramework;
            if (string.Equals(marker.Source, "EditorMode", StringComparison.OrdinalIgnoreCase))
                return OriginalObjectProvenance.EditorMode;
            return OriginalObjectProvenance.ThirdParty;
        }

        return OriginalObjectProvenance.Baseline;
    }
}

/// <summary>Collects all level objects under the pointer using physics and renderer bounds.</summary>
internal sealed class UnityOriginalObjectHitSource : IOriginalObjectHitSource
{
    private readonly string _frameworkLevelKey;
    private readonly Vector2 _screenPoint;

    internal UnityOriginalObjectHitSource(string frameworkLevelKey, Vector2 screenPoint)
    {
        _frameworkLevelKey = frameworkLevelKey ?? string.Empty;
        _screenPoint = screenPoint;
    }

    public bool TryFindAll(
        string frameworkLevelKey,
        out IList<OriginalObjectHit> hits,
        out string error)
    {
        hits = new OriginalObjectHit[0];
        error = null;
        if (string.IsNullOrEmpty(frameworkLevelKey)
            || !string.Equals(_frameworkLevelKey, frameworkLevelKey, StringComparison.Ordinal))
        {
            error = "Original Object hit source is scoped to a different room.";
            return false;
        }

        try
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                error = "Main camera is unavailable.";
                return false;
            }

            Vector2 cameraPixels = UnityScreenCoordinateMapper.ToCameraPixels(camera, _screenPoint);
            Vector3 world = UnityScreenCoordinateMapper.ToWorldPosition(camera, cameraPixels);
            Ray ray = camera.ScreenPointToRay(cameraPixels);
            List<GameObject> found = [];
            HashSet<int> objectKeys = [];

            Collider2D[] colliders = Physics2D.OverlapPointAll(new Vector2(world.x, world.y));
            foreach (Collider2D collider in colliders)
            {
                if (collider != null)
                    AddHit(collider.gameObject, found, objectKeys);
            }

            foreach (string sceneType in UnityOriginalObjectPaths.SceneTypes)
            {
                if (!UnityOriginalObjectPaths.TryGetScene(
                    frameworkLevelKey,
                    sceneType,
                    out Scene scene,
                    out string sceneError))
                {
                    error = sceneError;
                    return false;
                }

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root == null || UnityOriginalObjectPaths.IsTemporaryRoot(root.name))
                        continue;

                    Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                    foreach (Renderer renderer in renderers)
                    {
                        if (renderer == null
                            || !renderer.enabled
                            || !renderer.gameObject.activeInHierarchy)
                            continue;
                        if (renderer.bounds.IntersectRay(ray))
                            AddHit(renderer.gameObject, found, objectKeys);
                    }
                }
            }

            List<OriginalObjectHit> mapped = [];
            foreach (GameObject gameObject in found)
            {
                if (!UnityOriginalObjectPaths.TryGetAddress(
                    gameObject,
                    frameworkLevelKey,
                    out string scene,
                    out string path,
                    out string addressError))
                {
                    mapped.Add(new OriginalObjectHit(
                        UnityOriginalObjectPaths.GetObjectKey(gameObject),
                        scene,
                        null,
                        addressError));
                    continue;
                }

                mapped.Add(new OriginalObjectHit(
                    UnityOriginalObjectPaths.GetObjectKey(gameObject),
                    scene,
                    path));
            }

            hits = mapped;
            return true;
        }
        catch (Exception exception)
        {
            error = "Original Object pointer hit collection failed: " + exception.Message;
            return false;
        }
    }

    private static void AddHit(
        GameObject gameObject,
        List<GameObject> hits,
        HashSet<int> objectKeys)
    {
        if (gameObject == null || !objectKeys.Add(gameObject.GetInstanceID()))
            return;

        hits.Add(gameObject);
    }
}