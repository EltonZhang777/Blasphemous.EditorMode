using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blasphemous.EditorMode.Content;

/// <summary>Transform operations supported by a creator's base modifier.</summary>
[Flags]
internal enum TransformCapabilities
{
    None = 0,
    Position = 1,
    Rotation = 2,
    Scale = 4
}

/// <summary>Identifies where a creator is supplied.</summary>
internal enum CreatorOrigin
{
    BaseFramework,
    OptionalExtension,
    Architect
}

/// <summary>Stable editor metadata for one verified or proposed creator.</summary>
internal sealed class CreatorMetadata
{
    internal CreatorMetadata(
        string type,
        string displayName,
        string category,
        CreatorOrigin origin,
        string sourceScene,
        string sourcePath,
        TransformCapabilities transformCapabilities,
        IEnumerable<string> propertyKeys,
        bool isSupported = true)
    {
        if (IsBlank(type))
            throw new ArgumentException("Creator type is required.", nameof(type));
        if (IsBlank(displayName))
            throw new ArgumentException("Creator display name is required.", nameof(displayName));
        if (IsBlank(category))
            throw new ArgumentException("Creator category is required.", nameof(category));
        if (IsBlank(sourceScene))
            throw new ArgumentException("Creator source scene is required.", nameof(sourceScene));
        if (IsBlank(sourcePath))
            throw new ArgumentException("Creator source path is required.", nameof(sourcePath));

        Type = type;
        DisplayName = displayName;
        Category = category;
        Origin = origin;
        SourceScene = sourceScene;
        SourcePath = sourcePath;
        TransformCapabilities = transformCapabilities;
        PropertyKeys = new ReadOnlyCollection<string>(
            new List<string>(propertyKeys ?? new string[0]));
        IsSupported = isSupported;
    }

    internal string Type { get; }
    internal string DisplayName { get; }
    internal string Category { get; }
    internal CreatorOrigin Origin { get; }
    internal string SourceScene { get; }
    internal string SourcePath { get; }
    internal TransformCapabilities TransformCapabilities { get; }
    internal ReadOnlyCollection<string> PropertyKeys { get; }
    internal bool IsSupported { get; }

    private static bool IsBlank(string value)
    {
        return string.IsNullOrEmpty(value) || value.Trim().Length == 0;
    }
}

/// <summary>Default base Framework creator metadata used by the first catalog slice.</summary>
internal static class BaseCreatorCatalog
{
    internal const string BellIronType = "bell-iron";

    internal static readonly CreatorMetadata BellIron = new CreatorMetadata(
        BellIronType,
        "Iron Bell",
        "Traps",
        CreatorOrigin.BaseFramework,
        "D03Z02S06_LOGIC",
        "TRAPS/TRAP_SHOCK_CHAIN_REACTION",
        TransformCapabilities.Position | TransformCapabilities.Rotation | TransformCapabilities.Scale,
        new string[0]);

    private static readonly ReadOnlyCollection<CreatorMetadata> All =
        new ReadOnlyCollection<CreatorMetadata>(new[] { BellIron });

    internal static IEnumerable<CreatorMetadata> Entries
    {
        get { return All; }
    }
}
