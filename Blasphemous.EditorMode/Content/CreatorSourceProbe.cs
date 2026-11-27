using System;

namespace Blasphemous.EditorMode.Content;

internal enum CreatorSourceProbeState
{
    NotLoaded,
    Ready,
    Failed
}

internal readonly struct CreatorSourceProbeResult
{
    private CreatorSourceProbeResult(
        CreatorSourceProbeState state,
        string failureReason,
        bool registryFailure)
    {
        State = state;
        FailureReason = failureReason;
        RegistryFailure = registryFailure;
    }

    internal CreatorSourceProbeState State { get; }
    internal string FailureReason { get; }
    internal bool RegistryFailure { get; }

    internal static CreatorSourceProbeResult NotLoaded()
    {
        return new CreatorSourceProbeResult(
            CreatorSourceProbeState.NotLoaded,
            null,
            false);
    }

    internal static CreatorSourceProbeResult Ready()
    {
        return new CreatorSourceProbeResult(
            CreatorSourceProbeState.Ready,
            null,
            false);
    }

    internal static CreatorSourceProbeResult Failed(
        string reason,
        bool registryFailure = false)
    {
        return new CreatorSourceProbeResult(
            CreatorSourceProbeState.Failed,
            reason ?? "Creator source result could not be read.",
            registryFailure);
    }
}

internal static class CreatorSourceProbe
{
    internal static CreatorSourceProbeResult Inspect(Func<object> readResult)
    {
        if (readResult == null)
            return CreatorSourceProbeResult.Failed("Creator source result could not be read.");

        try
        {
            return readResult() == null
                ? CreatorSourceProbeResult.NotLoaded()
                : CreatorSourceProbeResult.Ready();
        }
        catch (Exception exception)
        {
            return CreatorSourceProbeResult.Failed(
                "Creator source result could not be read: " + exception.Message);
        }
    }

    internal static bool TryConfirmLoadedSource(
        Func<object> readResult,
        out string failureReason)
    {
        CreatorSourceProbeResult result = Inspect(readResult);
        if (result.State == CreatorSourceProbeState.Ready)
        {
            failureReason = null;
            return true;
        }

        failureReason = result.State == CreatorSourceProbeState.Failed
            ? result.FailureReason
            : "Creator source load returned no object.";
        return false;
    }
}