using Blasphemous.EditorMode.Content;
using System;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class ContentCatalogTests
{
    [Fact]
    public void ProbeBuildsACuratedCatalogWithCreatorMetadata()
    {
        ContentCatalog catalog = ContentCatalog.FromProbe(
            CreatorProbe.Evaluate(
                new[] { BaseCreatorCatalog.BellIron },
                new[] { BaseCreatorCatalog.BellIronType }));

        CreatorMetadata entry = Assert.Single(catalog.Entries);
        Assert.Equal("Iron Bell", entry.DisplayName);
        Assert.Equal("Traps", entry.Category);
        Assert.Equal(CreatorOrigin.BaseFramework, entry.Origin);
        Assert.Equal("D03Z02S06_LOGIC", entry.SourceScene);
        Assert.NotEqual(TransformCapabilities.None, entry.TransformCapabilities);
    }

    [Fact]
    public void MissingOrOptionalEntriesAreNotPlaceableAndRemainDiagnosed()
    {
        CreatorMetadata optional = new CreatorMetadata(
            "optional-object",
            "Optional object",
            "Optional",
            CreatorOrigin.OptionalExtension,
            "scene",
            "path",
            TransformCapabilities.Position,
            new string[0],
            false);
        ContentCatalog catalog = ContentCatalog.FromProbe(
            CreatorProbe.Evaluate(
                new[] { BaseCreatorCatalog.BellIron, optional },
                new[] { BaseCreatorCatalog.BellIronType }));

        Assert.Single(catalog.Entries);
        Assert.Single(catalog.Unavailable);
        PreviewInstance preview;
        string error;
        Assert.False(catalog.TryCreatePreview(optional.Type, out preview, out error));
        Assert.Contains("unavailable", error);
    }

    [Fact]
    public void PreviewIsIsolatedAndDiscardIsIdempotent()
    {
        int disposed = 0;
        ContentCatalog catalog = ContentCatalog.FromEntries(
            new[] { BaseCreatorCatalog.BellIron },
            metadata => new PreviewInstance(metadata, () => disposed++));

        PreviewInstance preview;
        string error;
        Assert.True(catalog.TryCreatePreview(BaseCreatorCatalog.BellIronType, out preview, out error), error);
        Assert.True(preview.IsGameplayIsolated);
        Assert.False(preview.AffectsRoomOverlay);
        preview.Dispose();
        preview.Dispose();
        Assert.True(preview.IsDisposed);
        Assert.Equal(1, disposed);
    }

    [Fact]
    public void PreviewFollowsCursorUntilDisposed()
    {
        int updates = 0;
        PreviewInstance preview = new PreviewInstance(BaseCreatorCatalog.BellIron, null, () => updates++);

        preview.FollowCursor();
        Assert.Equal(1, updates);

        preview.Dispose();
        preview.FollowCursor();
        Assert.Equal(1, updates);
    }

    [Fact]
    public void SearchFiltersByDisplayTextAndCategory()
    {
        ContentCatalog catalog = ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron });

        Assert.Single(catalog.Search("iron", "Traps"));
        Assert.Empty(catalog.Search("iron", "Platforms"));
        Assert.Empty(catalog.Search("missing", null));
    }
}
