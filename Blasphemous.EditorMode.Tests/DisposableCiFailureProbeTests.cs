using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class DisposableCiFailureProbeTests
{
    [Fact]
    public void Intentional_failure_probe_for_hosted_ci()
    {
        Assert.True(false, "Intentional disposable failure for PR Validation evidence.");
    }
}