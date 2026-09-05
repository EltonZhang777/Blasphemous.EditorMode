using System;

namespace Blasphemous.EditorMode.Diagnostics;

/// <summary>Last report exposed to the editor UI without changing the domain result.</summary>
internal sealed class ApplyReportSurface
{
    internal ApplyReport LastReport { get; private set; }
    internal string UiText { get; private set; }

    internal void Publish(string operation, ApplyReport report)
    {
        LastReport = report ?? throw new ArgumentNullException(nameof(report));
        UiText = report.ToSummary(operation);
    }
}

/// <summary>Publishes one exact report to both the log and UI surfaces.</summary>
internal sealed class ApplyReportPublisher
{
    private readonly ApplyReportSurface _surface;
    private readonly Action<string> _log;

    internal ApplyReportPublisher(ApplyReportSurface surface, Action<string> log)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _log = log;
    }

    internal ApplyReportSurface Surface { get { return _surface; } }

    internal void Publish(string operation, ApplyReport report)
    {
        _surface.Publish(operation, report);
        if (_log == null)
            return;

        _log(_surface.UiText);
        foreach (ApplyReportEntry entry in report.Entries)
        {
            _log(string.Format(
                "[{0}] {1} {2}: {3}",
                entry.Severity,
                entry.Code,
                entry.Location,
                entry.Message));
        }
    }
}
