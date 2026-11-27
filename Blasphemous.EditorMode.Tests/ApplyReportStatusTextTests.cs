using Blasphemous.EditorMode.Diagnostics;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class ApplyReportStatusTextTests
{
    [Fact]
    public void AllCodeGroupsAreAppendedWhenTheStatusHasRoom()
    {
        ApplyReport report = new ApplyReport();
        report.Error("PROPERTY_INVALID", "properties[0]", "first");
        report.Error("PROPERTY_INVALID", "properties[1]", "second");
        report.Warning("RUNTIME_APPLY_FAILED", "placement", "third");

        string result = ApplyReportStatusText.AppendCodeGroups(
            "base",
            null,
            report,
            _ => true);

        Assert.Equal(
            "base\nPROPERTY_INVALID x2, RUNTIME_APPLY_FAILED x1",
            result);
    }

    [Fact]
    public void ConstrainedStatusKeepsAPartialGroupSummaryAndAnEllipsis()
    {
        ApplyReport report = new ApplyReport();
        report.Error("FIRST", "one", "first");
        report.Error("SECOND", "two", "second");
        report.Error("THIRD", "three", "third");

        string result = ApplyReportStatusText.AppendCodeGroups(
            "base",
            null,
            report,
            candidate => candidate.Length <= 22);

        Assert.Equal("base\nFIRST x1\n+2 more…", result);
    }

    [Fact]
    public void ConstrainedStatusNeverSilentlyDropsTheOmissionMarker()
    {
        ApplyReport report = new ApplyReport();
        report.Error("PROPERTY_INVALID", "properties[0]", "first");

        string result = ApplyReportStatusText.AppendCodeGroups(
            "base diagnostic text",
            null,
            report,
            candidate => candidate.Length <= 8);

        Assert.Contains("…", result);
        Assert.True(result.Length <= 8, result);
    }

    [Fact]
    public void ReportsWithoutNonInfoDiagnosticsKeepTheBaseStatusUnchanged()
    {
        ApplyReport report = new ApplyReport();
        report.Info("PREVIEW_CREATED", "preview", "informational");

        string result = ApplyReportStatusText.AppendCodeGroups(
            "base",
            null,
            report,
            _ => true);

        Assert.Equal("base", result);
    }

    [Fact]
    public void AnEllipsisIsReturnedWhenNoLargerCandidateFits()
    {
        ApplyReport report = new ApplyReport();
        report.Error("PROPERTY_INVALID", "properties[0]", "first");

        string result = ApplyReportStatusText.AppendCodeGroups(
            "base",
            null,
            report,
            _ => false);

        Assert.Equal("…", result);
    }

    [Fact]
    public void AnEllipsisIsReturnedWhenEvenTheActionableDiagnosticCannotFit()
    {
        ApplyReport report = new ApplyReport();
        report.Error("PROPERTY_INVALID", "properties[0]", "first");

        string result = ApplyReportStatusText.AppendCodeGroups(
            "base",
            "[PROPERTY_INVALID] first",
            report,
            _ => false);

        Assert.Equal("…", result);
    }

    [Fact]
    public void NarrowStatusPrioritizesTheFirstActionableDiagnostic()
    {
        ApplyReport report = new ApplyReport();
        report.Error("PROPERTY_INVALID", "properties[0]", "bad property");
        const string actionable = "[PROPERTY_INVALID] bad property";

        string result = ApplyReportStatusText.AppendCodeGroups(
            "Room: D01Z02S01   Session: Partial   Last: failed summary",
            actionable,
            report,
            candidate => candidate.Length <= actionable.Length + 2);

        Assert.Equal(actionable + "\n…", result);
    }
}