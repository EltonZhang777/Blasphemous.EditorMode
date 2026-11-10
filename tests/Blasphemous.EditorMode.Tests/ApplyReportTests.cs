using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Framework;
using System;
using System.Collections.Generic;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class ApplyReportTests
{
    [Fact]
    public void Framework_and_dependency_failures_receive_actionable_codes()
    {
        var report = new ApplyReport();
        report.MergeFrameworkDiagnostics(LevelFrameworkAdapter.Parse("not-json").Diagnostics);
        report.MergeFrameworkDiagnostics(LevelFrameworkAdapter.Parse("{\"additions\":[]}").Diagnostics);
        report.MergeDependencyDiagnostics(
            DependencyGate.Evaluate(
                new[] { new DependencyRequirement("Framework", new Version(2, 0)) },
                new InstalledDependency[0]).Diagnostics);

        Assert.Contains(report.Entries, x => x.Code == "JSON_SYNTAX" && x.Severity == ApplySeverity.Error);
        Assert.Contains(report.Entries, x => x.Code == "JSON_SCHEMA");
        Assert.Contains(report.Entries, x => x.Code == "DEPENDENCY_MISSING");
        Assert.True(report.HasErrors);
        Assert.True(report.SkippedCount >= 3);
    }

    [Fact]
    public void The_same_report_is_published_to_ui_and_log_surfaces()
    {
        var report = new ApplyReport();
        report.Applied();
        report.Warning("TEST_WARNING", "item[1]", "visible warning");
        var surface = new ApplyReportSurface();
        var logs = new List<string>();
        var publisher = new ApplyReportPublisher(surface, logs.Add);

        publisher.Publish("import", report);

        Assert.Same(report, surface.LastReport);
        Assert.Equal(report.ToSummary("import"), surface.UiText);
        Assert.Contains("partial", surface.UiText);
        Assert.Contains(logs, x => x == surface.UiText);
        Assert.Contains(logs, x => x.Contains("TEST_WARNING"));
    }

    [Fact]
    public void A_failed_room_import_exposes_an_actionable_recovery_report()
    {
        var surface = new ApplyReportSurface();
        Assert.False(surface.HasActionableMessage);

        var report = new ApplyReport();
        report.Error("JSON_SYNTAX", "room.json", "Malformed JSON.");
        surface.Publish("Room import", report);

        Assert.True(surface.HasActionableMessage);
        Assert.Contains("JSON_SYNTAX", surface.ActionableMessage);
    }

    [Fact]
    public void Failed_edit_flow_returns_to_the_previous_safe_state_after_recovery()
    {
        var report = new ApplyReport();
        var recovery = new EditorFlowRecovery(EditorFlowState.Editing);

        Assert.False(recovery.Run("property", () => false, () => true, report));
        Assert.Equal(EditorFlowState.Editing, recovery.State);
        Assert.Contains(report.Entries, x => x.Code == "FLOW_RECOVERED");
    }

    [Fact]
    public void Failed_recovery_reduces_state_to_room_loaded()
    {
        var report = new ApplyReport();
        var recovery = new EditorFlowRecovery(EditorFlowState.Editing);

        Assert.False(recovery.Run("export", () => { throw new InvalidOperationException("write"); }, () => false, report));
        Assert.Equal(EditorFlowState.RoomLoaded, recovery.State);
        Assert.Contains(report.Entries, x => x.Code == "FLOW_RECOVERY_FAILED");
    }
}
