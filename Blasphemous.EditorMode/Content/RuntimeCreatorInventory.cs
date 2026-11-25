using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.Framework.Levels;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

namespace Blasphemous.EditorMode.Content;

internal enum RuntimeCreatorInventoryState
{
    Ready,
    Empty,
    Unavailable
}

internal sealed class CreatorInventoryDiagnostic
{
    internal CreatorInventoryDiagnostic(
        ApplySeverity severity,
        string code,
        string location,
        string message)
    {
        Severity = severity;
        Code = code ?? ReportCodes.Unknown;
        Location = location ?? "$";
        Message = message ?? string.Empty;
    }

    internal ApplySeverity Severity { get; }
    internal string Code { get; }
    internal string Location { get; }
    internal string Message { get; }
}

internal sealed class RuntimeCreatorRegistryEntry
{
    internal RuntimeCreatorRegistryEntry(string type, bool hasLoader, bool hasModifier)
    {
        Type = type;
        HasLoader = hasLoader;
        HasModifier = hasModifier;
    }

    internal string Type { get; }
    internal bool HasLoader { get; }
    internal bool HasModifier { get; }
}

/// <summary>
/// Unity-independent snapshot of every creator registered by the current Level Framework runtime.
/// </summary>
internal sealed class RuntimeCreatorInventory
{
    private const string RegistryLocation = "<registry>";

    private RuntimeCreatorInventory(
        RuntimeCreatorInventoryState state,
        IEnumerable<CreatorMetadata> entries,
        IEnumerable<CreatorInventoryDiagnostic> diagnostics,
        string message)
    {
        State = state;
        Entries = new ReadOnlyCollection<CreatorMetadata>(
            [.. entries ?? []]);
        Diagnostics = new ReadOnlyCollection<CreatorInventoryDiagnostic>(
            [.. diagnostics ?? []]);
        Message = message ?? string.Empty;
    }

    internal RuntimeCreatorInventoryState State { get; }
    internal ReadOnlyCollection<CreatorMetadata> Entries { get; }
    internal ReadOnlyCollection<CreatorInventoryDiagnostic> Diagnostics { get; }
    internal string Message { get; }
    internal bool IsRegistryAvailable { get { return State != RuntimeCreatorInventoryState.Unavailable; } }

    internal bool TryGetExact(string type, out CreatorMetadata metadata)
    {
        metadata = null;
        if (string.IsNullOrEmpty(type))
            return false;

        foreach (CreatorMetadata entry in Entries)
        {
            if (string.Equals(entry.Type, type, StringComparison.Ordinal))
            {
                metadata = entry;
                return true;
            }
        }

        return false;
    }

    internal static RuntimeCreatorInventory FromRegistryEntries(
        IEnumerable<RuntimeCreatorRegistryEntry> registryEntries)
    {
        if (registryEntries == null)
        {
            return RegistryUnavailable(
                "Level Framework creator registry returned no entry collection.");
        }

        List<CreatorMetadata> entries = [];
        List<CreatorInventoryDiagnostic> diagnostics = [];
        foreach (RuntimeCreatorRegistryEntry registryEntry in registryEntries)
        {
            if (registryEntry == null || IsBlank(registryEntry.Type))
            {
                diagnostics.Add(new CreatorInventoryDiagnostic(
                    ApplySeverity.Warning,
                    ReportCodes.CreatorRegistryEntryInvalid,
                    RegistryLocation,
                    "A Level Framework creator registry entry had no usable key and was skipped."));
                continue;
            }

            CreatorMetadata metadata = new(
                registryEntry.Type,
                registryEntry.Type,
                "Registered",
                CreatorOrigin.BaseFramework,
                "Not declared",
                "Not declared",
                TransformCapabilities.Position
                    | TransformCapabilities.Rotation
                    | TransformCapabilities.Scale,
                new CreatorPropertyDefinition[0],
                true,
                "Level Framework",
                registryEntry.HasLoader,
                registryEntry.HasModifier);
            entries.Add(metadata);

            if (!registryEntry.HasLoader)
            {
                diagnostics.Add(new CreatorInventoryDiagnostic(
                    ApplySeverity.Warning,
                    ReportCodes.CreatorLoaderMissing,
                    registryEntry.Type,
                    "Creator '" + registryEntry.Type + "' has no loader; Preview and Place are unavailable."));
            }
            if (!registryEntry.HasModifier)
            {
                diagnostics.Add(new CreatorInventoryDiagnostic(
                    ApplySeverity.Warning,
                    ReportCodes.CreatorModifierMissing,
                    registryEntry.Type,
                    "Creator '" + registryEntry.Type + "' has no modifier; Place is unavailable."));
            }
        }

        entries.Sort(CompareEntries);
        RuntimeCreatorInventoryState state = entries.Count == 0
            ? RuntimeCreatorInventoryState.Empty
            : RuntimeCreatorInventoryState.Ready;
        string message = state == RuntimeCreatorInventoryState.Empty
            ? "Level Framework creator registry is available but contains no creators."
            : string.Format(
                "Level Framework creator registry contains {0} creator(s) and {1} diagnostic(s).",
                entries.Count,
                diagnostics.Count);
        return new RuntimeCreatorInventory(state, entries, diagnostics, message);
    }

    internal static RuntimeCreatorInventory RegistryUnavailable(string message)
    {
        string detail = message ?? "Level Framework creator registry is unavailable.";
        return new RuntimeCreatorInventory(
            RuntimeCreatorInventoryState.Unavailable,
            new CreatorMetadata[0],
            new[]
            {
                new CreatorInventoryDiagnostic(
                    ApplySeverity.Error,
                    ReportCodes.CreatorRegistryUnavailable,
                    RegistryLocation,
                    detail)
            },
            detail);
    }

    private static int CompareEntries(CreatorMetadata left, CreatorMetadata right)
    {
        int insensitive = StringComparer.OrdinalIgnoreCase.Compare(left.Type, right.Type);
        return insensitive != 0
            ? insensitive
            : StringComparer.Ordinal.Compare(left.Type, right.Type);
    }

    private static bool IsBlank(string value)
    {
        return string.IsNullOrEmpty(value) || value.Trim().Length == 0;
    }
}

internal static class RuntimeCreatorInventoryReader
{
    private const BindingFlags StaticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;

    internal static RuntimeCreatorInventory Read()
    {
        return Read(ReadRegisteredEntries);
    }

    internal static RuntimeCreatorInventory Read(
        Func<IEnumerable<RuntimeCreatorRegistryEntry>> registryEntries)
    {
        try
        {
            if (registryEntries == null)
            {
                return RuntimeCreatorInventory.RegistryUnavailable(
                    "Level Framework creator registry reader was not supplied.");
            }

            IEnumerable<RuntimeCreatorRegistryEntry> entries = registryEntries();
            return RuntimeCreatorInventory.FromRegistryEntries(entries);
        }
        catch (Exception exception)
        {
            return RuntimeCreatorInventory.RegistryUnavailable(
                "Level Framework creator inventory failed safely: " + exception.Message);
        }
    }

    private static IEnumerable<RuntimeCreatorRegistryEntry> ReadRegisteredEntries()
    {
        PropertyInfo creatorsProperty = typeof(LevelRegister).GetProperty("Creators", StaticNonPublic);
        if (creatorsProperty == null)
        {
            throw new MissingMemberException(
                "Level Framework creator registry does not expose its Creators collection.");
        }

        if (creatorsProperty.GetValue(null, null) is not IEnumerable creators)
        {
            throw new InvalidOperationException(
                "Level Framework creator registry returned no enumerable entries.");
        }

        foreach (object entry in creators)
        {
            if (entry == null)
            {
                yield return null;
                continue;
            }

            Type entryType = entry.GetType();
            PropertyInfo keyProperty = entryType.GetProperty("Key");
            PropertyInfo valueProperty = entryType.GetProperty("Value");
            string type = keyProperty == null
                ? null
                : keyProperty.GetValue(entry, null) as string;
            ObjectCreator creator = valueProperty == null
                ? null
                : valueProperty.GetValue(entry, null) as ObjectCreator;
            yield return new RuntimeCreatorRegistryEntry(
                type,
                creator?.Loader != null,
                creator?.Modifier != null);
        }
    }
}