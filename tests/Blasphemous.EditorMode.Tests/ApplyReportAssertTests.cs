using Blasphemous.EditorMode.Diagnostics;
using Xunit;
using Xunit.Sdk;

namespace Blasphemous.EditorMode.Tests;

public sealed class ApplyReportAssertTests
{
    [Fact]
    public void Missing_expected_code_fails_as_stale()
    {
        ApplyReport report = new ApplyReport();
        report.Error("OBSERVED", "$", "failure");

        XunitException failure = Assert.ThrowsAny<XunitException>(
            () => ApplyReportAssert.MatchesCodes(report, "OBSERVED", "MISSING"));

        Assert.Contains("MISSING", failure.Message);
        Assert.Contains("stale", failure.Message.ToLowerInvariant());
    }

    [Fact]
    public void Unexpected_error_code_fails()
    {
        ApplyReport report = new ApplyReport();
        report.Error("OBSERVED", "$", "observed");
        report.Error("UNEXPECTED", "$", "unexpected");

        XunitException failure = Assert.ThrowsAny<XunitException>(
            () => ApplyReportAssert.MatchesCodes(report, "OBSERVED"));

        Assert.Contains("Unexpected:", failure.Message);
        Assert.Contains("UNEXPECTED", failure.Message);
    }

    [Fact]
    public void Skipped_error_is_still_unexpected()
    {
        ApplyReport report = new ApplyReport();
        report.Error("SKIPPED_ERROR", "$", "skipped", true);

        XunitException failure = Assert.ThrowsAny<XunitException>(
            () => ApplyReportAssert.MatchesCodes(report));

        Assert.Contains("Unexpected:", failure.Message);
        Assert.Contains("SKIPPED_ERROR", failure.Message);
        Assert.Contains("Skipped=True", failure.Message);
    }

    [Fact]
    public void Warning_and_info_codes_outside_the_allowlist_are_ignored()
    {
        ApplyReport report = new ApplyReport();
        report.Warning("EXPECTED_WARNING", "$", "expected");
        report.Warning("IGNORED_WARNING", "$", "ignored");
        report.Info("IGNORED_INFO", "$", "ignored");

        ApplyReportAssert.MatchesCodes(report, "EXPECTED_WARNING");
    }

    [Fact]
    public void Duplicate_entries_and_allowlist_codes_are_allowed()
    {
        ApplyReport report = new ApplyReport();
        report.Error("DUPLICATE_ERROR", "$", "first");
        report.Error("DUPLICATE_ERROR", "$", "second");

        ApplyReportAssert.MatchesCodes(
            report,
            "DUPLICATE_ERROR",
            "DUPLICATE_ERROR");
    }

    [Fact]
    public void Duplicate_unexpected_errors_report_the_code_once()
    {
        ApplyReport report = new ApplyReport();
        report.Error("DUPLICATE_UNEXPECTED", "$", "first");
        report.Error("DUPLICATE_UNEXPECTED", "$", "second");

        XunitException failure = Assert.ThrowsAny<XunitException>(
            () => ApplyReportAssert.MatchesCodes(report));

        int unexpectedHeading = failure.Message.IndexOf("Unexpected:", StringComparison.Ordinal);
        int entriesHeading = failure.Message.IndexOf("Entries:", StringComparison.Ordinal);
        string unexpectedSection = failure.Message.Substring(
            unexpectedHeading,
            entriesHeading - unexpectedHeading);
        Assert.Equal(1, CountOccurrences(unexpectedSection, "DUPLICATE_UNEXPECTED"));
    }

    [Fact]
    public void Matching_is_limited_to_the_ordinal_code_field()
    {
        ApplyReport report = new ApplyReport();
        report.Warning(
            "Expected",
            "lowercase_code_in_location",
            "lowercase_code_in_message");

        XunitException failure = Assert.ThrowsAny<XunitException>(
            () => ApplyReportAssert.MatchesCodes(report, "expected"));

        Assert.Contains("Stale:", failure.Message);
        Assert.Contains("  expected", failure.Message);
    }

    [Fact]
    public void Failure_output_contains_the_allowlist_and_ordered_entries()
    {
        ApplyReport report = new ApplyReport();
        report.Warning("FIRST_WARNING", "location-1", "first message");
        report.Error("UNEXPECTED_ERROR", "location-2", "second message");
        report.Info("THIRD_INFO", "location-3", "third message");

        XunitException failure = Assert.ThrowsAny<XunitException>(
            () => ApplyReportAssert.MatchesCodes(
                report,
                "MISSING_ONE",
                "MISSING_TWO"));

        Assert.Contains("MISSING_ONE", failure.Message);
        Assert.Contains("MISSING_TWO", failure.Message);
        int first = failure.Message.IndexOf("Code=FIRST_WARNING", StringComparison.Ordinal);
        int second = failure.Message.IndexOf("Code=UNEXPECTED_ERROR", StringComparison.Ordinal);
        int third = failure.Message.IndexOf("Code=THIRD_INFO", StringComparison.Ordinal);
        Assert.True(first >= 0);
        Assert.True(second > first);
        Assert.True(third > second);
        Assert.Contains("Location=location-1", failure.Message);
        Assert.Contains("Message=first message", failure.Message);
        Assert.Contains("Message=second message", failure.Message);
        Assert.Contains("Message=third message", failure.Message);
    }

    private static int CountOccurrences(string value, string expected)
    {
        int count = 0;
        int index = 0;
        while ((index = value.IndexOf(expected, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += expected.Length;
        }

        return count;
    }
}