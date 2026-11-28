using Blasphemous.EditorMode.Content;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class CreatorSourceProbeTests
{
    [Fact]
    public void Inspect_returns_ready_for_an_available_result()
    {
        object result = new object();

        CreatorSourceProbeResult probe = CreatorSourceProbe.Inspect(
            () => result,
            value => false);

        Assert.Equal(CreatorSourceProbeState.Ready, probe.State);
    }

    [Fact]
    public void Inspect_returns_not_loaded_when_the_availability_check_rejects_the_result()
    {
        object result = new object();

        CreatorSourceProbeResult probe = CreatorSourceProbe.Inspect(
            () => result,
            value => ReferenceEquals(value, result));

        Assert.Equal(CreatorSourceProbeState.NotLoaded, probe.State);
    }

    [Fact]
    public void TryConfirmLoadedSource_uses_the_availability_check()
    {
        object result = new object();

        bool confirmed = CreatorSourceProbe.TryConfirmLoadedSource(
            () => result,
            out string failureReason,
            value => ReferenceEquals(value, result));

        Assert.False(confirmed);
        Assert.Equal("Creator source load returned no object.", failureReason);
    }
}