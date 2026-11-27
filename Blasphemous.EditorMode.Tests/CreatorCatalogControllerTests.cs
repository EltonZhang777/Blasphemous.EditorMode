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

    [Fact]
    public void SelectingACreatorStartsAtMostOneSourceLoad()
    {
        CreatorCatalogController controller = CreateController(3);

        Assert.True(controller.TrySelect("creator-01"));
        Assert.True(controller.TryBeginNextSourceLoad(out string type, out int generation));
        Assert.Equal("creator-01", type);
        Assert.Equal(CreatorCatalogSourceState.Loading, controller.GetSourceState("creator-01"));

        Assert.False(controller.TryBeginNextSourceLoad(out _, out _));
        Assert.Equal(CreatorCatalogSourceState.NotRequested, controller.GetSourceState("creator-00"));
        Assert.Equal(CreatorCatalogSourceState.NotRequested, controller.GetSourceState("creator-02"));

        Assert.True(controller.CompleteSourceLoad("creator-01", generation, true, null));
        Assert.Equal(CreatorCatalogSourceState.Ready, controller.GetSourceState("creator-01"));
        Assert.False(controller.TryBeginNextSourceLoad(out _, out _));
    }

    [Fact]
    public void LatestSelectionWinsAndSupersededSelectionsAreSkipped()
    {
        CreatorCatalogController controller = CreateController(4);

        Assert.True(controller.TrySelect("creator-01"));
        Assert.True(controller.TryBeginNextSourceLoad(out string first, out int generation));
        Assert.Equal("creator-01", first);

        Assert.True(controller.TrySelect("creator-02"));
        Assert.True(controller.TrySelect("creator-03"));
        Assert.Equal("creator-03", controller.SelectedType);

        Assert.True(controller.CompleteSourceLoad(first, generation, true, null));
        Assert.True(controller.TryBeginNextSourceLoad(out string next, out generation));
        Assert.Equal("creator-03", next);
        Assert.True(controller.CompleteSourceLoad(next, generation, true, null));

        Assert.False(controller.TryBeginNextSourceLoad(out _, out _));
        Assert.Equal(CreatorCatalogSourceState.NotRequested, controller.GetSourceState("creator-02"));
        Assert.Equal(CreatorCatalogSourceState.Ready, controller.GetSourceState("creator-03"));
        Assert.Equal("creator-03", controller.SelectedType);
    }

    [Fact]
    public void ReselectingAFailedCreatorRetriesButPagesAndRefreshesDoNot()
    {
        CreatorCatalogController controller = CreateController(6);

        Assert.True(controller.TrySelect("creator-01"));
        Assert.True(controller.TryBeginNextSourceLoad(out string type, out int generation));
        Assert.True(controller.CompleteSourceLoad(
            type,
            generation,
            false,
            "Scene source was unavailable."));
        Assert.Equal(CreatorCatalogSourceState.Failed, controller.GetSourceState("creator-01"));

        Assert.True(controller.NextPage());
        Assert.True(controller.PreviousPage());
        CreatorCatalogSnapshot failed = controller.CreateSnapshot();
        Assert.Contains("Scene source was unavailable", failed.DetailsText);
        Assert.Contains("select again to retry", failed.DetailsText);
        Assert.Contains("Category:", failed.DetailsText);
        Assert.Contains("Source:", failed.DetailsText);
        Assert.False(controller.TryBeginNextSourceLoad(out _, out _));

        Assert.True(controller.TrySelect("creator-01"));
        Assert.True(controller.TryBeginNextSourceLoad(out type, out generation));
        Assert.Equal("creator-01", type);
        Assert.True(controller.CompleteSourceLoad(type, generation, true, null));
        Assert.Equal(CreatorCatalogSourceState.Ready, controller.GetSourceState("creator-01"));
    }

    [Fact]
    public void LatestSelectionLoadsAfterAFailureAndCachesSurviveOtherFailures()
    {
        CreatorCatalogController controller = CreateController(4);

        Assert.True(controller.TrySelect("creator-00"));
        Assert.True(controller.TryBeginNextSourceLoad(out string type, out int generation));
        Assert.True(controller.CompleteSourceLoad(type, generation, true, null));

        Assert.True(controller.TrySelect("creator-01"));
        Assert.True(controller.TryBeginNextSourceLoad(out type, out generation));
        Assert.True(controller.CompleteSourceLoad(type, generation, false, "Loader failed."));

        Assert.True(controller.TrySelect("creator-02"));
        Assert.True(controller.TryBeginNextSourceLoad(out type, out generation));
        Assert.Equal("creator-02", type);
        Assert.True(controller.CompleteSourceLoad(type, generation, true, null));

        Assert.Equal(CreatorCatalogSourceState.Ready, controller.GetSourceState("creator-00"));
        Assert.Equal(CreatorCatalogSourceState.Failed, controller.GetSourceState("creator-01"));
        Assert.Equal(CreatorCatalogSourceState.Ready, controller.GetSourceState("creator-02"));
        Assert.Equal("creator-02", controller.SelectedType);
    }

    [Fact]
    public void ExistingLoadedSourceEntersReadyWithoutStartingALoad()
    {
        CreatorCatalogController controller = new(
            RuntimeCreatorInventory.FromRegistryEntries(
                new[]
                {
                    new RuntimeCreatorRegistryEntry("loaded", true, true),
                    new RuntimeCreatorRegistryEntry("pending", true, true)
                }),
            type => Probe(string.Equals(type, "loaded", StringComparison.Ordinal)));

        Assert.Equal(CreatorCatalogSourceState.Ready, controller.GetSourceState("loaded"));
        Assert.Equal(CreatorCatalogSourceState.NotRequested, controller.GetSourceState("pending"));

        Assert.True(controller.TrySelect("loaded"));

        Assert.False(controller.TryBeginNextSourceLoad(out _, out _));
        Assert.Equal(CreatorCatalogSourceState.Ready, controller.GetSourceState("loaded"));

        Assert.True(controller.TrySelect("pending"));
        Assert.True(controller.TryBeginNextSourceLoad(out string type, out _));
        Assert.Equal("pending", type);
    }

    [Fact]
    public void ExistingSourceProbeExceptionIsIsolatedAndVisible()
    {
        CreatorCatalogController controller = new(
            RuntimeCreatorInventory.FromRegistryEntries(
                new[]
                {
                    new RuntimeCreatorRegistryEntry("broken", true, true)
                }),
            _ => throw new InvalidOperationException("Result getter failed"));

        Assert.True(controller.TrySelect("broken"));
        CreatorCatalogSnapshot snapshot = controller.CreateSnapshot();

        Assert.Equal(CreatorCatalogSourceState.Failed, snapshot.SelectedSourceState);
        Assert.Contains("Result getter failed", controller.GetSourceReason("broken"));
        Assert.Contains("Result getter failed", snapshot.DetailsText);
        Assert.False(snapshot.Preview.Available);
        Assert.False(snapshot.Place.Available);

        CreatorSourceProbeFailure[] failures = controller.DrainProbeFailures();
        CreatorSourceProbeFailure failure = Assert.Single(failures);
        Assert.Equal("broken", failure.Type);
        Assert.Contains("Result getter failed", failure.Reason);
    }

    [Fact]
    public void PostLoadResultExceptionIsIsolatedAndVisible()
    {
        CreatorCatalogController controller = CreateController(1);
        Assert.True(controller.TrySelect("creator-00"));
        Assert.True(controller.TryBeginNextSourceLoad(out string type, out int generation));

        bool succeeded = CreatorSourceProbe.TryConfirmLoadedSource(
            () => throw new InvalidOperationException("Result getter failed"),
            out string failureReason);

        Assert.False(succeeded);
        Assert.Contains("Result getter failed", failureReason);
        Assert.True(controller.CompleteSourceLoad(
            type,
            generation,
            false,
            failureReason));

        CreatorCatalogSnapshot snapshot = controller.CreateSnapshot();
        Assert.Equal(CreatorCatalogSourceState.Failed, snapshot.SelectedSourceState);
        Assert.Contains("Result getter failed", snapshot.DetailsText);
        Assert.False(snapshot.Preview.Available);
        Assert.False(snapshot.Place.Available);
    }

    [Fact]
    public void SelectionTimeProbeExceptionIsRecordedForReporting()
    {
        int probes = 0;
        CreatorCatalogController controller = new(
            RuntimeCreatorInventory.FromRegistryEntries(
                new[] { new RuntimeCreatorRegistryEntry("creator", true, true) }),
            _ =>
            {
                probes++;
                return probes == 1
                    ? CreatorSourceProbeResult.NotLoaded()
                    : throw new InvalidOperationException("late Result getter failed");
            });

        Assert.Equal(
            CreatorCatalogSourceState.NotRequested,
            controller.GetSourceState("creator"));
        Assert.True(controller.TrySelect("creator"));
        Assert.False(controller.TryBeginNextSourceLoad(out _, out _));
        Assert.Equal(CreatorCatalogSourceState.Failed, controller.GetSourceState("creator"));

        CreatorSourceProbeFailure failure = Assert.Single(controller.DrainProbeFailures());
        Assert.Equal("creator", failure.Type);
        Assert.Contains("late Result getter failed", failure.Reason);
    }

    [Fact]
    public void RegistryProbeFailureKeepsItsClassification()
    {
        CreatorCatalogController controller = new(
            RuntimeCreatorInventory.FromRegistryEntries(
                new[] { new RuntimeCreatorRegistryEntry("missing", true, true) }),
            _ => CreatorSourceProbeResult.Failed("Registry lookup failed.", true));

        CreatorSourceProbeFailure failure = Assert.Single(controller.DrainProbeFailures());
        Assert.Equal("missing", failure.Type);
        Assert.Equal("Registry lookup failed.", failure.Reason);
        Assert.True(failure.RegistryFailure);
        Assert.Equal(CreatorCatalogSourceState.Failed, controller.GetSourceState("missing"));
    }

    [Fact]
    public void CreatorWithoutALoaderStaysUnavailableAndNeverLoads()
    {
        CreatorCatalogController controller = new(
            RuntimeCreatorInventory.FromRegistryEntries(
                new[]
                {
                    new RuntimeCreatorRegistryEntry("no-loader", false, true)
                }));

        Assert.True(controller.TrySelect("no-loader"));

        Assert.False(controller.TryBeginNextSourceLoad(out _, out _));
        Assert.Equal(CreatorCatalogSourceState.Unavailable, controller.GetSourceState("no-loader"));
        Assert.False(controller.PreviewAvailable);
        Assert.False(controller.PlaceAvailable);
    }

    [Fact]
    public void LoadingStateIsVisibleAndKeepsActionsDisabled()
    {
        CreatorCatalogController controller = CreateController(2);

        Assert.True(controller.TrySelect("creator-00"));
        Assert.True(controller.TryBeginNextSourceLoad(out _, out _));
        CreatorCatalogSnapshot snapshot = controller.CreateSnapshot();

        Assert.Equal(CreatorCatalogSourceState.Loading, snapshot.SelectedSourceState);
        Assert.Contains(snapshot.Rows, row => row.IsSelected && row.Status == "Loading");
        Assert.Contains("Source is loading", snapshot.DetailsText);
        Assert.False(snapshot.Preview.Available);
        Assert.False(snapshot.Place.Available);
        Assert.Contains("loading", snapshot.Preview.UnavailableReason.ToLowerInvariant());
    }

    [Fact]
    public void InvalidationDiscardsStaleCompletionWithoutChangingSelectionOrPage()
    {
        CreatorCatalogController controller = CreateController(12);
        Assert.True(controller.NextPage());
        Assert.True(controller.TrySelect("creator-06"));
        Assert.True(controller.TryBeginNextSourceLoad(out string type, out int generation));

        controller.InvalidateActiveSourceLoad();

        Assert.Equal("creator-06", controller.SelectedType);
        Assert.Equal(1, controller.PageIndex);
        Assert.Equal(CreatorCatalogSourceState.NotRequested, controller.GetSourceState(type));
        Assert.False(controller.TryBeginNextSourceLoad(out _, out _));
        Assert.False(controller.CompleteSourceLoad(type, generation, true, null));
        Assert.Equal(CreatorCatalogSourceState.NotRequested, controller.GetSourceState(type));
        Assert.False(controller.PreviewAvailable);
        Assert.False(controller.PlaceAvailable);
        Assert.True(controller.TrySelect("creator-07"));
        Assert.True(controller.TryBeginNextSourceLoad(out string next, out int nextGeneration));
        Assert.Equal("creator-07", next);
        Assert.True(controller.CompleteSourceLoad(next, nextGeneration, true, null));
        Assert.False(controller.PlaceAvailable);
    }

    [Fact]
    public void InvalidatedSourceLoadRequiresAFreshLoadEvenWhenAResultAppears()
    {
        bool resultAvailable = false;
        CreatorCatalogController controller = new(
            RuntimeCreatorInventory.FromRegistryEntries(
                new[] { new RuntimeCreatorRegistryEntry("creator", true, true) }),
            type => Probe(resultAvailable));

        Assert.True(controller.TrySelect("creator"));
        Assert.True(controller.TryBeginNextSourceLoad(out string type, out int generation));
        resultAvailable = true;

        controller.InvalidateActiveSourceLoad();
        Assert.False(controller.CompleteSourceLoad(type, generation, true, null));
        Assert.Equal(CreatorCatalogSourceState.NotRequested, controller.GetSourceState("creator"));

        Assert.True(controller.TrySelect("creator"));
        Assert.True(controller.TryBeginNextSourceLoad(out type, out generation));
        Assert.Equal("creator", type);
        Assert.Equal(CreatorCatalogSourceState.Loading, controller.GetSourceState("creator"));
        Assert.True(controller.CompleteSourceLoad(type, generation, true, null));
        Assert.Equal(CreatorCatalogSourceState.Ready, controller.GetSourceState("creator"));
    }

    [Fact]
    public void InvalidationDiscardsAnActiveRequiredLoadBeforeItCanSeedSelection()
    {
        bool resultAvailable = false;
        CreatorCatalogController controller = new(
            RuntimeCreatorInventory.FromRegistryEntries(
                new[] { new RuntimeCreatorRegistryEntry("creator", true, true) }),
            type => Probe(resultAvailable));

        controller.RequireSourceLoad("creator");
        Assert.True(controller.TrySelect("creator"));
        Assert.True(controller.TryBeginNextSourceLoad(out string type, out int generation));
        resultAvailable = true;

        controller.InvalidateActiveSourceLoad();

        Assert.Equal(CreatorCatalogSourceState.NotRequested, controller.GetSourceState(type));
        Assert.False(controller.CompleteSourceLoad(type, generation, true, null));
        Assert.Equal(CreatorCatalogSourceState.NotRequested, controller.GetSourceState(type));

        Assert.True(controller.TrySelect("creator"));
        Assert.True(controller.TryBeginNextSourceLoad(out type, out generation));
        Assert.Equal("creator", type);
        Assert.Equal(CreatorCatalogSourceState.Loading, controller.GetSourceState(type));
    }

    [Fact]
    public void ReinitializedControllerDoesNotTrustAnInvalidatedRuntimeResult()
    {
        bool resultAvailable = false;
        RuntimeCreatorInventory inventory = RuntimeCreatorInventory.FromRegistryEntries(
            new[] { new RuntimeCreatorRegistryEntry("creator", true, true) });
        CreatorCatalogController original = new(inventory, type => Probe(resultAvailable));

        Assert.True(original.TrySelect("creator"));
        Assert.True(original.TryBeginNextSourceLoad(out string type, out int generation));
        resultAvailable = true;
        original.InvalidateActiveSourceLoad();
        Assert.False(original.CompleteSourceLoad(type, generation, true, null));

        CreatorCatalogController reinitialized = new(
            inventory,
            reinitializedType => Probe(resultAvailable),
            original.GetInvalidatedLoadedSourceTypes());

        Assert.Equal(
            CreatorCatalogSourceState.NotRequested,
            reinitialized.GetSourceState("creator"));
        Assert.True(reinitialized.TrySelect("creator"));
        Assert.True(reinitialized.TryBeginNextSourceLoad(out type, out generation));
        Assert.Equal(CreatorCatalogSourceState.Loading, reinitialized.GetSourceState(type));
    }

    [Fact]
    public void ResetInvalidatesActiveLoadsAndClearsCachedStates()
    {
        CreatorCatalogController controller = CreateController(3);
        Assert.True(controller.TrySelect("creator-00"));
        Assert.True(controller.TryBeginNextSourceLoad(out string type, out int generation));
        Assert.True(controller.CompleteSourceLoad(type, generation, true, null));
        Assert.True(controller.TrySelect("creator-01"));
        Assert.True(controller.TryBeginNextSourceLoad(out type, out generation));
        Assert.True(controller.CompleteSourceLoad(type, generation, false, "Loader failed."));
        Assert.True(controller.TrySelect("creator-02"));
        Assert.True(controller.TryBeginNextSourceLoad(out type, out generation));

        controller.ResetSourceStates();

        Assert.Equal(CreatorCatalogSourceState.NotRequested, controller.GetSourceState("creator-00"));
        Assert.Equal(CreatorCatalogSourceState.NotRequested, controller.GetSourceState("creator-01"));
        Assert.Equal(CreatorCatalogSourceState.NotRequested, controller.GetSourceState("creator-02"));
        Assert.False(controller.TryBeginNextSourceLoad(out _, out _));
        Assert.False(controller.CompleteSourceLoad(type, generation, true, null));
        Assert.True(controller.TrySelect("creator-00"));
        Assert.True(controller.TryBeginNextSourceLoad(out string next, out int nextGeneration));
        Assert.Equal("creator-00", next);
        Assert.True(controller.CompleteSourceLoad(next, nextGeneration, true, null));
    }

    [Fact]
    public void ResetReusesSourcesReportedByTheRuntimeProbe()
    {
        CreatorCatalogController controller = new(
            RuntimeCreatorInventory.FromRegistryEntries(
                new[]
                {
                    new RuntimeCreatorRegistryEntry("loaded", true, true),
                    new RuntimeCreatorRegistryEntry("pending", true, true)
                }),
            type => Probe(string.Equals(type, "loaded", StringComparison.Ordinal)));

        Assert.True(controller.TrySelect("pending"));
        Assert.True(controller.TryBeginNextSourceLoad(out string type, out int generation));
        Assert.True(controller.CompleteSourceLoad(type, generation, false, "Loader failed."));

        controller.ResetSourceStates();

        Assert.Equal(CreatorCatalogSourceState.Ready, controller.GetSourceState("loaded"));
        Assert.Equal(CreatorCatalogSourceState.NotRequested, controller.GetSourceState("pending"));
    }

    [Fact]
    public void RequiredRoomSourcesLoadSeriallyBeforeTheLatestSelection()
    {
        CreatorCatalogController controller = CreateController(4);
        controller.RequireSourceLoad("creator-01");
        controller.RequireSourceLoad("creator-02");
        controller.RequireSourceLoad("creator-01");
        Assert.True(controller.TrySelect("creator-00"));

        Assert.True(controller.TryBeginNextSourceLoad(out string type, out int generation));
        Assert.Equal("creator-01", type);
        Assert.True(controller.CompleteSourceLoad(type, generation, true, null));

        Assert.True(controller.TryBeginNextSourceLoad(out type, out generation));
        Assert.Equal("creator-02", type);
        Assert.True(controller.CompleteSourceLoad(type, generation, true, null));

        Assert.True(controller.TryBeginNextSourceLoad(out type, out generation));
        Assert.Equal("creator-00", type);
        Assert.True(controller.CompleteSourceLoad(type, generation, true, null));

        Assert.False(controller.TryBeginNextSourceLoad(out _, out _));
    }

    [Fact]
    public void FailedRequiredLoadForTheSelectionDoesNotRetryUntilReselected()
    {
        CreatorCatalogController controller = CreateController(2);
        controller.RequireSourceLoad("creator-00");
        Assert.True(controller.TrySelect("creator-00"));

        Assert.True(controller.TryBeginNextSourceLoad(out string type, out int generation));
        Assert.Equal("creator-00", type);
        Assert.True(controller.CompleteSourceLoad(type, generation, false, "Loader failed."));

        Assert.False(controller.TryBeginNextSourceLoad(out _, out _));
        Assert.True(controller.TrySelect("creator-00"));
        Assert.True(controller.TryBeginNextSourceLoad(out type, out generation));
        Assert.Equal("creator-00", type);
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

    private static CreatorSourceProbeResult Probe(bool loaded)
    {
        return loaded
            ? CreatorSourceProbeResult.Ready()
            : CreatorSourceProbeResult.NotLoaded();
    }
}