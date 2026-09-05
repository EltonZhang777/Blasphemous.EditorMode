using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

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

/// <summary>Value types accepted by a creator property inspector.</summary>
internal enum CreatorPropertyType
{
    String,
    Integer,
    Float,
    Boolean,
    Enum
}

/// <summary>Validated property metadata owned by one creator.</summary>
internal sealed class CreatorPropertyDefinition
{
    internal CreatorPropertyDefinition(
        string key,
        CreatorPropertyType type,
        IEnumerable<string> allowedValues = null)
    {
        if (string.IsNullOrEmpty(key) || key.Trim().Length == 0)
            throw new ArgumentException("Property key is required.", nameof(key));
        var values = new List<string>(allowedValues ?? new string[0]);
        if (type == CreatorPropertyType.Enum && values.Count == 0)
            throw new ArgumentException("Enum properties require allowed values.", nameof(allowedValues));

        Key = key;
        Type = type;
        AllowedValues = new ReadOnlyCollection<string>(values);
    }

    internal string Key { get; }
    internal CreatorPropertyType Type { get; }
    internal ReadOnlyCollection<string> AllowedValues { get; }

    internal bool TrySerialize(string rawValue, out string serialized, out string error)
    {
        serialized = null;
        error = null;
        if (string.IsNullOrEmpty(rawValue) || rawValue.Trim().Length == 0)
        {
            error = "Property value cannot be blank.";
            return false;
        }

        switch (Type)
        {
            case CreatorPropertyType.String:
                serialized = Key + "=" + rawValue;
                return true;
            case CreatorPropertyType.Integer:
                int integer;
                if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer))
                {
                    error = "Property value must be an integer.";
                    return false;
                }

                serialized = Key + "=" + integer.ToString(CultureInfo.InvariantCulture);
                return true;
            case CreatorPropertyType.Float:
                float single;
                if (!float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out single)
                    || float.IsNaN(single)
                    || float.IsInfinity(single))
                {
                    error = "Property value must be a finite number.";
                    return false;
                }

                serialized = Key + "=" + single.ToString("R", CultureInfo.InvariantCulture);
                return true;
            case CreatorPropertyType.Boolean:
                bool boolean;
                if (!bool.TryParse(rawValue, out boolean))
                {
                    error = "Property value must be true or false.";
                    return false;
                }

                serialized = Key + "=" + boolean.ToString().ToLowerInvariant();
                return true;
            case CreatorPropertyType.Enum:
                foreach (string allowedValue in AllowedValues)
                {
                    if (string.Equals(allowedValue, rawValue, StringComparison.OrdinalIgnoreCase))
                    {
                        serialized = Key + "=" + allowedValue;
                        return true;
                    }
                }

                error = "Property value is not an allowed enum value.";
                return false;
            default:
                error = "Property type is unsupported.";
                return false;
        }
    }

    internal bool IsValidSerialized(string serialized, out string error)
    {
        error = null;
        if (string.Equals(serialized, Key, StringComparison.OrdinalIgnoreCase))
            return true;

        string prefix = Key + "=";
        if (serialized == null || !serialized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            error = "Property key is not supported by this creator.";
            return false;
        }

        string canonical;
        return TrySerialize(serialized.Substring(prefix.Length), out canonical, out error);
    }
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
        : this(
            type,
            displayName,
            category,
            origin,
            sourceScene,
            sourcePath,
            transformCapabilities,
            CreatePropertyDefinitions(propertyKeys),
            isSupported)
    {
    }

    internal CreatorMetadata(
        string type,
        string displayName,
        string category,
        CreatorOrigin origin,
        string sourceScene,
        string sourcePath,
        TransformCapabilities transformCapabilities,
        IEnumerable<CreatorPropertyDefinition> propertyDefinitions,
        bool isSupported)
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
        PropertyDefinitions = new ReadOnlyCollection<CreatorPropertyDefinition>(
            new List<CreatorPropertyDefinition>(propertyDefinitions ?? new CreatorPropertyDefinition[0]));
        PropertyKeys = new ReadOnlyCollection<string>(
            GetPropertyKeys(PropertyDefinitions));
        IsSupported = isSupported;
    }

    internal string Type { get; }
    internal string DisplayName { get; }
    internal string Category { get; }
    internal CreatorOrigin Origin { get; }
    internal string SourceScene { get; }
    internal string SourcePath { get; }
    internal TransformCapabilities TransformCapabilities { get; }
    internal ReadOnlyCollection<CreatorPropertyDefinition> PropertyDefinitions { get; }
    internal ReadOnlyCollection<string> PropertyKeys { get; }
    internal bool IsSupported { get; }

    internal bool TryGetProperty(string key, out CreatorPropertyDefinition definition)
    {
        definition = null;
        foreach (CreatorPropertyDefinition candidate in PropertyDefinitions)
        {
            if (string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                definition = candidate;
                return true;
            }
        }

        return false;
    }

    internal bool TryValidateProperty(string serialized, out string error)
    {
        error = null;
        foreach (CreatorPropertyDefinition definition in PropertyDefinitions)
        {
            if (definition.IsValidSerialized(serialized, out error))
                return true;
        }

        error = error ?? "Property key is not supported by this creator.";
        return false;
    }

    private static IEnumerable<CreatorPropertyDefinition> CreatePropertyDefinitions(
        IEnumerable<string> propertyKeys)
    {
        foreach (string key in propertyKeys ?? new string[0])
            yield return new CreatorPropertyDefinition(key, CreatorPropertyType.String);
    }

    private static List<string> GetPropertyKeys(
        IEnumerable<CreatorPropertyDefinition> definitions)
    {
        var keys = new List<string>();
        foreach (CreatorPropertyDefinition definition in definitions)
            keys.Add(definition.Key);
        return keys;
    }

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
