using System;

namespace Blasphemous.EditorMode.Diagnostics;

/// <summary>Safe states in which a failed editor flow may leave the user.</summary>
internal enum EditorFlowState
{
    RoomLoaded,
    Editing
}

/// <summary>Runs a P0 operation and restores its prior safe flow state on failure.</summary>
internal sealed class EditorFlowRecovery
{
    internal EditorFlowRecovery(EditorFlowState state)
    {
        State = state;
    }

    internal EditorFlowState State { get; private set; }

    internal void SetState(EditorFlowState state)
    {
        State = state;
    }

    internal bool Run(
        string operation,
        Func<bool> action,
        Func<bool> recover,
        ApplyReport report)
    {
        EditorFlowState previous = State;
        bool succeeded = false;
        try
        {
            succeeded = action != null && action();
        }
        catch (Exception exception)
        {
            report.Error("FLOW_FAILED", operation, exception.Message);
        }

        if (succeeded)
            return true;

        report.Error("FLOW_FAILED", operation, "Operation failed; attempting safe state recovery.");
        bool recovered = false;
        try
        {
            recovered = recover == null || recover();
        }
        catch (Exception exception)
        {
            report.Error("FLOW_RECOVERY_FAILED", operation, exception.Message);
        }

        if (recovered)
        {
            State = previous;
            report.Warning("FLOW_RECOVERED", operation, "Operation failed; safe editor state was restored.");
        }
        else
        {
            State = EditorFlowState.RoomLoaded;
            report.Error("FLOW_RECOVERY_FAILED", operation, "Recovery failed; editor state was reduced to RoomLoaded.");
        }

        return false;
    }
}