using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.IsolatedStorage;
using System.Runtime.InteropServices;
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
        IEnumerable<string> properties,
        JToken originalAddition = null,
        bool preserveOriginalAddition = false)
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
            [.. properties ?? []]);
        OriginalAddition = originalAddition;
        PreserveOriginalAddition = preserveOriginalAddition;
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
    internal JToken OriginalAddition { get; }
    internal bool PreserveOriginalAddition { get; }

    internal static FrameworkObjectData PreservedAddition(JToken originalAddition)
    {
        JToken idToken = originalAddition is not JObject objectAddition ? null : objectAddition[LevelFrameworkContract.IdField];
        string id = idToken != null && idToken.Type == JTokenType.String
            ? idToken.Value<string>()
            : string.Empty;
        return new FrameworkObjectData(
            null,
            null,
            null,
            id,
            FrameworkVector.Zero,
            FrameworkVector.Zero,
            FrameworkVector.One,
            string.Empty,
            [],
            originalAddition?.DeepClone(),
            true);
    }

    internal FrameworkObjectData WithPreservedOriginalAddition()
    {
        return new FrameworkObjectData(
            Scene,
            Path,
            Type,
            Id,
            Position,
            Rotation,
            Scale,
            Condition,
            Properties,
            OriginalAddition,
            true);
    }
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
            [.. values ?? []]);
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
            [.. diagnostics ?? []]);
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
            [.. diagnostics ?? []]);
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
            [.. diagnostics ?? []]);
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
        List<FrameworkDiagnostic> diagnostics = [];
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

        HashSet<string> ids = new(StringComparer.Ordinal);
        List<FrameworkObjectData> additions = ReadArray(
            root,
            LevelFrameworkContract.AdditionsField,
            FrameworkOperation.Addition,
            ids,
            diagnostics);
        List<FrameworkObjectData> modifications = ReadArray(
            root,
            LevelFrameworkContract.ModificationsField,
            FrameworkOperation.Modification,
            ids,
            diagnostics);
        List<FrameworkObjectData> deletions = ReadArray(
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
        List<FrameworkDiagnostic> diagnostics = [];
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
        List<FrameworkDiagnostic> diagnostics = [];
        if (!ValidateRoomFilePath(path, expectedRoomKey, diagnostics))
            return new FrameworkFileResult(path, null, null, diagnostics);

        string originalText;
        try
        {
            originalText = File.ReadAllText(path);
        }
        catch (Exception exception)
        {
            int hresult = Marshal.GetHRForException(exception);
            if (IsMissingFileException(exception, path, hresult))
                return EmptyFile(path);

            AddError(
                diagnostics,
                path,
                "Level Export file could not be read (" + exception.GetType().FullName +
                ", HRESULT 0x" + hresult.ToString("X8") + "): " + exception.Message);
            return new FrameworkFileResult(path, null, null, diagnostics);
        }

        FrameworkCodecResult parsed = Parse(originalText);
        return new FrameworkFileResult(path, originalText, parsed.Document, parsed.Diagnostics);
    }

    internal static bool IsMissingFileHResult(int hresult)
    {
        return hresult == unchecked((int)0x80070002) ||
            hresult == unchecked((int)0x80070003);
    }

    internal static bool IsMissingFileException(Exception exception, string path, int hresult)
    {
        if (IsMissingFileHResult(hresult))
            return true;

        return exception is IsolatedStorageException isolatedStorageException &&
            isolatedStorageException.Message.IndexOf(
                "Could not find file \"" + path + "\"",
                StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Creates an empty room-scoped file result when no export exists yet.</summary>
    internal static FrameworkFileResult EmptyFile(string path)
    {
        return new FrameworkFileResult(
            path,
            null,
            new FrameworkLevelEdit(
                [],
                [],
                []),
            []);
    }

    /// <summary>Validates and atomically writes a room-scoped canonical JSON file.</summary>
    internal static FrameworkExportResult ExportFile(
        string path,
        string expectedRoomKey,
        FrameworkLevelEdit document)
    {
        List<FrameworkDiagnostic> diagnostics = [];
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
        string mainRollbackPath = fullPath + ".rollback-" + Guid.NewGuid().ToString("N");
        string backupTemporaryPath = backupPath + ".tmp-" + Guid.NewGuid().ToString("N");
        string backupRollbackPath = backupPath + ".rollback-" + Guid.NewGuid().ToString("N");
        string exportedBackupPath = null;
        bool preserveMainRollback = false;
        bool preserveBackupRollback = false;
        try
        {
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(temporaryPath, serialized.CanonicalJson, new UTF8Encoding(false));

            FrameworkCodecResult temporaryValidation = Parse(File.ReadAllText(temporaryPath));
            if (!temporaryValidation.IsValid)
            {
                diagnostics.AddRange(temporaryValidation.Diagnostics);
            }
            else if (File.Exists(fullPath))
            {
                File.Replace(temporaryPath, fullPath, mainRollbackPath, true);
                exportedBackupPath = mainRollbackPath;
                try
                {
                    JToken previousDocument = JToken.Parse(File.ReadAllText(mainRollbackPath));
                    File.WriteAllText(
                        backupTemporaryPath,
                        previousDocument.ToString(Formatting.Indented),
                        new UTF8Encoding(false));
                    if (File.Exists(backupPath))
                        File.Replace(backupTemporaryPath, backupPath, backupRollbackPath, true);
                    else
                        File.Move(backupTemporaryPath, backupPath);
                    exportedBackupPath = backupPath;
                }
                catch (Exception exception)
                {
                    string warning = "Export succeeded, but the previous JSON backup could not be formatted or committed: " + exception.Message;
                    if (File.Exists(backupRollbackPath))
                    {
                        try
                        {
                            if (File.Exists(backupPath))
                                File.Delete(backupPath);
                            File.Move(backupRollbackPath, backupPath);
                        }
                        catch (Exception restoreException)
                        {
                            preserveBackupRollback = File.Exists(backupRollbackPath);
                            warning += " The earlier backup could not be restored; inspect " + backupRollbackPath + ": " + restoreException.Message;
                        }
                    }

                    exportedBackupPath = mainRollbackPath;
                    preserveMainRollback = true;
                    AddWarning(diagnostics, mainRollbackPath, warning + " The previous room file remains recoverable at " + mainRollbackPath + ".");
                }
            }
            else
                File.Move(temporaryPath, fullPath);
        }
        catch (Exception exception)
        {
            if (File.Exists(mainRollbackPath))
            {
                bool restorePrevious = !File.Exists(fullPath);
                if (!restorePrevious)
                {
                    try
                    {
                        restorePrevious = !Parse(File.ReadAllText(fullPath)).IsValid;
                    }
                    catch
                    {
                        restorePrevious = true;
                    }
                }

                if (restorePrevious)
                {
                    try
                    {
                        if (File.Exists(fullPath))
                            File.Delete(fullPath);
                        File.Move(mainRollbackPath, fullPath);
                    }
                    catch (Exception restoreException)
                    {
                        exportedBackupPath = mainRollbackPath;
                        preserveMainRollback = true;
                        AddError(diagnostics, mainRollbackPath, "The previous Level Export could not be restored; recover it from " + mainRollbackPath + ": " + restoreException.Message);
                    }
                }

                if (File.Exists(mainRollbackPath))
                {
                    exportedBackupPath = mainRollbackPath;
                    preserveMainRollback = true;
                    AddWarning(diagnostics, mainRollbackPath, "The previous Level Export was retained at this recovery path because replacement state is uncertain.");
                }
            }

            AddError(diagnostics, path, "Atomic Level Export failed; inspect the source and backup files: " + exception.Message);
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

            try
            {
                if (!preserveMainRollback && File.Exists(mainRollbackPath))
                    File.Delete(mainRollbackPath);
            }
            catch (Exception exception)
            {
                exportedBackupPath = mainRollbackPath;
                AddWarning(diagnostics, mainRollbackPath, "Temporary main-file backup cleanup failed; previous data remains recoverable: " + exception.Message);
            }

            try
            {
                if (File.Exists(backupTemporaryPath))
                    File.Delete(backupTemporaryPath);
            }
            catch (Exception exception)
            {
                AddWarning(diagnostics, backupTemporaryPath, "Temporary backup cleanup failed: " + exception.Message);
            }

            try
            {
                if (!preserveBackupRollback && File.Exists(backupRollbackPath))
                    File.Delete(backupRollbackPath);
            }
            catch (Exception exception)
            {
                AddWarning(diagnostics, backupRollbackPath, "Temporary backup rollback cleanup failed: " + exception.Message);
            }
        }

        return new FrameworkExportResult(path, exportedBackupPath, diagnostics);
    }

    private static List<FrameworkObjectData> ReadArray(
        JObject root,
        string arrayName,
        FrameworkOperation operation,
        HashSet<string> ids,
        List<FrameworkDiagnostic> diagnostics)
    {
        List<FrameworkObjectData> result = [];
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
            if (array[index] is not JObject entry)
            {
                if (operation == FrameworkOperation.Addition)
                {
                    AddError(diagnostics, location, "ObjectData entry must be an object; source entry was retained unchanged.");
                    result.Add(FrameworkObjectData.PreservedAddition(array[index]));
                }
                else
                {
                    AddError(diagnostics, location, "ObjectData entry must be an object.");
                }
                continue;
            }

            if (!TryReadObject(entry, operation, location, diagnostics, out FrameworkObjectData data))
            {
                if (operation == FrameworkOperation.Addition)
                {
                    FrameworkObjectData preserved = FrameworkObjectData.PreservedAddition(entry);
                    if (!string.IsNullOrEmpty(preserved.Id) && !ids.Add(preserved.Id))
                    {
                        AddError(
                            diagnostics,
                            location + ".id",
                            "Duplicate object id: " + preserved.Id + "; source addition was retained unchanged.");
                    }
                    result.Add(preserved);
                }
                continue;
            }

            if (operation != FrameworkOperation.Deletion && !ids.Add(data.Id))
            {
                if (operation == FrameworkOperation.Addition)
                {
                    AddError(diagnostics, location + ".id", "Duplicate object id: " + data.Id + "; source addition was retained unchanged.");
                    result.Add(FrameworkObjectData.PreservedAddition(entry));
                }
                else
                {
                    AddError(diagnostics, location + ".id", "Duplicate object id: " + data.Id);
                }
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
        bool preserveOriginalAddition = false;
        foreach (JProperty property in entry.Properties())
        {
            if (!LevelFrameworkContract.IsKnownObjectField(property.Name))
            {
                AddWarning(diagnostics, location + "." + property.Name, "Unknown field ignored.");
                if (operation == FrameworkOperation.Addition)
                {
                    preserveOriginalAddition = true;
                }
            }
        }

        if (operation == FrameworkOperation.Addition)
        {
            foreach (string field in new[] { LevelFrameworkContract.SceneField, LevelFrameworkContract.PathField })
            {
                if (entry[field] != null)
                {
                    AddWarning(diagnostics, location + "." + field, "Field is unsupported for additions; source entry was retained unchanged.");
                    preserveOriginalAddition = true;
                }
            }
        }

        string scene = null;
        string path = null;
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
                []);
            return true;
        }

        if (operation == FrameworkOperation.Modification)
            valid &= TryReadRequiredString(entry, LevelFrameworkContract.SceneField, location, diagnostics, out scene);
        valid &= TryReadRequiredString(entry, LevelFrameworkContract.TypeField, location, diagnostics, out string type);
        valid &= TryReadRequiredString(entry, LevelFrameworkContract.IdField, location, diagnostics, out string id);
        if (operation == FrameworkOperation.Modification)
            valid &= TryReadRequiredString(entry, LevelFrameworkContract.PathField, location, diagnostics, out path);

        valid &= TryReadVector(entry, LevelFrameworkContract.PositionField, DefaultPosition, location, diagnostics, out FrameworkVector position);
        valid &= TryReadVector(entry, LevelFrameworkContract.RotationField, DefaultRotation, location, diagnostics, out FrameworkVector rotation);
        valid &= TryReadVector(entry, LevelFrameworkContract.ScaleField, DefaultScale, location, diagnostics, out FrameworkVector scale);
        valid &= TryReadCondition(entry, operation, location, diagnostics, out string condition);
        valid &= TryReadProperties(entry, location, diagnostics, out List<string> properties);

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
            properties,
            operation == FrameworkOperation.Addition ? (JToken)entry.DeepClone() : null,
            preserveOriginalAddition);
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

        if (token is not JObject vector)
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

        float y = 0f;
        float z = 0f;
        bool valid = TryReadAxis(vector, "x", location + "." + field, diagnostics, out float x)
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
        FrameworkOperation operation,
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
            if (operation == FrameworkOperation.Addition)
            {
                AddWarning(diagnostics, location + ".condition", "Unsupported condition kind: " + kind + "; source addition was retained unchanged.");
            }
            else
            {
                AddError(diagnostics, location + ".condition", "Unsupported condition kind: " + kind);
            }
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
        properties = [];
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
        JObject root = new()
        {
            [LevelFrameworkContract.AdditionsField] = BuildArray(document.Additions, FrameworkOperation.Addition),
            [LevelFrameworkContract.ModificationsField] = BuildArray(document.Modifications, FrameworkOperation.Modification),
            [LevelFrameworkContract.DeletionsField] = BuildArray(document.Deletions, FrameworkOperation.Deletion)
        };
        return root;
    }

    private static JArray BuildArray(
        IEnumerable<FrameworkObjectData> entries,
        FrameworkOperation operation)
    {
        JArray array = [];
        foreach (FrameworkObjectData entry in entries)
        {
            if (operation == FrameworkOperation.Addition
                && entry != null
                && entry.PreserveOriginalAddition
                && entry.OriginalAddition != null)
            {
                array.Add(entry.OriginalAddition.DeepClone());
            }
            else
            {
                array.Add(BuildObject(entry, operation));
            }
        }
        return array;
    }

    private static JObject BuildObject(FrameworkObjectData entry, FrameworkOperation operation)
    {
        if (entry == null)
            throw new InvalidOperationException("ObjectData entry cannot be null.");

        JObject result = [];
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

        JArray properties = [.. entry.Properties];
        result[LevelFrameworkContract.PropertiesField] = properties;
        return result;
    }

    private static JObject BuildVector(FrameworkVector vector)
    {
        if (vector == null)
            throw new InvalidOperationException("Transform vector cannot be null.");

        JObject result = new()
        {
            ["x"] = vector.X,
            ["y"] = vector.Y,
            ["z"] = vector.Z
        };
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
                [],
                [],
                []),
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
