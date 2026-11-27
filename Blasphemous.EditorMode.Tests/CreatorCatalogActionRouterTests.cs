using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Diagnostics;
using System;
using System.Collections.Generic;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class CreatorCatalogActionRouterTests
{
    [Fact]
    public void Selecting_another_creator_clears_the_previous_preview()
    {
        int disposed = 0;
        CreatorCatalogActionRouter router = CreateRouter(
            out CreatorCatalogController controller,
            out _,
            out _,
            () => disposed++);
        controller.TrySetSourceState("first", CreatorCatalogSourceState.Ready, null);
        Assert.True(router.TrySelect("first", out ApplyReport selectReport), selectReport?.ToSummary("select"));
        Assert.True(router.TryPreviewSelected(out ApplyReport previewReport), previewReport?.ToSummary("preview"));
        PreviewInstance preview = router.Preview;
        Assert.NotNull(preview);

        Assert.True(router.TrySelect("second", out selectReport), selectReport?.ToSummary("select"));

        Assert.True(preview.IsDisposed);
        Assert.Null(router.Preview);
        Assert.Equal(1, disposed);
    }

    [Fact]
    public void Place_without_preview_places_the_selected_creator()
    {
        CreatorCatalogActionRouter router = CreateRouter(
            out CreatorCatalogController controller,
            out _,
            out List<string> placedTypes,
            null);
        controller.TrySetSourceState("creator", CreatorCatalogSourceState.Ready, null);
        controller.SetPlacementReady(true);
        Assert.True(router.TrySelect("creator", out _));

        Assert.True(router.TryPlaceSelected(out ApplyReport report), report?.ToSummary("place"));

        Assert.Null(report);
        Assert.Equal("creator", Assert.Single(placedTypes));
        Assert.Null(router.Preview);
    }

    [Fact]
    public void Successful_place_preserves_the_current_preview()
    {
        int disposed = 0;
        CreatorCatalogActionRouter router = CreateRouter(
            out CreatorCatalogController controller,
            out _,
            out List<string> placedTypes,
            () => disposed++);
        controller.TrySetSourceState("creator", CreatorCatalogSourceState.Ready, null);
        controller.SetPlacementReady(true);
        Assert.True(router.TrySelect("creator", out _));
        Assert.True(router.TryPreviewSelected(out ApplyReport previewReport), previewReport?.ToSummary("preview"));
        PreviewInstance preview = router.Preview;

        Assert.True(router.TryPlaceSelected(out ApplyReport placeReport), placeReport?.ToSummary("place"));

        Assert.Equal("creator", Assert.Single(placedTypes));
        Assert.Same(preview, router.Preview);
        Assert.False(preview.IsDisposed);
        Assert.Equal(0, disposed);
    }

    [Fact]
    public void Unavailable_place_reports_the_reason_without_invoking_placement()
    {
        CreatorCatalogActionRouter router = CreateRouter(
            out CreatorCatalogController controller,
            out _,
            out List<string> placedTypes,
            null,
            hasModifier: false);
        controller.TrySetSourceState("creator", CreatorCatalogSourceState.Ready, null);
        controller.SetPlacementReady(true);
        Assert.True(router.TrySelect("creator", out _));

        Assert.False(router.TryPlaceSelected(out ApplyReport report));

        Assert.Empty(placedTypes);
        Assert.Contains(report.Entries, entry => entry.Code == ReportCodes.PlaceNotReady);
        Assert.Contains(
            report.Entries,
            entry => entry.Message.IndexOf("no modifier", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static CreatorCatalogActionRouter CreateRouter(
        out CreatorCatalogController controller,
        out ContentCatalog catalog,
        out List<string> placedTypes,
        Action onDispose,
        bool hasModifier = true)
    {
        RuntimeCreatorInventory inventory = RuntimeCreatorInventory.FromRegistryEntries(
            new[]
            {
                new RuntimeCreatorRegistryEntry("first", true, true),
                new RuntimeCreatorRegistryEntry("second", true, true),
                new RuntimeCreatorRegistryEntry("creator", true, hasModifier)
            });
        controller = new CreatorCatalogController(inventory);
        catalog = ContentCatalog.FromInventory(
            inventory,
            metadata => new PreviewInstance(metadata, onDispose));
        List<string> capturedTypes = new();
        CreatorCatalogActionRouter router = new(
            controller,
            catalog,
            type =>
            {
                capturedTypes.Add(type);
                return true;
            });
        placedTypes = capturedTypes;
        return router;
    }
}