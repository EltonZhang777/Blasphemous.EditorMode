using Blasphemous.EditorMode.Content;
using System;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class ContentCatalogTests
{
    [Fact]
    public void Runtime_inventory_drives_membership_and_preserves_case_variants()
    {
        RuntimeCreatorInventory inventory = RuntimeCreatorInventory.FromRegistryEntries(
            new[]
            {
                new RuntimeCreatorRegistryEntry("bell-iron", true, true),
                new RuntimeCreatorRegistryEntry("Uncurated", true, true),
                new RuntimeCreatorRegistryEntry("uncurated", false, true)
            });
        ContentCatalog catalog = ContentCatalog.FromInventory(inventory);

        Assert.Equal(
            new[] { "bell-iron", "Uncurated", "uncurated" },
            Array.ConvertAll([.. catalog.Entries], entry => entry.Type));
        Assert.True(catalog.TryGet("Uncurated", out CreatorMetadata upper));
        Assert.True(catalog.TryGet("uncurated", out CreatorMetadata lower));
        Assert.NotSame(upper, lower);
        Assert.False(catalog.TryGet("UNCURATED", out _));
    }

    [Fact]
    public void Missing_exact_key_reports_the_runtime_inventory_boundary()
    {
        ContentCatalog catalog = ContentCatalog.FromEntries(new[] { CreatorFixtures.BellIron });
        PreviewInstance preview;
        string error;

        Assert.False(catalog.TryCreatePreview("BELL-IRON", out preview, out error));
        Assert.Null(preview);
        Assert.Contains("runtime creator inventory", error);
    }

    [Fact]
    public void UnavailableRegistryExposesNoMembershipOrPreviewBoundary()
    {
        ContentCatalog catalog = ContentCatalog.FromInventory(
            RuntimeCreatorInventory.RegistryUnavailable("registry denied"));

        Assert.True(catalog.RegistryUnavailable);
        Assert.Empty(catalog.Entries);
        Assert.False(catalog.TryGet("bell-iron", out _));
        Assert.False(catalog.TryCreatePreview("bell-iron", out PreviewInstance preview, out string error));
        Assert.Null(preview);
        Assert.Contains("runtime creator inventory", error);
    }

    [Fact]
    public void Preview_is_isolated_and_discard_is_idempotent()
    {
        int disposed = 0;
        ContentCatalog catalog = ContentCatalog.FromEntries(
            new[] { CreatorFixtures.BellIron },
            metadata => new PreviewInstance(metadata, () => disposed++));

        PreviewInstance preview;
        string error;
        Assert.True(
            catalog.TryCreatePreview(CreatorFixtures.BellIronType, out preview, out error),
            error);
        Assert.True(preview.IsGameplayIsolated);
        Assert.False(preview.AffectsRoomOverlay);
        preview.Dispose();
        preview.Dispose();
        Assert.True(preview.IsDisposed);
        Assert.Equal(1, disposed);
    }

    [Fact]
    public void Preview_follows_cursor_until_disposed()
    {
        int updates = 0;
        PreviewInstance preview = new(
            CreatorFixtures.BellIron,
            null,
            () => updates++);

        preview.FollowCursor();
        Assert.Equal(1, updates);

        preview.Dispose();
        preview.FollowCursor();
        Assert.Equal(1, updates);
    }

}
