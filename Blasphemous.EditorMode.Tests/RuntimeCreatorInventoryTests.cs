using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class RuntimeCreatorInventoryTests
{
    [Fact]
    public void EveryRegisteredEntryIsIncludedWithExactCaseVariants()
    {
        RuntimeCreatorInventory inventory = RuntimeCreatorInventory.FromRegistryEntries(
            new[]
            {
                new RuntimeCreatorRegistryEntry("bell-iron", true, true),
                new RuntimeCreatorRegistryEntry("Uncurated", true, true),
                new RuntimeCreatorRegistryEntry("uncurated", false, true)
            });

        Assert.Equal(
            new[] { "bell-iron", "Uncurated", "uncurated" },
            inventory.Entries.Select(entry => entry.Type));
        Assert.True(inventory.TryGetExact("Uncurated", out CreatorMetadata upper));
        Assert.True(inventory.TryGetExact("uncurated", out CreatorMetadata lower));
        Assert.NotSame(upper, lower);
    }

    [Fact]
    public void EntriesSortCaseInsensitivelyWithAnExactKeyTieBreaker()
    {
        RuntimeCreatorInventory inventory = RuntimeCreatorInventory.FromRegistryEntries(
            new[]
            {
                new RuntimeCreatorRegistryEntry("zed", true, true),
                new RuntimeCreatorRegistryEntry("alpha", true, true),
                new RuntimeCreatorRegistryEntry("Beta", true, true),
                new RuntimeCreatorRegistryEntry("Alpha", true, true)
            });

        Assert.Equal(
            new[] { "Alpha", "alpha", "Beta", "zed" },
            inventory.Entries.Select(entry => entry.Type));
    }

    [Fact]
    public void MissingCurationUsesStableFallbackMetadataAndCapabilities()
    {
        RuntimeCreatorInventory inventory = RuntimeCreatorInventory.FromRegistryEntries(
            new[]
            {
                new RuntimeCreatorRegistryEntry("new-creator", false, false)
            });

        CreatorMetadata entry = Assert.Single(inventory.Entries);
        Assert.Equal("new-creator", entry.DisplayName);
        Assert.Equal("Registered", entry.Category);
        Assert.Equal("Level Framework", entry.Source);
        Assert.Equal("Not declared", entry.SourceScene);
        Assert.Equal("Not declared", entry.SourcePath);
        Assert.Equal(
            TransformCapabilities.Position
                | TransformCapabilities.Rotation
                | TransformCapabilities.Scale,
            entry.TransformCapabilities);
        Assert.Empty(entry.PropertyDefinitions);
        Assert.False(entry.HasLoader);
        Assert.False(entry.HasModifier);
        Assert.False(entry.CanPreview);
        Assert.False(entry.CanPlace);
        Assert.Contains(
            inventory.Diagnostics,
            diagnostic => diagnostic.Code == ReportCodes.CreatorLoaderMissing);
        Assert.Contains(
            inventory.Diagnostics,
            diagnostic => diagnostic.Code == ReportCodes.CreatorModifierMissing);
    }

    [Fact]
    public void LoaderAndModifierPresenceControlActionAvailability()
    {
        RuntimeCreatorInventory inventory = RuntimeCreatorInventory.FromRegistryEntries(
            new[]
            {
                new RuntimeCreatorRegistryEntry("preview-only", true, false),
                new RuntimeCreatorRegistryEntry("no-loader", false, true),
                new RuntimeCreatorRegistryEntry("placeable", true, true)
            });

        CreatorMetadata previewOnly = inventory.Entries.Single(entry => entry.Type == "preview-only");
        CreatorMetadata noLoader = inventory.Entries.Single(entry => entry.Type == "no-loader");
        CreatorMetadata placeable = inventory.Entries.Single(entry => entry.Type == "placeable");

        Assert.True(previewOnly.CanPreview);
        Assert.False(previewOnly.CanPlace);
        Assert.False(noLoader.CanPreview);
        Assert.False(noLoader.CanPlace);
        Assert.True(placeable.CanPreview);
        Assert.True(placeable.CanPlace);
        Assert.Contains(
            inventory.Diagnostics,
            diagnostic => diagnostic.Location == "no-loader"
                && diagnostic.Code == ReportCodes.CreatorLoaderMissing);
        Assert.DoesNotContain(
            inventory.Diagnostics,
            diagnostic => diagnostic.Location == "no-loader"
                && diagnostic.Code == ReportCodes.CreatorModifierMissing);
    }

    [Fact]
    public void EmptyAndUnavailableRegistriesAreDistinguishable()
    {
        RuntimeCreatorInventory empty = RuntimeCreatorInventory.FromRegistryEntries(
            Array.Empty<RuntimeCreatorRegistryEntry>());
        RuntimeCreatorInventory unavailable = RuntimeCreatorInventoryReader.Read(
            () => throw new InvalidOperationException("registry denied"));

        Assert.Equal(RuntimeCreatorInventoryState.Empty, empty.State);
        Assert.True(empty.IsRegistryAvailable);
        Assert.Empty(empty.Entries);
        Assert.Empty(empty.Diagnostics);

        Assert.Equal(RuntimeCreatorInventoryState.Unavailable, unavailable.State);
        Assert.False(unavailable.IsRegistryAvailable);
        Assert.Empty(unavailable.Entries);
        CreatorInventoryDiagnostic diagnostic = Assert.Single(unavailable.Diagnostics);
        Assert.Equal(ApplySeverity.Error, diagnostic.Severity);
        Assert.Equal(ReportCodes.CreatorRegistryUnavailable, diagnostic.Code);
        Assert.Equal("<registry>", diagnostic.Location);
        Assert.Contains("registry denied", diagnostic.Message);
    }

    [Fact]
    public void NullRegistryEntriesAreUnavailableInsteadOfAnEmptyRegistry()
    {
        RuntimeCreatorInventory inventory = RuntimeCreatorInventoryReader.Read(() => null);

        Assert.Equal(RuntimeCreatorInventoryState.Unavailable, inventory.State);
        Assert.False(inventory.IsRegistryAvailable);
        Assert.Empty(inventory.Entries);
        CreatorInventoryDiagnostic diagnostic = Assert.Single(inventory.Diagnostics);
        Assert.Equal(ApplySeverity.Error, diagnostic.Severity);
        Assert.Equal(ReportCodes.CreatorRegistryUnavailable, diagnostic.Code);
    }

    [Fact]
    public void RegistryFailureDuringLazyEnumerationIsReportedAsUnavailable()
    {
        RuntimeCreatorInventory inventory = RuntimeCreatorInventoryReader.Read(ThrowDuringEnumeration);

        Assert.Equal(RuntimeCreatorInventoryState.Unavailable, inventory.State);
        Assert.Empty(inventory.Entries);
        CreatorInventoryDiagnostic diagnostic = Assert.Single(inventory.Diagnostics);
        Assert.Equal(ApplySeverity.Error, diagnostic.Severity);
        Assert.Equal(ReportCodes.CreatorRegistryUnavailable, diagnostic.Code);
        Assert.Contains("enumeration failed", diagnostic.Message);
    }

    [Fact]
    public void InvalidRegistryEntriesAreDiagnosedWithoutHidingValidEntries()
    {
        RuntimeCreatorInventory inventory = RuntimeCreatorInventory.FromRegistryEntries(
            new[]
            {
                null,
                new RuntimeCreatorRegistryEntry("", true, true),
                new RuntimeCreatorRegistryEntry("valid", true, true)
            });

        Assert.Equal("valid", Assert.Single(inventory.Entries).Type);
        Assert.Equal(2, inventory.Diagnostics.Count);
        Assert.All(inventory.Diagnostics, diagnostic => Assert.NotEmpty(diagnostic.Message));
    }

    private static IEnumerable<RuntimeCreatorRegistryEntry> ThrowDuringEnumeration()
    {
        yield return new RuntimeCreatorRegistryEntry("valid", true, true);
        throw new InvalidOperationException("enumeration failed");
    }
}