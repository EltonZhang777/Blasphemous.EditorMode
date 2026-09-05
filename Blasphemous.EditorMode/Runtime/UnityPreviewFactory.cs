using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Editing;
using Blasphemous.Framework.Levels;
using Blasphemous.Framework.Levels.Modifiers;
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

        ObjectCreator creator;
        string error;
        if (!RuntimeCreatorRegistry.TryGet(metadata.Type, out creator, out error))
            throw new InvalidOperationException(error);
        if (creator.Loader == null || creator.Loader.Result == null)
            throw new InvalidOperationException(
                "Creator '" + metadata.Type + "' has no loaded source object.");

        Transform holder = holderProvider == null ? null : holderProvider();
        if (holder == null)
            throw new InvalidOperationException("Current LevelInitializer object holder is unavailable.");

        Vector3 position = positionProvider == null ? Vector3.zero : positionProvider();
        GameObject root = new GameObject("Editor Preview - " + metadata.Type);
        root.transform.SetParent(holder, false);
        root.SetActive(false);

        try
        {
            GameObject previewObject = UnityEngine.Object.Instantiate(
                creator.Loader.Result,
                root.transform);
            previewObject.name = metadata.DisplayName + " (Preview)";

            var placement = new PlacementRecord(
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
                new string[0],
                PlacementOwnership.ArchitectOwned);
            ObjectData data = RuntimeObjectData.Create(placement);
            var baseModifier = new BaseModifier();
            baseModifier.Apply(previewObject, data);
            if (creator.Modifier != null)
                creator.Modifier.Apply(previewObject, data);

            IsolateGameplay(previewObject);
            root.SetActive(true);
            return new PreviewInstance(metadata, () => UnityEngine.Object.Destroy(root));
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
            if (behaviour != null)
                behaviour.enabled = false;
        }

        foreach (Collider collider in previewObject.GetComponentsInChildren<Collider>(true))
        {
            if (collider != null)
                collider.enabled = false;
        }

        foreach (Collider2D collider in previewObject.GetComponentsInChildren<Collider2D>(true))
        {
            if (collider != null)
                collider.enabled = false;
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
            if (particles != null)
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

}
