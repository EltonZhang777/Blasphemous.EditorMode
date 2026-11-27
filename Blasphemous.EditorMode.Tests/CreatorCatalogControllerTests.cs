using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Diagnostics;
using System;
using System.Linq;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class CreatorCatalogControllerTests
{
    [Fact]
    public void Fixed_pages_expose_five_rows_and_boundary_controls()
    {
        CreatorCatalogController controller = CreateController(12);

        CreatorCatalogSnapshot first = controller.CreateSnapshot();
        Assert.Equal(5, first.Rows.Count);
        Assert.Equal(0, first.PageIndex);
        Assert.Equal(3, first.PageCount);
        Assert.False(first.HasPreviousPage);
        Assert.True(first.HasNextPage);

        Assert.True(controller.NextPage());
        CreatorCatalogSnapshot second = controller.CreateSnapshot();
        Assert.Equal(1, second.PageIndex);
        Assert.True(second.HasPreviousPage);
        Assert.True(second.HasNextPage);

        Assert.True(controller.NextPage());
        CreatorCatalogSnapshot last = controller.CreateSnapshot();
        Assert.Equal(2, last.PageIndex);
        Assert.Equal(2, last.Rows.Count);
        Assert.True(last.HasPreviousPage);
        Assert.False(last.HasNextPage);
        Assert.False(controller.NextPage());

        Assert.True(controller.PreviousPage());
        Assert.True(controller.PreviousPage());
        Assert.False(controller.PreviousPage());
        Assert.Equal(0, controller.PageIndex);
    }

    [Fact]
    public void Selection_persists_across_pages_without_changing_source_state()
    {
        CreatorCatalogController controller = CreateController(7);
        Assert.True(controller.TrySelect("creator-01"));
        CreatorCatalogSourceState stateBefore = controller.GetSourceState("creator-01");

        Assert.True(controller.NextPage());
        CreatorCatalogSnapshot second = controller.CreateSnapshot();
        Assert.Equal("creator-01", second.SelectedEntry.Type);
        Assert.DoesNotContain(second.Rows, row => row.IsSelected);
        Assert.Equal(stateBefore, controller.GetSourceState("creator-01"));

        Assert.True(controller.PreviousPage());
        CreatorCatalogSnapshot first = controller.CreateSnapshot();
        Assert.Contains(
            first.Rows,
            row => row.IsSelected
                && string.Equals(row.Metadata.Type, "creator-01", StringComparison.Ordinal));
        Assert.Equal(CreatorCatalogSourceState.NotRequested, controller.GetSourceState("creator-01"));
    }

    [Fact]
    public void Selection_and_action_targets_use_the_exact_registry_key()
    {
        CreatorCatalogController controller = new(
            RuntimeCreatorInventory.FromRegistryEntries(
                new[]
                {
                    new RuntimeCreatorRegistryEntry("Uncurated", true, true),
                    new RuntimeCreatorRegistryEntry("uncurated", true, true)
                }));

        Assert.True(controller.TrySelect("Uncurated"));
        Assert.Equal("Uncurated", controller.SelectedType);
        Assert.True(controller.TrySetSourceState(
            "Uncurated",
            CreatorCatalogSourceState.Ready,
            null));

        Assert.True(controller.TrySelect("uncurated"));
        Assert.Equal("uncurated", controller.SelectedType);
        Assert.Equal(CreatorCatalogSourceState.NotRequested, controller.GetSourceState("uncurated"));
        Assert.Equal(CreatorCatalogSourceState.Ready, controller.GetSourceState("Uncurated"));

        Assert.False(controller.TrySelect("UNCURATED"));
        Assert.Equal("uncurated", controller.SelectedType);
    }

    [Fact]
    public void Action_availability_uses_source_and_capability_state()
    {
        CreatorCatalogController controller = new(
            RuntimeCreatorInventory.FromRegistryEntries(
                new[]
                {
                    new RuntimeCreatorRegistryEntry("preview-only", true, false),
                    new RuntimeCreatorRegistryEntry("no-loader", false, true),
                    new RuntimeCreatorRegistryEntry("placeable", true, true)
                }));

        CreatorCatalogSnapshot empty = controller.CreateSnapshot();
        Assert.False(empty.Preview.Available);
        Assert.False(empty.Place.Available);
        Assert.Contains("Select a creator", empty.Preview.UnavailableReason);

        Assert.True(controller.TrySelect("preview-only"));
        CreatorCatalogSnapshot pending = controller.CreateSnapshot();
        Assert.False(pending.Preview.Available);
        Assert.Contains("not been loaded", pending.Preview.UnavailableReason);

        Assert.True(controller.TrySetSourceState(
            "preview-only",
            CreatorCatalogSourceState.Loading,
            "Source is loading."));
        CreatorCatalogSnapshot loading = controller.CreateSnapshot();
        Assert.Equal(CreatorCatalogSourceState.Loading, loading.SelectedSourceState);
        Assert.False(loading.Preview.Available);
        Assert.Contains("loading", loading.Preview.UnavailableReason.ToLowerInvariant());

        Assert.True(controller.TrySetSourceState(
            "preview-only",
            CreatorCatalogSourceState.Ready,
            null));
        CreatorCatalogSnapshot previewOnly = controller.CreateSnapshot();
        Assert.True(previewOnly.Preview.Available);
        Assert.False(previewOnly.Place.Available);
        Assert.Contains("no modifier", previewOnly.Place.UnavailableReason);

        controller.SetPlacementReady(true);
        Assert.False(controller.PlaceAvailable);

        Assert.True(controller.TrySelect("placeable"));
        Assert.True(controller.TrySetSourceState(
            "placeable",
            CreatorCatalogSourceState.Ready,
            null));
        controller.SetPlacementReady(false, "Room Overlay is not ready.");
        Assert.True(controller.PreviewAvailable);
        Assert.False(controller.PlaceAvailable);
        Assert.Contains("Room Overlay", controller.PlaceUnavailableReason);

        controller.SetPlacementReady(true);
        Assert.True(controller.PlaceAvailable);
        Assert.Equal("placeable", controller.SelectedType);

        Assert.True(controller.TrySelect("no-loader"));
        Assert.False(controller.PreviewAvailable);
        Assert.False(controller.PlaceAvailable);
        Assert.Contains("no loader", controller.PreviewUnavailableReason);
    }

    [Fact]
    public void Empty_and_unavailable_registries_have_distinct_states()
    {
        CreatorCatalogController empty = new(
            RuntimeCreatorInventory.FromRegistryEntries(
                Array.Empty<RuntimeCreatorRegistryEntry>()));
        CreatorCatalogSnapshot emptySnapshot = empty.CreateSnapshot();
        Assert.Equal(RuntimeCreatorInventoryState.Empty, emptySnapshot.RegistryState);
        Assert.Empty(emptySnapshot.Rows);
        Assert.Equal(0, emptySnapshot.PageCount);
        Assert.Contains("No creators registered", emptySnapshot.DetailsText);

        CreatorCatalogController unavailable = new(
            RuntimeCreatorInventoryReader.Read(
                () => throw new InvalidOperationException("registry denied")));
        CreatorCatalogSnapshot unavailableSnapshot = unavailable.CreateSnapshot();
        Assert.Equal(RuntimeCreatorInventoryState.Unavailable, unavailableSnapshot.RegistryState);
        Assert.Empty(unavailableSnapshot.Rows);
        Assert.Contains("Registry unavailable", unavailableSnapshot.DetailsText);
        Assert.Contains("registry denied", unavailableSnapshot.DetailsText);
        Assert.NotEqual(emptySnapshot.DetailsText, unavailableSnapshot.DetailsText);
    }

    [Fact]
    public void Empty_inventory_with_only_invalid_entries_keeps_diagnostics_visible()
    {
        CreatorCatalogController controller = new(
            RuntimeCreatorInventory.FromRegistryEntries(
                new[]
                {
                    null,
                    new RuntimeCreatorRegistryEntry(" ", true, true)
                }));

        CreatorCatalogSnapshot snapshot = controller.CreateSnapshot();

        Assert.Equal(RuntimeCreatorInventoryState.Empty, snapshot.RegistryState);
        Assert.Equal(2, snapshot.Diagnostics.Count);
        Assert.Contains("No creators registered", snapshot.DetailsText);
        Assert.Contains("Diagnostics: 2", snapshot.DetailsText);
    }

    [Fact]
    public void Diagnostics_and_creator_failures_remain_visible()
    {
        CreatorCatalogController controller = new(
            RuntimeCreatorInventory.FromRegistryEntries(
                new[]
                {
                    null,
                    new RuntimeCreatorRegistryEntry("", true, true),
                    new RuntimeCreatorRegistryEntry("valid", true, true)
                }));

        CreatorCatalogSnapshot unselected = controller.CreateSnapshot();
        Assert.Equal(2, unselected.Diagnostics.Count);
        Assert.Contains("Diagnostics: 2", unselected.DetailsText);

        Assert.True(controller.TrySelect("valid"));
        CreatorCatalogSnapshot selected = controller.CreateSnapshot();
        Assert.Contains("Scene: Not declared | Path: Not declared", selected.DetailsText);
        Assert.Contains("Diagnostics: 2", selected.DetailsText);
        Assert.True(controller.TrySetSourceState(
            "valid",
            CreatorCatalogSourceState.Failed,
            "Scene unavailable."));
        CreatorCatalogSnapshot failed = controller.CreateSnapshot();
        Assert.Equal(CreatorCatalogSourceState.Failed, failed.SelectedSourceState);
        Assert.Contains("Scene unavailable", failed.DetailsText);
        Assert.Contains("Diagnostics: 2", failed.DetailsText);
        Assert.False(failed.Preview.Available);
        Assert.False(failed.Place.Available);
    }

    [Fact]
    public void Source_state_updates_reject_unknown_or_case_mismatched_keys()
    {
        CreatorCatalogController controller = new(
            RuntimeCreatorInventory.FromRegistryEntries(
                new[]
                {
                    new RuntimeCreatorRegistryEntry("Creator", true, true)
                }));

        Assert.False(controller.TrySetSourceState(
            "creator",
            CreatorCatalogSourceState.Ready,
            null));
        Assert.True(controller.TrySetSourceState(
            "Creator",
            CreatorCatalogSourceState.Ready,
            null));
        Assert.Equal(CreatorCatalogSourceState.Ready, controller.GetSourceState("Creator"));
        Assert.Equal(CreatorCatalogSourceState.Unavailable, controller.GetSourceState("creator"));
    }

    private static CreatorCatalogController CreateController(int count)
    {
        RuntimeCreatorRegistryEntry[] entries = Enumerable.Range(0, count)
            .Select(index => new RuntimeCreatorRegistryEntry(
                "creator-" + index.ToString("00"),
                true,
                true))
            .ToArray();
        return new CreatorCatalogController(
            RuntimeCreatorInventory.FromRegistryEntries(entries));
    }
}