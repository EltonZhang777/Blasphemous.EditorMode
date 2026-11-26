using Blasphemous.EditorMode.Editing;
using Blasphemous.Framework.Levels;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Blasphemous.EditorMode.Runtime;

/// <summary>Resolves Level Framework scene/path identities in the live room.</summary>
internal sealed class UnityOriginalObjectPathResolver : IOriginalObjectPathResolver
{
    public bool TryResolve(
        string frameworkLevelKey,
        string scene,
        string path,
        out OriginalObjectProjection projection,
        out string error)
    {
        projection = null;
        error = null;
        try
        {
            if (!UnityOriginalObjectPaths.TryResolveGameObject(
                frameworkLevelKey,
                scene,
                path,
                out GameObject found,
                out error))
                return false;

            projection = new OriginalObjectProjection(
                UnityOriginalObjectPaths.GetObjectKey(found),
                found.activeInHierarchy,
                new PlacementTransform(
                    found.transform.position.x,
                    found.transform.position.y,
                    found.transform.position.z,
                    found.transform.eulerAngles.x,
                    found.transform.eulerAngles.y,
                    found.transform.eulerAngles.z,
                    found.transform.localScale.x,
                    found.transform.localScale.y,
                    found.transform.localScale.z),
                found.name,
                found.activeSelf);
            return true;
        }
        catch (Exception exception)
        {
            projection = null;
            error = "Framework scene/path resolution failed: " + exception.Message;
            return false;
        }
    }
}

/// <summary>Shares Framework-compatible scene and path rules between runtime adapters.</summary>
internal static class UnityOriginalObjectPaths
{
    internal const string DecorationScene = "decoration";
    internal const string LayoutScene = "layout";
    internal const string LogicScene = "logic";

    internal static readonly string[] SceneTypes =
    [
        DecorationScene,
        LayoutScene,
        LogicScene
    ];

    internal static bool TryGetSceneName(
        string frameworkLevelKey,
        string scene,
        out string sceneName,
        out string error)
    {
        sceneName = null;
        error = null;
        if (string.IsNullOrEmpty(frameworkLevelKey))
        {
            error = "Framework level key is required.";
            return false;
        }

        string suffix;
        switch (scene)
        {
            case DecorationScene:
                suffix = "_DECO";
                break;
            case LayoutScene:
                suffix = "_LAYOUT";
                break;
            case LogicScene:
                suffix = "_LOGIC";
                break;
            default:
                error = "Scene must be decoration, layout, or logic.";
                return false;
        }

        sceneName = frameworkLevelKey + suffix;
        return true;
    }

    internal static bool TryGetScene(
        string frameworkLevelKey,
        string scene,
        out Scene unityScene,
        out string error)
    {
        unityScene = default;
        if (!TryGetSceneName(frameworkLevelKey, scene, out string sceneName, out error))
            return false;

        unityScene = SceneManager.GetSceneByName(sceneName);
        if (!unityScene.IsValid() || !unityScene.isLoaded)
        {
            error = "Target scene is not loaded: " + sceneName;
            return false;
        }

        return true;
    }

    internal static bool TryResolveGameObject(
        string frameworkLevelKey,
        string scene,
        string path,
        out GameObject gameObject,
        out string error)
    {
        gameObject = null;
        error = null;
        if (!TryGetScene(
            frameworkLevelKey,
            scene,
            out Scene unityScene,
            out error))
            return false;
        if (string.IsNullOrEmpty(path))
        {
            error = "Original Object path is empty.";
            return false;
        }

        GameObject found = unityScene.FindObject(path, false);
        if (found == null)
        {
            error = "No GameObject resolves from the Framework scene/path.";
            return false;
        }
        if (!TryIdentifyScene(
            found.scene,
            frameworkLevelKey,
            out string actualScene,
            out _)
            || !string.Equals(actualScene, scene, StringComparison.Ordinal))
        {
            error = "The resolved GameObject belongs to a different target scene.";
            return false;
        }

        gameObject = found;
        return true;
    }

    internal static bool TryIdentifyScene(
        Scene unityScene,
        string frameworkLevelKey,
        out string scene,
        out string error)
    {
        scene = null;
        error = null;
        if (!unityScene.IsValid() || !unityScene.isLoaded)
        {
            error = "GameObject does not belong to a loaded Unity scene.";
            return false;
        }

        foreach (string candidate in SceneTypes)
        {
            if (!TryGetSceneName(frameworkLevelKey, candidate, out string sceneName, out _))
                continue;
            if (string.Equals(unityScene.name, sceneName, StringComparison.Ordinal))
            {
                scene = candidate;
                return true;
            }
        }

        error = "GameObject is outside the current room's DECO, LAYOUT, and LOGIC scenes.";
        return false;
    }

    internal static bool TryGetAddress(
        GameObject gameObject,
        string frameworkLevelKey,
        out string scene,
        out string path,
        out string error)
    {
        scene = null;
        path = null;
        error = null;
        if (gameObject == null)
        {
            error = "GameObject is missing.";
            return false;
        }
        if (!TryIdentifyScene(gameObject.scene, frameworkLevelKey, out scene, out error))
            return false;

        Transform target = gameObject.transform;
        Transform root = target.root;
        if (root == null || root.parent != null)
        {
            error = "GameObject hierarchy has no scene root.";
            return false;
        }
        if (IsTemporaryRoot(root.name))
        {
            error = "Framework temporary roots beginning with '=' are not addressable.";
            return false;
        }
        if (!IsUsableRootName(root.name))
        {
            error = "Scene root name cannot be represented by a Framework path.";
            return false;
        }

        Scene unityScene = gameObject.scene;
        GameObject firstNamedRoot = null;
        foreach (GameObject candidate in unityScene.GetRootGameObjects())
        {
            if (candidate != null
                && string.Equals(candidate.name, root.name, StringComparison.Ordinal))
            {
                firstNamedRoot = candidate;
                break;
            }
        }
        if (firstNamedRoot == null
            || firstNamedRoot.transform != root)
        {
            error = "Another root object claims this Framework root name.";
            return false;
        }

        List<Transform> hierarchy = [];
        for (Transform current = target; current != null && current != root; current = current.parent)
            hierarchy.Add(current);
        hierarchy.Reverse();

        string frameworkPath = root.name;
        foreach (Transform current in hierarchy)
        {
            string segment = BuildChildSegment(current);
            if (segment == null)
            {
                path = null;
                error = "A child segment cannot be represented by a Framework path.";
                return false;
            }

            frameworkPath += "/" + segment;
        }

        GameObject resolved = unityScene.FindObject(frameworkPath, false);
        if (resolved == null || resolved != gameObject)
        {
            path = null;
            error = "The generated Framework path does not resolve back to the same GameObject.";
            return false;
        }

        path = frameworkPath;
        return true;
    }

    internal static string GetObjectKey(GameObject gameObject)
    {
        if (gameObject == null)
            return string.Empty;

        return gameObject.GetInstanceID().ToString(CultureInfo.InvariantCulture);
    }

    internal static bool IsTemporaryRoot(string name)
    {
        return !string.IsNullOrEmpty(name) && name[0] == '=';
    }

    private static bool IsUsableRootName(string name)
    {
        return !string.IsNullOrEmpty(name)
            && name.IndexOf('/') < 0;
    }

    private static string BuildChildSegment(Transform child)
    {
        string name = child.name;
        if (!string.IsNullOrEmpty(name)
            && name.IndexOf('/') < 0
            && !IsBracedSegment(name)
            && CountNamedSiblings(child.parent, name) == 1)
        {
            return name;
        }

        return "{" + child.GetSiblingIndex().ToString(CultureInfo.InvariantCulture) + "}";
    }

    private static int CountNamedSiblings(Transform parent, string name)
    {
        int count = 0;
        for (int index = 0; index < parent.childCount; index++)
        {
            if (string.Equals(parent.GetChild(index).name, name, StringComparison.Ordinal))
                count++;
        }

        return count;
    }

    private static bool IsBracedSegment(string value)
    {
        return value != null
            && value.Length >= 3
            && value[0] == '{'
            && value[value.Length - 1] == '}';
    }
}