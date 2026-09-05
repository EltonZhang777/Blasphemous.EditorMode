using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace Blasphemous.EditorMode.Framework;

internal enum FrameworkDiagnosticSeverity
{
    Warning,
    Error
}

internal sealed class FrameworkDiagnostic
{
    internal FrameworkDiagnostic(FrameworkDiagnosticSeverity severity, string location, string message)
    {
        Severity = severity;
        Location = location;
        Message = message;
    }

    internal FrameworkDiagnosticSeverity Severity { get; }
    internal string Location { get; }
    internal string Message { get; }
}

internal sealed class FrameworkVector
{
    internal FrameworkVector(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    internal float X { get; }
    internal float Y { get; }
    internal float Z { get; }

    internal static FrameworkVector Zero { get { return new FrameworkVector(0f, 0f, 0f); } }
    internal static FrameworkVector One { get { return new FrameworkVector(1f, 1f, 1f); } }
}

internal sealed class FrameworkObjectData
{
    internal FrameworkObjectData(
        string scene,
        string path,
        string type,
        string id,
        FrameworkVector position,
        FrameworkVector rotation,
        FrameworkVector scale,
        string condition,
        IEnumerable<string> properties)
    {
        Scene = scene;
        Path = path;
        Type = type;
        Id = id;
        Position = position ?? FrameworkVector.Zero;
        Rotation = rotation ?? FrameworkVector.Zero;
        Scale = scale ?? FrameworkVector.One;
        Condition = condition ?? string.Empty;
        Properties = new ReadOnlyCollection<string>(
            new List<string>(properties ?? new string[0]));
    }

    internal string Scene { get; }
    internal string Path { get; }
    internal string Type { get; }
    internal string Id { get; }
    internal FrameworkVector Position { get; }
    internal FrameworkVector Rotation { get; }
    internal FrameworkVector Scale { get; }
    internal string Condition { get; }
    internal ReadOnlyCollection<string> Properties { get; }
}

internal sealed class FrameworkLevelEdit
{
    internal FrameworkLevelEdit(
        IEnumerable<FrameworkObjectData> additions,
        IEnumerable<FrameworkObjectData> modifications,
        IEnumerable<FrameworkObjectData> deletions)
    {
        Additions = ReadOnly(additions);
        Modifications = ReadOnly(modifications);
        Deletions = ReadOnly(deletions);
    }

    internal ReadOnlyCollection<FrameworkObjectData> Additions { get; }
    internal ReadOnlyCollection<FrameworkObjectData> Modifications { get; }
    internal ReadOnlyCollection<FrameworkObjectData> Deletions { get; }

    private static ReadOnlyCollection<FrameworkObjectData> ReadOnly(
        IEnumerable<FrameworkObjectData> values)
    {
        return new ReadOnlyCollection<FrameworkObjectData>(
            new List<FrameworkObjectData>(values ?? new FrameworkObjectData[0]));
    }
}

internal sealed class FrameworkCodecResult
{
    internal FrameworkCodecResult(
        FrameworkLevelEdit document,
        string canonicalJson,
        IEnumerable<FrameworkDiagnostic> diagnostics)
    {
        Document = document;
        CanonicalJson = canonicalJson;
        Diagnostics = new ReadOnlyCollection<FrameworkDiagnostic>(
            new List<FrameworkDiagnostic>(diagnostics ?? new FrameworkDiagnostic[0]));
    }

    internal FrameworkLevelEdit Document { get; }
    internal string CanonicalJson { get; }
    internal ReadOnlyCollection<FrameworkDiagnostic> Diagnostics { get; }

    internal bool IsValid
    {
        get
        {
            foreach (FrameworkDiagnostic diagnostic in Diagnostics)
            {
                if (diagnostic.Severity == FrameworkDiagnosticSeverity.Error)
                    return false;
            }

            return true;
        }
    }
}

internal sealed class FrameworkFileResult
{
    internal FrameworkFileResult(
        string path,
        string originalText,
        FrameworkLevelEdit document,
        IEnumerable<FrameworkDiagnostic> diagnostics)
    {
        Path = path;
        OriginalText = originalText;
        Document = document;
        Diagnostics = new ReadOnlyCollection<FrameworkDiagnostic>(
            new List<FrameworkDiagnostic>(diagnostics ?? new FrameworkDiagnostic[0]));
    }

    internal string Path { get; }
    internal string OriginalText { get; }
    internal FrameworkLevelEdit Document { get; }
    internal ReadOnlyCollection<FrameworkDiagnostic> Diagnostics { get; }

    internal bool IsValid
    {
        get
        {
            foreach (FrameworkDiagnostic diagnostic in Diagnostics)
            {
                if (diagnostic.Severity == FrameworkDiagnosticSeverity.Error)
                    return false;
            }

            return Document != null;
        }
    }
}

internal sealed class FrameworkExportResult
{
    internal FrameworkExportResult(
        string path,
        string backupPath,
        IEnumerable<FrameworkDiagnostic> diagnostics)
    {
        Path = path;
        BackupPath = backupPath;
        Diagnostics = new ReadOnlyCollection<FrameworkDiagnostic>(
            new List<FrameworkDiagnostic>(diagnostics ?? new FrameworkDiagnostic[0]));
    }

    internal string Path { get; }
    internal string BackupPath { get; }
    internal ReadOnlyCollection<FrameworkDiagnostic> Diagnostics { get; }

    internal bool Succeeded
    {
        get
        {
            foreach (FrameworkDiagnostic diagnostic in Diagnostics)
            {
                if (diagnostic.Severity == FrameworkDiagnosticSeverity.Error)
                    return false;
            }

            return true;
        }
    }
}

/// <summary>Owns the boundary between editor data and Framework canonical JSON.</summary>
internal static class LevelFrameworkAdapter
{
    private static readonly FrameworkVector DefaultPosition = FrameworkVector.Zero;
    private static readonly FrameworkVector DefaultRotation = FrameworkVector.Zero;
    private static readonly FrameworkVector DefaultScale = FrameworkVector.One;

    internal static FrameworkCodecResult Parse(string json)
    {
        var diagnostics = new List<FrameworkDiagnostic>();
        if (string.IsNullOrEmpty(json) || json.Trim().Length == 0)
        {
            AddError(diagnostics, "$", "JSON document is empty.");
            return EmptyResult(diagnostics);
        }

        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (Exception exception)
        {
            AddError(diagnostics, "$", "JSON syntax is invalid: " + exception.Message);
            return EmptyResult(diagnostics);
        }

        foreach (JProperty property in root.Properties())
        {
            if (property.Name != LevelFrameworkContract.AdditionsField
                && property.Name != LevelFrameworkContract.ModificationsField
                && property.Name != LevelFrameworkContract.DeletionsField)
            {
                AddWarning(
                    diagnostics,
                    property.Name,
                    "Unknown top-level field ignored; editor metadata does not enter canonical JSON.");
            }
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var additions = ReadArray(
            root,
            LevelFrameworkContract.AdditionsField,
            FrameworkOperation.Addition,
            ids,
            diagnostics);
        var modifications = ReadArray(
            root,
            LevelFrameworkContract.ModificationsField,
            FrameworkOperation.Modification,
            ids,
            diagnostics);
        var deletions = ReadArray(
            root,
            LevelFrameworkContract.DeletionsField,
            FrameworkOperation.Deletion,
            ids,
            diagnostics);

        return new FrameworkCodecResult(
            new FrameworkLevelEdit(additions, modifications, deletions),
            null,
            diagnostics);
    }

    internal static FrameworkCodecResult Serialize(FrameworkLevelEdit document)
    {
        var diagnostics = new List<FrameworkDiagnostic>();
        if (document == null)
        {
            AddError(diagnostics, "$", "Level Export document is required.");
            return new FrameworkCodecResult(null, null, diagnostics);
        }

        string json;
        try
        {
            json = BuildJson(document).ToString(Formatting.Indented);
        }
        catch (Exception exception)
        {
            AddError(diagnostics, "$", "Canonical JSON serialization failed: " + exception.Message);
            return new FrameworkCodecResult(document, null, diagnostics);
        }

        FrameworkCodecResult validation = Parse(json);
        return new FrameworkCodecResult(document, json, validation.Diagnostics);
    }

    /// <summary>Reads a room-scoped canonical JSON without modifying its source file.</summary>
    internal static FrameworkFileResult ImportFile(string path, string expectedRoomKey)
    {
        var diagnostics = new List<FrameworkDiagnostic>();
        if (!ValidateRoomFilePath(path, expectedRoomKey, diagnostics))
            return new FrameworkFileResult(path, null, null, diagnostics);

        string originalText;
        try
        {
            originalText = File.ReadAllText(path);
        }
        catch (Exception exception)
        {
            AddError(diagnostics, path, "Level Export file could not be read: " + exception.Message);
            return new FrameworkFileResult(path, null, null, diagnostics);
        }

        FrameworkCodecResult parsed = Parse(originalText);
        return new FrameworkFileResult(path, originalText, parsed.Document, parsed.Diagnostics);
    }

    /// <summary>Creates an empty room-scoped file result when no export exists yet.</summary>
    internal static FrameworkFileResult EmptyFile(string path)
    {
        return new FrameworkFileResult(
            path,
            null,
            new FrameworkLevelEdit(
                new FrameworkObjectData[0],
                new FrameworkObjectData[0],
                new FrameworkObjectData[0]),
            new FrameworkDiagnostic[0]);
    }

    /// <summary>Validates and atomically writes a room-scoped canonical JSON file.</summary>
    internal static FrameworkExportResult ExportFile(
        string path,
        string expectedRoomKey,
        FrameworkLevelEdit document)
    {
        var diagnostics = new List<FrameworkDiagnostic>();
        if (!ValidateRoomFilePath(path, expectedRoomKey, diagnostics))
            return new FrameworkExportResult(path, null, diagnostics);

        FrameworkCodecResult serialized = Serialize(document);
        diagnostics.AddRange(serialized.Diagnostics);
        if (!serialized.IsValid)
            return new FrameworkExportResult(path, null, diagnostics);

        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath);
        string temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        string backupPath = fullPath + ".prev";
        try
        {
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(temporaryPath, serialized.CanonicalJson, new UTF8Encoding(false));

            FrameworkCodecResult temporaryValidation = Parse(File.ReadAllText(temporaryPath));
            if (!temporaryValidation.IsValid)
            {
                diagnostics.AddRange(temporaryValidation.Diagnostics);
                return new FrameworkExportResult(path, null, diagnostics);
            }

            if (File.Exists(fullPath))
                File.Replace(temporaryPath, fullPath, backupPath, true);
            else
                File.Move(temporaryPath, fullPath);

            return new FrameworkExportResult(
                path,
                File.Exists(backupPath) ? backupPath : null,
                diagnostics);
        }
        catch (Exception exception)
        {
            AddError(diagnostics, path, "Atomic Level Export failed; source file was preserved: " + exception.Message);
            return new FrameworkExportResult(path, null, diagnostics);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (Exception exception)
            {
                AddWarning(diagnostics, temporaryPath, "Temporary export cleanup failed: " + exception.Message);
            }
        }
    }

    private static List<FrameworkObjectData> ReadArray(
        JObject root,
        string arrayName,
        FrameworkOperation operation,
        HashSet<string> ids,
        List<FrameworkDiagnostic> diagnostics)
    {
        var result = new List<FrameworkObjectData>();
        JToken token = root[arrayName];
        if (token == null)
        {
            AddError(diagnostics, arrayName, "Required top-level array is missing.");
            return result;
        }

        if (token.Type != JTokenType.Array)
        {
            AddError(diagnostics, arrayName, "Top-level field must be an array.");
            return result;
        }

        JArray array = (JArray)token;
        for (int index = 0; index < array.Count; index++)
        {
            string location = arrayName + "[" + index + "]";
            JObject entry = array[index] as JObject;
            if (entry == null)
            {
                AddError(diagnostics, location, "ObjectData entry must be an object.");
                continue;
            }

            FrameworkObjectData data;
            if (!TryReadObject(entry, operation, location, diagnostics, out data))
                continue;

            if (operation != FrameworkOperation.Deletion && !ids.Add(data.Id))
            {
                AddError(diagnostics, location + ".id", "Duplicate object id: " + data.Id);
                continue;
            }

            result.Add(data);
        }

        return result;
    }

    private static bool TryReadObject(
        JObject entry,
        FrameworkOperation operation,
        string location,
        List<FrameworkDiagnostic> diagnostics,
        out FrameworkObjectData data)
    {
        data = null;
        foreach (JProperty property in entry.Properties())
        {
            if (!LevelFrameworkContract.IsKnownObjectField(property.Name))
            {
                AddWarning(diagnostics, location + "." + property.Name, "Unknown field ignored.");
            }
        }

        string scene = null;
        string path = null;
        string type = null;
        string id = null;
        bool valid = true;

        if (operation == FrameworkOperation.Deletion)
        {
            valid &= TryReadRequiredString(entry, LevelFrameworkContract.SceneField, location, diagnostics, out scene);
            valid &= TryReadRequiredString(entry, LevelFrameworkContract.PathField, location, diagnostics, out path);
            if (!valid)
                return false;

            foreach (string field in new[]
            {
                LevelFrameworkContract.TypeField,
                LevelFrameworkContract.IdField,
                LevelFrameworkContract.PositionField,
                LevelFrameworkContract.RotationField,
                LevelFrameworkContract.ScaleField,
                LevelFrameworkContract.ConditionField,
                LevelFrameworkContract.PropertiesField
            })
            {
                if (entry[field] != null)
                    AddWarning(diagnostics, location + "." + field, "Field ignored for deletion.");
            }

            data = new FrameworkObjectData(
                scene,
                path,
                null,
                null,
                DefaultPosition,
                DefaultRotation,
                DefaultScale,
                string.Empty,
                new string[0]);
            return true;
        }

        if (operation == FrameworkOperation.Modification)
            valid &= TryReadRequiredString(entry, LevelFrameworkContract.SceneField, location, diagnostics, out scene);
        valid &= TryReadRequiredString(entry, LevelFrameworkContract.TypeField, location, diagnostics, out type);
        valid &= TryReadRequiredString(entry, LevelFrameworkContract.IdField, location, diagnostics, out id);
        if (operation == FrameworkOperation.Modification)
            valid &= TryReadRequiredString(entry, LevelFrameworkContract.PathField, location, diagnostics, out path);

        FrameworkVector position;
        FrameworkVector rotation;
        FrameworkVector scale;
        string condition;
        List<string> properties;
        valid &= TryReadVector(entry, LevelFrameworkContract.PositionField, DefaultPosition, location, diagnostics, out position);
        valid &= TryReadVector(entry, LevelFrameworkContract.RotationField, DefaultRotation, location, diagnostics, out rotation);
        valid &= TryReadVector(entry, LevelFrameworkContract.ScaleField, DefaultScale, location, diagnostics, out scale);
        valid &= TryReadCondition(entry, location, diagnostics, out condition);
        valid &= TryReadProperties(entry, location, diagnostics, out properties);

        if (!valid)
            return false;

        data = new FrameworkObjectData(
            scene,
            path,
            type,
            id,
            position,
            rotation,
            scale,
            condition,
            properties);
        return true;
    }

    private static bool TryReadRequiredString(
        JObject entry,
        string field,
        string location,
        List<FrameworkDiagnostic> diagnostics,
        out string value)
    {
        value = null;
        JToken token = entry[field];
        if (token == null)
        {
            AddError(diagnostics, location + "." + field, "Required string field is missing.");
            return false;
        }

        if (token.Type != JTokenType.String)
        {
            AddError(diagnostics, location + "." + field, "Field must be a string.");
            return false;
        }

        value = token.Value<string>();
        if (string.IsNullOrEmpty(value) || value.Trim().Length == 0)
        {
            AddError(diagnostics, location + "." + field, "String field cannot be blank.");
            return false;
        }

        return true;
    }

    private static bool TryReadVector(
        JObject entry,
        string field,
        FrameworkVector defaultValue,
        string location,
        List<FrameworkDiagnostic> diagnostics,
        out FrameworkVector value)
    {
        value = defaultValue;
        JToken token = entry[field];
        if (token == null)
            return true;

        JObject vector = token as JObject;
        if (vector == null)
        {
            AddError(diagnostics, location + "." + field, "Transform must be an object with numeric axes.");
            return false;
        }

        foreach (JProperty property in vector.Properties())
        {
            if (!string.Equals(property.Name, "x", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(property.Name, "y", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(property.Name, "z", StringComparison.OrdinalIgnoreCase))
            {
                AddWarning(diagnostics, location + "." + field + "." + property.Name, "Unknown transform axis ignored.");
            }
        }

        float x = 0f;
        float y = 0f;
        float z = 0f;
        bool valid = TryReadAxis(vector, "x", location + "." + field, diagnostics, out x)
            && TryReadAxis(vector, "y", location + "." + field, diagnostics, out y)
            && TryReadAxis(vector, "z", location + "." + field, diagnostics, out z);
        if (!valid)
            return false;

        value = new FrameworkVector(x, y, z);
        return true;
    }

    private static bool TryReadAxis(
        JObject vector,
        string axis,
        string location,
        List<FrameworkDiagnostic> diagnostics,
        out float value)
    {
        value = 0f;
        JToken token = null;
        foreach (JProperty property in vector.Properties())
        {
            if (string.Equals(property.Name, axis, StringComparison.OrdinalIgnoreCase))
            {
                token = property.Value;
                break;
            }
        }

        if (token == null)
            return true;

        if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
        {
            AddError(diagnostics, location + "." + axis, "Transform axis must be numeric.");
            return false;
        }

        try
        {
            value = token.Value<float>();
        }
        catch (Exception exception)
        {
            AddError(diagnostics, location + "." + axis, "Transform axis is invalid: " + exception.Message);
            return false;
        }

        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            AddError(diagnostics, location + "." + axis, "Transform axis must be finite.");
            return false;
        }

        return true;
    }

    private static bool TryReadCondition(
        JObject entry,
        string location,
        List<FrameworkDiagnostic> diagnostics,
        out string condition)
    {
        condition = string.Empty;
        JToken token = entry[LevelFrameworkContract.ConditionField];
        if (token == null)
            return true;

        if (token.Type != JTokenType.String)
        {
            AddError(diagnostics, location + ".condition", "Condition must be a string.");
            return false;
        }

        condition = token.Value<string>();
        if (string.IsNullOrEmpty(condition) || condition.Trim().Length == 0)
        {
            condition = string.Empty;
            return true;
        }

        int colon = condition.IndexOf(':');
        if (colon <= 0)
        {
            AddError(diagnostics, location + ".condition", "Condition must use flag:<id>, penitence:<id>, or gamemode:<id>.");
            return false;
        }

        string kind = condition.Substring(0, colon);
        if (kind != "flag" && kind != "penitence" && kind != "gamemode")
        {
            AddError(diagnostics, location + ".condition", "Unsupported condition kind: " + kind);
            return false;
        }

        if (condition.Substring(colon + 1).Trim().Length == 0)
        {
            AddError(diagnostics, location + ".condition", "Condition value cannot be blank.");
            return false;
        }

        return true;
    }

    private static bool TryReadProperties(
        JObject entry,
        string location,
        List<FrameworkDiagnostic> diagnostics,
        out List<string> properties)
    {
        properties = new List<string>();
        JToken token = entry[LevelFrameworkContract.PropertiesField];
        if (token == null)
            return true;

        if (token.Type != JTokenType.Array)
        {
            AddError(diagnostics, location + ".properties", "Properties must be an array of strings.");
            return false;
        }

        JArray array = (JArray)token;
        bool valid = true;
        for (int index = 0; index < array.Count; index++)
        {
            JToken property = array[index];
            if (property.Type != JTokenType.String)
            {
                AddError(diagnostics, location + ".properties[" + index + "]", "Property must be a string.");
                valid = false;
                continue;
            }

            string value = property.Value<string>();
            if (string.IsNullOrEmpty(value) || value.Trim().Length == 0)
            {
                AddError(diagnostics, location + ".properties[" + index + "]", "Property cannot be blank.");
                valid = false;
                continue;
            }

            properties.Add(value);
        }

        return valid;
    }

    private static JObject BuildJson(FrameworkLevelEdit document)
    {
        var root = new JObject();
        root[LevelFrameworkContract.AdditionsField] = BuildArray(document.Additions, FrameworkOperation.Addition);
        root[LevelFrameworkContract.ModificationsField] = BuildArray(document.Modifications, FrameworkOperation.Modification);
        root[LevelFrameworkContract.DeletionsField] = BuildArray(document.Deletions, FrameworkOperation.Deletion);
        return root;
    }

    private static JArray BuildArray(
        IEnumerable<FrameworkObjectData> entries,
        FrameworkOperation operation)
    {
        var array = new JArray();
        foreach (FrameworkObjectData entry in entries)
            array.Add(BuildObject(entry, operation));
        return array;
    }

    private static JObject BuildObject(FrameworkObjectData entry, FrameworkOperation operation)
    {
        if (entry == null)
            throw new InvalidOperationException("ObjectData entry cannot be null.");

        var result = new JObject();
        if (operation == FrameworkOperation.Deletion)
        {
            result[LevelFrameworkContract.SceneField] = entry.Scene;
            result[LevelFrameworkContract.PathField] = entry.Path;
            return result;
        }

        if (operation == FrameworkOperation.Modification)
        {
            result[LevelFrameworkContract.SceneField] = entry.Scene;
            result[LevelFrameworkContract.PathField] = entry.Path;
        }

        result[LevelFrameworkContract.TypeField] = entry.Type;
        result[LevelFrameworkContract.IdField] = entry.Id;
        result[LevelFrameworkContract.PositionField] = BuildVector(entry.Position);
        result[LevelFrameworkContract.RotationField] = BuildVector(entry.Rotation);
        result[LevelFrameworkContract.ScaleField] = BuildVector(entry.Scale);
        result[LevelFrameworkContract.ConditionField] = entry.Condition;

        var properties = new JArray();
        foreach (string property in entry.Properties)
            properties.Add(property);
        result[LevelFrameworkContract.PropertiesField] = properties;
        return result;
    }

    private static JObject BuildVector(FrameworkVector vector)
    {
        if (vector == null)
            throw new InvalidOperationException("Transform vector cannot be null.");

        var result = new JObject();
        result["x"] = vector.X;
        result["y"] = vector.Y;
        result["z"] = vector.Z;
        return result;
    }

    private static bool ValidateRoomFilePath(
        string path,
        string expectedRoomKey,
        List<FrameworkDiagnostic> diagnostics)
    {
        if (string.IsNullOrEmpty(path) || path.Trim().Length == 0)
        {
            AddError(diagnostics, "$", "Room export path is required.");
            return false;
        }

        if (string.IsNullOrEmpty(expectedRoomKey) || expectedRoomKey.Trim().Length == 0)
        {
            AddError(diagnostics, path, "Expected Room Identity export key is required.");
            return false;
        }

        if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
        {
            AddError(diagnostics, path, "Room export path must use the .json extension.");
            return false;
        }

        if (!string.Equals(
            Path.GetFileNameWithoutExtension(path),
            expectedRoomKey,
            StringComparison.OrdinalIgnoreCase))
        {
            AddError(
                diagnostics,
                path,
                "Room export key does not match Room Identity: expected " + expectedRoomKey + ".");
            return false;
        }

        return true;
    }

    private static FrameworkCodecResult EmptyResult(List<FrameworkDiagnostic> diagnostics)
    {
        return new FrameworkCodecResult(
            new FrameworkLevelEdit(
                new FrameworkObjectData[0],
                new FrameworkObjectData[0],
                new FrameworkObjectData[0]),
            null,
            diagnostics);
    }

    private static void AddError(List<FrameworkDiagnostic> diagnostics, string location, string message)
    {
        diagnostics.Add(new FrameworkDiagnostic(FrameworkDiagnosticSeverity.Error, location, message));
    }

    private static void AddWarning(List<FrameworkDiagnostic> diagnostics, string location, string message)
    {
        diagnostics.Add(new FrameworkDiagnostic(FrameworkDiagnosticSeverity.Warning, location, message));
    }
}
