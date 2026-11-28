using Framework.Managers;
using Gameplay.GameControllers.Environment;
using Tools.Level.Layout;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Blasphemous.EditorMode.Lifecycle;

internal sealed class RoomRuntimeContextAdapter
{
    internal RoomRuntimeContextRecord ObserveAndRebindAtPreload(string level)
    {
        return Observe("OnLevelPreloaded", level, true);
    }

    internal RoomRuntimeContextRecord ObserveAfterLoad(string level)
    {
        return Observe("OnLevelLoaded", level, false);
    }

    private RoomRuntimeContextRecord Observe(
        string callbackStage,
        string level,
        bool allowRebind)
    {
        LogicManager logic = Core.Logic;
        Scene activeScene = SceneManager.GetActiveScene();
        string activeSceneName = activeScene.IsValid() ? activeScene.name : string.Empty;
        LevelInitializer initializer = FindInitializer(activeScene);
        RoomIdentity identity = null;
        RoomIdentity.TryCreate(level, activeSceneName, out identity, out _);

        bool storeComponentPresent = initializer != null
            && initializer.GetComponentInChildren<LevelEffectsStore>() != null;
        bool contextReferencesInitializer = logic != null
            && initializer != null
            && logic.CurrentLevelConfig == initializer;
        RoomRuntimeContextEvidence evidence = new()
        {
            Level = level,
            ActiveScene = activeSceneName,
            LogicAvailable = logic != null,
            SceneValid = activeScene.IsValid(),
            SceneLoaded = activeScene.isLoaded,
            Identity = identity,
            InitializerPresent = initializer != null,
            InitializerActiveAndEnabled = initializer != null && initializer.isActiveAndEnabled,
            StoreComponentPresent = storeComponentPresent,
            StoreValueInitialized = initializer != null && initializer.LevelEffectsStore != null,
            ContextReferencesInitializer = contextReferencesInitializer
        };

        RoomRuntimeContextDecision decision = RoomRuntimeContextReadiness.Evaluate(evidence);
        string contextBefore = Describe(logic == null ? null : logic.CurrentLevelConfig);
        string contextAfter = contextBefore;
        RoomRuntimeContextRecordKind kind = decision.State == RoomRuntimeContextState.Failed
            ? RoomRuntimeContextRecordKind.Failed
            : RoomRuntimeContextRecordKind.Checked;

        if (allowRebind && decision.Action == RoomRuntimeContextAction.Rebind)
        {
            logic.SetCurrentLevelConfig(initializer);
            contextAfter = Describe(initializer);
            kind = RoomRuntimeContextRecordKind.Rebound;
        }

        return RoomRuntimeContextRecord.Create(
            kind,
            callbackStage,
            evidence,
            decision,
            contextBefore,
            contextAfter,
            Time.frameCount);
    }

    private static LevelInitializer FindInitializer(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return null;
        }

        LevelInitializer selected = null;
        bool selectedActive = false;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (selectedActive)
            {
                break;
            }

            foreach (LevelInitializer candidate in root.GetComponentsInChildren<LevelInitializer>(true))
            {
                if (selectedActive)
                {
                    break;
                }

                if (candidate.isActiveAndEnabled)
                {
                    selected = candidate;
                    selectedActive = true;
                }
                else if (selected == null)
                {
                    selected = candidate;
                }
            }
        }

        return selected;
    }

    private static string Describe(LevelInitializer initializer)
    {
        if (initializer == null)
        {
            return "<none>";
        }

        string objectName = string.IsNullOrEmpty(initializer.name)
            ? "<unnamed>"
            : initializer.name;
        return objectName + "#" + initializer.GetInstanceID();
    }
}