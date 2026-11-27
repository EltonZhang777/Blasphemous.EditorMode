using Blasphemous.EditorMode.Diagnostics;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class ApplyReportStatusTextTests
{
    [Fact]
    public void All_code_groups_are_appended_when_the_status_has_room()
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
    public void Constrained_status_keeps_a_partial_group_summary_and_an_ellipsis()
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
    public void Constrained_status_never_silently_drops_the_omission_marker()
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
    public void Reports_without_non_info_diagnostics_keep_the_base_status_unchanged()
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
    public void An_ellipsis_is_returned_when_no_larger_candidate_fits()
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
    public void An_ellipsis_is_returned_when_even_the_actionable_diagnostic_cannot_fit()
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
    public void Narrow_status_prioritizes_the_first_actionable_diagnostic()
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