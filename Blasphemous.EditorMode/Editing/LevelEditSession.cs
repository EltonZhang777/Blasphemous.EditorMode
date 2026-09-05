using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Framework;
using Blasphemous.EditorMode.Lifecycle;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace Blasphemous.EditorMode.Editing;

/// <summary>Observable state of a current Room Overlay editing session.</summary>
internal enum LevelEditSessionState
{
    Clean,
    Partial,
    Dirty,
    Failed
}

/// <summary>Result of importing a room-scoped Level Export.</summary>
internal sealed class LevelEditSessionImportResult
{
    internal LevelEditSessionImportResult(
        LevelEditSession session,
        LevelEditSessionState state,
        ApplyReport report,
        string sourceText)
    {
        Session = session;
        State = state;
        Report = report;
        SourceText = sourceText;
    }

    internal LevelEditSession Session { get; }
    internal LevelEditSessionState State { get; }
    internal ApplyReport Report { get; }
    internal string SourceText { get; }
    internal bool Succeeded { get { return Session != null && State != LevelEditSessionState.Failed; } }
    internal string StatusMessage { get { return Report.ToSummary("Level import"); } }
}

/// <summary>Domain seam for importing and editing one current Room Overlay.</summary>
internal sealed class LevelEditSession
{
    private readonly List<PlacementRecord> _placements;

    private LevelEditSession(
        RoomIdentity room,
        FrameworkLevelEdit importedDocument,
        string sourceText,
        IEnumerable<PlacementRecord> placements,
        LevelEditSessionState state,
        ApplyReport report)
    {
        Room = room;
        ImportedDocument = importedDocument;
        OriginalSourceText = sourceText;
        _placements = new List<PlacementRecord>(placements ?? new PlacementRecord[0]);
        State = state;
        LastReport = report;
    }

    internal RoomIdentity Room { get; }
    internal FrameworkLevelEdit ImportedDocument { get; }
    internal string OriginalSourceText { get; }
    internal LevelEditSessionState State { get; private set; }
    internal ApplyReport LastReport { get; private set; }

    internal ReadOnlyCollection<PlacementRecord> Placements
    {
        get { return new ReadOnlyCollection<PlacementRecord>(_placements); }
    }

    internal static LevelEditSessionImportResult Import(
        RoomIdentity room,
        FrameworkFileResult file,
        IEnumerable<CreatorMetadata> supportedCreators)
    {
        var report = new ApplyReport();
        if (room == null)
        {
            report.Error("ROOM_REQUIRED", "$", "Current Room Identity is required.");
            return Failed(report, null);
        }

        if (file == null)
        {
            report.Error("FILE_REQUIRED", "$", "A Level Export file is required.");
            return Failed(report, null);
        }

        if (!string.Equals(
            Path.GetFileNameWithoutExtension(file.Path),
            room.ExportFileKey,
            StringComparison.OrdinalIgnoreCase))
        {
            report.Error(
                "ROOM_SCOPE",
                file.Path,
                "Level Export does not match the current Room Identity.");
            return Failed(report, file.OriginalText);
        }

        if (file.Document == null || HasFatalFrameworkDiagnostic(file.Diagnostics))
        {
            report.MergeFrameworkDiagnostics(file.Diagnostics);
            report.Error(
                "IMPORT_INVALID",
                file.Path,
                "Level Export cannot be imported; the source remains unchanged.");
            return Failed(report, file.OriginalText);
        }

        report.MergeFrameworkDiagnostics(file.Diagnostics);
        var creators = new Dictionary<string, CreatorMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (CreatorMetadata creator in supportedCreators ?? new CreatorMetadata[0])
        {
            if (creator != null && creator.IsSupported && !creators.ContainsKey(creator.Type))
                creators.Add(creator.Type, creator);
        }

        var placements = new List<PlacementRecord>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (FrameworkObjectData data in file.Document.Additions)
        {
            CreatorMetadata creator;
            if (!creators.TryGetValue(data.Type, out creator))
            {
                report.Error(
                    "CREATOR_UNSUPPORTED",
                    "additions[" + data.Id + "]",
                    "Creator '" + data.Type + "' is unavailable in the current profile.");
                continue;
            }

            if (!PlacementValidation.Validate(data, creator, report))
                continue;

            if (!ids.Add(data.Id))
            {
                report.Error(
                    "PLACEMENT_DUPLICATE_ID",
                    "additions[" + data.Id + "]",
                    "Placement id is duplicated in the session.");
                continue;
            }

            placements.Add(PlacementRecord.FromFramework(data));
            report.Applied();
        }

        if (file.Document.Modifications.Count > 0 || file.Document.Deletions.Count > 0)
        {
            report.Warning(
                "ORIGINAL_OBJECT_READ_ONLY",
                "$",
                "Original-object modifications and deletions were retained for round-trip but are not editable.");
        }

        LevelEditSessionState state = report.IsPartial
            ? LevelEditSessionState.Partial
            : LevelEditSessionState.Clean;
        var session = new LevelEditSession(
            room,
            file.Document,
            file.OriginalText,
            placements,
            state,
            report);
        return new LevelEditSessionImportResult(session, state, report, file.OriginalText);
    }

    internal bool TryGetPlacement(string id, out PlacementRecord placement)
    {
        placement = null;
        foreach (PlacementRecord candidate in _placements)
        {
            if (string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                placement = candidate;
                return true;
            }
        }

        return false;
    }

    internal bool TryAddPlacement(PlacementRecord placement, ApplyReport report)
    {
        if (placement == null)
        {
            report.Error("PLACEMENT_REQUIRED", "$", "Placement is required.");
            return false;
        }
        if (placement.Ownership != PlacementOwnership.ArchitectOwned)
        {
            report.Error("OWNERSHIP_REJECTED", placement.Id, "Only Architect-owned placements can be edited.");
            return false;
        }
        if (TryGetPlacement(placement.Id, out _))
        {
            report.Error("PLACEMENT_DUPLICATE_ID", placement.Id, "Placement id already exists.");
            return false;
        }

        _placements.Add(placement);
        MarkDirty(report);
        return true;
    }

    internal bool TryReplacePlacement(PlacementRecord placement, ApplyReport report)
    {
        if (placement == null || placement.Ownership != PlacementOwnership.ArchitectOwned)
        {
            report.Error("OWNERSHIP_REJECTED", placement == null ? "$" : placement.Id, "Placement is not editable.");
            return false;
        }

        for (int index = 0; index < _placements.Count; index++)
        {
            if (!string.Equals(_placements[index].Id, placement.Id, StringComparison.OrdinalIgnoreCase))
                continue;

            _placements[index] = placement;
            MarkDirty(report);
            return true;
        }

        report.Error("PLACEMENT_NOT_FOUND", placement.Id, "Placement is not present in the session.");
        return false;
    }

    internal bool TryRemovePlacement(string id, ApplyReport report, out PlacementRecord removed)
    {
        removed = null;
        for (int index = 0; index < _placements.Count; index++)
        {
            PlacementRecord placement = _placements[index];
            if (!string.Equals(placement.Id, id, StringComparison.OrdinalIgnoreCase))
                continue;
            if (placement.Ownership != PlacementOwnership.ArchitectOwned)
            {
                report.Error("OWNERSHIP_REJECTED", id, "Original objects cannot be removed.");
                return false;
            }

            removed = placement;
            _placements.RemoveAt(index);
            MarkDirty(report);
            return true;
        }

        report.Error("PLACEMENT_NOT_FOUND", id, "Placement is not present in the session.");
        return false;
    }

    internal void SetState(LevelEditSessionState state, ApplyReport report)
    {
        State = state;
        LastReport = report;
    }

    internal void MarkDirty(ApplyReport report)
    {
        State = LevelEditSessionState.Dirty;
        LastReport = report;
    }

    private static bool HasFatalFrameworkDiagnostic(IEnumerable<FrameworkDiagnostic> diagnostics)
    {
        foreach (FrameworkDiagnostic diagnostic in diagnostics ?? new FrameworkDiagnostic[0])
        {
            if (diagnostic.Severity != FrameworkDiagnosticSeverity.Error)
                continue;
            if (diagnostic.Location == "$"
                || diagnostic.Location == LevelFrameworkContract.AdditionsField
                || diagnostic.Location == LevelFrameworkContract.ModificationsField
                || diagnostic.Location == LevelFrameworkContract.DeletionsField)
                return true;
        }

        return false;
    }

    private static LevelEditSessionImportResult Failed(ApplyReport report, string sourceText)
    {
        return new LevelEditSessionImportResult(
            null,
            LevelEditSessionState.Failed,
            report,
            sourceText);
    }
}
