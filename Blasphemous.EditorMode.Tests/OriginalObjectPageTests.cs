using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Editing;
using Blasphemous.EditorMode.Framework;
using Blasphemous.EditorMode.Lifecycle;
using System;
using System.Collections.Generic;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class OriginalObjectPageTests
{
    private const string EmptySource = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";

    [Fact]
    public void Baseline_catalog_includes_addressable_vanilla_deco_layout_and_logic_objects()
    {
        OriginalObjectBaseline baseline = Baseline(
            Entry("deco-1", "decoration", "DECORATION/PROPS/LAMP", true),
            Entry("layout-1", "layout", "LAYOUT/ROOMS/ROOM_1", true),
            Entry("logic-1", "logic", "LOGIC/INTERACTABLES/LEVER", true));
        FakeOriginalPathResolver resolver = new(
            ("decoration", "DECORATION/PROPS/LAMP", "deco-1", true),
            ("layout", "LAYOUT/ROOMS/ROOM_1", "layout-1", true),
            ("logic", "LOGIC/INTERACTABLES/LEVER", "logic-1", true));

        RoomOverlayReconcileResult result = Reconcile(baseline, resolver);

        Assert.True(result.Succeeded, result.StatusMessage);
        Assert.Equal(3, result.Pages.OriginalCatalogCount);
        Assert.Equal(3, result.Pages.OriginalVisibleCount);
        Assert.Contains(
            result.Report.Entries,
            entry => entry.Code == ReportCodes.OriginalObjectCatalogReady);
    }

    [Fact]
    public void Non_baseline_provenance_is_rejected_item_by_item()
    {
        List<OriginalObjectSnapshotEntry> entries = new()
        {
            Entry("vanilla", "decoration", "DECORATION/VANILLA", true)
        };
        FakeOriginalPathResolver resolver = new();
        OriginalObjectProvenance[] rejected =
        {
            OriginalObjectProvenance.LevelFramework,
            OriginalObjectProvenance.EditorMode,
            OriginalObjectProvenance.LevelFrameworkExtended,
            OriginalObjectProvenance.ThirdParty,
            OriginalObjectProvenance.PostBaseline,
            OriginalObjectProvenance.Clone,
            OriginalObjectProvenance.Unknown
        };
        foreach (OriginalObjectProvenance provenance in rejected)
        {
            string key = provenance.ToString();
            string path = "DECORATION/" + key;
            entries.Add(Entry(key, "decoration", path, true, provenance));
            resolver.Add("decoration", path, key, true);
        }
        resolver.Add("decoration", "DECORATION/VANILLA", "vanilla", true);

        RoomOverlayReconcileResult result = Reconcile(Baseline([.. entries]), resolver);

        Assert.Equal(1, result.Pages.OriginalCatalogCount);
        foreach (OriginalObjectProvenance provenance in rejected)
        {
            string key = provenance.ToString();
            ApplyReportEntry diagnostic = Assert.Single(
                result.Report.Entries,
                entry => entry.Code == ReportCodes.OriginalObjectProvenanceRejected
                    && entry.Location.EndsWith("#" + key, StringComparison.Ordinal));
            Assert.Contains(provenance.ToString(), diagnostic.Message);
        }
    }

    [Fact]
    public void Clones_unaddressable_paths_and_different_resolved_objects_are_omitted()
    {
        OriginalObjectBaseline baseline = Baseline(
            Entry("vanilla", "decoration", "DECORATION/VANILLA", true),
            Entry("not-addressable", "layout", "LAYOUT/BROKEN", true, addressable: false),
            Entry("bad-scene", "renderer", "RENDERER/BROKEN", true),
            Entry("missing", "logic", "LOGIC/MISSING", true),
            Entry("replaced", "logic", "LOGIC/REPLACED", true));
        FakeOriginalPathResolver resolver = new(
            ("decoration", "DECORATION/VANILLA", "vanilla", true),
            ("logic", "LOGIC/REPLACED", "clone", true));

        RoomOverlayReconcileResult result = Reconcile(baseline, resolver);

        Assert.Equal(1, result.Pages.OriginalCatalogCount);
        Assert.Equal(4, result.Report.Entries.Count(
            entry => entry.Code == ReportCodes.OriginalObjectUnaddressable));
        Assert.Contains(
            result.Report.Entries,
            entry => entry.Code == ReportCodes.OriginalObjectUnaddressable
                && entry.Location.IndexOf("#replaced", StringComparison.Ordinal) >= 0
                && entry.Message.IndexOf("different object", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void Inactive_baseline_objects_are_visible_only_when_an_imported_operation_targets_them()
    {
        OriginalObjectBaseline baseline = Baseline(
            Entry("active", "decoration", "DECORATION/ACTIVE", true),
            Entry("inactive", "layout", "LAYOUT/INACTIVE", false));
        FakeOriginalPathResolver resolver = new(
            ("decoration", "DECORATION/ACTIVE", "active", true),
            ("layout", "LAYOUT/INACTIVE", "inactive", false));

        RoomOverlayReconcileResult hidden = Reconcile(baseline, resolver);
        Assert.Equal(2, hidden.Pages.OriginalCatalogCount);
        Assert.Equal(1, hidden.Pages.OriginalVisibleCount);

        const string targetedSource =
            "{\"additions\":[],\"modifications\":[{\"scene\":\"layout\",\"path\":\"LAYOUT/INACTIVE\",\"type\":\"editor-mode-empty\",\"id\":\"target\"}],\"deletions\":[]}";
        RoomOverlayReconcileResult targeted = Reconcile(baseline, resolver, source: targetedSource);

        Assert.Equal(2, targeted.Pages.OriginalCatalogCount);
        Assert.Equal(2, targeted.Pages.OriginalVisibleCount);
        Assert.Contains(
            targeted.Pages.OriginalObjects.VisibleEntries,
            entry => entry.Identity.DisplayText == "layout:LAYOUT/INACTIVE");
    }

    [Fact]
    public void Active_baseline_objects_remain_visible_when_the_runtime_projection_is_inactive()
    {
        OriginalObjectBaseline baseline = Baseline(
            Entry("baseline-active", "decoration", "DECORATION/ACTIVE", true));
        FakeOriginalPathResolver resolver = new(
            ("decoration", "DECORATION/ACTIVE", "baseline-active", false));

        RoomOverlayReconcileResult result = Reconcile(baseline, resolver);

        Assert.Equal(1, result.Pages.OriginalCatalogCount);
        Assert.Equal(1, result.Pages.OriginalVisibleCount);
        OriginalObjectCatalogEntry entry = Assert.Single(result.Pages.OriginalObjects.VisibleEntries);
        Assert.True(entry.IsActive);
        Assert.False(entry.IsRuntimeActive);
    }

    [Fact]
    public void A_scene_path_cannot_claim_two_baseline_objects()
    {
        OriginalObjectBaseline baseline = Baseline(
            Entry("first", "decoration", "DECORATION/DUPLICATE", true),
            Entry("second", "decoration", "DECORATION/DUPLICATE", true));
        FakeOriginalPathResolver resolver = new(
            ("decoration", "DECORATION/DUPLICATE", "first", true));

        RoomOverlayReconcileResult result = Reconcile(baseline, resolver);

        Assert.Equal(1, result.Pages.OriginalCatalogCount);
        Assert.Contains(
            result.Report.Entries,
            entry => entry.Code == ReportCodes.OriginalObjectUnaddressable
                && entry.Location.EndsWith("#second", StringComparison.Ordinal)
                && entry.Message.IndexOf("different object", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void Inactive_baseline_objects_targeted_by_a_deletion_remain_visible_and_diagnosed()
    {
        OriginalObjectBaseline baseline = Baseline(
            Entry("active", "decoration", "DECORATION/ACTIVE", true),
            Entry("inactive", "logic", "LOGIC/INACTIVE", false));
        FakeOriginalPathResolver resolver = new(
            ("decoration", "DECORATION/ACTIVE", "active", true),
            ("logic", "LOGIC/INACTIVE", "inactive", false));
        const string source =
            "{\"additions\":[],\"modifications\":[],\"deletions\":[{\"scene\":\"logic\",\"path\":\"LOGIC/INACTIVE\"}]}";

        RoomOverlayReconcileResult result = Reconcile(baseline, resolver, source: source);

        Assert.Equal(2, result.Pages.OriginalVisibleCount);
        Assert.Contains(
            result.Pages.OriginalObjects.VisibleEntries,
            entry => entry.Identity.DisplayText == "logic:LOGIC/INACTIVE");
        OriginalObjectOperation operation = Assert.Single(result.Pages.OriginalObjects.Operations);
        Assert.Equal(OriginalObjectOperationKind.Deletion, operation.Kind);
        Assert.True(operation.IsLocatable);
    }

    [Fact]
    public void Targeted_inactive_object_can_be_selected_read_only_from_an_imported_operation()
    {
        OriginalObjectBaseline baseline = Baseline(
            Entry("active", "decoration", "DECORATION/ACTIVE", true),
            Entry("inactive", "layout", "LAYOUT/INACTIVE", false));
        FakeOriginalPathResolver resolver = new(
            ("decoration", "DECORATION/ACTIVE", "active", true),
            ("layout", "LAYOUT/INACTIVE", "inactive", false));
        const string source =
            "{\"additions\":[],\"modifications\":[{\"scene\":\"layout\",\"path\":\"LAYOUT/INACTIVE\",\"type\":\"editor-mode-empty\",\"id\":\"inspect\"}],\"deletions\":[]}";
        RoomOverlayReconcileResult result = Reconcile(baseline, resolver, source: source);
        ApplyReport report = new();

        Assert.True(result.Pages.TrySwitchPage(report));
        Assert.True(result.Pages.TrySelectOriginal(
            "layout:LAYOUT/INACTIVE",
            report,
            out OriginalObjectCatalogEntry selected));

        Assert.Equal("inactive", selected.ObjectKey);
        Assert.Equal("layout:LAYOUT/INACTIVE", result.Pages.OriginalObjects.Selected.Identity.DisplayText);
        Assert.True(Assert.Single(result.Pages.OriginalObjects.Operations).IsLocatable);
    }

    [Fact]
    public void Missing_baseline_is_reported_instead_of_silently_enabling_original_objects()
    {
        FakeOriginalPathResolver resolver = new();

        RoomOverlayReconcileResult result = Reconcile(null, resolver);

        Assert.True(result.Succeeded, result.StatusMessage);
        Assert.Equal(0, result.Pages.OriginalCatalogCount);
        Assert.Contains(
            result.Report.Entries,
            entry => entry.Code == ReportCodes.OriginalObjectBaselineMissing
                && entry.Severity == ApplySeverity.Warning);
    }

    [Fact]
    public void Failed_baseline_capture_is_fail_closed_and_marks_the_session_partial()
    {
        ApplyReport capture = new();
        capture.Error(
            ReportCodes.OriginalObjectBaselineMissing,
            "D03Z02S06_DECO",
            "Target scene is not loaded.");
        OriginalObjectBaseline baseline = new("D03Z02S06", [], capture);

        RoomOverlayReconcileResult result = Reconcile(baseline, new FakeOriginalPathResolver());

        Assert.True(result.Succeeded, result.StatusMessage);
        Assert.Equal(0, result.Pages.OriginalCatalogCount);
        Assert.Equal(LevelEditSessionState.Partial, result.Session.State);
        Assert.Contains(
            result.Report.Entries,
            entry => entry.Code == ReportCodes.OriginalObjectBaselineMissing
                && entry.Severity == ApplySeverity.Error);
        Assert.DoesNotContain(
            result.Report.Entries,
            entry => entry.Code == ReportCodes.OriginalObjectCatalogReady);
    }

    [Fact]
    public void Baseline_from_another_room_is_rejected_fail_closed()
    {
        OriginalObjectBaseline baseline = new(
            "D04Z01S01",
            [Entry("foreign", "decoration", "DECORATION/FOREIGN", true)],
            new ApplyReport());
        FakeOriginalPathResolver resolver = new(
            ("decoration", "DECORATION/FOREIGN", "foreign", true));

        RoomOverlayReconcileResult result = Reconcile(baseline, resolver);

        Assert.Equal(0, result.Pages.OriginalCatalogCount);
        Assert.Contains(
            result.Report.Entries,
            entry => entry.Code == ReportCodes.OriginalObjectBaselineMissing
                && entry.Severity == ApplySeverity.Error
                && entry.Message.IndexOf("different room", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void Imported_modifications_and_deletions_are_listed_read_only_with_locator_status()
    {
        OriginalObjectBaseline baseline = Baseline(
            Entry("modified", "decoration", "DECORATION/MODIFIED", true),
            Entry("deleted", "logic", "LOGIC/DELETED", true));
        FakeOriginalPathResolver resolver = new(
            ("decoration", "DECORATION/MODIFIED", "modified", true),
            ("logic", "LOGIC/DELETED", "deleted", true));
        const string source =
            "{\"additions\":[],\"modifications\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/MODIFIED\",\"type\":\"editor-mode-empty\",\"id\":\"modify-1\"},{\"scene\":\"layout\",\"path\":\"LAYOUT/MISSING\",\"type\":\"editor-mode-empty\",\"id\":\"modify-2\"}],\"deletions\":[{\"scene\":\"logic\",\"path\":\"LOGIC/DELETED\"}]}";

        RoomOverlayReconcileResult result = Reconcile(baseline, resolver, source: source);

        Assert.Equal(3, result.Pages.OriginalObjects.Operations.Count);
        Assert.Collection(
            result.Pages.OriginalObjects.Operations,
            operation =>
            {
                Assert.Equal(OriginalObjectOperationKind.Modification, operation.Kind);
                Assert.Equal("modifications[0]", operation.Source);
                Assert.Equal("editor-mode-empty", operation.Type);
                Assert.Equal("modify-1", operation.Id);
                Assert.True(operation.IsLocatable);
                Assert.True(operation.IsImported);
                Assert.False(operation.IsPending);
            },
            operation =>
            {
                Assert.Equal(OriginalObjectOperationKind.Modification, operation.Kind);
                Assert.Equal("modifications[1]", operation.Source);
                Assert.False(operation.IsLocatable);
            },
            operation =>
            {
                Assert.Equal(OriginalObjectOperationKind.Deletion, operation.Kind);
                Assert.Equal("deletions[0]", operation.Source);
                Assert.True(operation.IsLocatable);
            });
        Assert.All(
            result.Pages.OriginalObjects.Operations,
            operation =>
            {
                Assert.True(operation.IsImported);
                Assert.False(operation.IsPending);
            });
        Assert.Contains(
            result.Report.Entries,
            entry => entry.Code == ReportCodes.OriginalObjectOperationUnlocatable
                && entry.Location == "modifications[1]");
    }

    [Fact]
    public void An_operation_is_unlocatable_when_its_path_resolves_to_another_baseline_object()
    {
        OriginalObjectBaseline baseline = Baseline(
            Entry("changed", "decoration", "DECORATION/CHANGED", true),
            Entry("other", "logic", "LOGIC/OTHER", false));
        FakeOriginalPathResolver resolver = new(
            ("decoration", "DECORATION/CHANGED", "other", false),
            ("logic", "LOGIC/OTHER", "other", false));
        const string source =
            "{\"additions\":[],\"modifications\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/CHANGED\",\"type\":\"editor-mode-empty\",\"id\":\"changed\"}],\"deletions\":[]}";

        RoomOverlayReconcileResult result = Reconcile(baseline, resolver, source: source);

        OriginalObjectOperation operation = Assert.Single(result.Pages.OriginalObjects.Operations);
        Assert.False(operation.IsLocatable);
        Assert.Equal(0, result.Pages.OriginalVisibleCount);
        Assert.Contains(
            result.Report.Entries,
            entry => entry.Code == ReportCodes.OriginalObjectOperationUnlocatable
                && entry.Location == "modifications[0]");
    }

    [Fact]
    public void Zero_pointer_hits_report_and_preserve_the_existing_original_selection()
    {
        RoomOverlayReconcileResult result = Reconcile(
            Baseline(Entry("one", "decoration", "DECORATION/ONE", true)),
            new FakeOriginalPathResolver(("decoration", "DECORATION/ONE", "one", true)));
        Assert.True(result.Pages.TrySwitchPage(new ApplyReport()));
        ApplyReport selection = new();
        Assert.True(result.Pages.TrySelectOriginal("decoration:DECORATION/ONE", selection, out _));
        FakeOriginalHitSource hits = new();
        ApplyReport report = new();

        Assert.False(result.Pages.TryPresentOriginalCandidates(hits, report));

        Assert.Equal("decoration:DECORATION/ONE", result.Pages.OriginalObjects.Selected.Identity.DisplayText);
        Assert.Empty(result.Pages.OriginalCandidates);
        Assert.Contains(report.Entries, entry => entry.Code == ReportCodes.SelectionNoCandidates);
    }

    [Fact]
    public void A_single_locatable_pointer_hit_selects_immediately()
    {
        RoomOverlayReconcileResult result = Reconcile(
            Baseline(Entry("one", "logic", "LOGIC/ONE", true)),
            new FakeOriginalPathResolver(("logic", "LOGIC/ONE", "one", true)));
        Assert.True(result.Pages.TrySwitchPage(new ApplyReport()));
        FakeOriginalHitSource hits = new(new OriginalObjectHit("one", "logic", "LOGIC/ONE"));
        ApplyReport report = new();

        Assert.True(result.Pages.TryPresentOriginalCandidates(hits, report));

        Assert.Single(result.Pages.OriginalCandidates);
        Assert.Equal("logic:LOGIC/ONE", result.Pages.OriginalObjects.Selected.Identity.DisplayText);
        Assert.Contains(report.Entries, entry => entry.Code == ReportCodes.SelectionCandidateSelected);
    }

    [Fact]
    public void Overlapping_pointer_hits_are_complete_deduplicated_stable_and_require_a_row_choice()
    {
        OriginalObjectBaseline baseline = Baseline(
            Entry("logic", "logic", "LOGIC/ONE", true),
            Entry("deco", "decoration", "DECORATION/ONE", true),
            Entry("layout", "layout", "LAYOUT/ONE", true),
            Entry("kept", "decoration", "DECORATION/KEPT", true));
        FakeOriginalPathResolver resolver = new(
            ("logic", "LOGIC/ONE", "logic", true),
            ("decoration", "DECORATION/ONE", "deco", true),
            ("layout", "LAYOUT/ONE", "layout", true),
            ("decoration", "DECORATION/KEPT", "kept", true));
        RoomOverlayReconcileResult result = Reconcile(baseline, resolver);
        Assert.True(result.Pages.TrySwitchPage(new ApplyReport()));
        ApplyReport select = new();
        Assert.True(result.Pages.TrySelectOriginal("decoration:DECORATION/KEPT", select, out _));
        FakeOriginalHitSource hits = new(
            new OriginalObjectHit("logic", "logic", "LOGIC/ONE"),
            new OriginalObjectHit("layout", "layout", "LAYOUT/ONE"),
            new OriginalObjectHit("deco", "decoration", "DECORATION/ONE"),
            new OriginalObjectHit("deco", "decoration", "DECORATION/ONE"));
        ApplyReport report = new();

        Assert.True(result.Pages.TryPresentOriginalCandidates(hits, report));

        Assert.Equal(
            new[]
            {
                "decoration:DECORATION/ONE",
                "layout:LAYOUT/ONE",
                "logic:LOGIC/ONE"
            },
            result.Pages.OriginalCandidates.Select(candidate => candidate.DisplayText));
        Assert.Equal("decoration:DECORATION/KEPT", result.Pages.OriginalObjects.Selected.Identity.DisplayText);

        ApplyReport choose = new();
        Assert.True(result.Pages.TryChooseOriginalCandidate(
            "layout:LAYOUT/ONE",
            choose,
            out OriginalObjectCatalogEntry selected));
        Assert.Equal("layout", selected.ObjectKey);
        Assert.Equal("layout:LAYOUT/ONE", result.Pages.OriginalObjects.Selected.Identity.DisplayText);
    }

    [Fact]
    public void Multiple_pointer_hits_keep_null_selection_until_a_row_is_chosen()
    {
        OriginalObjectBaseline baseline = Baseline(
            Entry("logic", "logic", "LOGIC/ONE", true),
            Entry("deco", "decoration", "DECORATION/ONE", true));
        FakeOriginalPathResolver resolver = new(
            ("logic", "LOGIC/ONE", "logic", true),
            ("decoration", "DECORATION/ONE", "deco", true));
        RoomOverlayReconcileResult result = Reconcile(baseline, resolver);
        ApplyReport report = new();
        Assert.True(result.Pages.TrySwitchPage(report));
        Assert.Null(result.Pages.OriginalObjects.Selected);

        Assert.True(result.Pages.TryPresentOriginalCandidates(
            new FakeOriginalHitSource(
                new OriginalObjectHit("logic", "logic", "LOGIC/ONE"),
                new OriginalObjectHit("deco", "decoration", "DECORATION/ONE")),
            report));

        Assert.Null(result.Pages.OriginalObjects.Selected);
        Assert.Equal(
            new[]
            {
                "decoration:DECORATION/ONE",
                "logic:LOGIC/ONE"
            },
            result.Pages.OriginalCandidates.Select(candidate => candidate.DisplayText));

        Assert.True(result.Pages.TryChooseOriginalCandidate(
            "logic:LOGIC/ONE",
            report,
            out OriginalObjectCatalogEntry selected));
        Assert.Equal("logic", selected.ObjectKey);
        Assert.Equal("logic:LOGIC/ONE", result.Pages.OriginalObjects.Selected.Identity.DisplayText);
    }

    [Fact]
    public void Non_baseline_and_mismatched_pointer_hits_are_diagnosed_without_replacing_candidates()
    {
        OriginalObjectBaseline baseline = Baseline(
            Entry("vanilla", "decoration", "DECORATION/VANILLA", true),
            Entry("replaced", "logic", "LOGIC/REPLACED", true));
        FakeOriginalPathResolver resolver = new(
            ("decoration", "DECORATION/VANILLA", "vanilla", true),
            ("logic", "LOGIC/REPLACED", "clone", true));
        RoomOverlayReconcileResult result = Reconcile(baseline, resolver);
        Assert.True(result.Pages.TrySwitchPage(new ApplyReport()));
        FakeOriginalHitSource hits = new(
            new OriginalObjectHit("vanilla", "decoration", "DECORATION/VANILLA"),
            new OriginalObjectHit("unknown", "decoration", "DECORATION/CLONE"),
            new OriginalObjectHit("replaced", "logic", "LOGIC/REPLACED"));
        ApplyReport report = new();

        Assert.True(result.Pages.TryPresentOriginalCandidates(hits, report));

        Assert.Single(result.Pages.OriginalCandidates);
        Assert.Equal("vanilla", result.Pages.OriginalObjects.Selected.ObjectKey);
        Assert.Equal(
            2,
            report.Entries.Count(entry => entry.Code == ReportCodes.OriginalObjectProvenanceRejected));
    }

    [Fact]
    public void A_hit_with_a_valid_baseline_path_but_a_different_object_key_is_rejected_as_a_clone()
    {
        RoomOverlayReconcileResult result = Reconcile(
            Baseline(Entry("vanilla", "decoration", "DECORATION/VANILLA", true)),
            new FakeOriginalPathResolver(("decoration", "DECORATION/VANILLA", "vanilla", true)));
        Assert.True(result.Pages.TrySwitchPage(new ApplyReport()));
        ApplyReport selection = new();
        Assert.True(result.Pages.TrySelectOriginal("decoration:DECORATION/VANILLA", selection, out _));
        ApplyReport report = new();

        Assert.False(result.Pages.TryPresentOriginalCandidates(
            new FakeOriginalHitSource(
                new OriginalObjectHit("clone", "decoration", "DECORATION/VANILLA")),
            report));

        Assert.Equal("decoration:DECORATION/VANILLA", result.Pages.OriginalObjects.Selected.Identity.DisplayText);
        Assert.Contains(
            report.Entries,
            entry => entry.Code == ReportCodes.OriginalObjectProvenanceRejected
                && entry.Message.IndexOf("same object", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void Unaddressable_pointer_hit_is_diagnosed_and_preserves_the_existing_selection()
    {
        RoomOverlayReconcileResult result = Reconcile(
            Baseline(Entry("one", "decoration", "DECORATION/ONE", true)),
            new FakeOriginalPathResolver(("decoration", "DECORATION/ONE", "one", true)));
        Assert.True(result.Pages.TrySwitchPage(new ApplyReport()));
        ApplyReport selection = new();
        Assert.True(result.Pages.TrySelectOriginal("decoration:DECORATION/ONE", selection, out _));
        FakeOriginalHitSource hits = new(
            new OriginalObjectHit(
                "unknown",
                "decoration",
                null,
                "The hit cannot be represented by a Framework path."));
        ApplyReport report = new();

        Assert.False(result.Pages.TryPresentOriginalCandidates(hits, report));

        Assert.Equal("decoration:DECORATION/ONE", result.Pages.OriginalObjects.Selected.Identity.DisplayText);
        Assert.Contains(
            report.Entries,
            entry => entry.Code == ReportCodes.OriginalObjectUnaddressable
                && entry.Message.IndexOf("Framework path", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void Original_selection_and_candidates_persist_independently_across_page_switches()
    {
        RoomOverlayReconcileResult result = Reconcile(
            Baseline(
                Entry("first", "decoration", "DECORATION/FIRST", true),
                Entry("second", "logic", "LOGIC/SECOND", true)),
            new FakeOriginalPathResolver(
                ("decoration", "DECORATION/FIRST", "first", true),
                ("logic", "LOGIC/SECOND", "second", true)));
        ApplyReport report = new();
        Assert.True(result.Pages.TrySwitchPage(report));
        Assert.True(result.Pages.TrySelectOriginal("decoration:DECORATION/FIRST", report, out _));
        Assert.True(result.Pages.TryPresentOriginalCandidates(
            new FakeOriginalHitSource(
                new OriginalObjectHit("first", "decoration", "DECORATION/FIRST"),
                new OriginalObjectHit("second", "logic", "LOGIC/SECOND")),
            report));

        Assert.True(result.Pages.TrySwitchPage(report));
        Assert.Null(result.Pages.OriginalObjects.Selected);
        Assert.Empty(result.Pages.OriginalCandidates);
        Assert.True(result.Pages.TrySwitchPage(report));

        Assert.Equal("decoration:DECORATION/FIRST", result.Pages.OriginalObjects.Selected.Identity.DisplayText);
        Assert.Equal(2, result.Pages.OriginalCandidates.Count);
    }

    [Fact]
    public void Original_candidate_commands_are_page_routed_and_do_not_mutate_additions_candidates()
    {
        RoomOverlayReconcileResult result = Reconcile(
            Baseline(Entry("one", "decoration", "DECORATION/ONE", true)),
            new FakeOriginalPathResolver(("decoration", "DECORATION/ONE", "one", true)));
        ApplyReport report = new();

        Assert.False(result.Pages.TryPresentOriginalCandidates(
            new FakeOriginalHitSource(
                new OriginalObjectHit("one", "decoration", "DECORATION/ONE")),
            report));
        Assert.Empty(result.Pages.OriginalCandidates);
        Assert.Empty(result.Pages.AdditionCandidates);

        Assert.True(result.Pages.TrySwitchPage(report));
        Assert.True(result.Pages.TryPresentOriginalCandidates(
            new FakeOriginalHitSource(
                new OriginalObjectHit("one", "decoration", "DECORATION/ONE")),
            report));
        Assert.Single(result.Pages.OriginalCandidates);
        Assert.Empty(result.Pages.AdditionCandidates);

        Assert.False(result.Pages.TryPresentAdditionCandidates([], report));
        Assert.Single(result.Pages.OriginalCandidates);
    }

    private static RoomOverlayReconcileResult Reconcile(
        OriginalObjectBaseline baseline,
        IOriginalObjectPathResolver resolver,
        IOriginalObjectHitSource hitSource = null,
        string source = EmptySource)
    {
        FrameworkCodecResult parsed = LevelFrameworkAdapter.Parse(source);
        Assert.True(parsed.IsValid);
        FrameworkFileResult file = new(
            "D03Z02S06.json",
            source,
            parsed.Document,
            parsed.Diagnostics);
        RoomIdentity room = CreateRoom();
        RoomOverlayReconciler reconciler = new(
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            new FakeFactory());

        return reconciler.Apply(room, file, baseline, resolver);
    }

    private static OriginalObjectBaseline Baseline(params OriginalObjectSnapshotEntry[] entries)
    {
        return new OriginalObjectBaseline(
            "D03Z02S06",
            entries,
            new ApplyReport());
    }

    private static OriginalObjectSnapshotEntry Entry(
        string objectKey,
        string scene,
        string path,
        bool active,
        OriginalObjectProvenance provenance = OriginalObjectProvenance.Baseline,
        bool addressable = true)
    {
        return new OriginalObjectSnapshotEntry(
            objectKey,
            scene,
            path,
            active,
            provenance,
            addressable);
    }

    private static RoomIdentity CreateRoom()
    {
        RoomIdentity room;
        string error;
        Assert.True(
            RoomIdentity.TryCreate("D03Z02S06", "D03Z02S06_LOGIC", out room, out error),
            error);
        return room;
    }

    private sealed class FakeOriginalPathResolver : IOriginalObjectPathResolver
    {
        private readonly Dictionary<string, OriginalObjectProjection> _projections = new(StringComparer.Ordinal);

        internal FakeOriginalPathResolver(params (string Scene, string Path, string ObjectKey, bool Active)[] entries)
        {
            foreach ((string scene, string path, string objectKey, bool active) in entries)
                Add(scene, path, objectKey, active);
        }

        internal void Add(string scene, string path, string objectKey, bool active)
        {
            _projections[Key(scene, path)] = new OriginalObjectProjection(objectKey, active);
        }

        public bool TryResolve(
            string frameworkLevelKey,
            string scene,
            string path,
            out OriginalObjectProjection projection,
            out string error)
        {
            error = null;
            if (_projections.TryGetValue(Key(scene, path), out projection))
                return true;

            error = "Path does not resolve.";
            return false;
        }

        private static string Key(string scene, string path)
        {
            return scene + "\n" + path;
        }
    }

    private sealed class FakeOriginalHitSource : IOriginalObjectHitSource
    {
        private readonly List<OriginalObjectHit> _hits;

        internal FakeOriginalHitSource(params OriginalObjectHit[] hits)
        {
            _hits = new List<OriginalObjectHit>(hits ?? []);
        }

        public bool TryFindAll(
            string frameworkLevelKey,
            out IList<OriginalObjectHit> hits,
            out string error)
        {
            error = null;
            hits = _hits;
            return _hits.Count > 0;
        }
    }

    private sealed class FakeFactory : IRuntimePlacementFactory
    {
        public IRuntimePlacementHandle Create(PlacementRecord placement)
        {
            return new FakeHandle(placement.Id);
        }
    }

    private sealed class FakeHandle : IRuntimePlacementHandle
    {
        internal FakeHandle(string id)
        {
            PlacementId = id;
            IsAlive = true;
        }

        public string PlacementId { get; }
        public bool IsOriginal { get { return false; } }
        public bool IsAlive { get; private set; }

        public bool TryApply(PlacementRecord placement, out string error)
        {
            error = null;
            return IsAlive;
        }

        public bool TryDestroy(out string error)
        {
            error = null;
            IsAlive = false;
            return true;
        }
    }
}