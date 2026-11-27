using Blasphemous.EditorMode.Diagnostics;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blasphemous.EditorMode.Framework;

/// <summary>Outcome of refreshing Level Framework's in-memory level snapshot.</summary>
internal enum LevelFrameworkReloadStatus
{
    Succeeded,
    Unsupported,
    FailedBeforeMutation,
    FailedRolledBack,
    FailedRestartRequired
}

/// <summary>Immutable observation of one Level Framework level snapshot.</summary>
internal sealed class LevelFrameworkSnapshot
{
    private readonly Dictionary<string, ReadOnlyCollection<FrameworkObjectData>> _additions;
    private readonly Dictionary<string, ReadOnlyCollection<FrameworkObjectData>> _modifications;
    private readonly Dictionary<string, ReadOnlyCollection<FrameworkObjectData>> _deletions;

    internal LevelFrameworkSnapshot(
        object nativeState,
        IDictionary additions,
        IDictionary modifications,
        IDictionary deletions)
    {
        NativeState = nativeState;
        _additions = Copy(additions);
        _modifications = Copy(modifications);
        _deletions = Copy(deletions);
    }

    internal object NativeState { get; }

    internal bool TryGetEntries(
        FrameworkOperation operation,
        string roomKey,
        out ReadOnlyCollection<FrameworkObjectData> entries)
    {
        Dictionary<string, ReadOnlyCollection<FrameworkObjectData>> source = operation switch
        {
            FrameworkOperation.Addition => _additions,
            FrameworkOperation.Modification => _modifications,
            FrameworkOperation.Deletion => _deletions,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown Framework operation.")
        };

        return source.TryGetValue(roomKey ?? string.Empty, out entries);
    }

    private static Dictionary<string, ReadOnlyCollection<FrameworkObjectData>> Copy(
        IDictionary source)
    {
        Dictionary<string, ReadOnlyCollection<FrameworkObjectData>> result =
            new(StringComparer.Ordinal);
        if (source == null)
            return result;

        foreach (DictionaryEntry entry in source)
        {
            string roomKey = entry.Key as string;
            if (roomKey == null)
                continue;

            result[roomKey] = new ReadOnlyCollection<FrameworkObjectData>(
                [.. (entry.Value as IEnumerable<FrameworkObjectData>) ?? []]);
        }

        return result;
    }
}

/// <summary>Seam for reading, reloading, and restoring Level Framework's in-memory snapshot.</summary>
internal interface ILevelFrameworkSnapshotStore
{
    bool TryCapture(out LevelFrameworkSnapshot snapshot, out string error);
    bool TryReload(out LevelFrameworkSnapshot snapshot, out string error);
    bool TryRestore(LevelFrameworkSnapshot snapshot, out string error);
}

/// <summary>Result of resolving the version-and-signature-gated snapshot store.</summary>
internal sealed class LevelFrameworkSnapshotStoreBinding
{
    internal LevelFrameworkSnapshotStoreBinding(
        ILevelFrameworkSnapshotStore store,
        ApplyReport compatibilityReport)
    {
        Store = store;
        CompatibilityReport = compatibilityReport ?? new ApplyReport();
    }

    internal ILevelFrameworkSnapshotStore Store { get; }
    internal ApplyReport CompatibilityReport { get; }
    internal bool IsCompatible
    {
        get { return Store != null && !CompatibilityReport.HasErrors; }
    }

    internal static LevelFrameworkSnapshotStoreBinding Compatible(
        ILevelFrameworkSnapshotStore store)
    {
        return new LevelFrameworkSnapshotStoreBinding(store, new ApplyReport());
    }

    internal static LevelFrameworkSnapshotStoreBinding Unsupported(ApplyReport report)
    {
        return new LevelFrameworkSnapshotStoreBinding(null, report);
    }
}

/// <summary>Result of one Level Framework snapshot reload attempt.</summary>
internal sealed class LevelFrameworkReloadResult
{
    internal LevelFrameworkReloadResult(
        LevelFrameworkReloadStatus status,
        bool reloadAttempted,
        bool restoreAttempted,
        bool restoreSucceeded,
        ApplyReport report)
    {
        Status = status;
        ReloadAttempted = reloadAttempted;
        RestoreAttempted = restoreAttempted;
        RestoreSucceeded = restoreSucceeded;
        Report = report ?? new ApplyReport();
    }

    internal LevelFrameworkReloadStatus Status { get; }
    internal bool ReloadAttempted { get; }
    internal bool RestoreAttempted { get; }
    internal bool RestoreSucceeded { get; }
    internal ApplyReport Report { get; }
    internal bool Succeeded { get { return Status == LevelFrameworkReloadStatus.Succeeded; } }
    internal bool RestartRequired
    {
        get { return Status == LevelFrameworkReloadStatus.FailedRestartRequired; }
    }
}

/// <summary>
/// Refreshes Level Framework's snapshot for the next room preload without applying it to the current room.
/// </summary>
internal sealed class LevelFrameworkSnapshotReloader
{
    private readonly LevelFrameworkSnapshotStoreBinding _binding;

    internal LevelFrameworkSnapshotReloader(ILevelFrameworkSnapshotStore store)
        : this(LevelFrameworkSnapshotStoreBinding.Compatible(store))
    {
    }

    internal LevelFrameworkSnapshotReloader(LevelFrameworkSnapshotStoreBinding binding)
    {
        _binding = binding ?? LevelFrameworkSnapshotStoreBinding.Unsupported(
            MissingStoreReport());
    }

    internal static LevelFrameworkSnapshotReloader CreateRuntime()
    {
        return new LevelFrameworkSnapshotReloader(
            LevelFrameworkReflectionSnapshotStore.Resolve());
    }

    internal LevelFrameworkReloadResult Reload(
        string roomKey,
        FrameworkLevelEdit expectedDocument)
    {
        ApplyReport report = new();
        if (_binding == null || !_binding.IsCompatible)
        {
            report.Merge(_binding?.CompatibilityReport);
            if (report.DiagnosticCount == 0)
            {
                report.Error(
                    ReportCodes.FrameworkReloadUnsupported,
                    "framework",
                    "Level Framework snapshot store is unavailable; no reload was attempted.",
                    skipped: false);
            }

            return new LevelFrameworkReloadResult(
                LevelFrameworkReloadStatus.Unsupported,
                reloadAttempted: false,
                restoreAttempted: false,
                restoreSucceeded: false,
                report);
        }

        if (string.IsNullOrEmpty(roomKey) || roomKey.Trim().Length == 0)
        {
            report.Error(
                ReportCodes.FrameworkReloadValidationFailed,
                "framework.room",
                "Room key is required before reloading the Level Framework snapshot.",
                skipped: false);
            return FailedBeforeMutation(report);
        }

        if (expectedDocument == null)
        {
            report.Error(
                ReportCodes.FrameworkReloadValidationFailed,
                "framework.document",
                "The exported Level Framework document is required before reloading.",
                skipped: false);
            return FailedBeforeMutation(report);
        }

        LevelFrameworkSnapshot before;
        string error;
        if (!_binding.Store.TryCapture(out before, out error))
        {
            report.Error(
                ReportCodes.FrameworkReloadCaptureFailed,
                "framework.snapshot",
                "The previous Level Framework snapshot could not be captured; no reload was attempted: "
                    + Describe(error),
                skipped: false);
            return FailedBeforeMutation(report);
        }

        LevelFrameworkSnapshot reloaded;
        if (!_binding.Store.TryReload(out reloaded, out error))
        {
            report.Error(
                ReportCodes.FrameworkReloadFailed,
                "framework.reload",
                "The Level Framework snapshot could not be reloaded: " + Describe(error),
                skipped: false);
            return RestoreAfterFailure(report, before, reloadAttempted: true);
        }

        if (!ValidateReloadedSnapshot(report, reloaded, roomKey, expectedDocument))
        {
            return RestoreAfterFailure(report, before, reloadAttempted: true);
        }

        report.Applied();
        report.Info(
            ReportCodes.FrameworkReloadSucceeded,
            roomKey,
            "Level Framework now uses the exported snapshot for the next room preload; "
                + "the current room was not reloaded or re-applied.");
        return new LevelFrameworkReloadResult(
            LevelFrameworkReloadStatus.Succeeded,
            reloadAttempted: true,
            restoreAttempted: false,
            restoreSucceeded: false,
            report);
    }

    private LevelFrameworkReloadResult RestoreAfterFailure(
        ApplyReport report,
        LevelFrameworkSnapshot before,
        bool reloadAttempted)
    {
        if (_binding.Store.TryRestore(before, out string restoreError))
        {
            report.Warning(
                ReportCodes.FrameworkReloadRolledBack,
                "framework.snapshot",
                "The previous Level Framework snapshot was restored; the canonical Level Export remains committed.");
            return new LevelFrameworkReloadResult(
                LevelFrameworkReloadStatus.FailedRolledBack,
                reloadAttempted,
                restoreAttempted: true,
                restoreSucceeded: true,
                report);
        }

        report.Error(
            ReportCodes.FrameworkRestartRequired,
            "framework.snapshot",
            "The previous Level Framework snapshot could not be restored ("
                + Describe(restoreError)
                + "). Restart the game before editing or relying on a later room preload; "
                + "the snapshot used by later preloads is unknown.",
            skipped: false);
        return new LevelFrameworkReloadResult(
            LevelFrameworkReloadStatus.FailedRestartRequired,
            reloadAttempted,
            restoreAttempted: true,
            restoreSucceeded: false,
            report);
    }

    private static LevelFrameworkReloadResult FailedBeforeMutation(ApplyReport report)
    {
        return new LevelFrameworkReloadResult(
            LevelFrameworkReloadStatus.FailedBeforeMutation,
            reloadAttempted: false,
            restoreAttempted: false,
            restoreSucceeded: false,
            report);
    }

    private static bool ValidateReloadedSnapshot(
        ApplyReport report,
        LevelFrameworkSnapshot reloaded,
        string roomKey,
        FrameworkLevelEdit expected)
    {
        if (reloaded == null)
        {
            report.Error(
                ReportCodes.FrameworkReloadValidationFailed,
                "framework.snapshot",
                "The Level Framework reload returned no snapshot.",
                skipped: false);
            return false;
        }

        bool valid = true;
        valid &= ValidateOperation(
            report,
            reloaded,
            FrameworkOperation.Addition,
            roomKey,
            expected.Additions);
        valid &= ValidateOperation(
            report,
            reloaded,
            FrameworkOperation.Modification,
            roomKey,
            expected.Modifications);
        valid &= ValidateOperation(
            report,
            reloaded,
            FrameworkOperation.Deletion,
            roomKey,
            expected.Deletions);
        return valid;
    }

    private static bool ValidateOperation(
        ApplyReport report,
        LevelFrameworkSnapshot snapshot,
        FrameworkOperation operation,
        string roomKey,
        IEnumerable<FrameworkObjectData> expected)
    {
        snapshot.TryGetEntries(operation, roomKey, out ReadOnlyCollection<FrameworkObjectData> actual);
        bool valid = true;
        foreach (FrameworkObjectData entry in expected ?? [])
        {
            int matches = CountMatches(actual, entry);
            if (matches == 1)
                continue;

            valid = false;
            report.Error(
                ReportCodes.FrameworkReloadValidationFailed,
                Location(operation, entry),
                "Reloaded Level Framework snapshot contains " + matches
                    + " matching entries; exactly one was expected.",
                skipped: false);
        }

        return valid;
    }

    private static int CountMatches(
        IEnumerable<FrameworkObjectData> actual,
        FrameworkObjectData expected)
    {
        int count = 0;
        foreach (FrameworkObjectData candidate in actual ?? [])
        {
            if (expected.PreserveOriginalAddition
                && expected.OriginalAddition is JObject original)
            {
                if (PreservedAdditionMatches(candidate, original))
                    count++;
            }
            else if (Same(candidate, expected))
                count++;
        }

        return count;
    }

    private static bool PreservedAdditionMatches(
        FrameworkObjectData candidate,
        JObject original)
    {
        bool compared = false;
        bool valid = true;
        valid &= CompareString(
            original,
            LevelFrameworkContract.SceneField,
            candidate.Scene,
            ref compared);
        valid &= CompareString(
            original,
            LevelFrameworkContract.PathField,
            candidate.Path,
            ref compared);
        valid &= CompareString(
            original,
            LevelFrameworkContract.TypeField,
            candidate.Type,
            ref compared);
        valid &= CompareString(
            original,
            LevelFrameworkContract.IdField,
            candidate.Id,
            ref compared);
        valid &= CompareVector(
            original,
            LevelFrameworkContract.PositionField,
            candidate.Position,
            ref compared);
        valid &= CompareVector(
            original,
            LevelFrameworkContract.RotationField,
            candidate.Rotation,
            ref compared);
        valid &= CompareVector(
            original,
            LevelFrameworkContract.ScaleField,
            candidate.Scale,
            ref compared);
        valid &= CompareCondition(original, candidate.Condition, ref compared);
        valid &= CompareProperties(original, candidate.Properties, ref compared);
        return compared && valid;
    }

    private static bool CompareString(
        JObject original,
        string field,
        string actual,
        ref bool compared)
    {
        if (!TryGetField(original, field, out JToken token))
            return true;

        compared = true;
        if (token.Type == JTokenType.Null)
            return actual == null;
        return token.Type == JTokenType.String
            && SameText(token.Value<string>(), actual);
    }

    private static bool CompareCondition(
        JObject original,
        string actual,
        ref bool compared)
    {
        if (!TryGetField(original, LevelFrameworkContract.ConditionField, out JToken token))
            return true;

        compared = true;
        if (token.Type == JTokenType.Null)
            return SameText(actual, string.Empty);
        return token.Type == JTokenType.String
            && SameText(token.Value<string>(), actual);
    }

    private static bool CompareVector(
        JObject original,
        string field,
        FrameworkVector actual,
        ref bool compared)
    {
        if (!TryGetField(original, field, out JToken token))
            return true;

        compared = true;
        if (token.Type == JTokenType.Null)
            return actual == null;
        if (token is not JObject vector || actual == null)
            return false;

        bool comparedAxis = false;
        bool valid = true;
        foreach (JProperty axis in vector.Properties())
        {
            float? expected = AxisValue(axis.Value);
            if (expected == null)
                return false;
            if (string.Equals(axis.Name, "x", StringComparison.OrdinalIgnoreCase))
            {
                comparedAxis = true;
                valid &= actual.X.Equals(expected.Value);
            }
            else if (string.Equals(axis.Name, "y", StringComparison.OrdinalIgnoreCase))
            {
                comparedAxis = true;
                valid &= actual.Y.Equals(expected.Value);
            }
            else if (string.Equals(axis.Name, "z", StringComparison.OrdinalIgnoreCase))
            {
                comparedAxis = true;
                valid &= actual.Z.Equals(expected.Value);
            }
        }

        return comparedAxis && valid;
    }

    private static float? AxisValue(JToken token)
    {
        if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            return null;
        return token.Value<float>();
    }

    private static bool CompareProperties(
        JObject original,
        ReadOnlyCollection<string> actual,
        ref bool compared)
    {
        if (!TryGetField(original, LevelFrameworkContract.PropertiesField, out JToken token))
            return true;

        compared = true;
        if (token.Type == JTokenType.Null)
            return actual == null || actual.Count == 0;
        if (token is not JArray array || actual == null)
            return false;

        List<string> values = [];
        foreach (JToken value in array)
        {
            if (value.Type != JTokenType.String)
                return false;
            values.Add(value.Value<string>());
        }

        return SameProperties(
            actual,
            new ReadOnlyCollection<string>(LevelFrameworkProperties.Normalize(values)));
    }

    private static bool TryGetField(JObject original, string field, out JToken token)
    {
        return original.TryGetValue(field, StringComparison.OrdinalIgnoreCase, out token);
    }

    private static bool Same(FrameworkObjectData left, FrameworkObjectData right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left == null || right == null)
            return false;

        return SameText(left.Scene, right.Scene)
            && SameText(left.Path, right.Path)
            && SameText(left.Type, right.Type)
            && SameText(left.Id, right.Id)
            && SameText(left.Condition, right.Condition)
            && SameVector(left.Position, right.Position)
            && SameVector(left.Rotation, right.Rotation)
            && SameVector(left.Scale, right.Scale)
            && SameProperties(left.Properties, right.Properties);
    }

    private static bool SameText(string left, string right)
    {
        return string.Equals(left, right, StringComparison.Ordinal);
    }

    private static bool SameVector(FrameworkVector left, FrameworkVector right)
    {
        if (left == null || right == null)
            return left == right;

        return left.X.Equals(right.X)
            && left.Y.Equals(right.Y)
            && left.Z.Equals(right.Z);
    }

    private static bool SameProperties(
        ReadOnlyCollection<string> left,
        ReadOnlyCollection<string> right)
    {
        if (left == null || right == null)
            return left == right;
        if (left.Count != right.Count)
            return false;

        for (int index = 0; index < left.Count; index++)
        {
            if (!SameText(left[index], right[index]))
                return false;
        }

        return true;
    }

    private static string Location(FrameworkOperation operation, FrameworkObjectData entry)
    {
        string field = operation switch
        {
            FrameworkOperation.Addition => "additions",
            FrameworkOperation.Modification => "modifications",
            FrameworkOperation.Deletion => "deletions",
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown Framework operation.")
        };
        if (operation == FrameworkOperation.Addition)
            return field + "[id='" + (entry.Id ?? string.Empty) + "']";

        return field + "[scene='" + (entry.Scene ?? string.Empty)
            + "',path='" + (entry.Path ?? string.Empty) + "']";
    }

    private static string Describe(string error)
    {
        return string.IsNullOrEmpty(error) ? "no detail was provided" : error;
    }

    private static ApplyReport MissingStoreReport()
    {
        ApplyReport report = new();
        report.Error(
            ReportCodes.FrameworkReloadUnsupported,
            "framework",
            "Level Framework snapshot store is unavailable.",
            skipped: false);
        return report;
    }
}