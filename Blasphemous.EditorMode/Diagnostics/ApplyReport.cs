using Blasphemous.EditorMode.Framework;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blasphemous.EditorMode.Diagnostics;

/// <summary>Severity used by import, edit, runtime, and export reports.</summary>
internal enum ApplySeverity
{
    Info,
    Warning,
    Error
}

/// <summary>One actionable result entry.</summary>
internal sealed class ApplyReportEntry
{
    internal ApplyReportEntry(
        ApplySeverity severity,
        string code,
        string location,
        string message,
        bool skipped)
    {
        Severity = severity;
        Code = code;
        Location = location;
        Message = message;
        Skipped = skipped;
    }

    internal ApplySeverity Severity { get; }
    internal string Code { get; }
    internal string Location { get; }
    internal string Message { get; }
    internal bool Skipped { get; }
}

/// <summary>Shared report surface for nonfatal editor operations.</summary>
internal sealed class ApplyReport
{
    private readonly List<ApplyReportEntry> _entries = [];

    internal ReadOnlyCollection<ApplyReportEntry> Entries
    {
        get { return new ReadOnlyCollection<ApplyReportEntry>(_entries); }
    }

    internal int AppliedCount { get; private set; }
    internal int SkippedCount { get; private set; }

    internal int FailedCount
    {
        get
        {
            int count = 0;
            foreach (ApplyReportEntry entry in _entries)
            {
                if (entry.Severity == ApplySeverity.Error)
                    count++;
            }

            return count;
        }
    }

    internal int DiagnosticCount { get { return _entries.Count; } }

    internal bool HasErrors
    {
        get { return FailedCount > 0; }
    }

    internal bool IsPartial
    {
        get { return SkippedCount > 0 || _entries.Exists(entry => entry.Severity == ApplySeverity.Warning); }
    }

    internal void Applied()
    {
        AppliedCount++;
    }

    internal void Info(string code, string location, string message)
    {
        Add(ApplySeverity.Info, code, location, message, false);
    }

    internal void Warning(string code, string location, string message, bool skipped = false)
    {
        Add(ApplySeverity.Warning, code, location, message, skipped);
    }

    internal void Error(string code, string location, string message, bool skipped = true)
    {
        Add(ApplySeverity.Error, code, location, message, skipped);
    }

    internal void MergeFrameworkDiagnostics(IEnumerable<FrameworkDiagnostic> diagnostics)
    {
        foreach (FrameworkDiagnostic diagnostic in diagnostics ?? [])
        {
            string code = ClassifyFrameworkDiagnostic(diagnostic.Message);
            if (diagnostic.Severity == FrameworkDiagnosticSeverity.Error)
                Error(code, diagnostic.Location, diagnostic.Message);
            else
                Warning(code, diagnostic.Location, diagnostic.Message);
        }
    }

    internal void Merge(ApplyReport other)
    {
        if (other == null)
            return;

        foreach (ApplyReportEntry entry in other.Entries)
            Add(entry.Severity, entry.Code, entry.Location, entry.Message, entry.Skipped);
        AppliedCount += other.AppliedCount;
    }

    internal void MergeDependencyDiagnostics(IEnumerable<DependencyDiagnostic> diagnostics)
    {
        foreach (DependencyDiagnostic diagnostic in diagnostics ?? [])
        {
            Error(
                diagnostic.Reason == DependencyFailureReason.Missing
                    ? ReportCodes.DependencyMissing
                    : ReportCodes.DependencyVersion,
                diagnostic.Id,
                diagnostic.Message);
        }
    }

    internal string ToSummary(string operation)
    {
        string status = HasErrors ? "failed" : IsPartial ? "partial" : "passed";
        return string.Format(
            "{0} {1}: Applied={2} item(s), Skipped={3} item(s), Failed={4} error(s), Diagnostic={5} entry(ies).",
            operation,
            status,
            AppliedCount,
            SkippedCount,
            FailedCount,
            DiagnosticCount);
    }

    private void Add(
        ApplySeverity severity,
        string code,
        string location,
        string message,
        bool skipped)
    {
        _entries.Add(new ApplyReportEntry(
            severity,
            code ?? ReportCodes.Unknown,
            location ?? "$",
            message ?? string.Empty,
            skipped));
        if (skipped)
            SkippedCount++;
    }

    private static string ClassifyFrameworkDiagnostic(string message)
    {
        if (message == null)
            return ReportCodes.FrameworkJson;
        if (message.IndexOf("syntax", StringComparison.OrdinalIgnoreCase) >= 0)
            return ReportCodes.JsonSyntax;
        if (message.IndexOf("does not match Room Identity", StringComparison.OrdinalIgnoreCase) >= 0)
            return ReportCodes.RoomScope;
        if (message.IndexOf("Required top-level array", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("Top-level field", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("Required string field", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0)
            return ReportCodes.JsonSchema;
        if (message.IndexOf("Atomic Level Export", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("export", StringComparison.OrdinalIgnoreCase) >= 0)
            return ReportCodes.ExportIo;
        if (message.IndexOf("file could not be read", StringComparison.OrdinalIgnoreCase) >= 0)
            return ReportCodes.FileIo;
        if (message.IndexOf("condition", StringComparison.OrdinalIgnoreCase) >= 0)
            return ReportCodes.ConditionInvalid;
        if (message.IndexOf("transform", StringComparison.OrdinalIgnoreCase) >= 0)
            return ReportCodes.TransformInvalid;
        if (message.IndexOf("property", StringComparison.OrdinalIgnoreCase) >= 0)
            return ReportCodes.PropertyInvalid;
        if (message.IndexOf("Duplicate object id", StringComparison.OrdinalIgnoreCase) >= 0)
            return ReportCodes.PlacementDuplicateId;
        if (message.IndexOf("Unknown", StringComparison.OrdinalIgnoreCase) >= 0)
            return ReportCodes.JsonUnknownField;
        return ReportCodes.FrameworkJson;
    }
}