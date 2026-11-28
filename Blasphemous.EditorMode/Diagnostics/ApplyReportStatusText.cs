using System;
using System.Collections.Generic;
using System.Text;

namespace Blasphemous.EditorMode.Diagnostics;

internal static class ApplyReportStatusText
{
    internal static string AppendCodeGroups(
        string statusText,
        string actionableMessage,
        ApplyReport report,
        Func<string, bool> fits)
    {
        string baseText = statusText;
        if (!string.IsNullOrEmpty(actionableMessage))
        {
            baseText += "\n" + actionableMessage;
        }

        List<DiagnosticCodeCount> groups = CountByCode(report);
        if (groups.Count == 0)
        {
            return baseText;
        }

        string candidate = baseText + "\n" + FormatGroups(groups, groups.Count);
        if (fits(candidate))
        {
            return candidate;
        }

        for (int included = groups.Count - 1; included >= 0; included--)
        {
            int omitted = groups.Count - included;
            string marker = "+" + omitted + " more…";
            candidate = included == 0
                ? baseText + "\n" + marker
                : baseText + "\n" + FormatGroups(groups, included) + "\n" + marker;
            if (fits(candidate))
            {
                return candidate;
            }
        }

        candidate = baseText + "\n…";
        if (fits(candidate))
        {
            return candidate;
        }

        if (!string.IsNullOrEmpty(actionableMessage))
        {
            candidate = actionableMessage + "\n…";
            if (fits(candidate))
            {
                return candidate;
            }

            candidate = baseText + " …";
            if (fits(candidate))
            {
                return candidate;
            }

            for (int length = statusText.Length - 1; length >= 0; length--)
            {
                string prefix = statusText.Substring(0, length).TrimEnd();
                candidate = prefix.Length == 0
                    ? actionableMessage + " …"
                    : prefix + "\n" + actionableMessage + " …";
                if (fits(candidate))
                {
                    return candidate;
                }
            }

            for (int length = actionableMessage.Length - 1; length >= 0; length--)
            {
                candidate = actionableMessage.Substring(0, length).TrimEnd() + " …";
                if (fits(candidate))
                {
                    return candidate;
                }
            }

            return "…";
        }

        for (int length = statusText.Length - 1; length >= 0; length--)
        {
            candidate = statusText.Substring(0, length).TrimEnd() + " …";
            if (fits(candidate))
            {
                return candidate;
            }
        }

        return "…";
    }

    private static List<DiagnosticCodeCount> CountByCode(ApplyReport report)
    {
        List<DiagnosticCodeCount> groups = [];
        if (report == null)
        {
            return groups;
        }

        foreach (ApplyReportEntry entry in report.Entries)
        {
            if (entry.Severity == ApplySeverity.Info)
            {
                continue;
            }

            DiagnosticCodeCount group = groups.Find(existing =>
                string.Equals(existing.Code, entry.Code, StringComparison.Ordinal));
            if (group == null)
            {
                groups.Add(new DiagnosticCodeCount(entry.Code));
            }
            else
            {
                group.Count++;
            }
        }

        return groups;
    }

    private static string FormatGroups(List<DiagnosticCodeCount> groups, int count)
    {
        StringBuilder text = new();
        for (int index = 0; index < count; index++)
        {
            if (index > 0)
            {
                text.Append(", ");
            }

            DiagnosticCodeCount group = groups[index];
            text.Append(group.Code).Append(" x").Append(group.Count);
        }

        return text.ToString();
    }

    private sealed class DiagnosticCodeCount
    {
        internal DiagnosticCodeCount(string code)
        {
            Code = code;
            Count = 1;
        }

        internal string Code { get; }
        internal int Count { get; set; }
    }
}