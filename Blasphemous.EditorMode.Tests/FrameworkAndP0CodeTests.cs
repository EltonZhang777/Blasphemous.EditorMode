using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Framework;
using System.Linq;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class FrameworkAndP0CodeTests
{
    [Theory]
    [InlineData(null, "FRAMEWORK_JSON")]
    [InlineData("JSON syntax is invalid", "JSON_SYNTAX")]
    [InlineData("Room export key does not match Room Identity: expected room.", "ROOM_SCOPE")]
    [InlineData("Required top-level array is missing.", "JSON_SCHEMA")]
    [InlineData("Top-level field must be an array.", "JSON_SCHEMA")]
    [InlineData("Required string field is missing.", "JSON_SCHEMA")]
    [InlineData("The path is invalid.", "JSON_SCHEMA")]
    [InlineData("Atomic Level Export failed.", "EXPORT_IO")]
    [InlineData("Export failed.", "EXPORT_IO")]
    [InlineData("file could not be read", "FILE_IO")]
    [InlineData("condition is invalid", "CONDITION_INVALID")]
    [InlineData("transform is invalid", "TRANSFORM_INVALID")]
    [InlineData("property is invalid", "PROPERTY_INVALID")]
    [InlineData("Duplicate object id: torch", "PLACEMENT_DUPLICATE_ID")]
    [InlineData("Unknown field ignored.", "JSON_UNKNOWN_FIELD")]
    [InlineData("Unclassified diagnostic", "FRAMEWORK_JSON")]
    public void Framework_messages_map_to_exact_codes(string message, string expectedCode)
    {
        ApplyReportEntry entry = Assert.Single(ReportFor(message).Entries);

        Assert.Equal(expectedCode, entry.Code);
    }

    [Theory]
    [InlineData("syntax, path, export", "JSON_SYNTAX")]
    [InlineData("does not match Room Identity; path; export", "ROOM_SCOPE")]
    [InlineData("Required top-level array; export", "JSON_SCHEMA")]
    [InlineData("Atomic Level Export; file could not be read", "EXPORT_IO")]
    [InlineData("file could not be read; condition; transform", "FILE_IO")]
    [InlineData("condition; transform; property", "CONDITION_INVALID")]
    [InlineData("transform; property", "TRANSFORM_INVALID")]
    [InlineData("property; Unknown", "PROPERTY_INVALID")]
    [InlineData("Duplicate object id; Unknown", "PLACEMENT_DUPLICATE_ID")]
    public void Framework_classifier_uses_the_first_matching_branch(string message, string expectedCode)
    {
        ApplyReportEntry entry = Assert.Single(ReportFor(message).Entries);

        Assert.Equal(expectedCode, entry.Code);
    }

    [Theory]
    [InlineData("JaSoN SyNtAx Is InVaLiD", "JSON_SYNTAX")]
    [InlineData("dOeS nOt MaTcH rOoM iDeNtItY", "ROOM_SCOPE")]
    [InlineData("rEqUiReD sTrInG fIeLd Is MiSsInG", "JSON_SCHEMA")]
    [InlineData("fIlE cOuLd NoT bE rEaD", "FILE_IO")]
    [InlineData("dUpLiCaTe ObJeCt Id: torch", "PLACEMENT_DUPLICATE_ID")]
    [InlineData("uNkNoWn field ignored", "JSON_UNKNOWN_FIELD")]
    public void Framework_classifier_is_case_insensitive(string message, string expectedCode)
    {
        ApplyReportEntry entry = Assert.Single(ReportFor(message).Entries);

        Assert.Equal(expectedCode, entry.Code);
    }

    [Fact]
    public void Framework_diagnostic_severity_is_preserved()
    {
        ApplyReport report = new ApplyReport();
        report.MergeFrameworkDiagnostics(
            new[]
            {
                new FrameworkDiagnostic(
                    FrameworkDiagnosticSeverity.Warning,
                    "warning",
                    "syntax warning"),
                new FrameworkDiagnostic(
                    FrameworkDiagnosticSeverity.Error,
                    "error",
                    "export failure")
            });

        Assert.Collection(
            report.Entries,
            entry =>
            {
                Assert.Equal(ApplySeverity.Warning, entry.Severity);
                Assert.Equal("JSON_SYNTAX", entry.Code);
            },
            entry =>
            {
                Assert.Equal(ApplySeverity.Error, entry.Severity);
                Assert.Equal("EXPORT_IO", entry.Code);
            });
    }

    [Fact]
    public void P0_checks_emit_only_the_exact_pass_codes()
    {
        P0AcceptanceResult result = P0AcceptanceGate.Evaluate(new P0AcceptanceEvidence
        {
            SeamPassed = true,
            DependencySafetyPassed = true,
            RoomSafetyPassed = true,
            OriginalSaveProtected = true,
            RecoveryPassed = true,
            NoDuplicateOrLeakPassed = true,
            RealGameSmokePassed = true
        });

        Assert.Equal(
            new[]
            {
                "SEAM_PASSED",
                "DEPENDENCY_PASSED",
                "ROOM_SAFETY_PASSED",
                "ORIGINAL_SAVE_PASSED",
                "RECOVERY_PASSED",
                "NO_DUPLICATE_PASSED",
                "REAL_GAME_PASSED"
            },
            result.Report.Entries.Select(entry => entry.Code));
        Assert.All(
            result.Report.Entries,
            entry => Assert.Equal(ApplySeverity.Info, entry.Severity));
    }

    [Fact]
    public void P0_checks_emit_only_the_exact_fail_codes()
    {
        P0AcceptanceResult result = P0AcceptanceGate.Evaluate(new P0AcceptanceEvidence
        {
            SeamPassed = false,
            DependencySafetyPassed = false,
            RoomSafetyPassed = false,
            OriginalSaveProtected = false,
            RecoveryPassed = false,
            NoDuplicateOrLeakPassed = false,
            RealGameSmokePassed = true
        });

        Assert.Equal(
            new[]
            {
                "SEAM_FAILED",
                "DEPENDENCY_FAILED",
                "ROOM_SAFETY_FAILED",
                "ORIGINAL_SAVE_FAILED",
                "RECOVERY_FAILED",
                "NO_DUPLICATE_FAILED",
                "REAL_GAME_PASSED"
            },
            result.Report.Entries.Select(entry => entry.Code));
        Assert.All(
            result.Report.Entries.Take(6),
            entry => Assert.Equal(ApplySeverity.Error, entry.Severity));
        Assert.Equal(ApplySeverity.Info, result.Report.Entries[6].Severity);
    }

    [Theory]
    [InlineData("SEAM", "SEAM_FAILED", false, true, true, true, true, true)]
    [InlineData("DEPENDENCY", "DEPENDENCY_FAILED", true, false, true, true, true, true)]
    [InlineData("ROOM_SAFETY", "ROOM_SAFETY_FAILED", true, true, false, true, true, true)]
    [InlineData("ORIGINAL_SAVE", "ORIGINAL_SAVE_FAILED", true, true, true, false, true, true)]
    [InlineData("RECOVERY", "RECOVERY_FAILED", true, true, true, true, false, true)]
    [InlineData("NO_DUPLICATE", "NO_DUPLICATE_FAILED", true, true, true, true, true, false)]
    public void Exactly_one_failed_p0_check_blocks_release_with_its_exact_code(
        string expectedIdentity,
        string expectedCode,
        bool seamPassed,
        bool dependencyPassed,
        bool roomSafetyPassed,
        bool originalSavePassed,
        bool recoveryPassed,
        bool noDuplicatePassed)
    {
        P0AcceptanceResult result = P0AcceptanceGate.Evaluate(new P0AcceptanceEvidence
        {
            SeamPassed = seamPassed,
            DependencySafetyPassed = dependencyPassed,
            RoomSafetyPassed = roomSafetyPassed,
            OriginalSaveProtected = originalSavePassed,
            RecoveryPassed = recoveryPassed,
            NoDuplicateOrLeakPassed = noDuplicatePassed,
            RealGameSmokePassed = true
        });
        ApplyReportEntry failure = Assert.Single(
            result.Report.Entries.Where(entry => entry.Severity == ApplySeverity.Error));

        Assert.Equal(expectedCode, failure.Code);
        Assert.Equal(expectedIdentity, failure.Location);
        Assert.False(result.PureChecksPassed);
        Assert.False(result.RuntimePending);
        Assert.False(result.ReleaseReady);
    }

    private static ApplyReport ReportFor(string message)
    {
        ApplyReport report = new ApplyReport();
        report.MergeFrameworkDiagnostics(
            new[]
            {
                new FrameworkDiagnostic(
                    FrameworkDiagnosticSeverity.Error,
                    "framework",
                    message)
            });
        return report;
    }
}