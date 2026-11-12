using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Editing;
using Blasphemous.Framework.Levels;
using Blasphemous.Framework.Levels.Modifiers;
using Blasphemous.ModdingAPI;
using System;
using UnityEngine;

namespace Blasphemous.EditorMode.Runtime;

/// <summary>Creates disposable previews without adding them to the edit session.</summary>
internal static class UnityPreviewFactory
{
    internal static PreviewInstance Create(
        CreatorMetadata metadata,
        Func<Transform> holderProvider,
        Func<Vector3> positionProvider)
    {
        if (metadata == null)
            throw new ArgumentNullException(nameof(metadata));

        if (!RuntimeCreatorRegistry.TryGet(metadata.Type, out ObjectCreator creator, out string error))
            throw new InvalidOperationException(error);
        if (creator.Loader == null || creator.Loader.Result == null)
            throw new InvalidOperationException(
                "Creator '" + metadata.Type + "' has no loaded source object.");

        Transform holder = (holderProvider == null ? null : holderProvider()) ?? throw new InvalidOperationException("Current LevelInitializer object holder is unavailable.");
        Vector3 position = positionProvider == null ? Vector3.zero : positionProvider();
        GameObject root = new("Editor Preview - " + metadata.Type);
        root.transform.SetParent(holder, false);
        root.SetActive(false);

        try
        {
            GameObject previewObject = UnityEngine.Object.Instantiate(
                creator.Loader.Result,
                root.transform);
            previewObject.name = metadata.DisplayName + " (Preview)";

            PlacementRecord placement = new(
                "preview-" + metadata.Type,
                metadata.Type,
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
                PlacementOwnership.ArchitectOwned);
            ObjectData data = RuntimeObjectData.Create(placement);
            BaseModifier baseModifier = new();
            baseModifier.Apply(previewObject, data);
            creator.Modifier?.Apply(previewObject, data);

            IsolateGameplay(previewObject);
            root.SetActive(true);
            Camera camera = Camera.main;
            foreach (Renderer renderer in previewObject.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;

                bool cameraIncludesLayer = camera != null
                    && (camera.cullingMask & (1 << renderer.gameObject.layer)) != 0;
                ModLog.Debug(
                    "[DIAG] PREVIEW_RENDERER type=" + metadata.Type
                    + " renderer=" + renderer.name
                    + " active=" + renderer.gameObject.activeInHierarchy
                    + " enabled=" + renderer.enabled
                    + " layer=" + renderer.gameObject.layer
                    + " cameraIncludesLayer=" + cameraIncludesLayer
                    + " camera=" + (camera == null ? "<none>" : camera.name)
                    + " screenPosition=" + (camera == null ? "<none>" : camera.WorldToScreenPoint(renderer.bounds.center).ToString())
                    + " worldPosition=" + renderer.bounds.center
                    + " bounds=" + renderer.bounds);
            }
            return new PreviewInstance(
                metadata,
                () => UnityEngine.Object.Destroy(root),
                () =>
                {
                    if (previewObject != null && positionProvider != null)
                        previewObject.transform.position = positionProvider();
                });
        }
        catch
        {
            UnityEngine.Object.Destroy(root);
            throw;
        }
    }

    private static void IsolateGameplay(GameObject previewObject)
    {
        foreach (Behaviour behaviour in previewObject.GetComponentsInChildren<Behaviour>(true))
        {
            behaviour?.enabled = false;
        }

        Renderer[] renderers = previewObject.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            throw new InvalidOperationException("Creator preview has no Renderer: " + previewObject.name);
        }

        foreach (Renderer renderer in renderers)
        {
            if (renderer != null)
            {
                renderer.enabled = true;
                // Unity ignores enabled Renderers beneath inactive GameObjects.
                for (Transform node = renderer.transform; node != null; node = node.parent)
                {
                    node.gameObject.SetActive(true);
                    if (node == previewObject.transform)
                    {
                        break;
                    }
                }
            }
        }

        foreach (Collider collider in previewObject.GetComponentsInChildren<Collider>(true))
        {
            collider?.enabled = false;
        }

        foreach (Collider2D collider in previewObject.GetComponentsInChildren<Collider2D>(true))
        {
            collider?.enabled = false;
        }

        foreach (Rigidbody body in previewObject.GetComponentsInChildren<Rigidbody>(true))
        {
            if (body != null)
            {
                body.isKinematic = true;
                body.detectCollisions = false;
            }
        }

        foreach (Rigidbody2D body in previewObject.GetComponentsInChildren<Rigidbody2D>(true))
        {
            if (body != null)
            {
                body.bodyType = RigidbodyType2D.Kinematic;
                body.simulated = false;
            }
        }

        foreach (ParticleSystem particles in previewObject.GetComponentsInChildren<ParticleSystem>(true))
        {
            particles?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

}