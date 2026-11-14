using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blasphemous.EditorMode.Framework;

/// <summary>
/// Names and required fields of the base Level Framework JSON contract.
/// </summary>
internal static class LevelFrameworkContract
{
    internal const string AdditionsField = "additions";
    internal const string ModificationsField = "modifications";
    internal const string DeletionsField = "deletions";

    internal const string SceneField = "scene";
    internal const string PathField = "path";
    internal const string TypeField = "type";
    internal const string IdField = "id";
    internal const string PositionField = "position";
    internal const string RotationField = "rotation";
    internal const string ScaleField = "scale";
    internal const string ConditionField = "condition";
    internal const string PropertiesField = "properties";

    private static readonly ReadOnlyCollection<string> AdditionFields = ReadOnly(
        TypeField,
        IdField,
        PositionField,
        RotationField,
        ScaleField,
        ConditionField,
        PropertiesField);

    private static readonly ReadOnlyCollection<string> ModificationFields = ReadOnly(
        SceneField,
        PathField,
        TypeField,
        IdField,
        PositionField,
        RotationField,
        ScaleField,
        ConditionField,
        PropertiesField);

    private static readonly ReadOnlyCollection<string> DeletionFields = ReadOnly(
        SceneField,
        PathField);

    /// <summary>Gets the required fields for one Framework operation.</summary>
    internal static IEnumerable<string> RequiredFields(FrameworkOperation operation)
    {
        return operation switch
        {
            FrameworkOperation.Addition => AdditionFields,
            FrameworkOperation.Modification => ModificationFields,
            FrameworkOperation.Deletion => DeletionFields,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown Framework operation."),
        };
    }

    /// <summary>Returns whether a field is part of the canonical ObjectData model.</summary>
    internal static bool IsKnownObjectField(string field)
    {
        return field == SceneField
            || field == PathField
            || field == TypeField
            || field == IdField
            || field == PositionField
            || field == RotationField
            || field == ScaleField
            || field == ConditionField
            || field == PropertiesField;
    }

    private static ReadOnlyCollection<string> ReadOnly(params string[] fields)
    {
        return new ReadOnlyCollection<string>(fields);
    }
}

/// <summary>Describes how a Framework ObjectData entry is used.</summary>
internal enum FrameworkOperation
{
    Addition,
    Modification,
    Deletion
}
