using Blasphemous.EditorMode.Diagnostics;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

internal static class ApplyReportAssert
{
    internal static void MatchesCodes(ApplyReport report, params string[] allowlist)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));

        allowlist ??= [];
        HashSet<string> allowed = new HashSet<string>(allowlist, StringComparer.Ordinal);
        HashSet<string> observed = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string> unexpectedSet = new HashSet<string>(StringComparer.Ordinal);
        List<string> unexpected = [];

        foreach (ApplyReportEntry entry in report.Entries)
        {
            observed.Add(entry.Code);
            if (entry.Severity == ApplySeverity.Error
                && !allowed.Contains(entry.Code)
                && unexpectedSet.Add(entry.Code))
            {
                unexpected.Add(entry.Code);
            }
        }

        HashSet<string> staleSet = new HashSet<string>(StringComparer.Ordinal);
        List<string> stale = [];
        foreach (string code in allowlist)
        {
            if (!observed.Contains(code) && staleSet.Add(code))
                stale.Add(code);
        }

        Assert.True(
            stale.Count == 0 && unexpected.Count == 0,
            FailureMessage(report, allowlist, stale, unexpected));
    }

    private static string FailureMessage(
        ApplyReport report,
        IReadOnlyList<string> allowlist,
        IReadOnlyList<string> stale,
        IReadOnlyList<string> unexpected)
    {
        StringBuilder message = new StringBuilder();
        message.AppendLine("ApplyReport codes did not match the allowlist.");
        AppendCodes(message, "Allowlist", allowlist);
        AppendCodes(message, "Stale", stale);
        AppendCodes(message, "Unexpected", unexpected);
        message.AppendLine("Entries:");
        for (int index = 0; index < report.Entries.Count; index++)
        {
            ApplyReportEntry entry = report.Entries[index];
            message.AppendFormat(
                "  [{0}] Severity={1}, Code={2}, Location={3}, Skipped={4}, Message={5}{6}",
                index,
                entry.Severity,
                entry.Code,
                entry.Location,
                entry.Skipped,
                entry.Message,
                Environment.NewLine);
        }

        return message.ToString();
    }

    private static void AppendCodes(
        StringBuilder message,
        string heading,
        IReadOnlyList<string> codes)
    {
        message.Append(heading).AppendLine(":");
        if (codes.Count == 0)
        {
            message.AppendLine("  <none>");
            return;
        }

        foreach (string code in codes)
            message.Append("  ").AppendLine(code);
    }
}