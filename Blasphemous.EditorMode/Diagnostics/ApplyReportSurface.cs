using System;
using System.Collections.Generic;
using System.Text;

namespace Blasphemous.EditorMode.Diagnostics;

/// <summary>Last report exposed to the editor UI without changing the domain result.</summary>
internal sealed class ApplyReportSurface
{
    private readonly List<DiagnosticCodeCount> _diagnosticGroups = [];

    internal ApplyReport LastReport { get; private set; }
    internal string UiText { get; private set; }
    internal string ActionableMessage { get; private set; }
    internal bool HasActionableMessage { get { return !string.IsNullOrEmpty(ActionableMessage); } }

    internal void Publish(string operation, ApplyReport report)
    {
        LastReport = report ?? throw new ArgumentNullException(nameof(report));
        UiText = report.ToSummary(operation);
        ActionableMessage = null;
        _diagnosticGroups.Clear();
        string errorMessage = null;
        string warningMessage = null;
        foreach (ApplyReportEntry entry in report.Entries)
        {
            if (entry.Severity == ApplySeverity.Info)
                continue;

            DiagnosticCodeCount group = null;
            foreach (DiagnosticCodeCount existing in _diagnosticGroups)
            {
                if (string.Equals(existing.Code, entry.Code, StringComparison.Ordinal))
                {
                    group = existing;
                    break;
                }
            }

            if (group == null)
                _diagnosticGroups.Add(new DiagnosticCodeCount(entry.Code));
            else
                group.Count++;

            string message = "[" + entry.Code + "] " + entry.Message;
            if (entry.Severity == ApplySeverity.Error)
                errorMessage ??= message;
            else
                warningMessage ??= message;
        }

        ActionableMessage = errorMessage ?? warningMessage;
    }

    internal string ComposeStatusText(string prefix, Func<string, bool> fits)
    {
        string baseText = (prefix ?? string.Empty) + UiText;
        if (HasActionableMessage)
            baseText += "\n" + ActionableMessage;
        if (_diagnosticGroups.Count == 0)
            return baseText;

        string candidate = baseText + "\n" + FormatGroups(_diagnosticGroups.Count);
        if (fits == null || fits(candidate))
            return candidate;

        for (int included = _diagnosticGroups.Count - 1; included >= 0; included--)
        {
            int omitted = _diagnosticGroups.Count - included;
            string marker = "+" + omitted + " more diagnostic code(s)";
            candidate = baseText + "\n"
                + (included == 0 ? marker : FormatGroups(included) + ", " + marker);
            if (fits(candidate))
                return candidate;
        }

        return baseText;
    }

    private string FormatGroups(int count)
    {
        StringBuilder text = new();
        for (int index = 0; index < count; index++)
        {
            if (index > 0)
                text.Append(", ");
            DiagnosticCodeCount group = _diagnosticGroups[index];
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

/// <summary>Publishes one exact report to both the log and UI surfaces.</summary>
internal sealed class ApplyReportPublisher
{
    private readonly ApplyReportSurface _surface;
    private readonly Action<ApplySeverity, string> _log;
    private readonly bool _verboseDiagnostics;

    internal ApplyReportPublisher(
        ApplyReportSurface surface,
        Action<ApplySeverity, string> log,
        bool verboseDiagnostics)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _log = log;
        _verboseDiagnostics = verboseDiagnostics;
    }

    internal ApplyReportSurface Surface { get { return _surface; } }

    internal void Publish(string operation, ApplyReport report)
    {
        _surface.Publish(operation, report);
        if (_log == null)
            return;

        _log(
            report.HasErrors ? ApplySeverity.Error : report.IsPartial ? ApplySeverity.Warning : ApplySeverity.Info,
            _surface.UiText);
        if (!_verboseDiagnostics)
            return;

        foreach (ApplyReportEntry entry in report.Entries)
        {
            _log(entry.Severity, string.Format(
                "[{0}] {1} {2}: {3}",
                entry.Severity,
                entry.Code,
                entry.Location,
                entry.Message));
        }
    }
}