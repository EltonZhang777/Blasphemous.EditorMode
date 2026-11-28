using System;
using System.Collections.Generic;

namespace Blasphemous.EditorMode.Diagnostics;

internal enum P0AcceptanceCheck
{
    Seam,
    Dependency,
    RoomSafety,
    OriginalSave,
    Recovery,
    NoDuplicate
}

internal static class ReportCodes
{
    internal const string Unknown = "UNKNOWN";

    internal const string DependencyMissing = "DEPENDENCY_MISSING";
    internal const string DependencyVersion = "DEPENDENCY_VERSION";

    internal const string FrameworkJson = "FRAMEWORK_JSON";
    internal const string JsonSyntax = "JSON_SYNTAX";
    internal const string JsonSchema = "JSON_SCHEMA";
    internal const string ExportIo = "EXPORT_IO";
    internal const string FileIo = "FILE_IO";
    internal const string JsonUnknownField = "JSON_UNKNOWN_FIELD";

    internal const string FlowFailed = "FLOW_FAILED";
    internal const string FlowRecoveryFailed = "FLOW_RECOVERY_FAILED";
    internal const string FlowRecovered = "FLOW_RECOVERED";

    internal const string RoomRequired = "ROOM_REQUIRED";
    internal const string FileRequired = "FILE_REQUIRED";
    internal const string RoomScope = "ROOM_SCOPE";
    internal const string ImportInvalid = "IMPORT_INVALID";
    internal const string CreatorUnsupported = "CREATOR_UNSUPPORTED";
    internal const string AdditionUneditablePreserved = "ADDITION_UNEDITABLE_PRESERVED";
    internal const string PlacementDuplicateId = "PLACEMENT_DUPLICATE_ID";
    internal const string OriginalObjectReadOnly = "ORIGINAL_OBJECT_READ_ONLY";
    internal const string PlacementRequired = "PLACEMENT_REQUIRED";
    internal const string OwnershipRejected = "OWNERSHIP_REJECTED";
    internal const string PlacementNotFound = "PLACEMENT_NOT_FOUND";
    internal const string ExportSessionFailed = "EXPORT_SESSION_FAILED";
    internal const string ExportFailed = "EXPORT_FAILED";
    internal const string UneditableAdditionPreserved = "UNEDITABLE_ADDITION_PRESERVED";
    internal const string ExportSucceeded = "EXPORT_SUCCEEDED";

    internal const string PlacementInvalid = "PLACEMENT_INVALID";
    internal const string TransformUnsupported = "TRANSFORM_UNSUPPORTED";
    internal const string PropertyUnsupported = "PROPERTY_UNSUPPORTED";
    internal const string TransformInvalid = "TRANSFORM_INVALID";
    internal const string ConditionInvalid = "CONDITION_INVALID";

    internal const string RuntimeCreateFailed = "RUNTIME_CREATE_FAILED";
    internal const string RuntimeApplyFailed = "RUNTIME_APPLY_FAILED";
    internal const string RuntimeAdoptionFailed = "RUNTIME_ADOPTION_FAILED";
    internal const string PlacementCopied = "PLACEMENT_COPIED";
    internal const string ClipboardEmpty = "CLIPBOARD_EMPTY";
    internal const string RuntimeClearFailed = "RUNTIME_CLEAR_FAILED";
    internal const string PlacementLocked = "PLACEMENT_LOCKED";
    internal const string PropertyInvalid = "PROPERTY_INVALID";
    internal const string RuntimeRemoveFailed = "RUNTIME_REMOVE_FAILED";
    internal const string SessionReset = "SESSION_RESET";
    internal const string HistoryRestoreFailed = "HISTORY_RESTORE_FAILED";
    internal const string HistoryRollbackFailed = "HISTORY_ROLLBACK_FAILED";
    internal const string RuntimeRollbackFailed = "RUNTIME_ROLLBACK_FAILED";
    internal const string ConditionUnmet = "CONDITION_UNMET";

    internal const string UndoEmpty = "UNDO_EMPTY";
    internal const string RedoEmpty = "REDO_EMPTY";

    internal const string RoundTripInvalid = "ROUND_TRIP_INVALID";
    internal const string RoundTripScope = "ROUND_TRIP_SCOPE";
    internal const string RoundTripCount = "ROUND_TRIP_COUNT";
    internal const string RoundTripId = "ROUND_TRIP_ID";
    internal const string RoundTripState = "ROUND_TRIP_STATE";
    internal const string RoundTripMatch = "ROUND_TRIP_MATCH";

    internal const string RoomEffectsStoreMissing = "ROOM_EFFECTS_STORE_MISSING";
    internal const string PlaceNotReady = "PLACE_NOT_READY";
    internal const string PlaceNoPreview = "PLACE_NO_PREVIEW";
    internal const string PickNotReady = "PICK_NOT_READY";
    internal const string PickNoSelection = "PICK_NO_SELECTION";
    internal const string PickNoTarget = "PICK_NO_TARGET";
    internal const string PickFailed = "PICK_FAILED";
    internal const string PickPointerOverEditor = "PICK_POINTER_OVER_EDITOR";
    internal const string SelectionFailed = "SELECTION_FAILED";
    internal const string PreviewFailed = "PREVIEW_FAILED";
    internal const string PreviewCreated = "PREVIEW_CREATED";
    internal const string PreviewDisposed = "PREVIEW_DISPOSED";

    internal const string SeamPassed = "SEAM_PASSED";
    internal const string SeamFailed = "SEAM_FAILED";
    internal const string DependencyPassed = "DEPENDENCY_PASSED";
    internal const string DependencyFailed = "DEPENDENCY_FAILED";
    internal const string RoomSafetyPassed = "ROOM_SAFETY_PASSED";
    internal const string RoomSafetyFailed = "ROOM_SAFETY_FAILED";
    internal const string OriginalSavePassed = "ORIGINAL_SAVE_PASSED";
    internal const string OriginalSaveFailed = "ORIGINAL_SAVE_FAILED";
    internal const string RecoveryPassed = "RECOVERY_PASSED";
    internal const string RecoveryFailed = "RECOVERY_FAILED";
    internal const string NoDuplicatePassed = "NO_DUPLICATE_PASSED";
    internal const string NoDuplicateFailed = "NO_DUPLICATE_FAILED";

    internal const string RealGamePending = "REAL_GAME_PENDING";
    internal const string RealGamePassed = "REAL_GAME_PASSED";

    internal static KeyValuePair<string, string> P0Code(
        P0AcceptanceCheck check,
        bool passed)
    {
        return check switch
        {
            P0AcceptanceCheck.Seam => new KeyValuePair<string, string>(
                passed ? SeamPassed : SeamFailed,
                "SEAM"),
            P0AcceptanceCheck.Dependency => new KeyValuePair<string, string>(
                passed ? DependencyPassed : DependencyFailed,
                "DEPENDENCY"),
            P0AcceptanceCheck.RoomSafety => new KeyValuePair<string, string>(
                passed ? RoomSafetyPassed : RoomSafetyFailed,
                "ROOM_SAFETY"),
            P0AcceptanceCheck.OriginalSave => new KeyValuePair<string, string>(
                passed ? OriginalSavePassed : OriginalSaveFailed,
                "ORIGINAL_SAVE"),
            P0AcceptanceCheck.Recovery => new KeyValuePair<string, string>(
                passed ? RecoveryPassed : RecoveryFailed,
                "RECOVERY"),
            P0AcceptanceCheck.NoDuplicate => new KeyValuePair<string, string>(
                passed ? NoDuplicatePassed : NoDuplicateFailed,
                "NO_DUPLICATE"),
            _ => throw new ArgumentOutOfRangeException(nameof(check), check, "Unknown P0 acceptance check.")
        };
    }
}