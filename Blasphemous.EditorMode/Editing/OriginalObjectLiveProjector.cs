using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Framework;
using System;

namespace Blasphemous.EditorMode.Editing;

/// <summary>Editor-owned constants and helpers for Original Object Edits.</summary>
internal static class OriginalObjectEditor
{
    internal const string EmptyModificationType = "editor-mode-empty";
}

/// <summary>Recoverable runtime state captured for one Original Object.</summary>
internal sealed class OriginalObjectLiveState
{
    internal OriginalObjectLiveState(
        string objectKey,
        bool activeSelf,
        PlacementTransform transform,
        string name)
    {
        ObjectKey = objectKey ?? string.Empty;
        IsActiveSelf = activeSelf;
        Transform = transform ?? PlacementTransform.Default;
        Name = name ?? string.Empty;
    }

    internal string ObjectKey { get; }
    internal bool IsActiveSelf { get; }
    internal PlacementTransform Transform { get; }
    internal string Name { get; }
}

/// <summary>Applies and restores Original Object live projections.</summary>
internal interface IOriginalObjectLiveProjector
{
    bool TryApplyModification(
        string frameworkLevelKey,
        FrameworkObjectData modification,
        ApplyReport report,
        out OriginalObjectLiveState appliedState);

    bool TryApplyDeletion(
        string frameworkLevelKey,
        OriginalObjectIdentity identity,
        ApplyReport report,
        out OriginalObjectLiveState appliedState);

    bool TryRestore(
        string frameworkLevelKey,
        OriginalObjectIdentity identity,
        OriginalObjectLiveState state,
        ApplyReport report);
}