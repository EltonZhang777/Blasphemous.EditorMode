using Blasphemous.EditorMode.Diagnostics;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class DependencyGateTests
{
    [Fact]
    public void Compatible_dependencies_enable_editor_mode()
    {
        DependencyGateResult result = DependencyGate.Evaluate(
            new[] { Requirement("Blasphemous.ModdingAPI", "3.0.1") },
            new[] { Installed("Blasphemous.ModdingAPI", "3.0.1") });

        Assert.True(result.IsCompatible);
        Assert.Empty(result.Diagnostics);
        Assert.Contains("dependency gate passed", result.StatusMessage);
    }

    [Fact]
    public void Missing_dependency_disables_editor_mode_with_actionable_diagnostic()
    {
        DependencyGateResult result = DependencyGate.Evaluate(
            new[] { Requirement("Blasphemous.Framework.Levels", "0.2.0") },
            Array.Empty<InstalledDependency>());

        DependencyDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.False(result.IsCompatible);
        Assert.Equal(DependencyFailureReason.Missing, diagnostic.Reason);
        Assert.Contains("Blasphemous.Framework.Levels", diagnostic.Message);
        Assert.Contains("Editor Mode disabled", result.StatusMessage);
        Assert.Contains("Blasphemous.Framework.Levels", result.StatusMessage);
    }

    [Fact]
    public void Older_dependency_version_disables_editor_mode_with_required_and_actual_versions()
    {
        DependencyGateResult result = DependencyGate.Evaluate(
            new[] { Requirement("Blasphemous.ModdingAPI", "3.0.1") },
            new[] { Installed("Blasphemous.ModdingAPI", "2.4.1") });

        DependencyDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.False(result.IsCompatible);
        Assert.Equal(DependencyFailureReason.VersionTooLow, diagnostic.Reason);
        Assert.Equal(new Version(3, 0, 1), diagnostic.RequiredVersion);
        Assert.Equal(new Version(2, 4, 1), diagnostic.ActualVersion);
        Assert.Contains("Editor Mode disabled", result.StatusMessage);
        Assert.Contains("2.4.1", result.StatusMessage);
        Assert.Contains("3.0.1", result.StatusMessage);
    }

    private static DependencyRequirement Requirement(string id, string version)
    {
        return new DependencyRequirement(id, Version.Parse(version));
    }

    private static InstalledDependency Installed(string id, string version)
    {
        return new InstalledDependency(id, Version.Parse(version));
    }
}
