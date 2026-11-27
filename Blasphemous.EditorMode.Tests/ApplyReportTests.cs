using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Framework;
using System;
using System.Collections.Generic;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class ApplyReportTests
{
    [Fact]
    public void FrameworkAndDependencyFailuresReceiveActionableCodes()
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
    public void CountsTrackAppliedSkippedFailedAndDiagnosticsIndependently()
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
    public void SummaryHasStableKeysUnitsCountsAndFailureStatus()
    {
        ApplyReport report = new ApplyReport();
        report.Applied();
        report.Warning("SKIPPED_WARNING", "item[1]", "skipped warning", true);
        report.Error("SKIPPED_ERROR", "item[2]", "skipped error");
        report.Error("FAILED_ERROR", "item[3]", "failed error", false);

        Assert.Equal(
            "import failed: Applied=1 successful items/operations; Skipped=2 entries; Failed=2 entries; Diagnostic=3 entries.",
            report.ToSummary("import"));
    }

    [Fact]
    public void SummarySeverityTracksPassedPartialAndFailed()
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
    public void EveryDiagnosticPublishesItsFullEntryAlongsideTheSummary()
    {
        ApplyReport report = new ApplyReport();
        report.Warning("HIDDEN_WARNING", "item[1]", "warning entry");
        report.Error("HIDDEN_ERROR", "item[2]", "error entry");
        ApplyReportSurface surface = new ApplyReportSurface();
        List<(ApplySeverity Severity, string Message)> logs = new List<(ApplySeverity Severity, string Message)>();
        ApplyReportPublisher publisher = new ApplyReportPublisher(
            surface,
            (severity, message) => logs.Add((severity, message)));

        publisher.Publish("import", report);

        Assert.Equal(3, logs.Count);
        Assert.Equal(ApplySeverity.Error, logs[0].Severity);
        Assert.Equal(surface.UiText, logs[0].Message);
        Assert.Equal(
            (ApplySeverity.Warning, "[Warning] HIDDEN_WARNING item[1]: warning entry"),
            logs[1]);
        Assert.Equal(
            (ApplySeverity.Error, "[Error] HIDDEN_ERROR item[2]: error entry"),
            logs[2]);
    }

    [Fact]
    public void AllDiagnosticsFollowTheSummaryAndKeepTheirOwnSeverity()
    {
        ApplyReport report = new ApplyReport();
        report.Warning("VERBOSE_WARNING", "item[1]", "warning entry");
        report.Info("VERBOSE_INFO", "item[2]", "info entry");
        report.Error("VERBOSE_ERROR", "item[3]", "error entry", false);
        ApplyReportSurface surface = new ApplyReportSurface();
        List<(ApplySeverity Severity, string Message)> logs = new List<(ApplySeverity Severity, string Message)>();
        ApplyReportPublisher publisher = new ApplyReportPublisher(
            surface,
            (severity, message) => logs.Add((severity, message)));

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
    public void RuntimeVerboseDiagnosticsIsDisabledByDefault()
    {
        EditorModeConfig config = new EditorModeConfig();

        Assert.False(config.VerboseDiagnostics);
    }

    [Fact]
    public void TheSameReportIsPublishedToUiAndLogSurfaces()
    {
        ApplyReport report = new ApplyReport();
        report.Applied();
        report.Warning("TEST_WARNING", "item[1]", "visible warning");
        ApplyReportSurface surface = new ApplyReportSurface();
        List<(ApplySeverity Severity, string Message)> logs = new List<(ApplySeverity Severity, string Message)>();
        ApplyReportPublisher publisher = new ApplyReportPublisher(
            surface,
            (severity, message) => logs.Add((severity, message)));

        publisher.Publish("import", report);

        Assert.Same(report, surface.LastReport);
        Assert.Equal(report.ToSummary("import"), surface.UiText);
        Assert.Contains("partial", surface.UiText);
        Assert.Contains(logs, x => x.Severity == ApplySeverity.Warning && x.Message == surface.UiText);
        Assert.Contains(logs, x => x.Severity == ApplySeverity.Warning && x.Message.Contains("TEST_WARNING"));
    }

    [Fact]
    public void AFailedRoomImportExposesAnActionableRecoveryReport()
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
    public void AnErrorTakesPriorityOverAnEarlierWarning()
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
    public void TheFirstWarningIsActionableWhenTheReportHasNoError()
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
    public void InfoOnlyReportsDoNotAddAnActionableMessage()
    {
        ApplyReport report = new ApplyReport();
        report.Info("ONLY_INFO", "$", "informational");
        ApplyReportSurface surface = new ApplyReportSurface();

        surface.Publish("apply", report);

        Assert.False(surface.HasActionableMessage);
        Assert.Equal(report.ToSummary("apply"), surface.UiText);
    }

    [Fact]
    public void ClearingPropertyDiagnosticsDoesNotRemoveAnUnrelatedReport()
    {
        ApplyReportSurface surface = new ApplyReportSurface();
        ApplyReport properties = new ApplyReport();
        properties.Error("PROPERTY_INVALID", "properties[1]", "bad property");
        surface.Publish("Properties", properties);

        Assert.True(surface.ClearOperation("Properties"));
        Assert.Null(surface.LastReport);
        Assert.Equal(string.Empty, surface.UiText);
        Assert.False(surface.HasActionableMessage);

        ApplyReport selection = new ApplyReport();
        selection.Info("SELECTION_PASSED", "cursor", "selected");
        surface.Publish("Select", selection);

        Assert.False(surface.ClearOperation("Properties"));
        Assert.Same(selection, surface.LastReport);
        Assert.Equal(selection.ToSummary("Select"), surface.UiText);
    }

    [Fact]
    public void FailedEditFlowReturnsToThePreviousSafeStateAfterRecovery()
    {
        ApplyReport report = new ApplyReport();
        EditorFlowRecovery recovery = new EditorFlowRecovery(EditorFlowState.Editing);

        Assert.False(recovery.Run("property", () => false, () => true, report));
        Assert.Equal(EditorFlowState.Editing, recovery.State);
        Assert.Contains(report.Entries, x => x.Code == "FLOW_RECOVERED");
    }

    [Fact]
    public void FailedRecoveryReducesStateToRoomLoaded()
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
            (severity, message) => logs.Add((severity, message)));

        publisher.Publish("test", report);

        Assert.NotEmpty(logs);
        (ApplySeverity Severity, string Message) log = logs[0];
        Assert.Equal(expectedSeverity, log.Severity);
        Assert.True(
            log.Message.StartsWith("test " + status + ": Applied=", StringComparison.Ordinal),
            log.Message);
    }
}