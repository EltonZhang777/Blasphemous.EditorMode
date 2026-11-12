using System;

namespace Blasphemous.EditorMode.Lifecycle;

/// <summary>Stable identity for the current Room Overlay.</summary>
internal sealed class RoomIdentity : IEquatable<RoomIdentity>
{
    private static readonly string[] SupportedSceneSuffixes =
    [
        "_LOGIC",
        "_DECO",
        "_LAYOUT",
        "_AUDIO",
        "_MAIN"
    ];

    private RoomIdentity(
        string frameworkLevelKey,
        string targetSceneSuffix,
        string activeSceneName)
    {
        FrameworkLevelKey = frameworkLevelKey;
        TargetSceneSuffix = targetSceneSuffix;
        ActiveSceneName = activeSceneName;
        ExportFileKey = frameworkLevelKey;
    }

    internal string FrameworkLevelKey { get; }
    internal string TargetSceneSuffix { get; }
    internal string ActiveSceneName { get; }
    internal string ExportFileKey { get; }

    internal static bool TryCreate(
        string frameworkLevelKey,
        string activeSceneName,
        out RoomIdentity identity,
        out string error)
    {
        identity = null;
        error = null;

        if (IsBlank(frameworkLevelKey))
        {
            error = "Framework level key is required.";
            return false;
        }

        if (string.Equals(frameworkLevelKey, "MainMenu", StringComparison.OrdinalIgnoreCase))
        {
            error = "Main menu is not a Room Overlay.";
            return false;
        }

        if (IsBlank(activeSceneName))
        {
            error = "Active Unity scene name is required.";
            return false;
        }

        if (!activeSceneName.StartsWith(frameworkLevelKey, StringComparison.OrdinalIgnoreCase))
        {
            error = string.Format(
                "Active scene '{0}' does not belong to level '{1}'.",
                activeSceneName,
                frameworkLevelKey);
            return false;
        }

        string suffix = activeSceneName.Substring(frameworkLevelKey.Length).ToUpperInvariant();
        if (Array.IndexOf(SupportedSceneSuffixes, suffix) < 0)
        {
            error = string.Format("Scene suffix '{0}' is not supported.", suffix);
            return false;
        }

        identity = new RoomIdentity(frameworkLevelKey, suffix, activeSceneName);
        return true;
    }

    public bool Equals(RoomIdentity other)
    {
        return other != null
            && string.Equals(FrameworkLevelKey, other.FrameworkLevelKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(TargetSceneSuffix, other.TargetSceneSuffix, StringComparison.OrdinalIgnoreCase)
            && string.Equals(ActiveSceneName, other.ActiveSceneName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(ExportFileKey, other.ExportFileKey, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as RoomIdentity);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(FrameworkLevelKey);
            hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(TargetSceneSuffix);
            hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(ActiveSceneName);
            return (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(ExportFileKey);
        }
    }

    private static bool IsBlank(string value)
    {
        return string.IsNullOrEmpty(value) || value.Trim().Length == 0;
    }
}