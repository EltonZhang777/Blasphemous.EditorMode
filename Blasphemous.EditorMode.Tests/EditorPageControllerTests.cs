using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Editing;
using Blasphemous.EditorMode.Framework;
using Blasphemous.EditorMode.Lifecycle;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class EditorPageControllerTests
{
    [Fact]
    public void Multiple_overlapping_additions_are_deduplicated_and_stably_ordered()
    {
        RuntimePlacementManager manager = CreateManager(out _);
        PlacementRecord first = Place(manager, "second");
        PlacementRecord second = Place(manager, "first");
        FakeHandle firstHandle = Handle(manager, first.Id);
        FakeHandle secondHandle = Handle(manager, second.Id);
        EditorPageController pages = new(manager);
        ApplyReport report = new ApplyReport();

        Assert.True(
            manager.TryPresentCandidates(
                new[] { firstHandle, secondHandle, firstHandle },
                report),
            report.ToSummary("candidates"));

        Assert.Equal(
            new[] { second.Id, first.Id },
            manager.Candidates.Select(candidate => candidate.Id));
        Assert.All(manager.Candidates, candidate => Assert.Equal(EditorPage.Additions, candidate.Page));
        Assert.Null(manager.Selected);
        Assert.Contains(report.Entries, entry => entry.Code == ReportCodes.SelectionCandidatesAvailable);
        Assert.Equal(EditorPage.Additions, pages.ActivePage);
    }

    [Fact]
    public void Multiple_candidates_keep_existing_selection_until_a_row_is_chosen()
    {
        RuntimePlacementManager manager = CreateManager(out _);
        PlacementRecord first = Place(manager, "first");
        PlacementRecord second = Place(manager, "second");
        ApplyReport report = new ApplyReport();
        Assert.True(manager.TrySelect(first.Id, report, out _));

        Assert.True(manager.TryPresentCandidates(
            new[] { Handle(manager, second.Id), Handle(manager, first.Id) },
            report));
        Assert.Equal(first.Id, manager.Selected.Id);

        Assert.True(manager.TryChooseCandidate("bell-iron/" + second.Id, report, out PlacementRecord selected));
        Assert.Equal(second.Id, selected.Id);
        Assert.Equal(second.Id, manager.Selected.Id);
    }

    [Fact]
    public void A_single_candidate_is_selected_immediately()
    {
        RuntimePlacementManager manager = CreateManager(out _);
        PlacementRecord placement = Place(manager, "only");
        ApplyReport report = new ApplyReport();

        Assert.True(manager.TryPresentCandidates(
            new[] { Handle(manager, placement.Id) },
            report));

        Assert.Equal(placement.Id, manager.Selected.Id);
        Assert.Single(manager.Candidates);
        Assert.Contains(report.Entries, entry => entry.Code == ReportCodes.SelectionCandidateSelected);
    }

    [Fact]
    public void Zero_candidates_report_a_page_specific_result_without_clearing_selection()
    {
        RuntimePlacementManager manager = CreateManager(out _);
        PlacementRecord placement = Place(manager, "kept");
        ApplyReport report = new ApplyReport();
        Assert.True(manager.TrySelect(placement.Id, report, out _));

        Assert.False(manager.TryPresentCandidates(Array.Empty<IRuntimePlacementHandle>(), report));

        Assert.Equal(placement.Id, manager.Selected.Id);
        Assert.Empty(manager.Candidates);
        Assert.Contains(report.Entries, entry => entry.Code == ReportCodes.SelectionNoCandidates);
        Assert.Contains("Additions", Assert.Single(report.Entries, entry => entry.Code == ReportCodes.SelectionNoCandidates).Message);
    }

    [Fact]
    public void Switching_pages_preserves_each_pages_selection_and_candidate_list()
    {
        RuntimePlacementManager manager = CreateManager(out _);
        PlacementRecord first = Place(manager, "first");
        PlacementRecord second = Place(manager, "second");
        EditorPageController pages = new(manager);
        ApplyReport report = new ApplyReport();
        Assert.True(manager.TryPresentCandidates(
            new[] { Handle(manager, first.Id), Handle(manager, second.Id) },
            report));
        Assert.True(manager.TrySelect(first.Id, report, out _));

        Assert.True(pages.TrySwitchPage(report));

        Assert.Equal(EditorPage.OriginalObjects, pages.ActivePage);
        Assert.Equal(first.Id, manager.Selected.Id);
        Assert.Equal(2, manager.Candidates.Count);
        Assert.Empty(pages.OriginalCandidates);

        Assert.True(pages.TrySwitchPage(report));
        Assert.Equal(EditorPage.Additions, pages.ActivePage);
        Assert.Equal(first.Id, manager.Selected.Id);
        Assert.Equal(2, manager.Candidates.Count);
    }

    [Fact]
    public void Switching_pages_preserves_additions_history_and_only_routes_it_on_that_page()
    {
        RuntimePlacementManager manager = CreateManager(out _);
        PlacementRecord placement = Place(manager, "history");
        EditorPageController pages = new(manager);
        ApplyReport report = new ApplyReport();
        Assert.True(manager.TryMove(
            placement.Id,
            new PlacementTransform(9f, 8f, 7f, 0f, 0f, 0f, 1f, 1f, 1f),
            report),
            report.ToSummary("move"));

        Assert.True(pages.TrySwitchPage(report));
        Assert.False(pages.TryUndo(report));

        Assert.True(pages.TrySwitchPage(report));
        Assert.True(pages.TryUndo(report), report.ToSummary("undo"));
        Assert.True(manager.Session.TryGetPlacement(placement.Id, out PlacementRecord restored));
        Assert.Equal(PlacementTransform.Default, restored.Transform);
    }

    [Fact]
    public void Candidate_resolution_excludes_original_runtime_handles_and_non_projections()
    {
        RuntimePlacementManager manager = CreateManager(out _);
        PlacementRecord placement = Place(manager, "owned");
        ApplyReport report = new ApplyReport();

        Assert.False(manager.TryPresentCandidates(
            new IRuntimePlacementHandle[]
            {
                new FakeHandle("original", "original", true),
                new FakeHandle("missing", "bell-iron", false)
            },
            report));

        Assert.Empty(manager.Candidates);
        Assert.Null(manager.Selected);
        Assert.Contains(report.Entries, entry => entry.Code == ReportCodes.SelectionNoCandidates);
        Assert.NotEmpty(manager.OwnedPlacementIds);
        Assert.NotNull(placement);
    }

    [Fact]
    public void Reset_without_an_original_catalog_still_clears_additions_history()
    {
        RuntimePlacementManager manager = CreateManager(out LevelEditSession session);
        EditorPageController pages = new(manager);
        ApplyReport report = new();
        PlacementRecord placement;
        Assert.True(
            manager.TryPlace(
                BaseCreatorCatalog.BellIronType,
                PlacementTransform.Default,
                [],
                report,
                out placement),
            report.ToSummary("place"));
        Assert.True(manager.History.CanUndo);

        ApplyReport resetReport = new();
        Assert.True(pages.TryReset(resetReport), resetReport.ToSummary("reset"));

        Assert.Empty(session.Placements);
        Assert.Empty(manager.OwnedPlacementIds);
        Assert.False(manager.History.CanUndo);
        Assert.False(manager.History.CanRedo);
        Assert.Equal(LevelEditSessionState.Clean, session.State);
    }

    private static PlacementRecord Place(RuntimePlacementManager manager, string id)
    {
        LevelEditSession session = GetSession(manager);
        PlacementRecord placement = new(
            id,
            BaseCreatorCatalog.BellIronType,
            PlacementTransform.Default,
            [],
            PlacementOwnership.ArchitectOwned);
        ApplyReport report = new ApplyReport();
        Assert.True(session.TryAddPlacement(placement, report), report.ToSummary("seed"));
        Assert.True(manager.TryProjectExisting(placement, report), report.ToSummary("project"));
        return placement;
    }

    private static FakeHandle Handle(RuntimePlacementManager manager, string id)
    {
        return new FakeHandle(id, "bell-iron", false);
    }

    private static LevelEditSession GetSession(RuntimePlacementManager manager)
    {
        return manager.Session;
    }

    private static RuntimePlacementManager CreateManager(out LevelEditSession session)
    {
        string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        FrameworkCodecResult parsed = LevelFrameworkAdapter.Parse(source);
        Assert.True(parsed.IsValid);
        FrameworkFileResult file = new(
            "D03Z02S06.json",
            source,
            parsed.Document,
            parsed.Diagnostics);
        RoomIdentity room;
        string error;
        Assert.True(RoomIdentity.TryCreate("D03Z02S06", "D03Z02S06_LOGIC", out room, out error), error);
        LevelEditSessionImportResult imported = LevelEditSession.Import(
            room,
            file,
            new[] { BaseCreatorCatalog.BellIron });
        Assert.True(imported.Succeeded, imported.StatusMessage);
        session = imported.Session;
        return new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            new FakeFactory());
    }

    private sealed class FakeFactory : IRuntimePlacementFactory
    {
        public IRuntimePlacementHandle Create(PlacementRecord placement)
        {
            return new FakeHandle(placement.Id, placement.Type, false);
        }
    }

    private sealed class FakeHandle : IRuntimePlacementHandle
    {
        internal FakeHandle(string placementId, string type, bool original)
        {
            PlacementId = placementId;
            Type = type;
            IsOriginal = original;
            IsAlive = true;
        }

        public string PlacementId { get; }
        public bool IsOriginal { get; }
        public bool IsAlive { get; }
        public string Type { get; }

        public bool TryApply(PlacementRecord placement, out string error)
        {
            error = null;
            return IsAlive;
        }

        public bool TryDestroy(out string error)
        {
            error = null;
            return true;
        }
    }
}