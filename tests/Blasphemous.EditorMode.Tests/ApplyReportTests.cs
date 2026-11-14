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
        ApplyReport report = new ApplyReport();
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
    public void Counts_track_applied_skipped_failed_and_diagnostics_independently()
    {
        ApplyReport report = new ApplyReport();
        report.Applied();
        report.Applied();
        report.Info("INFO", "item[0]", "info entry");
        report.Warning("SKIPPED_WARNING", "item[1]", "skipped warning", true);
        report.Error("SKIPPED_ERROR", "item[2]", "skipped error");
        report.Error("FAILED_ERROR", "item[3]", "failed error", false);

        Assert.Equal(2, report.AppliedCount);
        Assert.Equal(2, report.SkippedCount);
        Assert.Equal(2, report.FailedCount);
        Assert.Equal(4, report.DiagnosticCount);
        Assert.True(report.HasErrors);
        Assert.True(report.IsPartial);
    }

    [Fact]
    public void Summary_has_stable_keys_units_counts_and_failure_status()
    {
        ApplyReport report = new ApplyReport();
        report.Applied();
        report.Warning("SKIPPED_WARNING", "item[1]", "skipped warning", true);
        report.Error("SKIPPED_ERROR", "item[2]", "skipped error");
        report.Error("FAILED_ERROR", "item[3]", "failed error", false);

        Assert.Equal(
            "import failed: Applied=1 item(s), Skipped=2 item(s), Failed=2 error(s), Diagnostic=3 entry(ies).",
            report.ToSummary("import"));
    }

    [Fact]
    public void Summary_severity_tracks_passed_partial_and_failed()
    {
        ApplyReport passed = new ApplyReport();
        passed.Info("INFO", "item[0]", "info entry");
        AssertSummarySeverity(passed, "passed", ApplySeverity.Info);

        ApplyReport partial = new ApplyReport();
        partial.Warning("WARNING", "item[1]", "warning entry");
        AssertSummarySeverity(partial, "partial", ApplySeverity.Warning);

        ApplyReport failed = new ApplyReport();
        failed.Warning("SKIPPED_WARNING", "item[1]", "skipped warning", true);
        failed.Error("ERROR", "item[2]", "error entry");
        AssertSummarySeverity(failed, "failed", ApplySeverity.Error);
    }

    [Fact]
    public void Verbose_off_publishes_only_the_summary()
    {
        ApplyReport report = new ApplyReport();
        report.Warning("HIDDEN_WARNING", "item[1]", "warning entry");
        report.Error("HIDDEN_ERROR", "item[2]", "error entry");
        ApplyReportSurface surface = new ApplyReportSurface();
        List<(ApplySeverity Severity, string Message)> logs = new List<(ApplySeverity Severity, string Message)>();
        ApplyReportPublisher publisher = new ApplyReportPublisher(
            surface,
            (severity, message) => logs.Add((severity, message)),
            false);

        publisher.Publish("import", report);

        (ApplySeverity Severity, string Message) log = Assert.Single(logs);
        Assert.Equal(ApplySeverity.Error, log.Severity);
        Assert.Equal(surface.UiText, log.Message);
        Assert.DoesNotContain("HIDDEN_WARNING", log.Message);
        Assert.DoesNotContain("HIDDEN_ERROR", log.Message);
    }

    [Fact]
    public void Verbose_on_publishes_summary_then_entries_with_their_own_severity()
    {
        ApplyReport report = new ApplyReport();
        report.Warning("VERBOSE_WARNING", "item[1]", "warning entry");
        report.Info("VERBOSE_INFO", "item[2]", "info entry");
        report.Error("VERBOSE_ERROR", "item[3]", "error entry", false);
        ApplyReportSurface surface = new ApplyReportSurface();
        List<(ApplySeverity Severity, string Message)> logs = new List<(ApplySeverity Severity, string Message)>();
        ApplyReportPublisher publisher = new ApplyReportPublisher(
            surface,
            (severity, message) => logs.Add((severity, message)),
            true);

        publisher.Publish("import", report);

        Assert.Equal(4, logs.Count);
        Assert.Equal(ApplySeverity.Error, logs[0].Severity);
        Assert.Equal(surface.UiText, logs[0].Message);
        Assert.Equal(
            (ApplySeverity.Warning, "[Warning] VERBOSE_WARNING item[1]: warning entry"),
            logs[1]);
        Assert.Equal(
            (ApplySeverity.Info, "[Info] VERBOSE_INFO item[2]: info entry"),
            logs[2]);
        Assert.Equal(
            (ApplySeverity.Error, "[Error] VERBOSE_ERROR item[3]: error entry"),
            logs[3]);
    }

    [Fact]
    public void Verbose_diagnostics_is_disabled_by_default()
    {
        EditorModeConfig config = new EditorModeConfig();

        Assert.False(config.VerboseDiagnostics);
    }

    [Fact]
    public void The_same_report_is_published_to_ui_and_log_surfaces()
    {
        ApplyReport report = new ApplyReport();
        report.Applied();
        report.Warning("TEST_WARNING", "item[1]", "visible warning");
        ApplyReportSurface surface = new ApplyReportSurface();
        List<(ApplySeverity Severity, string Message)> logs = new List<(ApplySeverity Severity, string Message)>();
        ApplyReportPublisher publisher = new ApplyReportPublisher(
            surface,
            (severity, message) => logs.Add((severity, message)),
            true);

        publisher.Publish("import", report);

        Assert.Same(report, surface.LastReport);
        Assert.Equal(report.ToSummary("import"), surface.UiText);
        Assert.Contains("partial", surface.UiText);
        Assert.Contains(logs, x => x.Severity == ApplySeverity.Warning && x.Message == surface.UiText);
        Assert.Contains(logs, x => x.Severity == ApplySeverity.Warning && x.Message.Contains("TEST_WARNING"));
    }

    [Fact]
    public void A_failed_room_import_exposes_an_actionable_recovery_report()
    {
        ApplyReportSurface surface = new ApplyReportSurface();
        Assert.False(surface.HasActionableMessage);

        ApplyReport report = new ApplyReport();
        report.Error("JSON_SYNTAX", "room.json", "Malformed JSON.");
        surface.Publish("Room import", report);

        Assert.True(surface.HasActionableMessage);
        Assert.Contains("JSON_SYNTAX", surface.ActionableMessage);
    }

    [Fact]
    public void An_error_takes_priority_over_an_earlier_warning()
    {
        ApplyReport report = new ApplyReport();
        report.Warning("FIRST_WARNING", "$", "first warning");
        report.Error("FIRST_ERROR", "$", "first error");
        report.Error("SECOND_ERROR", "$", "second error");
        ApplyReportSurface surface = new ApplyReportSurface();

        surface.Publish("apply", report);

        Assert.Equal("[FIRST_ERROR] first error", surface.ActionableMessage);
    }

    [Fact]
    public void The_first_warning_is_actionable_when_the_report_has_no_error()
    {
        ApplyReport report = new ApplyReport();
        report.Info("IGNORED_INFO", "$", "info");
        report.Warning("FIRST_WARNING", "$", "first warning");
        report.Warning("SECOND_WARNING", "$", "second warning");
        ApplyReportSurface surface = new ApplyReportSurface();

        surface.Publish("apply", report);

        Assert.Equal("[FIRST_WARNING] first warning", surface.ActionableMessage);
    }

    [Fact]
    public void Info_only_reports_do_not_add_an_actionable_message()
    {
        ApplyReport report = new ApplyReport();
        report.Info("ONLY_INFO", "$", "informational");
        ApplyReportSurface surface = new ApplyReportSurface();

        surface.Publish("apply", report);

        Assert.False(surface.HasActionableMessage);
        Assert.Equal("Last: " + report.ToSummary("apply"), surface.ComposeStatusText("Last: ", _ => true));
    }

    [Fact]
    public void Non_info_codes_are_grouped_in_first_seen_order()
    {
        ApplyReport report = new ApplyReport();
        report.Warning("FIRST_CODE", "$", "first warning");
        report.Info("IGNORED_INFO", "$", "info");
        report.Error("ERROR_CODE", "$", "first error");
        report.Warning("FIRST_CODE", "$", "second warning");
        report.Warning("LAST_CODE", "$", "last warning");
        ApplyReportSurface surface = new ApplyReportSurface();

        surface.Publish("apply", report);
        string text = surface.ComposeStatusText("Last: ", _ => true);

        Assert.Contains("[ERROR_CODE] first error", text);
        Assert.Contains("FIRST_CODE x2", text);
        Assert.Contains("ERROR_CODE x1", text);
        Assert.Contains("LAST_CODE x1", text);
        Assert.DoesNotContain("IGNORED_INFO", text);
        Assert.True(text.IndexOf("FIRST_CODE x2", StringComparison.Ordinal)
            < text.IndexOf("ERROR_CODE x1", StringComparison.Ordinal));
        Assert.True(text.IndexOf("ERROR_CODE x1", StringComparison.Ordinal)
            < text.IndexOf("LAST_CODE x1", StringComparison.Ordinal));
    }

    [Fact]
    public void Trailing_code_groups_are_omitted_with_a_distinct_code_marker()
    {
        ApplyReport report = new ApplyReport();
        report.Warning("FIRST_CODE", "$", "first warning");
        report.Error("ERROR_CODE", "$", "first error");
        report.Warning("LAST_CODE", "$", "last warning");
        ApplyReportSurface surface = new ApplyReportSurface();
        surface.Publish("apply", report);

        string text = surface.ComposeStatusText(
            "Last: ",
            candidate => candidate.Contains("FIRST_CODE x1")
                && !candidate.Contains("ERROR_CODE x1")
                && !candidate.Contains("LAST_CODE x1"));

        Assert.Contains("[ERROR_CODE] first error", text);
        Assert.Contains("FIRST_CODE x1", text);
        Assert.DoesNotContain("ERROR_CODE x1", text);
        Assert.DoesNotContain("LAST_CODE x1", text);
        Assert.Contains("+2 more diagnostic code(s)", text);
    }

    [Fact]
    public void The_summary_and_actionable_message_are_not_truncated_for_groups()
    {
        ApplyReport report = new ApplyReport();
        report.Error("ERROR_CODE", "$", "complete error message");
        ApplyReportSurface surface = new ApplyReportSurface();
        surface.Publish("apply", report);

        string text = surface.ComposeStatusText("Last: ", _ => false);

        Assert.Equal(
            "Last: " + report.ToSummary("apply") + "\n[ERROR_CODE] complete error message",
            text);
    }

    [Fact]
    public void Failed_edit_flow_returns_to_the_previous_safe_state_after_recovery()
    {
        ApplyReport report = new ApplyReport();
        EditorFlowRecovery recovery = new EditorFlowRecovery(EditorFlowState.Editing);

        Assert.False(recovery.Run("property", () => false, () => true, report));
        Assert.Equal(EditorFlowState.Editing, recovery.State);
        Assert.Contains(report.Entries, x => x.Code == "FLOW_RECOVERED");
    }

    [Fact]
    public void Failed_recovery_reduces_state_to_room_loaded()
    {
        ApplyReport report = new ApplyReport();
        EditorFlowRecovery recovery = new EditorFlowRecovery(EditorFlowState.Editing);

        Assert.False(recovery.Run("export", () => { throw new InvalidOperationException("write"); }, () => false, report));
        Assert.Equal(EditorFlowState.RoomLoaded, recovery.State);
        Assert.Contains(report.Entries, x => x.Code == "FLOW_RECOVERY_FAILED");
    }

    private static void AssertSummarySeverity(
        ApplyReport report,
        string status,
        ApplySeverity expectedSeverity)
    {
        ApplyReportSurface surface = new ApplyReportSurface();
        List<(ApplySeverity Severity, string Message)> logs = new List<(ApplySeverity Severity, string Message)>();
        ApplyReportPublisher publisher = new ApplyReportPublisher(
            surface,
            (severity, message) => logs.Add((severity, message)),
            false);

        publisher.Publish("test", report);

        (ApplySeverity Severity, string Message) log = Assert.Single(logs);
        Assert.Equal(expectedSeverity, log.Severity);
        Assert.True(
            log.Message.StartsWith("test " + status + ": Applied=", StringComparison.Ordinal),
            log.Message);
    }
}