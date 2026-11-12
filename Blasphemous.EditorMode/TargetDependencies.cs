using Blasphemous.EditorMode.Diagnostics;
using System;

namespace Blasphemous.EditorMode;

internal static class TargetDependencies
{
    internal const string UnityProfile = "2017.4.40f1";
    internal const string GameLibsProfile = "4.0.67";
    internal const string ModdingApiId = "Blasphemous.ModdingAPI";
    internal const string ModdingApiMinimumVersion = "3.0.1";
    internal const string LevelFrameworkId = "Blasphemous.Framework.Levels";
    internal const string LevelFrameworkMinimumVersion = "0.2.0";
    internal const string UiFrameworkId = "Blasphemous.Framework.UI";
    internal const string UiFrameworkMinimumVersion = "0.2.0";

    internal static readonly DependencyRequirement[] Required =
    [
        new(ModdingApiId, new Version(3, 0, 1)),
        new(LevelFrameworkId, new Version(0, 2, 0)),
        new(UiFrameworkId, new Version(0, 2, 0))
    ];
}