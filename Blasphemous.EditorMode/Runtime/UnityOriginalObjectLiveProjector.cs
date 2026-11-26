using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Editing;
using Blasphemous.EditorMode.Framework;
using Blasphemous.Framework.Levels;
using Blasphemous.Framework.Levels.Loaders;
using Blasphemous.Framework.Levels.Modifiers;
using System;
using System.Collections;
using UnityEngine;

namespace Blasphemous.EditorMode.Runtime;

/// <summary>Loader placeholder for the reserved modification-only creator type.</summary>
internal sealed class EditorModeEmptyLoader : ILoader
{
    public GameObject Result { get { return null; } }

    public IEnumerator Apply()
    {
        yield break;
    }
}

/// <summary>No-op modifier that leaves the object's name and components untouched.</summary>
internal sealed class EditorModeEmptyFrameworkModifier :
    IModifier,
    IOriginalObjectModifier
{
    public void Apply(GameObject obj, ObjectData data)
    {
    }

    public void Apply(FrameworkObjectData modification)
    {
    }
}

/// <summary>Projects Original Object modifications onto live room GameObjects.</summary>
internal sealed class UnityOriginalObjectLiveProjector : IOriginalObjectLiveProjector
{
    public bool IsModifierAvailable(string type)
    {
        return RuntimeOriginalObjectModifierSource.IsModifierAvailable(type);
    }

    public bool TryApplyModification(
        string frameworkLevelKey,
        FrameworkObjectData modification,
        ApplyReport report,
        out OriginalObjectLiveState appliedState)
    {
        appliedState = null;
        if (report == null)
            throw new ArgumentNullException(nameof(report));
        if (modification == null)
        {
            report.Error(
                ReportCodes.OriginalObjectModificationInvalid,
                "$",
                "Original Object modification is required.");
            return false;
        }

        string location = modification.Scene + ":" + modification.Path;
        if (!TryResolve(
            frameworkLevelKey,
            modification.Scene,
            modification.Path,
            out GameObject target,
            report))
            return false;

        UnityOriginalObjectModifierTarget modifierTarget = new(target);
        RuntimeOriginalObjectModifierSource source = new(target);
        OriginalObjectLiveState before;
        try
        {
            before = modifierTarget.CaptureState();
        }
        catch (Exception exception)
        {
            report.Error(
                ReportCodes.OriginalObjectApplyFailed,
                location,
                "Original Object state could not be captured: " + exception.Message);
            return false;
        }

        if (!OriginalObjectModifierPipeline.TryApply(
            modification,
            source,
            modifierTarget,
            report))
            return false;

        try
        {
            appliedState = modifierTarget.CaptureState();
            return true;
        }
        catch (Exception exception)
        {
            report.Error(
                ReportCodes.OriginalObjectApplyFailed,
                location,
                "Original Object state could not be captured after modification: " + exception.Message);
            RestoreAfterFailedApply(before, modifierTarget, report, location);
            return false;
        }
    }

    public bool TryApplyDeletion(
        string frameworkLevelKey,
        OriginalObjectIdentity identity,
        OriginalObjectLiveState authoredState,
        ApplyReport report,
        out OriginalObjectLiveState appliedState)
    {
        appliedState = null;
        if (report == null)
            throw new ArgumentNullException(nameof(report));
        if (identity == null)
        {
            report.Error(
                ReportCodes.OriginalObjectIdentityInvalid,
                "$",
                "Original Object identity is required.");
            return false;
        }
        if (authoredState == null)
        {
            report.Error(
                ReportCodes.OriginalObjectDeletionInvalid,
                identity.DisplayText,
                "Original Object recoverable authored state is required for deletion.");
            return false;
        }
        if (!TryResolve(
            frameworkLevelKey,
            identity.Scene,
            identity.Path,
            out GameObject target,
            report))
            return false;

        UnityOriginalObjectModifierTarget modifierTarget = new(target);
        OriginalObjectLiveState before = null;
        try
        {
            before = modifierTarget.CaptureState();
            modifierTarget.RestoreState(new OriginalObjectLiveState(
                authoredState.ObjectKey,
                false,
                authoredState.Transform,
                authoredState.Name));
            appliedState = modifierTarget.CaptureState();
            return true;
        }
        catch (Exception exception)
        {
            report.Error(
                ReportCodes.OriginalObjectApplyFailed,
                identity.DisplayText,
                "Original Object deletion preview failed: " + exception.Message);
            if (before != null)
            {
                try
                {
                    modifierTarget.RestoreState(before);
                }
                catch (Exception restoreException)
                {
                    report.Error(
                        ReportCodes.OriginalObjectLiveRestoreFailed,
                        identity.DisplayText,
                        "The previous live state could not be restored: " + restoreException.Message);
                }
            }
            return false;
        }
    }

    private static void RestoreAfterFailedApply(
        OriginalObjectLiveState state,
        IOriginalObjectModifierTarget target,
        ApplyReport report,
        string location)
    {
        try
        {
            target.RestoreState(state);
        }
        catch (Exception exception)
        {
            report.Error(
                ReportCodes.OriginalObjectLiveRestoreFailed,
                location,
                "The previous live state could not be restored: " + exception.Message);
        }
    }

    public bool TryRestore(
        string frameworkLevelKey,
        OriginalObjectIdentity identity,
        OriginalObjectLiveState state,
        ApplyReport report)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));
        if (identity == null || state == null)
        {
            report.Error(
                ReportCodes.OriginalObjectLiveRestoreFailed,
                identity == null ? "$" : identity.DisplayText,
                "Original Object identity and recoverable live state are required.");
            return false;
        }
        if (!TryResolve(
            frameworkLevelKey,
            identity.Scene,
            identity.Path,
            out GameObject target,
            report))
            return false;

        try
        {
            new UnityOriginalObjectModifierTarget(target).RestoreState(state);
            return true;
        }
        catch (Exception exception)
        {
            report.Error(
                ReportCodes.OriginalObjectLiveRestoreFailed,
                identity.DisplayText,
                "Original Object live state could not be restored: " + exception.Message);
            return false;
        }
    }

    private static bool TryResolve(
        string frameworkLevelKey,
        string scene,
        string path,
        out GameObject target,
        ApplyReport report)
    {
        target = null;
        string error;
        try
        {
            if (UnityOriginalObjectPaths.TryResolveGameObject(
                frameworkLevelKey,
                scene,
                path,
                out target,
                out error))
                return true;
        }
        catch (Exception exception)
        {
            error = "Framework scene/path resolution failed: " + exception.Message;
        }

        report.Error(
            ReportCodes.OriginalObjectApplyFailed,
            scene + ":" + path,
            error ?? "Original Object target could not be resolved.");
        return false;
    }
}

internal sealed class RuntimeOriginalObjectModifierSource : IOriginalObjectModifierSource
{
    private readonly GameObject _target;

    internal RuntimeOriginalObjectModifierSource(GameObject target)
    {
        _target = target;
    }

    internal static bool IsModifierAvailable(string type)
    {
        if (!RuntimeCreatorRegistry.TryGet(type, out ObjectCreator creator, out _))
            return false;
        if (creator == null || creator.Modifier == null)
            return false;

        return !string.Equals(
                type,
                OriginalObjectEditor.EmptyModificationType,
                StringComparison.Ordinal)
            || creator.Modifier is IOriginalObjectModifier;
    }

    public bool TryCreate(
        string type,
        out IOriginalObjectModifier modifier,
        out string error)
    {
        modifier = null;
        error = null;
        if (!RuntimeCreatorRegistry.TryGet(type, out ObjectCreator creator, out error))
            return false;
        if (creator == null || creator.Modifier == null)
        {
            error = "Creator '" + type + "' has no modifier.";
            return false;
        }
        if (string.Equals(
            type,
            OriginalObjectEditor.EmptyModificationType,
            StringComparison.Ordinal))
        {
            modifier = creator.Modifier as IOriginalObjectModifier;
            if (modifier == null)
            {
                error = "Reserved modifier '" + type + "' is not the Editor Mode empty modifier.";
                return false;
            }
            return true;
        }

        modifier = new UnityFrameworkOriginalObjectModifier(_target, creator.Modifier);
        return true;
    }
}

internal sealed class UnityFrameworkOriginalObjectModifier : IOriginalObjectModifier
{
    private readonly GameObject _target;
    private readonly IModifier _modifier;

    internal UnityFrameworkOriginalObjectModifier(
        GameObject target,
        IModifier modifier)
    {
        _target = target;
        _modifier = modifier;
    }

    public void Apply(FrameworkObjectData modification)
    {
        _modifier.Apply(_target, RuntimeObjectData.Create(modification));
    }
}

internal sealed class UnityOriginalObjectModifierTarget : IOriginalObjectModifierTarget
{
    private readonly GameObject _target;
    private readonly BaseModifier _baseModifier = new();

    internal UnityOriginalObjectModifierTarget(GameObject target)
    {
        _target = target;
    }

    public OriginalObjectLiveState CaptureState()
    {
        return new OriginalObjectLiveState(
            UnityOriginalObjectPaths.GetObjectKey(_target),
            _target.activeSelf,
            new PlacementTransform(
                _target.transform.position.x,
                _target.transform.position.y,
                _target.transform.position.z,
                _target.transform.eulerAngles.x,
                _target.transform.eulerAngles.y,
                _target.transform.eulerAngles.z,
                _target.transform.localScale.x,
                _target.transform.localScale.y,
                _target.transform.localScale.z),
            _target.name);
    }

    public void ApplyBase(FrameworkObjectData modification)
    {
        _baseModifier.Apply(_target, RuntimeObjectData.Create(modification));
    }

    public void ApplyType(
        IOriginalObjectModifier modifier,
        FrameworkObjectData modification)
    {
        modifier.Apply(modification);
    }

    public void RestoreState(OriginalObjectLiveState state)
    {
        _target.name = state.Name ?? string.Empty;
        _target.transform.position = new Vector3(
            state.Transform.PositionX,
            state.Transform.PositionY,
            state.Transform.PositionZ);
        _target.transform.eulerAngles = new Vector3(
            state.Transform.RotationX,
            state.Transform.RotationY,
            state.Transform.RotationZ);
        _target.transform.localScale = new Vector3(
            state.Transform.ScaleX,
            state.Transform.ScaleY,
            state.Transform.ScaleZ);
        _target.SetActive(state.IsActiveSelf);
    }
}