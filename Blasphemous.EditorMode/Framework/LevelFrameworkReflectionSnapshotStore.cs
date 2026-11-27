using Blasphemous.EditorMode.Diagnostics;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;

namespace Blasphemous.EditorMode.Framework;

/// <summary>
/// Version-and-signature-gated adapter over Level Framework 0.2.0's private level snapshot.
/// </summary>
internal sealed class LevelFrameworkReflectionSnapshotStore : ILevelFrameworkSnapshotStore
{
    private const string FrameworkTypeName = "Blasphemous.Framework.Levels.LevelFramework";
    private const string MainTypeName = "Blasphemous.Framework.Levels.Main";
    private const string LevelEditTypeName = "Blasphemous.Framework.Levels.LevelEdit";
    private const string ObjectDataTypeName = "Blasphemous.Framework.Levels.ObjectData";
    private const string VectorTypeName = "Blasphemous.Framework.Levels.Vector";
    private static readonly Version ExpectedVersion = new(0, 2, 0, 0);

    private readonly object _framework;
    private readonly IDictionary _additions;
    private readonly IDictionary _modifications;
    private readonly IDictionary _deletions;
    private readonly MethodInfo _loadAllEdits;
    private readonly MethodInfo _processLevelEdits;
    private readonly Type _objectDataType;
    private readonly FieldInfo _sceneField;
    private readonly FieldInfo _pathField;
    private readonly FieldInfo _typeField;
    private readonly FieldInfo _idField;
    private readonly FieldInfo _positionField;
    private readonly FieldInfo _rotationField;
    private readonly FieldInfo _scaleField;
    private readonly FieldInfo _conditionField;
    private readonly FieldInfo _propertiesField;

    private LevelFrameworkReflectionSnapshotStore(
        object framework,
        IDictionary additions,
        IDictionary modifications,
        IDictionary deletions,
        MethodInfo loadAllEdits,
        MethodInfo processLevelEdits,
        Type objectDataType,
        FieldInfo sceneField,
        FieldInfo pathField,
        FieldInfo typeField,
        FieldInfo idField,
        FieldInfo positionField,
        FieldInfo rotationField,
        FieldInfo scaleField,
        FieldInfo conditionField,
        FieldInfo propertiesField)
    {
        _framework = framework;
        _additions = additions;
        _modifications = modifications;
        _deletions = deletions;
        _loadAllEdits = loadAllEdits;
        _processLevelEdits = processLevelEdits;
        _objectDataType = objectDataType;
        _sceneField = sceneField;
        _pathField = pathField;
        _typeField = typeField;
        _idField = idField;
        _positionField = positionField;
        _rotationField = rotationField;
        _scaleField = scaleField;
        _conditionField = conditionField;
        _propertiesField = propertiesField;
    }

    internal static LevelFrameworkSnapshotStoreBinding Resolve()
    {
        ApplyReport report = new();
        Type frameworkType = FindFrameworkType();
        if (frameworkType == null)
        {
            AddUnsupported(
                report,
                "framework.assembly",
                "Level Framework 0.2.0 is not loaded or its LevelFramework type is unavailable.");
            return LevelFrameworkSnapshotStoreBinding.Unsupported(report);
        }

        Assembly assembly = frameworkType.Assembly;
        ValidateVersion(assembly, report);

        Type mainType = assembly.GetType(MainTypeName, throwOnError: false);
        Type levelEditType = assembly.GetType(LevelEditTypeName, throwOnError: false);
        Type objectDataType = assembly.GetType(ObjectDataTypeName, throwOnError: false);
        Type vectorType = assembly.GetType(VectorTypeName, throwOnError: false);
        RequireType(mainType, MainTypeName, report);
        RequireType(levelEditType, LevelEditTypeName, report);
        RequireType(objectDataType, ObjectDataTypeName, report);
        RequireType(vectorType, VectorTypeName, report);
        if (mainType == null
            || levelEditType == null
            || objectDataType == null
            || vectorType == null)
        {
            return LevelFrameworkSnapshotStoreBinding.Unsupported(report);
        }

        object framework = ResolveFrameworkInstance(mainType, frameworkType, report);
        Type editDictionaryType = typeof(Dictionary<,>).MakeGenericType(
            typeof(string),
            levelEditType);
        Type editDictionariesType = typeof(IEnumerable<>).MakeGenericType(editDictionaryType);
        Type objectEnumerableType = typeof(IEnumerable<>).MakeGenericType(objectDataType);
        Type snapshotDictionaryType = typeof(Dictionary<,>).MakeGenericType(
            typeof(string),
            objectEnumerableType);

        MethodInfo loadAllEdits = RequireMethod(
            frameworkType,
            "LoadAllEdits",
            Type.EmptyTypes,
            editDictionariesType,
            report,
            "framework.method.LoadAllEdits");
        MethodInfo processLevelEdits = RequireMethod(
            frameworkType,
            "ProcessLevelEdits",
            [editDictionaryType],
            typeof(void),
            report,
            "framework.method.ProcessLevelEdits");
        FieldInfo additionsField = RequireField(
            frameworkType,
            "_additions",
            snapshotDictionaryType,
            BindingFlags.Instance | BindingFlags.NonPublic,
            report,
            "framework.field._additions");
        FieldInfo modificationsField = RequireField(
            frameworkType,
            "_modifications",
            snapshotDictionaryType,
            BindingFlags.Instance | BindingFlags.NonPublic,
            report,
            "framework.field._modifications");
        FieldInfo deletionsField = RequireField(
            frameworkType,
            "_deletions",
            snapshotDictionaryType,
            BindingFlags.Instance | BindingFlags.NonPublic,
            report,
            "framework.field._deletions");

        FieldInfo sceneField = RequireField(
            objectDataType,
            "scene",
            typeof(string),
            BindingFlags.Instance | BindingFlags.Public,
            report,
            "framework.type.ObjectData.scene");
        FieldInfo pathField = RequireField(
            objectDataType,
            "path",
            typeof(string),
            BindingFlags.Instance | BindingFlags.Public,
            report,
            "framework.type.ObjectData.path");
        FieldInfo typeField = RequireField(
            objectDataType,
            "type",
            typeof(string),
            BindingFlags.Instance | BindingFlags.Public,
            report,
            "framework.type.ObjectData.type");
        FieldInfo idField = RequireField(
            objectDataType,
            "id",
            typeof(string),
            BindingFlags.Instance | BindingFlags.Public,
            report,
            "framework.type.ObjectData.id");
        FieldInfo positionField = RequireField(
            objectDataType,
            "position",
            vectorType,
            BindingFlags.Instance | BindingFlags.Public,
            report,
            "framework.type.ObjectData.position");
        FieldInfo rotationField = RequireField(
            objectDataType,
            "rotation",
            vectorType,
            BindingFlags.Instance | BindingFlags.Public,
            report,
            "framework.type.ObjectData.rotation");
        FieldInfo scaleField = RequireField(
            objectDataType,
            "scale",
            vectorType,
            BindingFlags.Instance | BindingFlags.Public,
            report,
            "framework.type.ObjectData.scale");
        FieldInfo conditionField = RequireField(
            objectDataType,
            "condition",
            typeof(string),
            BindingFlags.Instance | BindingFlags.Public,
            report,
            "framework.type.ObjectData.condition");
        FieldInfo propertiesField = RequireField(
            objectDataType,
            "properties",
            typeof(string[]),
            BindingFlags.Instance | BindingFlags.Public,
            report,
            "framework.type.ObjectData.properties");

        RequireField(
            levelEditType,
            "additions",
            objectDataType.MakeArrayType(),
            BindingFlags.Instance | BindingFlags.Public,
            report,
            "framework.type.LevelEdit.additions");
        RequireField(
            levelEditType,
            "modifications",
            objectDataType.MakeArrayType(),
            BindingFlags.Instance | BindingFlags.Public,
            report,
            "framework.type.LevelEdit.modifications");
        RequireField(
            levelEditType,
            "deletions",
            objectDataType.MakeArrayType(),
            BindingFlags.Instance | BindingFlags.Public,
            report,
            "framework.type.LevelEdit.deletions");
        RequireProperty(
            vectorType,
            "X",
            typeof(float),
            report,
            "framework.type.Vector.X");
        RequireProperty(
            vectorType,
            "Y",
            typeof(float),
            report,
            "framework.type.Vector.Y");
        RequireProperty(
            vectorType,
            "Z",
            typeof(float),
            report,
            "framework.type.Vector.Z");

        if (report.HasErrors
            || framework == null
            || additionsField == null
            || modificationsField == null
            || deletionsField == null
            || loadAllEdits == null
            || processLevelEdits == null)
        {
            return LevelFrameworkSnapshotStoreBinding.Unsupported(report);
        }

        IDictionary additions = ReadDictionary(
            additionsField,
            framework,
            report,
            "framework.field._additions");
        IDictionary modifications = ReadDictionary(
            modificationsField,
            framework,
            report,
            "framework.field._modifications");
        IDictionary deletions = ReadDictionary(
            deletionsField,
            framework,
            report,
            "framework.field._deletions");
        if (report.HasErrors)
            return LevelFrameworkSnapshotStoreBinding.Unsupported(report);

        return LevelFrameworkSnapshotStoreBinding.Compatible(
            new LevelFrameworkReflectionSnapshotStore(
                framework,
                additions,
                modifications,
                deletions,
                loadAllEdits,
                processLevelEdits,
                objectDataType,
                sceneField,
                pathField,
                typeField,
                idField,
                positionField,
                rotationField,
                scaleField,
                conditionField,
                propertiesField));
    }

    public bool TryCapture(out LevelFrameworkSnapshot snapshot, out string error)
    {
        snapshot = null;
        error = null;
        try
        {
            Dictionary<string, Array> additions = CopyDictionary(_additions, "_additions");
            Dictionary<string, Array> modifications = CopyDictionary(
                _modifications,
                "_modifications");
            Dictionary<string, Array> deletions = CopyDictionary(_deletions, "_deletions");
            Dictionary<string, ReadOnlyCollection<FrameworkObjectData>> observedAdditions =
                Observe(additions, "_additions");
            Dictionary<string, ReadOnlyCollection<FrameworkObjectData>> observedModifications =
                Observe(modifications, "_modifications");
            Dictionary<string, ReadOnlyCollection<FrameworkObjectData>> observedDeletions =
                Observe(deletions, "_deletions");
            snapshot = new LevelFrameworkSnapshot(
                new NativeSnapshot(additions, modifications, deletions),
                observedAdditions,
                observedModifications,
                observedDeletions);
            return true;
        }
        catch (Exception exception)
        {
            error = Describe(exception);
            return false;
        }
    }

    public bool TryReload(out LevelFrameworkSnapshot snapshot, out string error)
    {
        snapshot = null;
        error = null;
        try
        {
            object loaded = _loadAllEdits.Invoke(_framework, null);
            if (loaded == null)
            {
                error = "Level Framework LoadAllEdits returned null.";
                return false;
            }

            List<object> edits = [];
            foreach (object edit in (IEnumerable)loaded)
            {
                if (edit == null)
                {
                    error = "Level Framework LoadAllEdits returned a null edit dictionary.";
                    return false;
                }

                edits.Add(edit);
            }

            _additions.Clear();
            _modifications.Clear();
            _deletions.Clear();
            foreach (object edit in edits)
                _processLevelEdits.Invoke(_framework, [edit]);

            return TryCapture(out snapshot, out error);
        }
        catch (Exception exception)
        {
            error = Describe(exception);
            return false;
        }
    }

    public bool TryRestore(LevelFrameworkSnapshot snapshot, out string error)
    {
        error = null;
        NativeSnapshot native = snapshot?.NativeState as NativeSnapshot;
        if (native == null)
        {
            error = "The captured Level Framework snapshot has no native state.";
            return false;
        }

        List<string> errors = [];
        RestoreDictionary(_additions, native.Additions, "_additions", errors);
        RestoreDictionary(_modifications, native.Modifications, "_modifications", errors);
        RestoreDictionary(_deletions, native.Deletions, "_deletions", errors);
        if (errors.Count == 0)
            return true;

        error = string.Join("; ", errors.ToArray());
        return false;
    }

    private Dictionary<string, ReadOnlyCollection<FrameworkObjectData>> Observe(
        Dictionary<string, Array> source,
        string location)
    {
        Dictionary<string, ReadOnlyCollection<FrameworkObjectData>> result =
            new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, Array> entry in source)
        {
            List<FrameworkObjectData> entries = [];
            foreach (object value in entry.Value)
            {
                if (value == null)
                {
                    throw new InvalidOperationException(
                        location + "[" + entry.Key + "] contains a null ObjectData entry.");
                }

                entries.Add(ReadObject(value, location + "[" + entry.Key + "]"));
            }

            result.Add(entry.Key, new ReadOnlyCollection<FrameworkObjectData>(entries));
        }

        return result;
    }

    private Dictionary<string, Array> CopyDictionary(IDictionary source, string location)
    {
        Dictionary<string, Array> result = new(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in source)
        {
            string key = entry.Key as string;
            if (string.IsNullOrEmpty(key))
            {
                throw new InvalidOperationException(
                    location + " contains a null or empty room key.");
            }

            if (entry.Value == null)
            {
                throw new InvalidOperationException(
                    location + "[" + key + "] contains a null entry sequence.");
            }

            List<object> values = [];
            foreach (object value in (IEnumerable)entry.Value)
                values.Add(value);
            Array copy = Array.CreateInstance(_objectDataType, values.Count);
            for (int index = 0; index < values.Count; index++)
                copy.SetValue(values[index], index);
            result.Add(key, copy);
        }

        return result;
    }

    private FrameworkObjectData ReadObject(
        object value,
        string location)
    {
        string scene = (string)_sceneField.GetValue(value);
        string path = (string)_pathField.GetValue(value);
        string type = (string)_typeField.GetValue(value);
        string id = (string)_idField.GetValue(value);
        FrameworkVector position = ReadVector(
            _positionField.GetValue(value),
            location + ".position");
        FrameworkVector rotation = ReadVector(
            _rotationField.GetValue(value),
            location + ".rotation");
        FrameworkVector scale = ReadVector(
            _scaleField.GetValue(value),
            location + ".scale");
        string condition = (string)_conditionField.GetValue(value);
        string[] properties = (string[])_propertiesField.GetValue(value) ?? [];
        return new FrameworkObjectData(
            scene,
            path,
            type,
            id,
            position,
            rotation,
            scale,
            condition,
            properties,
            originalAddition: null,
            preserveOriginalAddition: false);
    }

    private static FrameworkVector ReadVector(object value, string location)
    {
        if (value == null)
            throw new InvalidOperationException(location + " is null.");

        Type type = value.GetType();
        return new FrameworkVector(
            ReadAxis(type, value, "X", location),
            ReadAxis(type, value, "Y", location),
            ReadAxis(type, value, "Z", location));
    }

    private static float ReadAxis(Type type, object value, string axis, string location)
    {
        PropertyInfo property = type.GetProperty(
            axis,
            BindingFlags.Instance | BindingFlags.Public);
        if (property == null)
            throw new InvalidOperationException(location + "." + axis + " is unavailable.");

        object axisValue = property.GetValue(value, null);
        return Convert.ToSingle(axisValue, CultureInfo.InvariantCulture);
    }

    private static void RestoreDictionary(
        IDictionary target,
        Dictionary<string, Array> source,
        string location,
        List<string> errors)
    {
        try
        {
            target.Clear();
            foreach (KeyValuePair<string, Array> entry in source)
                target.Add(entry.Key, entry.Value);
        }
        catch (Exception exception)
        {
            errors.Add(location + ": " + Describe(exception));
        }
    }

    private static Type FindFrameworkType()
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = assembly.GetType(FrameworkTypeName, throwOnError: false);
            if (type != null)
                return type;
        }

        return Type.GetType(FrameworkTypeName + ", LevelFramework", throwOnError: false);
    }

    private static void ValidateVersion(Assembly assembly, ApplyReport report)
    {
        Version version = assembly.GetName().Version;
        if (version != ExpectedVersion)
        {
            AddUnsupported(
                report,
                "framework.version",
                "Expected Level Framework version 0.2.0.0 but found "
                    + (version == null ? "<unknown>" : version.ToString()) + ".");
        }

        AssemblyInformationalVersionAttribute informational =
            GetInformationalVersion(assembly);
        string value = informational?.InformationalVersion;
        if (string.IsNullOrEmpty(value)
            || !value.StartsWith("0.2.0", StringComparison.Ordinal))
        {
            AddUnsupported(
                report,
                "framework.informationalVersion",
                "Expected a Level Framework 0.2.0 informational version but found "
                    + (string.IsNullOrEmpty(value) ? "<missing>" : value) + ".");
        }
    }

    private static AssemblyInformationalVersionAttribute GetInformationalVersion(
        Assembly assembly)
    {
        object[] attributes = assembly.GetCustomAttributes(
            typeof(AssemblyInformationalVersionAttribute),
            inherit: false);
        return attributes.Length == 0
            ? null
            : attributes[0] as AssemblyInformationalVersionAttribute;
    }

    private static object ResolveFrameworkInstance(
        Type mainType,
        Type frameworkType,
        ApplyReport report)
    {
        PropertyInfo property = mainType.GetProperty(
            "LevelFramework",
            BindingFlags.Public | BindingFlags.Static);
        if (property == null || property.PropertyType != frameworkType)
        {
            AddUnsupported(
                report,
                "framework.instance",
                "Level Framework Main.LevelFramework static property signature did not match.");
            return null;
        }

        try
        {
            object value = property.GetValue(null, null);
            if (value == null)
            {
                AddUnsupported(
                    report,
                    "framework.instance",
                    "Level Framework has not initialized its runtime instance.");
            }

            return value;
        }
        catch (Exception exception)
        {
            AddUnsupported(
                report,
                "framework.instance",
                "Level Framework runtime instance could not be read: " + Describe(exception));
            return null;
        }
    }

    private static Type RequireType(Type type, string name, ApplyReport report)
    {
        if (type != null)
            return type;

        AddUnsupported(report, "framework.type." + name, "Required type is unavailable: " + name + ".");
        return null;
    }

    private static FieldInfo RequireField(
        Type owner,
        string name,
        Type expectedType,
        BindingFlags flags,
        ApplyReport report,
        string location)
    {
        FieldInfo field = owner.GetField(name, flags);
        if (field != null && field.FieldType == expectedType)
            return field;

        AddUnsupported(
            report,
            location,
            "Expected " + (expectedType == null ? "<unknown>" : expectedType.FullName)
                + " but found "
                + (field == null ? "<missing>" : field.FieldType.FullName) + ".");
        return null;
    }

    private static MethodInfo RequireMethod(
        Type owner,
        string name,
        Type[] parameters,
        Type returnType,
        ApplyReport report,
        string location)
    {
        MethodInfo method = owner.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            parameters,
            null);
        if (method != null && method.ReturnType == returnType)
            return method;

        AddUnsupported(
            report,
            location,
            "Expected Level Framework method signature did not match for " + name + ".");
        return null;
    }

    private static PropertyInfo RequireProperty(
        Type owner,
        string name,
        Type expectedType,
        ApplyReport report,
        string location)
    {
        PropertyInfo property = owner.GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public);
        if (property != null && property.PropertyType == expectedType)
            return property;

        AddUnsupported(
            report,
            location,
            "Expected Level Framework property signature did not match for " + name + ".");
        return null;
    }

    private static IDictionary ReadDictionary(
        FieldInfo field,
        object instance,
        ApplyReport report,
        string location)
    {
        try
        {
            IDictionary dictionary = field.GetValue(instance) as IDictionary;
            if (dictionary != null)
                return dictionary;

            AddUnsupported(report, location, "Level Framework snapshot field value is not a dictionary.");
            return null;
        }
        catch (Exception exception)
        {
            AddUnsupported(
                report,
                location,
                "Level Framework snapshot field could not be read: " + Describe(exception));
            return null;
        }
    }

    private static void AddUnsupported(ApplyReport report, string location, string message)
    {
        report.Error(
            ReportCodes.FrameworkReloadUnsupported,
            location,
            message,
            skipped: false);
    }

    private static string Describe(Exception exception)
    {
        Exception current = exception;
        while (current is TargetInvocationException target
            && target.InnerException != null)
        {
            current = target.InnerException;
        }

        return current.GetType().Name + ": " + current.Message;
    }

    private sealed class NativeSnapshot
    {
        internal NativeSnapshot(
            Dictionary<string, Array> additions,
            Dictionary<string, Array> modifications,
            Dictionary<string, Array> deletions)
        {
            Additions = additions;
            Modifications = modifications;
            Deletions = deletions;
        }

        internal Dictionary<string, Array> Additions { get; }
        internal Dictionary<string, Array> Modifications { get; }
        internal Dictionary<string, Array> Deletions { get; }
    }
}