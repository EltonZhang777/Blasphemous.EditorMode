using Blasphemous.EditorMode.Content;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class CreatorProbeTests
{
    [Fact]
    public void Registered_base_creator_is_available()
    {
        CreatorProbeResult result = CreatorProbe.Evaluate(
            new[] { BaseCreatorCatalog.BellIron },
            new[] { BaseCreatorCatalog.BellIronType });

        Assert.True(result.IsReady);
        Assert.Single(result.Available);
        Assert.Equal("D03Z02S06_LOGIC", result.Available[0].SourceScene);
        Assert.Equal("TRAPS/TRAP_SHOCK_CHAIN_REACTION", result.Available[0].SourcePath);
        Assert.Equal(TransformCapabilities.Position | TransformCapabilities.Rotation | TransformCapabilities.Scale,
            result.Available[0].TransformCapabilities);
    }

    [Fact]
    public void Missing_creator_is_reported_without_throwing()
    {
        CreatorProbeResult result = CreatorProbe.Evaluate(
            new[] { BaseCreatorCatalog.BellIron },
            new string[0]);

        CreatorProbeDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.False(result.IsReady);
        Assert.Equal(CreatorProbeFailure.Missing, diagnostic.Failure);
        Assert.Contains(BaseCreatorCatalog.BellIronType, diagnostic.Message);
    }

    [Fact]
    public void Unsupported_creator_is_reported_without_throwing()
    {
        CreatorMetadata unsupported = new CreatorMetadata(
            "optional-object",
            "Optional object",
            "Optional",
            CreatorOrigin.OptionalExtension,
            "scene",
            "path",
            TransformCapabilities.None,
            new string[0],
            false);

        CreatorProbeResult result = CreatorProbe.Evaluate(
            new[] { unsupported },
            new[] { unsupported.Type });

        CreatorProbeDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(CreatorProbeFailure.Unsupported, diagnostic.Failure);
        Assert.Contains(unsupported.Type, diagnostic.Message);
    }
}
