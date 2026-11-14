using Blasphemous.EditorMode.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class ReportCodesTests
{
    [Fact]
    public void Catalog_preserves_the_exact_existing_static_codes()
    {
        Dictionary<string, string> expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Unknown"] = "UNKNOWN",
            ["FrameworkJson"] = "FRAMEWORK_JSON",
            ["JsonSyntax"] = "JSON_SYNTAX",
            ["JsonSchema"] = "JSON_SCHEMA",
            ["ExportIo"] = "EXPORT_IO",
            ["FileIo"] = "FILE_IO",
            ["JsonUnknownField"] = "JSON_UNKNOWN_FIELD",
            ["DependencyMissing"] = "DEPENDENCY_MISSING",
            ["DependencyVersion"] = "DEPENDENCY_VERSION",
            ["FlowFailed"] = "FLOW_FAILED",
            ["FlowRecoveryFailed"] = "FLOW_RECOVERY_FAILED",
            ["FlowRecovered"] = "FLOW_RECOVERED",
            ["RoomRequired"] = "ROOM_REQUIRED",
            ["FileRequired"] = "FILE_REQUIRED",
            ["RoomScope"] = "ROOM_SCOPE",
            ["ImportInvalid"] = "IMPORT_INVALID",
            ["CreatorUnsupported"] = "CREATOR_UNSUPPORTED",
            ["AdditionUneditablePreserved"] = "ADDITION_UNEDITABLE_PRESERVED",
            ["PlacementDuplicateId"] = "PLACEMENT_DUPLICATE_ID",
            ["OriginalObjectReadOnly"] = "ORIGINAL_OBJECT_READ_ONLY",
            ["PlacementRequired"] = "PLACEMENT_REQUIRED",
            ["OwnershipRejected"] = "OWNERSHIP_REJECTED",
            ["PlacementNotFound"] = "PLACEMENT_NOT_FOUND",
            ["ExportSessionFailed"] = "EXPORT_SESSION_FAILED",
            ["ExportFailed"] = "EXPORT_FAILED",
            ["UneditableAdditionPreserved"] = "UNEDITABLE_ADDITION_PRESERVED",
            ["ExportSucceeded"] = "EXPORT_SUCCEEDED",
            ["PlacementInvalid"] = "PLACEMENT_INVALID",
            ["TransformUnsupported"] = "TRANSFORM_UNSUPPORTED",
            ["PropertyUnsupported"] = "PROPERTY_UNSUPPORTED",
            ["TransformInvalid"] = "TRANSFORM_INVALID",
            ["ConditionInvalid"] = "CONDITION_INVALID",
            ["RuntimeCreateFailed"] = "RUNTIME_CREATE_FAILED",
            ["RuntimeApplyFailed"] = "RUNTIME_APPLY_FAILED",
            ["RuntimeAdoptionFailed"] = "RUNTIME_ADOPTION_FAILED",
            ["PlacementCopied"] = "PLACEMENT_COPIED",
            ["ClipboardEmpty"] = "CLIPBOARD_EMPTY",
            ["RuntimeClearFailed"] = "RUNTIME_CLEAR_FAILED",
            ["PlacementLocked"] = "PLACEMENT_LOCKED",
            ["PropertyInvalid"] = "PROPERTY_INVALID",
            ["RuntimeRemoveFailed"] = "RUNTIME_REMOVE_FAILED",
            ["SessionReset"] = "SESSION_RESET",
            ["HistoryRestoreFailed"] = "HISTORY_RESTORE_FAILED",
            ["HistoryRollbackFailed"] = "HISTORY_ROLLBACK_FAILED",
            ["RuntimeRollbackFailed"] = "RUNTIME_ROLLBACK_FAILED",
            ["ConditionUnmet"] = "CONDITION_UNMET",
            ["UndoEmpty"] = "UNDO_EMPTY",
            ["RedoEmpty"] = "REDO_EMPTY",
            ["RoundTripInvalid"] = "ROUND_TRIP_INVALID",
            ["RoundTripScope"] = "ROUND_TRIP_SCOPE",
            ["RoundTripCount"] = "ROUND_TRIP_COUNT",
            ["RoundTripId"] = "ROUND_TRIP_ID",
            ["RoundTripState"] = "ROUND_TRIP_STATE",
            ["RoundTripMatch"] = "ROUND_TRIP_MATCH",
            ["RoomEffectsStoreMissing"] = "ROOM_EFFECTS_STORE_MISSING",
            ["PlaceNotReady"] = "PLACE_NOT_READY",
            ["PlaceNoPreview"] = "PLACE_NO_PREVIEW",
            ["PickNotReady"] = "PICK_NOT_READY",
            ["PickNoSelection"] = "PICK_NO_SELECTION",
            ["PickNoTarget"] = "PICK_NO_TARGET",
            ["PickFailed"] = "PICK_FAILED",
            ["PickPointerOverEditor"] = "PICK_POINTER_OVER_EDITOR",
            ["SelectionFailed"] = "SELECTION_FAILED",
            ["PreviewFailed"] = "PREVIEW_FAILED",
            ["PreviewCreated"] = "PREVIEW_CREATED",
            ["PreviewDisposed"] = "PREVIEW_DISPOSED",
            ["SeamPassed"] = "SEAM_PASSED",
            ["SeamFailed"] = "SEAM_FAILED",
            ["DependencyPassed"] = "DEPENDENCY_PASSED",
            ["DependencyFailed"] = "DEPENDENCY_FAILED",
            ["RoomSafetyPassed"] = "ROOM_SAFETY_PASSED",
            ["RoomSafetyFailed"] = "ROOM_SAFETY_FAILED",
            ["OriginalSavePassed"] = "ORIGINAL_SAVE_PASSED",
            ["OriginalSaveFailed"] = "ORIGINAL_SAVE_FAILED",
            ["RecoveryPassed"] = "RECOVERY_PASSED",
            ["RecoveryFailed"] = "RECOVERY_FAILED",
            ["NoDuplicatePassed"] = "NO_DUPLICATE_PASSED",
            ["NoDuplicateFailed"] = "NO_DUPLICATE_FAILED",
            ["RealGamePending"] = "REAL_GAME_PENDING",
            ["RealGamePassed"] = "REAL_GAME_PASSED"
        };

        FieldInfo[] fields = typeof(ReportCodes)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .ToArray();

        Assert.Equal(expected.Count, fields.Length);
        foreach (FieldInfo field in fields)
        {
            Assert.True(
                expected.TryGetValue(field.Name, out string value),
                "Unexpected ReportCodes member: " + field.Name);
            Assert.Equal(value, field.GetRawConstantValue());
        }
    }

    [Fact]
    public void P0_code_factory_rejects_an_invalid_check_identity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReportCodes.P0Code((P0AcceptanceCheck)int.MaxValue, true));
    }
}