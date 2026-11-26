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

/// <summary>Result of exporting the current Room Overlay.</summary>
internal sealed class LevelEditSessionExportResult
{
    internal LevelEditSessionExportResult(
        FrameworkExportResult frameworkResult,
        ApplyReport report,
        FrameworkLevelEdit document)
    {
        FrameworkResult = frameworkResult;
        Report = report;
        Document = document;
    }

    internal FrameworkExportResult FrameworkResult { get; }
    internal ApplyReport Report { get; }
    internal FrameworkLevelEdit Document { get; }
    internal bool Succeeded { get { return FrameworkResult != null && FrameworkResult.Succeeded; } }
    internal string StatusMessage { get { return Report.ToSummary("Level export"); } }
}

/// <summary>Domain seam for importing and editing one current Room Overlay.</summary>
internal sealed class LevelEditSession
{
    private readonly List<PlacementRecord> _placements;
    private readonly List<FrameworkObjectData> _modifications;
    private readonly List<FrameworkObjectData> _deletions;
    private readonly HashSet<string> _issuedOriginalObjectIds;

    private LevelEditSession(
        RoomIdentity room,
        FrameworkLevelEdit importedDocument,
        string sourceText,
        IEnumerable<PlacementRecord> placements,
        IEnumerable<FrameworkObjectData> modifications,
        IEnumerable<FrameworkObjectData> deletions,
        LevelEditSessionState state,
        ApplyReport report)
    {
        Room = room;
        ImportedDocument = importedDocument;
        OriginalSourceText = sourceText;
        _placements = [.. placements ?? []];
        State = state;
        ImportedState = state;
        LastReport = report;
        _modifications = [.. modifications ?? []];
        _deletions = [.. deletions ?? []];
        _issuedOriginalObjectIds = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (FrameworkObjectData addition in importedDocument.Additions)
            _issuedOriginalObjectIds.Add(addition.Id);
        foreach (FrameworkObjectData modification in _modifications)
            _issuedOriginalObjectIds.Add(modification.Id);
    }

    internal RoomIdentity Room { get; }
    internal FrameworkLevelEdit ImportedDocument { get; }
    internal string OriginalSourceText { get; }
    internal LevelEditSessionState State { get; private set; }
    internal LevelEditSessionState ImportedState { get; }
    internal ApplyReport LastReport { get; private set; }

    internal ReadOnlyCollection<PlacementRecord> Placements
    {
        get { return new ReadOnlyCollection<PlacementRecord>(_placements); }
    }

    internal ReadOnlyCollection<FrameworkObjectData> Modifications
    {
        get { return new ReadOnlyCollection<FrameworkObjectData>(_modifications); }
    }

    internal ReadOnlyCollection<FrameworkObjectData> Deletions
    {
        get { return new ReadOnlyCollection<FrameworkObjectData>(_deletions); }
    }

    internal static LevelEditSessionImportResult Import(
        RoomIdentity room,
        FrameworkFileResult file,
        IEnumerable<CreatorMetadata> supportedCreators)
    {
        ApplyReport report = new();
        if (room == null)
        {
            report.Error(ReportCodes.RoomRequired, "$", "Current Room Identity is required.");
            return Failed(report, null);
        }

        if (file == null)
        {
            report.Error(ReportCodes.FileRequired, "$", "A Level Export file is required.");
            return Failed(report, null);
        }

        if (!string.Equals(
            Path.GetFileNameWithoutExtension(file.Path),
            room.ExportFileKey,
            StringComparison.OrdinalIgnoreCase))
        {
            report.Error(
                ReportCodes.RoomScope,
                file.Path,
                "Level Export does not match the current Room Identity.");
            return Failed(report, file.OriginalText);
        }

        if (file.Document == null || HasFatalFrameworkDiagnostic(file.Diagnostics))
        {
            MergeImportDiagnostics(report, file);
            report.Error(
                ReportCodes.ImportInvalid,
                file.Path,
                "Level Export cannot be imported; the source remains unchanged.");
            return Failed(report, file.OriginalText);
        }

        MergeImportDiagnostics(report, file);
        Dictionary<string, CreatorMetadata> creators = new(StringComparer.OrdinalIgnoreCase);
        foreach (CreatorMetadata creator in supportedCreators ?? [])
        {
            if (creator != null && creator.IsSupported && !creators.ContainsKey(creator.Type))
                creators.Add(creator.Type, creator);
        }

        List<PlacementRecord> placements = [];
        List<FrameworkObjectData> importedAdditions = [];
        HashSet<string> sourceIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (FrameworkObjectData data in file.Document.Additions)
        {
            bool duplicateSourceId = !string.IsNullOrEmpty(data.Id)
                && !sourceIds.Add(data.Id);
            if (data.PreserveOriginalAddition)
            {
                string location = "additions[" + (string.IsNullOrEmpty(data.Id) ? "unknown" : data.Id) + "]";
                importedAdditions.Add(data);
                CreatorMetadata originalCreator = null;
                if (!string.IsNullOrEmpty(data.Type)
                    && !creators.TryGetValue(data.Type, out originalCreator))
                {
                    report.Warning(
                        ReportCodes.CreatorUnsupported,
                        location,
                        "Creator '" + data.Type + "' is unavailable in the current profile.",
                        true);
                }
                else
                {
                    if (originalCreator != null)
                        ReportValidationWarnings(data, originalCreator, report);
                }
                ReportPreservedAddition(report, location);
                continue;
            }

            if (!creators.TryGetValue(data.Type, out CreatorMetadata creator))
            {
                string location = "additions[" + data.Id + "]";
                report.Warning(
                    ReportCodes.CreatorUnsupported,
                    location,
                    "Creator '" + data.Type + "' is unavailable in the current profile.",
                    true);
                importedAdditions.Add(data.WithPreservedOriginalAddition());
                ReportPreservedAddition(report, location);
                continue;
            }

            ApplyReport validation = new();
            if (!PlacementValidation.Validate(data, creator, validation))
            {
                string location = "additions[" + data.Id + "]";
                MergeValidationWarnings(report, validation);
                importedAdditions.Add(data.WithPreservedOriginalAddition());
                ReportPreservedAddition(report, location);
                continue;
            }

            if (duplicateSourceId)
            {
                string location = "additions[" + data.Id + "]";
                report.Warning(
                    ReportCodes.PlacementDuplicateId,
                    location,
                    "Placement id is duplicated in the session; the source addition was retained unchanged.",
                    true);
                importedAdditions.Add(data.WithPreservedOriginalAddition());
                ReportPreservedAddition(report, location);
                continue;
            }

            importedAdditions.Add(data);
            placements.Add(PlacementRecord.FromFramework(data));
            report.Applied();
        }

        FrameworkLevelEdit importedDocument = new(
            importedAdditions,
            file.Document.Modifications,
            file.Document.Deletions);
        LevelEditSessionState state = report.IsPartial
            ? LevelEditSessionState.Partial
            : LevelEditSessionState.Clean;
        LevelEditSession session = new(
            room,
            importedDocument,
            file.OriginalText,
            placements,
            file.Document.Modifications,
            file.Document.Deletions,
            state,
            report);
        return new LevelEditSessionImportResult(session, state, report, file.OriginalText);
    }

    private static void MergeImportDiagnostics(
        ApplyReport report,
        FrameworkFileResult file)
    {
        List<FrameworkDiagnostic> retained = [];
        List<FrameworkDiagnostic> other = [];
        foreach (FrameworkDiagnostic diagnostic in file.Diagnostics)
        {
            if (IsRetainedAdditionDiagnostic(diagnostic, file.Document?.Additions))
                retained.Add(diagnostic);
            else
                other.Add(diagnostic);
        }

        report.MergeRetainedAdditionDiagnostics(retained);
        report.MergeFrameworkDiagnostics(other);
    }

    private static bool IsRetainedAdditionDiagnostic(
        FrameworkDiagnostic diagnostic,
        IList<FrameworkObjectData> additions)
    {
        string prefix = LevelFrameworkContract.AdditionsField + "[";
        if (additions == null
            || !diagnostic.Location.StartsWith(prefix, StringComparison.Ordinal)
            || diagnostic.Message.IndexOf(
                "Duplicate object id",
                StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        int close = diagnostic.Location.IndexOf(']', prefix.Length);
        if (close <= prefix.Length
            || !int.TryParse(
                diagnostic.Location.Substring(prefix.Length, close - prefix.Length),
                out int index)
            || index < 0
            || index >= additions.Count)
        {
            return false;
        }

        return additions[index] != null && additions[index].PreserveOriginalAddition;
    }

    private static void ReportPreservedAddition(ApplyReport report, string location)
    {
        report.Warning(
            ReportCodes.AdditionUneditablePreserved,
            location,
            "The source addition cannot be represented by this editor profile and was retained unchanged.",
            true);
    }

    private static void ReportValidationWarnings(
        FrameworkObjectData data,
        CreatorMetadata creator,
        ApplyReport report)
    {
        ApplyReport validation = new();
        PlacementValidation.Validate(data, creator, validation);
        MergeValidationWarnings(report, validation);
    }

    private static void MergeValidationWarnings(ApplyReport report, ApplyReport validation)
    {
        foreach (ApplyReportEntry entry in validation.Entries)
        {
            report.Warning(
                entry.Code,
                entry.Location,
                entry.Message,
                entry.Skipped);
        }
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
            report.Error(ReportCodes.PlacementRequired, "$", "Placement is required.");
            return false;
        }
        if (placement.Ownership != PlacementOwnership.ArchitectOwned)
        {
            report.Error(ReportCodes.OwnershipRejected, placement.Id, "Only Architect-owned placements can be edited.");
            return false;
        }
        if (TryGetPlacement(placement.Id, out _)
            || IsIdIssued(placement.Id))
        {
            report.Error(ReportCodes.PlacementDuplicateId, placement.Id, "Placement id already exists.");
            return false;
        }

        _placements.Add(placement);
        MarkOriginalObjectModificationIdIssued(placement.Id);
        MarkDirty(report);
        return true;
    }

    internal bool TryReplacePlacement(PlacementRecord placement, ApplyReport report)
    {
        if (placement == null || placement.Ownership != PlacementOwnership.ArchitectOwned)
        {
            report.Error(ReportCodes.OwnershipRejected, placement == null ? "$" : placement.Id, "Placement is not editable.");
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

        report.Error(ReportCodes.PlacementNotFound, placement.Id, "Placement is not present in the session.");
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
                report.Error(ReportCodes.OwnershipRejected, id, "Original objects cannot be removed.");
                return false;
            }

            removed = placement;
            _placements.RemoveAt(index);
            MarkDirty(report);
            return true;
        }

        report.Error(ReportCodes.PlacementNotFound, id, "Placement is not present in the session.");
        return false;
    }

    internal string NextOriginalObjectModificationId()
    {
        return PlacementIdGenerator.Next(
            OriginalObjectEditor.EmptyModificationType,
            _issuedOriginalObjectIds);
    }

    internal void MarkOriginalObjectModificationIdIssued(string id)
    {
        if (!string.IsNullOrEmpty(id))
            _issuedOriginalObjectIds.Add(id);
    }

    internal bool IsIdIssued(string id)
    {
        return !string.IsNullOrEmpty(id)
            && _issuedOriginalObjectIds.Contains(id);
    }

    internal IEnumerable<string> IssuedIds()
    {
        foreach (string id in _issuedOriginalObjectIds)
            yield return id;
    }

    internal bool TryUpsertOriginalObjectModification(
        FrameworkObjectData modification,
        ApplyReport report)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));
        if (!ValidateOriginalObjectModification(modification, report))
            return false;

        int modificationIndex = FindModificationIndex(modification.Scene, modification.Path);
        int deletionIndex = FindDeletionIndex(modification.Scene, modification.Path);
        int operationCount = CountMatchingOperations(modification.Scene, modification.Path);
        if (operationCount > 1)
        {
            report.Error(
                ReportCodes.OriginalObjectEditConflict,
                modification.Scene + ":" + modification.Path,
                "Multiple imported operations target this identity; resolve the conflict before editing.");
            return false;
        }

        if (modificationIndex >= 0)
        {
            _modifications[modificationIndex] = modification;
        }
        else
        {
            if (deletionIndex >= 0)
                _deletions.RemoveAt(deletionIndex);
            _modifications.Add(modification);
        }

        MarkOriginalObjectModificationIdIssued(modification.Id);
        MarkDirty(report);
        return true;
    }

    internal bool TryUpsertOriginalObjectDeletion(
        OriginalObjectIdentity identity,
        ApplyReport report)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));
        if (!ValidateOriginalObjectDeletion(identity, report))
            return false;

        int modificationIndex = FindModificationIndex(identity.Scene, identity.Path);
        int deletionIndex = FindDeletionIndex(identity.Scene, identity.Path);
        int operationCount = CountMatchingOperations(identity.Scene, identity.Path);
        if (operationCount > 1)
        {
            report.Error(
                ReportCodes.OriginalObjectEditConflict,
                identity.DisplayText,
                "Multiple imported operations target this identity; resolve the conflict before editing.");
            return false;
        }

        FrameworkObjectData deletion = OriginalObjectEditor.CreateDeletion(identity);
        if (deletionIndex >= 0)
        {
            _deletions[deletionIndex] = deletion;
        }
        else
        {
            if (modificationIndex >= 0)
                _modifications.RemoveAt(modificationIndex);
            _deletions.Add(deletion);
        }

        MarkDirty(report);
        return true;
    }

    internal bool TryRestoreImportedOriginalObjectEdit(
        OriginalObjectIdentity identity,
        ApplyReport report)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));
        if (identity == null)
        {
            report.Error(
                ReportCodes.OriginalObjectIdentityInvalid,
                "$",
                "Original Object identity is required.");
            return false;
        }

        if (CountMatchingOperations(identity.Scene, identity.Path) > 1)
        {
            report.Error(
                ReportCodes.OriginalObjectEditConflict,
                identity.DisplayText,
                "Multiple operations target this identity and cannot be restored as one operation.");
            return false;
        }

        List<FrameworkObjectData> importedModifications =
            FindMatching(ImportedDocument.Modifications, identity);
        List<FrameworkObjectData> importedDeletions =
            FindMatching(ImportedDocument.Deletions, identity);
        if (importedModifications.Count + importedDeletions.Count > 1)
        {
            report.Error(
                ReportCodes.OriginalObjectEditConflict,
                identity.DisplayText,
                "Multiple imported operations target this identity and cannot be restored as one operation.");
            return false;
        }

        RemoveMatching(_modifications, identity);
        RemoveMatching(_deletions, identity);
        if (importedModifications.Count == 1)
        {
            int index = ImportedModificationInsertionIndex(identity);
            _modifications.Insert(
                Math.Min(index, _modifications.Count),
                importedModifications[0]);
        }
        if (importedDeletions.Count == 1)
        {
            int index = ImportedDeletionInsertionIndex(identity);
            _deletions.Insert(
                Math.Min(index, _deletions.Count),
                importedDeletions[0]);
        }

        SetState(ResolveRestoredState(State), report);
        return true;
    }

    internal void RestoreOriginalObjectEdits(
        IEnumerable<FrameworkObjectData> modifications,
        IEnumerable<FrameworkObjectData> deletions,
        LevelEditSessionState state,
        ApplyReport report)
    {
        _modifications.Clear();
        _modifications.AddRange(modifications ?? []);
        _deletions.Clear();
        _deletions.AddRange(deletions ?? []);
        SetState(ResolveRestoredState(state), report);
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

    internal void RestorePlacements(
        IEnumerable<PlacementRecord> placements,
        LevelEditSessionState state,
        ApplyReport report)
    {
        _placements.Clear();
        _placements.AddRange(placements ?? []);
        SetState(ResolveRestoredState(state), report);
    }

    internal FrameworkLevelEdit BuildExportDocument()
    {
        List<FrameworkObjectData> additions = [];
        Dictionary<string, PlacementRecord> currentById = new(StringComparer.OrdinalIgnoreCase);
        foreach (PlacementRecord placement in _placements)
        {
            if (placement.Ownership == PlacementOwnership.ArchitectOwned)
                currentById.Add(placement.Id, placement);
        }

        HashSet<string> importedIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (FrameworkObjectData sourceAddition in ImportedDocument.Additions)
        {
            importedIds.Add(sourceAddition.Id);
            if (sourceAddition.PreserveOriginalAddition)
            {
                additions.Add(sourceAddition.WithPreservedOriginalAddition());
                continue;
            }

            if (currentById.TryGetValue(sourceAddition.Id, out PlacementRecord placement))
                additions.Add(placement.ToFrameworkData());
        }

        foreach (PlacementRecord placement in _placements)
        {
            if (placement.Ownership == PlacementOwnership.ArchitectOwned
                && !importedIds.Contains(placement.Id))
            {
                additions.Add(placement.ToFrameworkData());
            }
        }

        return new FrameworkLevelEdit(
            additions,
            _modifications,
            _deletions);
    }

    internal LevelEditSessionExportResult Export(string path)
    {
        ApplyReport report = new();
        if (State == LevelEditSessionState.Failed)
        {
            report.Error(ReportCodes.ExportSessionFailed, path, "A failed session cannot be exported.");
            return new LevelEditSessionExportResult(null, report, null);
        }

        FrameworkLevelEdit document = BuildExportDocument();
        FrameworkExportResult frameworkResult = LevelFrameworkAdapter.ExportFile(
            path,
            Room.ExportFileKey,
            document);
        report.MergeFrameworkDiagnostics(frameworkResult.Diagnostics);
        if (!frameworkResult.Succeeded)
        {
            report.Error(
                ReportCodes.ExportFailed,
                path,
                "Export failed; the previous valid file remains available.");
            LastReport = report;
            return new LevelEditSessionExportResult(frameworkResult, report, document);
        }

        foreach (FrameworkObjectData addition in ImportedDocument.Additions)
        {
            if (addition.PreserveOriginalAddition)
            {
                report.Warning(
                    ReportCodes.UneditableAdditionPreserved,
                    addition.Id,
                    "An uneditable source addition was retained unchanged.",
                    true);
            }
        }

        State = report.IsPartial ? LevelEditSessionState.Partial : LevelEditSessionState.Clean;
        LastReport = report;
        report.Applied();
        report.Info(ReportCodes.ExportSucceeded, path, "Framework-compatible Level Export written; Original Save was not changed.");
        return new LevelEditSessionExportResult(frameworkResult, report, document);
    }

    private static bool HasFatalFrameworkDiagnostic(IEnumerable<FrameworkDiagnostic> diagnostics)
    {
        foreach (FrameworkDiagnostic diagnostic in diagnostics ?? [])
        {
            if (diagnostic.Severity != FrameworkDiagnosticSeverity.Error)
                continue;
            if (diagnostic.Location == "$"
                || diagnostic.Location == LevelFrameworkContract.AdditionsField
                || diagnostic.Location == LevelFrameworkContract.ModificationsField
                || diagnostic.Location == LevelFrameworkContract.DeletionsField
                || diagnostic.Location.StartsWith(LevelFrameworkContract.ModificationsField + "[", StringComparison.Ordinal)
                || diagnostic.Location.StartsWith(LevelFrameworkContract.DeletionsField + "[", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private int CountMatchingOperations(string scene, string path)
    {
        int count = 0;
        foreach (FrameworkObjectData modification in _modifications)
        {
            if (SameIdentity(modification, scene, path))
                count++;
        }
        foreach (FrameworkObjectData deletion in _deletions)
        {
            if (SameIdentity(deletion, scene, path))
                count++;
        }

        return count;
    }

    private int FindModificationIndex(string scene, string path)
    {
        for (int index = 0; index < _modifications.Count; index++)
        {
            if (SameIdentity(_modifications[index], scene, path))
                return index;
        }

        return -1;
    }

    private int FindDeletionIndex(string scene, string path)
    {
        for (int index = 0; index < _deletions.Count; index++)
        {
            if (SameIdentity(_deletions[index], scene, path))
                return index;
        }

        return -1;
    }

    private int ImportedModificationInsertionIndex(OriginalObjectIdentity identity)
    {
        int index = 0;
        foreach (FrameworkObjectData operation in ImportedDocument.Modifications)
        {
            if (SameIdentity(operation, identity.Scene, identity.Path))
                return index;
            index++;
        }

        return index;
    }

    private int ImportedDeletionInsertionIndex(OriginalObjectIdentity identity)
    {
        int index = 0;
        foreach (FrameworkObjectData operation in ImportedDocument.Deletions)
        {
            if (SameIdentity(operation, identity.Scene, identity.Path))
                return index;
            index++;
        }

        return index;
    }

    private static List<FrameworkObjectData> FindMatching(
        IEnumerable<FrameworkObjectData> operations,
        OriginalObjectIdentity identity)
    {
        List<FrameworkObjectData> matching = [];
        foreach (FrameworkObjectData operation in operations ?? [])
        {
            if (SameIdentity(operation, identity.Scene, identity.Path))
                matching.Add(operation);
        }

        return matching;
    }

    private static void RemoveMatching(
        List<FrameworkObjectData> operations,
        OriginalObjectIdentity identity)
    {
        for (int index = operations.Count - 1; index >= 0; index--)
        {
            if (SameIdentity(operations[index], identity.Scene, identity.Path))
                operations.RemoveAt(index);
        }
    }

    private LevelEditSessionState ResolveRestoredState(LevelEditSessionState requested)
    {
        if (HasPendingChanges())
            return LevelEditSessionState.Dirty;
        return requested == LevelEditSessionState.Failed
            ? requested
            : ImportedState;
    }

    private bool HasPendingChanges()
    {
        return HasPendingOriginalObjectChanges()
            || HasPendingPlacementChanges();
    }

    private bool HasPendingOriginalObjectChanges()
    {
        return !OperationsMatch(_modifications, ImportedDocument.Modifications)
            || !OperationsMatch(_deletions, ImportedDocument.Deletions);
    }

    private bool HasPendingPlacementChanges()
    {
        HashSet<string> importedIds = new(StringComparer.OrdinalIgnoreCase);
        int importedCount = 0;
        foreach (FrameworkObjectData addition in ImportedDocument.Additions)
        {
            if (addition == null
                || addition.PreserveOriginalAddition)
                continue;

            importedIds.Add(addition.Id);
            importedCount++;
            PlacementRecord expected;
            try
            {
                expected = PlacementRecord.FromFramework(addition);
            }
            catch (ArgumentException)
            {
                return true;
            }

            if (!TryGetPlacement(addition.Id, out PlacementRecord current)
                || !PlacementsMatch(current, expected))
                return true;
        }

        foreach (PlacementRecord placement in _placements)
        {
            if (!importedIds.Contains(placement.Id))
                return true;
        }

        return _placements.Count != importedCount;
    }

    private static bool OperationsMatch(
        IList<FrameworkObjectData> actual,
        IList<FrameworkObjectData> expected)
    {
        if (actual.Count != expected.Count)
            return false;

        for (int index = 0; index < actual.Count; index++)
        {
            if (!OperationMatches(actual[index], expected[index]))
                return false;
        }

        return true;
    }

    private static bool OperationMatches(
        FrameworkObjectData actual,
        FrameworkObjectData expected)
    {
        if (ReferenceEquals(actual, expected))
            return true;
        if (actual == null || expected == null)
            return false;

        return string.Equals(actual.Scene, expected.Scene, StringComparison.Ordinal)
            && string.Equals(actual.Path, expected.Path, StringComparison.Ordinal)
            && string.Equals(actual.Type, expected.Type, StringComparison.Ordinal)
            && string.Equals(actual.Id, expected.Id, StringComparison.Ordinal)
            && VectorsMatch(actual.Position, expected.Position)
            && VectorsMatch(actual.Rotation, expected.Rotation)
            && VectorsMatch(actual.Scale, expected.Scale)
            && string.Equals(actual.Condition, expected.Condition, StringComparison.Ordinal)
            && StringsMatch(actual.Properties, expected.Properties);
    }

    private static bool PlacementsMatch(
        PlacementRecord actual,
        PlacementRecord expected)
    {
        return string.Equals(actual.Id, expected.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(actual.Type, expected.Type, StringComparison.Ordinal)
            && actual.Transform.Equals(expected.Transform)
            && string.Equals(actual.Condition, expected.Condition, StringComparison.Ordinal)
            && actual.IsLocked == expected.IsLocked
            && actual.Ownership == expected.Ownership
            && StringsMatch(actual.Properties, expected.Properties);
    }

    private static bool VectorsMatch(FrameworkVector left, FrameworkVector right)
    {
        return left != null
            && right != null
            && left.X == right.X
            && left.Y == right.Y
            && left.Z == right.Z;
    }

    private static bool StringsMatch(
        IList<string> left,
        IList<string> right)
    {
        if (left == null || right == null || left.Count != right.Count)
            return false;

        for (int index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static bool SameIdentity(FrameworkObjectData operation, string scene, string path)
    {
        return operation != null
            && string.Equals(operation.Scene, scene, StringComparison.Ordinal)
            && string.Equals(operation.Path, path, StringComparison.Ordinal);
    }

    private static bool ValidateOriginalObjectModification(
        FrameworkObjectData modification,
        ApplyReport report)
    {
        if (modification == null)
        {
            report.Error(
                ReportCodes.OriginalObjectModificationInvalid,
                "$",
                "Original Object modification is required.");
            return false;
        }
        if (!OriginalObjectIdentity.TryCreate(
            modification.Scene,
            modification.Path,
            out _,
            out string identityError))
        {
            report.Error(
                ReportCodes.OriginalObjectIdentityInvalid,
                "$",
                identityError);
            return false;
        }
        if (string.IsNullOrEmpty(modification.Type)
            || string.IsNullOrEmpty(modification.Id))
        {
            report.Error(
                ReportCodes.OriginalObjectModificationInvalid,
                modification.Scene + ":" + modification.Path,
                "Original Object modification type and id are required.");
            return false;
        }

        return true;
    }

    private static bool ValidateOriginalObjectDeletion(
        OriginalObjectIdentity identity,
        ApplyReport report)
    {
        if (identity == null)
        {
            report.Error(
                ReportCodes.OriginalObjectDeletionInvalid,
                "$",
                "Original Object deletion identity is required.");
            return false;
        }
        if (!OriginalObjectIdentity.TryCreate(
            identity.Scene,
            identity.Path,
            out _,
            out string identityError))
        {
            report.Error(
                ReportCodes.OriginalObjectIdentityInvalid,
                "$",
                identityError);
            return false;
        }

        return true;
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