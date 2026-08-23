using Blasphemous.EditorMode.Diagnostics;
using System;

namespace Blasphemous.EditorMode;

internal static class TargetDependencies
{
    internal const string ModdingApiId = "Blasphemous.ModdingAPI";
    internal const string ModdingApiMinimumVersion = "3.0.1";
    internal const string LevelFrameworkId = "Blasphemous.Framework.Levels";
    internal const string LevelFrameworkMinimumVersion = "0.2.0";
    internal const string UiFrameworkId = "Blasphemous.Framework.UI";
    internal const string UiFrameworkMinimumVersion = "0.2.0";

    internal static readonly DependencyRequirement[] Required =
    {
        new DependencyRequirement(ModdingApiId, new Version(3, 0, 1)),
        new DependencyRequirement(LevelFrameworkId, new Version(0, 2, 0)),
        new DependencyRequirement(UiFrameworkId, new Version(0, 2, 0))
    };
}
